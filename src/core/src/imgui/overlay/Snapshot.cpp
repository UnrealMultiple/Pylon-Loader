#include "imgui/overlay/Snapshot.h"

#include <chrono>
#include <cstring>

namespace pylon::imgui {

std::shared_ptr<const Snapshot> Snapshot::Capture(const ImDrawData& draw) {
    auto snapshot = std::make_shared<Snapshot>();
    snapshot->displayPos_ = draw.DisplayPos;
    snapshot->displaySize_ = draw.DisplaySize;
    snapshot->framebufferScale_ = draw.FramebufferScale;
    snapshot->takenAt_ = NowNs();

    if (draw.Textures != nullptr) {
        snapshot->textures_.reserve(draw.Textures->size());
        for (ImTextureData* tex : *draw.Textures) {
            if (tex == nullptr) continue;
            TextureSnap snap;
            snap.pointer = tex;
            snap.uniqueId = static_cast<unsigned int>(tex->UniqueID);
            snap.width = tex->Width;
            snap.height = tex->Height;
            snap.bytesPerPixel = tex->BytesPerPixel;
            snap.status = tex->Status;
            if ((tex->Status == ImTextureStatus_WantCreate || tex->Status == ImTextureStatus_WantUpdates) &&
                tex->Pixels != nullptr && tex->Width > 0 && tex->Height > 0) {
                const size_t bytes = static_cast<size_t>(tex->Width) * static_cast<size_t>(tex->Height) *
                                     static_cast<size_t>(tex->BytesPerPixel);
                snap.pixels.assign(tex->Pixels, tex->Pixels + bytes);
            }
            snapshot->textures_.push_back(std::move(snap));
        }
    }

    snapshot->lists_.resize(static_cast<size_t>(draw.CmdListsCount));
    for (int i = 0; i < draw.CmdListsCount; ++i) {
        const ImDrawList* source = draw.CmdLists[i];
        List& list = snapshot->lists_[static_cast<size_t>(i)];
        if (source->VtxBuffer.Size > 0) {
            list.vertices.assign(source->VtxBuffer.Data, source->VtxBuffer.Data + source->VtxBuffer.Size);
        }
        if (source->IdxBuffer.Size > 0) {
            list.indices.assign(source->IdxBuffer.Data, source->IdxBuffer.Data + source->IdxBuffer.Size);
        }
        if (source->CmdBuffer.Size > 0) {
            list.commands.assign(source->CmdBuffer.Data, source->CmdBuffer.Data + source->CmdBuffer.Size);
        }
        snapshot->vertices_ += source->VtxBuffer.Size;
        snapshot->commands_ += source->CmdBuffer.Size;
    }
    return snapshot;
}

int64_t NowNs() {
    return std::chrono::steady_clock::now().time_since_epoch().count();
}

}
