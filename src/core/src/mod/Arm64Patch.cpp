#include "pylon/mod/ModApi.h"

#include <cerrno>
#include <cstdio>
#include <cstddef>
#include <cstdlib>
#include <cstring>
#include <fcntl.h>
#include <limits>
#include <mutex>
#include <link.h>
#include <elf.h>
#include <sys/mman.h>
#include <unistd.h>

namespace {
std::mutex patch_mutex;
int32_t Result(PylonPatchDiagnostic* d, int32_t status, uint64_t address = 0,
               uint32_t expected = 0, uint32_t actual = 0, int error = 0) {
    if (d) *d = {status, error, address, expected, actual};
    return status;
}
// Fail closed unless every byte belongs to adjacent readable executable mappings
// with identical permissions. Gaps or permission changes are rejected.
int Mapping(uint64_t address, uint64_t bytes, int* protection) {
    FILE* maps = std::fopen("/proc/self/maps", "r");
    if (!maps) return PylonPatch_Unmapped;
    char line[1024];
    int result = PylonPatch_Unmapped;
    uint64_t cursor = address;
    uint64_t remaining = bytes;
    int originalProtection = -1;
    while (std::fgets(line, sizeof(line), maps)) {
        unsigned long long start, end;
        char perms[5] = {};
        if (std::sscanf(line, "%llx-%llx %4s", &start, &end, perms) != 3) continue;
        if (cursor >= end) continue;
        if (cursor < start) break;
        if (perms[0] != 'r' || perms[2] != 'x') { result = PylonPatch_NotExecutable; break; }
        const int current = PROT_READ | PROT_EXEC | (perms[1] == 'w' ? PROT_WRITE : 0);
        if (originalProtection >= 0 && originalProtection != current) break;
        originalProtection = current;
        const uint64_t available = end - cursor;
        if (remaining <= available) {
            *protection = current;
            result = PylonPatch_Ok;
            break;
        }
        remaining -= available;
        cursor = end;
    }
    std::fclose(maps);
    return result;
}
int32_t Check(const PylonCodeSite* site, uint32_t offset, uint64_t bytes,
              PylonPatchDiagnostic* d, int* protection) {
    if (!site || !site->address || site->reserved || (site->address & 3) ||
        (site->byteLength & 3) || !site->byteLength || (offset & 3) || !bytes ||
        site->address > std::numeric_limits<uintptr_t>::max() ||
        site->byteLength > std::numeric_limits<uintptr_t>::max() - site->address)
        return Result(d, PylonPatch_InvalidArgument);
    if (offset > site->byteLength || bytes > site->byteLength - offset)
        return Result(d, PylonPatch_OutOfBounds, site->address);
    return Result(d, Mapping(site->address + offset, bytes, protection), site->address + offset);
}
int Readable(uint64_t address, uint64_t bytes) {
    FILE* maps = std::fopen("/proc/self/maps", "r");
    if (!maps) return 0;
    char line[1024];
    int result = 0;
    uint64_t cursor = address;
    uint64_t remaining = bytes;
    while (std::fgets(line, sizeof(line), maps)) {
        unsigned long long start, end;
        char perms[5] = {};
        if (std::sscanf(line, "%llx-%llx %4s", &start, &end, perms) != 3) continue;
        if (cursor >= end) continue;
        if (cursor < start) break;
        if (perms[0] != 'r') break;
        const uint64_t available = end - cursor;
        if (remaining <= available) {
            result = 1;
            break;
        }
        remaining -= available;
        cursor = end;
    }
    std::fclose(maps);
    return result;
}

// Write one aligned 32-bit instruction through /proc/self/mem. This does NOT change
// page permissions, so the page stays executable for the whole operation: no window
// where another thread can hit a non-executable page. It also works for file-backed
// private mappings (libil2cpp) and rwxp pages, where restoring the original protection
// after a temporary RW mprotect can fail on Android.
// The caller still serializes competing writers via patch_mutex; execution does not
// need to be stopped for this single aligned word write.
int32_t WriteWord(uint64_t address, uint32_t expected, uint32_t replacement,
                  bool* written, uint32_t* actual, int* error) {
    *written = false;
    *error = 0;
    auto* at = reinterpret_cast<uint32_t*>(static_cast<uintptr_t>(address));
    uint32_t current = __atomic_load_n(at, __ATOMIC_ACQUIRE);
    *actual = current;
    if (current != expected) return PylonPatch_Conflict;
    const int fd = open("/proc/self/mem", O_RDWR | O_CLOEXEC);
    if (fd < 0) {
        *error = errno;
        return PylonPatch_BackendFailure;
    }
    ssize_t result;
    do {
        result = pwrite(fd, &replacement, sizeof(replacement), static_cast<off_t>(address));
    } while (result < 0 && errno == EINTR);
    if (result != static_cast<ssize_t>(sizeof(replacement))) {
        *error = result < 0 ? errno : EIO;
        close(fd);
        return PylonPatch_BackendFailure;
    }
    close(fd);
    __builtin___clear_cache(reinterpret_cast<char*>(at), reinterpret_cast<char*>(at + 1));
    current = __atomic_load_n(at, __ATOMIC_ACQUIRE);
    *actual = current;
    if (current != replacement) return PylonPatch_Conflict;
    *written = true;
    return PylonPatch_Ok;
}
}

static_assert(sizeof(PylonCodeSite) == 16);
static_assert(sizeof(PylonPatchDiagnostic) == 24);
static_assert(sizeof(PylonArm64Instruction) == 24);

PYLON_MOD_EXPORT int32_t PylonMod_CodeSiteCreate(uint64_t address, uint32_t bytes,
    PylonCodeSite* out, PylonPatchDiagnostic* d) {
    if (!out) return Result(d, PylonPatch_InvalidArgument, address);
    *out = {};
    PylonCodeSite candidate{address, bytes, 0};
    int protection;
    const int32_t status = Check(&candidate, 0, bytes, d, &protection);
    if (!status) *out = candidate;
    return status;
}

PYLON_MOD_EXPORT int32_t PylonMod_Arm64Read(const PylonCodeSite* site, uint32_t offset,
    uint32_t* out, PylonPatchDiagnostic* d) {
    if (!out) return Result(d, PylonPatch_InvalidArgument);
    *out = 0;
    int protection;
    const int32_t status = Check(site, offset, 4, d, &protection);
    if (status) return status;
    const auto* at = reinterpret_cast<const uint32_t*>(static_cast<uintptr_t>(site->address + offset));
    *out = __atomic_load_n(at, __ATOMIC_ACQUIRE);
    return Result(d, PylonPatch_Ok, site->address + offset, 0, *out);
}

PYLON_MOD_EXPORT int32_t PylonMod_TryReadMemory(uint64_t address, uint8_t* out,
    uint32_t byteLength, PylonPatchDiagnostic* d) {
    if (!out || address == 0 || byteLength == 0 || (address & 3))
        return Result(d, PylonPatch_InvalidArgument, address);
    if (address > std::numeric_limits<uintptr_t>::max() ||
        byteLength > std::numeric_limits<uintptr_t>::max() - address)
        return Result(d, PylonPatch_OutOfBounds, address);
    if (!Readable(address, byteLength)) return Result(d, PylonPatch_Unmapped, address);
    std::memcpy(out, reinterpret_cast<const void*>(static_cast<uintptr_t>(address)), byteLength);
    return Result(d, PylonPatch_Ok, address);
}

PYLON_MOD_EXPORT int32_t PylonMod_Arm64Decode(uint64_t pc, uint32_t word,
    PylonArm64Instruction* out, PylonPatchDiagnostic* d) {
    if (!out || (pc & 3)) return Result(d, PylonPatch_InvalidArgument, pc);
    *out = {word, PylonArm64_Unknown, 0, 0};
    // Deprecated ABI: managed decoding is provided by AsmArm64.
    return Result(d, PylonPatch_UnsupportedInstruction, pc, 0, word);
}

PYLON_MOD_EXPORT int32_t PylonMod_Arm64PcRelative(uint64_t pc, uint32_t word,
    uint64_t* target, PylonPatchDiagnostic* d) {
    if (!target) return Result(d, PylonPatch_InvalidArgument, pc);
    *target = 0;
    if (pc & 3) return Result(d, PylonPatch_InvalidArgument, pc);
    // Deprecated ABI: managed decoding is provided by AsmArm64.
    return Result(d, PylonPatch_UnsupportedInstruction, pc, 0, word);
}

PYLON_MOD_EXPORT int32_t PylonMod_Arm64Validate(const PylonCodeSite* site, uint32_t offset,
    const uint32_t* expected, uint32_t count, PylonPatchDiagnostic* d) {
    if (!expected || !count) return Result(d, PylonPatch_InvalidArgument);
    int protection;
    const int32_t status = Check(site, offset, uint64_t{count} * 4, d, &protection);
    if (status) return status;
    const auto* at = reinterpret_cast<const uint32_t*>(static_cast<uintptr_t>(site->address + offset));
    for (uint32_t i = 0; i < count; ++i) {
        const uint32_t actual = __atomic_load_n(at + i, __ATOMIC_ACQUIRE);
        if (actual != expected[i]) return Result(d, PylonPatch_FingerprintMismatch,
            site->address + offset + uint64_t{i} * 4, expected[i], actual);
    }
    return Result(d, PylonPatch_Ok, site->address + offset);
}

PYLON_MOD_EXPORT int32_t PylonMod_Arm64Write(const PylonCodeSite* site, uint32_t offset,
    uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* d) {
#if !defined(__aarch64__)
    (void)site; (void)offset; (void)expected; (void)replacement;
    return Result(d, PylonPatch_Unsupported);
#else
    std::lock_guard<std::mutex> lock(patch_mutex);
    int protection;
    const int32_t status = Check(site, offset, 4, d, &protection);
    if (status) return status;
    const uint64_t address = site->address + offset;
    bool written = false;
    uint32_t actual = 0;
    int error = 0;
    const int32_t writeStatus = WriteWord(address, expected, replacement, &written, &actual, &error);
    if (writeStatus == PylonPatch_Ok)
        return Result(d, PylonPatch_Ok, address, expected, replacement);
    if (writeStatus == PylonPatch_Conflict)
        return Result(d, PylonPatch_Conflict, address, expected, actual);
    return Result(d, writeStatus, address, expected, actual, error);
#endif
}

PYLON_MOD_EXPORT int32_t PylonMod_TryValidateCodeSite(const PylonCodeSite* site,
    uint32_t offset, const uint32_t* expected, uint32_t count, PylonPatchDiagnostic* d) {
    return PylonMod_Arm64Validate(site, offset, expected, count, d);
}

PYLON_MOD_EXPORT int32_t PylonMod_TryReadInstruction32(const PylonCodeSite* site,
    uint32_t offset, uint32_t* out, PylonPatchDiagnostic* d) {
    return PylonMod_Arm64Read(site, offset, out, d);
}

PYLON_MOD_EXPORT int32_t PylonMod_TryReadWords(const PylonCodeSite* site,
    uint32_t offset, uint32_t* out, uint32_t count, PylonPatchDiagnostic* d) {
    if (!out || !count) return Result(d, PylonPatch_InvalidArgument);
    int protection;
    const int32_t checked = Check(site, offset, uint64_t{count} * 4, d, &protection);
    if (checked) return checked; // Validate the whole interval before touching any output.
    for (uint32_t i = 0; i < count; ++i) {
        const int32_t status = PylonMod_Arm64Read(site, offset + i * 4u, &out[i], d);
        if (status) return status;
    }
    return PylonPatch_Ok;
}

PYLON_MOD_EXPORT int32_t PylonMod_TryWriteInstruction32(const PylonCodeSite* site,
    uint32_t offset, uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* d) {
    return PylonMod_Arm64Write(site, offset, expected, replacement, d);
}

PYLON_MOD_EXPORT int32_t PylonMod_TryWritePatch(const PylonCodeSite* site,
    uint32_t offset, uint32_t expected, uint32_t replacement, PylonPatchDiagnostic* d) {
    return PylonMod_Arm64Write(site, offset, expected, replacement, d);
}

namespace {
struct ImageLookup { uint64_t address; PylonCodeImage* image; };
int FindCodeImage(dl_phdr_info* info, size_t, void* opaque) {
    auto& lookup = *static_cast<ImageLookup*>(opaque);
    bool contains = false;
    for (uint16_t i = 0; i < info->dlpi_phnum; ++i) {
        const auto& ph = info->dlpi_phdr[i];
        if (ph.p_type != PT_LOAD || !(ph.p_flags & PF_X)) continue;
        const uint64_t start = info->dlpi_addr + ph.p_vaddr;
        if (lookup.address >= start && lookup.address - start < ph.p_memsz) contains = true;
    }
    if (!contains) return 0;
    lookup.image->imageBase = info->dlpi_addr;
    for (uint16_t i = 0; i < info->dlpi_phnum; ++i) {
        const auto& ph = info->dlpi_phdr[i];
        if (ph.p_type != PT_NOTE) continue;
        const auto* bytes = reinterpret_cast<const uint8_t*>(info->dlpi_addr + ph.p_vaddr);
        size_t offset = 0;
        while (offset <= ph.p_memsz && ph.p_memsz - offset >= sizeof(Elf64_Nhdr)) {
            Elf64_Nhdr note{};
            std::memcpy(&note, bytes + offset, sizeof(note));
            offset += sizeof(note);
            const uint64_t nameSize = (uint64_t{note.n_namesz} + 3) & ~uint64_t{3};
            const uint64_t dataSize = (uint64_t{note.n_descsz} + 3) & ~uint64_t{3};
            if (nameSize > ph.p_memsz - offset || dataSize > ph.p_memsz - offset - nameSize) break;
            if (note.n_type == NT_GNU_BUILD_ID && note.n_namesz == 4 &&
                std::memcmp(bytes + offset, "GNU", 4) == 0 && note.n_descsz &&
                note.n_descsz <= sizeof(lookup.image->buildId)) {
                lookup.image->buildIdLength = note.n_descsz;
                std::memcpy(lookup.image->buildId, bytes + offset + nameSize, note.n_descsz);
                return 1;
            }
            offset += static_cast<size_t>(nameSize + dataSize);
        }
    }
    return 1;
}
}
static_assert(sizeof(PylonCodeImage) == 80);
PYLON_MOD_EXPORT int32_t PylonMod_TryGetCodeImage(uint64_t address, PylonCodeImage* image) {
    if (!address || !image) return PylonPatch_InvalidArgument;
    *image = {};
    ImageLookup lookup{address, image};
    dl_iterate_phdr(FindCodeImage, &lookup);
    return image->buildIdLength ? PylonPatch_Ok : PylonPatch_BuildMismatch;
}

static_assert(sizeof(PylonPatchRequestV2) == 120);
static_assert(alignof(PylonPatchRequestV2) == 8);
static_assert(offsetof(PylonPatchRequestV2, imageBase) == 8);
static_assert(offsetof(PylonPatchRequestV2, expectedWords) == 80);
static_assert(offsetof(PylonPatchRequestV2, flags) == 100);
static_assert(offsetof(PylonPatchRequestV2, fingerprintByteOffset) == 104);
static_assert(offsetof(PylonPatchRequestV2, reserved) == 112);
static_assert(sizeof(PylonPatchDiagnosticV2) == 64);
static_assert(offsetof(PylonPatchDiagnosticV2, methodRva) == 16);
static_assert(offsetof(PylonPatchDiagnosticV2, writeState) == 52);

namespace {
struct InstalledPatch {
    PylonPatchRequestV2 request;
    uint64_t address;
    InstalledPatch* next;
    // Complete original method fingerprint follows this record.
};
InstalledPatch* installed_patches = nullptr; // guarded by patch_mutex, process lifetime

int32_t ResultV2(PylonPatchDiagnosticV2* d, const PylonPatchRequestV2* r,
                 int32_t status, uint32_t offset = 0, uint32_t expected = 0,
                 uint32_t actual = 0, int error = 0, bool written = false) {
    if (d) {
        *d = {};
        d->structSize = sizeof(*d);
        d->abiVersion = PylonPatch_AbiV2;
        d->status = status;
        d->systemError = error;
        d->byteOffset = offset;
        d->expected = expected;
        d->actual = actual;
        d->writeState = written ? PylonPatchV2_Written : PylonPatchV2_NotWritten;
        if (r) {
            d->methodRva = r->methodRva;
            if (r->methodRva <= UINT64_MAX - offset) {
                d->siteRva = r->methodRva + offset;
                if (r->imageBase <= UINT64_MAX - d->siteRva)
                    d->address = r->imageBase + d->siteRva;
            }
        }
    }
    return status;
}
int32_t CheckV2(const PylonPatchRequestV2* r, PylonPatchDiagnosticV2* d,
                int* protection) {
    if (!r || r->structSize != sizeof(*r) || r->abiVersion != PylonPatch_AbiV2)
        return ResultV2(d, nullptr, PylonPatch_InvalidArgument);
    if (r->reserved ||
        (r->flags & ~(PylonPatchV2_BuildAuthenticated | PylonPatchV2_Quiescent)) ||
        !r->imageBase || !r->ownerId || !r->siteId || !r->expectedWords ||
        r->expectedWords > UINTPTR_MAX || (r->expectedWords & 3) ||
        !r->methodByteLength || r->expectedWordCount != r->methodByteLength / 4 ||
        uint64_t{r->expectedWordCount} * 4 != r->methodByteLength ||
        uint64_t{r->methodByteLength} > UINTPTR_MAX - r->expectedWords)
        return ResultV2(d, r, PylonPatch_InvalidArgument, r->byteOffset);
    if (!(r->flags & PylonPatchV2_BuildAuthenticated) ||
        !(r->expectedBuildIdentityLow | r->expectedBuildIdentityHigh) ||
        r->expectedBuildIdentityLow != r->authenticatedBuildIdentityLow ||
        r->expectedBuildIdentityHigh != r->authenticatedBuildIdentityHigh)
        return ResultV2(d, r, PylonPatch_BuildMismatch, r->byteOffset);
    if ((r->imageBase & 3) || (r->methodRva & 3) || (r->byteOffset & 3))
        return ResultV2(d, r, PylonPatch_Unaligned, r->byteOffset);
    if (r->methodRva > UINTPTR_MAX || r->imageBase > UINTPTR_MAX - r->methodRva ||
        r->methodByteLength > UINTPTR_MAX - (r->imageBase + r->methodRva) ||
        r->byteOffset >= r->methodByteLength)
        return ResultV2(d, r, PylonPatch_OutOfBounds, r->byteOffset);
    if (r->replacementWordCount != 1)
        return ResultV2(d, r, PylonPatch_Unsupported, r->byteOffset);
    const uint32_t fingerprintOffset = r->fingerprintWordCount ? r->fingerprintByteOffset : 0;
    const uint64_t fingerprintBytes = r->fingerprintWordCount ? uint64_t{r->fingerprintWordCount} * 4 : r->methodByteLength;
    if ((fingerprintOffset & 3) || fingerprintOffset > r->methodByteLength ||
        fingerprintBytes > r->methodByteLength - fingerprintOffset ||
        r->byteOffset < fingerprintOffset || uint64_t{r->byteOffset} + 4 > fingerprintOffset + fingerprintBytes)
        return ResultV2(d, r, PylonPatch_OutOfBounds, r->byteOffset);
    const int status = Mapping(r->imageBase + r->methodRva + fingerprintOffset, fingerprintBytes, protection);
    return ResultV2(d, r, status, r->byteOffset);
}
const uint32_t* ExpectedV2(const PylonPatchRequestV2* r) {
    return reinterpret_cast<const uint32_t*>(static_cast<uintptr_t>(r->expectedWords));
}
int32_t FingerprintV2(const PylonPatchRequestV2* r, PylonPatchDiagnosticV2* d) {
    const auto* at = reinterpret_cast<const uint32_t*>(static_cast<uintptr_t>(r->imageBase + r->methodRva));
    const auto* expected = ExpectedV2(r);
    const uint32_t first = r->fingerprintWordCount ? r->fingerprintByteOffset / 4 : 0;
    const uint32_t end = r->fingerprintWordCount ? first + r->fingerprintWordCount : r->expectedWordCount;
    for (uint32_t i = first; i < end; ++i) {
        const uint32_t actual = __atomic_load_n(at + i, __ATOMIC_ACQUIRE);
        uint32_t effectiveExpected = expected[i];
        // Account only for writes already registered against the SAME immutable
        // original method profile. Never bless unknown live bytes as expected.
        for (auto* entry = installed_patches; entry; entry = entry->next) {
            const auto& old = entry->request;
            if (old.imageBase != r->imageBase || old.methodRva != r->methodRva ||
                old.methodByteLength != r->methodByteLength || old.byteOffset != i * 4) continue;
            const auto* original = reinterpret_cast<const uint32_t*>(entry + 1);
            if (old.expectedBuildIdentityLow != r->expectedBuildIdentityLow ||
                old.expectedBuildIdentityHigh != r->expectedBuildIdentityHigh ||
                std::memcmp(original, expected, r->methodByteLength) != 0)
                return ResultV2(d, r, PylonPatch_Conflict, i * 4, expected[i], actual);
            effectiveExpected = old.replacement;
        }
        if (actual != effectiveExpected)
            return ResultV2(d, r, PylonPatch_FingerprintMismatch, i * 4, effectiveExpected, actual);
    }
    return ResultV2(d, r, PylonPatch_Ok, r->byteOffset);
}
int32_t RegistryV2(const PylonPatchRequestV2* r, PylonPatchDiagnosticV2* d) {
    const uint64_t address = r->imageBase + r->methodRva + r->byteOffset;
    for (auto* entry = installed_patches; entry; entry = entry->next) {
        const auto& old = entry->request;
        const bool identity = old.ownerId == r->ownerId && old.siteId == r->siteId;
        if (!identity && entry->address != address) continue;
        const auto* fingerprint = reinterpret_cast<const uint32_t*>(entry + 1);
        bool same = identity && entry->address == address &&
            old.imageBase == r->imageBase && old.methodRva == r->methodRva &&
            old.methodByteLength == r->methodByteLength && old.byteOffset == r->byteOffset &&
            old.expectedBuildIdentityLow == r->expectedBuildIdentityLow &&
            old.expectedBuildIdentityHigh == r->expectedBuildIdentityHigh &&
            old.replacement == r->replacement &&
            std::memcmp(fingerprint, ExpectedV2(r), r->methodByteLength) == 0;
        uint32_t actual = 0;
        const auto* at = reinterpret_cast<const uint32_t*>(static_cast<uintptr_t>(address - r->byteOffset));
        if (same) {
            const uint32_t first = r->fingerprintWordCount ? r->fingerprintByteOffset / 4 : 0;
            const uint32_t end = r->fingerprintWordCount ? first + r->fingerprintWordCount : r->expectedWordCount;
            for (uint32_t i = first; i < end; ++i) {
                actual = __atomic_load_n(at + i, __ATOMIC_ACQUIRE);
                const uint32_t expected = i == r->byteOffset / 4 ? r->replacement : fingerprint[i];
                if (actual != expected)
                    return ResultV2(d, r, PylonPatch_Conflict, i * 4, expected, actual);
            }
            return ResultV2(d, r, PylonPatch_DuplicateInstall, r->byteOffset,
                            fingerprint[r->byteOffset / 4], r->replacement);
        }
        actual = __atomic_load_n(at + r->byteOffset / 4, __ATOMIC_ACQUIRE);
        return ResultV2(d, r, PylonPatch_Conflict, r->byteOffset,
                        ExpectedV2(r)[r->byteOffset / 4], actual);
    }
    return PylonPatch_Ok;
}
}

PYLON_MOD_EXPORT int32_t PylonMod_TryValidateCodeSiteV2(const PylonPatchRequestV2* r,
    PylonPatchDiagnosticV2* d) {
    std::lock_guard<std::mutex> lock(patch_mutex);
    int protection;
    int32_t status = CheckV2(r, d, &protection);
    if (status) return status;
    status = RegistryV2(r, d);
    return status ? status : FingerprintV2(r, d);
}

PYLON_MOD_EXPORT int32_t PylonMod_TryWritePatchV2(const PylonPatchRequestV2* r,
    PylonPatchDiagnosticV2* d) {
    std::lock_guard<std::mutex> lock(patch_mutex);
    int protection;
    int32_t status = CheckV2(r, d, &protection);
    if (status) return status;
    if (!(r->flags & PylonPatchV2_Quiescent))
        return ResultV2(d, r, PylonPatch_LifecycleFailure, r->byteOffset);
    status = RegistryV2(r, d);
    if (status) return status;
    status = FingerprintV2(r, d);
    if (status) return status;
#if !defined(__aarch64__)
    return ResultV2(d, r, PylonPatch_Unsupported, r->byteOffset);
#else
    // Allocate and snapshot BEFORE writing so successful writes are always registered.
    auto* entry = static_cast<InstalledPatch*>(std::malloc(sizeof(InstalledPatch) + size_t{r->methodByteLength}));
    if (!entry) return ResultV2(d, r, PylonPatch_BackendFailure, r->byteOffset, 0, 0, ENOMEM);
    entry->request = *r;
    std::memcpy(entry + 1, ExpectedV2(r), r->methodByteLength);
    entry->address = r->imageBase + r->methodRva + r->byteOffset;
    const uint32_t expected = reinterpret_cast<const uint32_t*>(entry + 1)[r->byteOffset / 4];
    bool written = false;
    uint32_t actual = expected;
    int error = 0;
    const int32_t writeStatus = WriteWord(entry->address, expected, r->replacement,
                                          &written, &actual, &error);
    if (written) {
        entry->next = installed_patches;
        installed_patches = entry;
    } else std::free(entry);
    return ResultV2(d, r, writeStatus, r->byteOffset, expected,
                    written ? r->replacement : actual, error, written);
#endif
}
