using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using AsmArm64;
using Pylon.ModLoader;
using static AsmArm64.Arm64RegisterD;
using static AsmArm64.Arm64RegisterQ;
using static AsmArm64.Arm64RegisterS;
using static AsmArm64.Arm64RegisterSP;
using static AsmArm64.Arm64RegisterV;
using static AsmArm64.Arm64RegisterX;
using AsmInstruction = AsmArm64.Arm64Instruction;

namespace RegressionTest
{
    /// <summary>
    /// P1 device test: generate real ARM64 target functions in a sealed code page, install
    /// <see cref="MidBodyIntercept"/> on them, and call them from managed code. This exercises
    /// the generated InterceptStub (800-byte frame), not a hand-written B passthrough.
    /// </summary>
    internal static class Arm64InterceptDeviceTests
    {
        private const string BuildId = "device-selftest";
        private const string ProfileName = "arm64-midbody-selftest";
        private const string Owner = "RegressionTest";
        private const int PageBytes = 0x1000;
        private const int ResultBytes = 0x400;
        private const ulong HelperOffset = 0xB00;
        private const ulong NzcvValue = 0xF0000000UL;

        private const int PreX18Offset = 0x3F0;
        private const int PreX30Offset = 0x3F8;

        private static readonly byte[] ZeroBuffer = new byte[ResultBytes];

        private static readonly Arm64RegisterX[] Gprs =
        {
            X0, X1, X2, X3, X4, X5, X6, X7, X8, X9, X10, X11, X12, X13, X14, X15,
            X16, X17, X18, X19, X20, X21, X22, X23, X24, X25, X26, X27, X28, X29, X30
        };

        private static readonly Arm64RegisterQ[] Vectors =
        {
            Q0, Q1, Q2, Q3, Q4, Q5, Q6, Q7, Q8, Q9, Q10, Q11, Q12, Q13, Q14, Q15,
            Q16, Q17, Q18, Q19, Q20, Q21, Q22, Q23, Q24, Q25, Q26, Q27, Q28, Q29, Q30, Q31
        };

        private static readonly Arm64RegisterV_16B[] VectorBytes =
        {
            V0.T_16B, V1.T_16B, V2.T_16B, V3.T_16B, V4.T_16B, V5.T_16B, V6.T_16B, V7.T_16B,
            V8.T_16B, V9.T_16B, V10.T_16B, V11.T_16B, V12.T_16B, V13.T_16B, V14.T_16B, V15.T_16B,
            V16.T_16B, V17.T_16B, V18.T_16B, V19.T_16B, V20.T_16B, V21.T_16B, V22.T_16B,
            V23.T_16B, V24.T_16B, V25.T_16B, V26.T_16B, V27.T_16B, V28.T_16B, V29.T_16B,
            V30.T_16B, V31.T_16B
        };

        [DllImport("pylon", EntryPoint = "PylonMod_ReserveCodePage")]
        private static extern int ReserveCodePage(ulong nearAddress, uint byteSize,
            out ulong pageAddress);

        [DllImport("pylon", EntryPoint = "PylonMod_SealCodePage")]
        private static extern int SealCodePage(ulong pageAddress, byte[] bytes, uint byteLength);

        [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
        private delegate void TargetFunction(IntPtr result);

        private sealed class TestFunction
        {
            internal string Name;
            internal ulong Base;
            internal ulong Site;
            internal byte[] Bytes;
            internal uint[] Words;
            internal string ExpectedStrategy;

            internal int SiteOffset => (int)(Site - Base);
        }

        public static void Run(ModLogger log)
        {
            try
            {
                RunCore(log);
            }
            catch (Exception exception)
            {
                log.Warn("[p1] FAIL harness exception: " + exception.GetType().Name + ": " +
                         exception.Message);
            }
        }

        private static void RunCore(ModLogger log)
        {
            var anchor = ResolveAnchor();
            if (anchor == 0)
            {
                log.Warn("[p1] FAIL no executable anchor for ReserveCodePage");
                return;
            }

            var reserve = ReserveCodePage(anchor, PageBytes, out var page);
            if (reserve != 0)
            {
                log.Warn($"[p1] FAIL ReserveCodePage status={reserve} anchor=0x{anchor:x}");
                return;
            }
            log.Info($"[p1] code page 0x{page:x} (anchor 0x{anchor:x})");

            if (!TryBuildImage(page, log, out var image, out var functions))
            {
                log.Warn("[p1] FAIL build target image");
                return;
            }
            var seal = SealCodePage(page, image, PageBytes);
            if (seal != 0)
            {
                log.Warn($"[p1] FAIL SealCodePage status={seal} page=0x{page:x}");
                return;
            }
            log.Info($"[p1] sealed target page 0x{page:x} functions={functions.Count}");

            var raw = Marshal.AllocHGlobal(ResultBytes + 16);
            var result = (IntPtr)(((long)raw + 15) & ~15L);
            var passed = 0;
            var failed = 0;
            try
            {
                foreach (var scenario in new Func<bool>[]
                         {
                             () => RunFullFrame(functions[0], page, result, log),
                             () => RunSkipAndExecute(functions[1], page, result, log),
                             () => RunLinkRegister(functions[2], page, result, log, "bl"),
                             () => RunLinkRegister(functions[3], page, result, log, "blr"),
                             () => RunConditional(functions[4], page, result, log, "b.cond", 10),
                             () => RunConditional(functions[5], page, result, log, "cbz", 11),
                             () => RunConditional(functions[6], page, result, log, "tbz", 12),
                             () => RunLiteral(functions[7], page, result, log, "s"),
                             () => RunLiteral(functions[8], page, result, log, "d"),
                             () => RunLiteral(functions[9], page, result, log, "q")
                         })
                {
                    if (scenario()) passed++;
                    else failed++;
                }
            }
            finally
            {
                Marshal.FreeHGlobal(raw);
            }

            if (failed == 0)
                log.Info($"[p1] RESULT PASS {passed}/{passed + failed} scenarios, page=0x{page:x}");
            else
                log.Warn($"[p1] RESULT FAIL passed={passed} failed={failed} page=0x{page:x}");
        }

        private static ulong ResolveAnchor()
        {
            if (NativeLibrary.TryLoad("libil2cpp.so", out var il2cpp))
            {
                if (NativeLibrary.TryGetExport(il2cpp, "il2cpp_domain_get", out var export))
                {
                    var address = unchecked((ulong)export.ToInt64());
                    NativeLibrary.Free(il2cpp);
                    if (address != 0) return address;
                }
                else NativeLibrary.Free(il2cpp);
            }
            if (NativeLibrary.TryLoad("libpylon.so", out var pylon))
            {
                if (NativeLibrary.TryGetExport(pylon, "PylonMod_ReserveCodePage", out var export))
                {
                    var address = unchecked((ulong)export.ToInt64());
                    NativeLibrary.Free(pylon);
                    return address;
                }
                NativeLibrary.Free(pylon);
            }
            return 0;
        }

        private static bool TryBuildImage(ulong page, ModLogger log, out byte[] image,
            out List<TestFunction> functions)
        {
            image = new byte[PageBytes];
            functions = new List<TestFunction>();
            var builders = new Func<TestFunction>[]
            {
                () => BuildFull(page, 0x000),
                () => BuildSkip(page, 0x600),
                () => BuildBl(page, 0x680),
                () => BuildBlr(page, 0x700),
                () => BuildConditional(page, 0x780, "b.cond", true),
                () => BuildConditional(page, 0x800, "cbz", false),
                () => BuildConditional(page, 0x880, "tbz", false),
                () => BuildLiteral(page, 0x900, "s"),
                () => BuildLiteral(page, 0x980, "d"),
                () => BuildLiteral(page, 0xA00, "q")
            };
            foreach (var builder in builders)
            {
                var function = builder();
                if (function == null)
                {
                    log.Warn("[p1] FAIL function generation");
                    return false;
                }
                functions.Add(function);
            }
            var helper = BuildHelper(page + HelperOffset);
            if (helper == null)
            {
                log.Warn("[p1] FAIL helper generation");
                return false;
            }

            foreach (var function in functions)
            {
                var offset = (int)(function.Base - page);
                if (offset < 0 || offset + function.Bytes.Length > PageBytes)
                {
                    log.Warn($"[p1] FAIL layout outside page: {function.Name}");
                    return false;
                }
                Buffer.BlockCopy(function.Bytes, 0, image, offset, function.Bytes.Length);
            }
            Buffer.BlockCopy(helper, 0, image, (int)HelperOffset, helper.Length);

            // Assert the generated functions do not overlap.
            for (var i = 0; i < functions.Count; i++)
                for (var j = i + 1; j < functions.Count; j++)
                {
                    var a = functions[i];
                    var b = functions[j];
                    if (a.Base < b.Base + (ulong)b.Bytes.Length && b.Base < a.Base + (ulong)a.Bytes.Length)
                    {
                        log.Warn($"[p1] FAIL layout overlap: {a.Name}/{b.Name}");
                        return false;
                    }
                }
            return true;
        }

        private static TestFunction BuildFull(ulong page, int offset)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0xF0);
            for (var i = 0; i < 4; i++) asm.STP(Vectors[8 + i * 2], Vectors[9 + i * 2], Sp(i * 32));
            asm.STR(X0, Sp(0x80));
            asm.STP(X27, X28, Sp(0x90));
            asm.STP(X25, X26, Sp(0xA0));
            asm.STP(X23, X24, Sp(0xB0));
            asm.STP(X21, X22, Sp(0xC0));
            asm.STP(X19, X20, Sp(0xD0));
            asm.STP(X29, X30, Sp(0xE0));

            // Capture the values that are not loaded from constants before the site.
            asm.LDR(X9, Sp(0x80));
            asm.STR(X18, Mem(X9, PreX18Offset));
            asm.STR(X30, Mem(X9, PreX30Offset));

            MovConst(asm, X9, NzcvValue);
            asm.MSR(Sys(Arm64SystemRegisterKnownId.NZCV), X9);
            asm.MOVZ(X9, 0);
            asm.MSR(Sys(Arm64SystemRegisterKnownId.FPCR), X9);
            asm.MSR(Sys(Arm64SystemRegisterKnownId.FPSR), X9);

            for (var i = 0; i < 18; i++) MovConst(asm, Gprs[i], GprConstant(i));
            for (var i = 19; i < 30; i++) MovConst(asm, Gprs[i], GprConstant(i));
            // x9 is also the result pointer below; preserve its constant in the free stack slot.
            asm.STR(X9, Sp(0x88));

            for (var i = 0; i < 32; i++)
                asm.MOVI(VectorBytes[i], (byte)(0xA0 + i), default, 0);

            asm.Label("site", out var site);
            asm.NOP();

            asm.LDR(X9, Sp(0x80));
            for (var i = 0; i < 9; i++) asm.STR(Gprs[i], Mem(X9, i * 8));
            asm.STR(X18, Mem(X9, 0x90));
            for (var i = 10; i < 18; i++) asm.STR(Gprs[i], Mem(X9, i * 8));
            for (var i = 19; i < 31; i++) asm.STR(Gprs[i], Mem(X9, i * 8));
            asm.LDR(X10, Sp(0x88));
            asm.STR(X10, Mem(X9, 9 * 8));

            asm.MRS(X10, Sys(Arm64SystemRegisterKnownId.NZCV));
            asm.STR(X10, Mem(X9, InterceptFrame.NzcvOffset));
            asm.MRS(X10, Sys(Arm64SystemRegisterKnownId.FPCR));
            asm.STR(X10, Mem(X9, InterceptFrame.FpcrOffset));
            asm.MRS(X10, Sys(Arm64SystemRegisterKnownId.FPSR));
            asm.STR(X10, Mem(X9, InterceptFrame.FpsrOffset));
            for (var i = 0; i < 16; i++)
                asm.STP(Vectors[i * 2], Vectors[i * 2 + 1], Mem(X9, InterceptFrame.VectorOffset + i * 32));

            // q8-q15 are callee-saved; restore the caller's values after capturing results.
            for (var i = 0; i < 4; i++) asm.LDP(Vectors[8 + i * 2], Vectors[9 + i * 2], Sp(i * 32));
            asm.LDP(X29, X30, Sp(0xE0));
            asm.LDP(X19, X20, Sp(0xD0));
            asm.LDP(X21, X22, Sp(0xC0));
            asm.LDP(X23, X24, Sp(0xB0));
            asm.LDP(X25, X26, Sp(0xA0));
            asm.LDP(X27, X28, Sp(0x90));
            asm.ADD(SP, SP, 0xF0);
            asm.RET();
            return Finish("full", baseAddress, asm, site, 0, "verbatim");
        }

        private static TestFunction BuildSkip(ulong page, int offset)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0x10);
            asm.STR(X30, Sp(0));
            asm.STR(X0, Sp(8));
            asm.MOVZ(X0, 0);
            asm.Label("site", out var site);
            asm.ADD(X0, X0, 100);
            asm.LDR(X9, Sp(8));
            asm.STR(X0, Mem(X9, 0));
            asm.LDR(X30, Sp(0));
            asm.ADD(SP, SP, 0x10);
            asm.RET();
            return Finish("skip", baseAddress, asm, site, 0, "verbatim");
        }

        private static TestFunction BuildBl(ulong page, int offset)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0x10);
            asm.STR(X30, Sp(0));
            asm.STR(X0, Sp(8));
            asm.Label("site", out var site);
            asm.BL(new Arm64Label(page + HelperOffset));
            asm.LDR(X9, Sp(8));
            asm.STR(X0, Mem(X9, 0));
            asm.LDR(X30, Sp(0));
            asm.ADD(SP, SP, 0x10);
            asm.RET();
            return Finish("bl", baseAddress, asm, site, 0, "bl");
        }

        private static TestFunction BuildBlr(ulong page, int offset)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0x10);
            asm.STR(X30, Sp(0));
            asm.STR(X0, Sp(8));
            MovConst(asm, X9, page + HelperOffset);
            asm.Label("site", out var site);
            asm.BLR(X9);
            asm.LDR(X10, Sp(8));
            asm.STR(X0, Mem(X10, 0));
            asm.LDR(X30, Sp(0));
            asm.ADD(SP, SP, 0x10);
            asm.RET();
            return Finish("blr", baseAddress, asm, site, 0, "blr");
        }

        private static TestFunction BuildConditional(ulong page, int offset, string kind, bool cond)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0x10);
            asm.STR(X30, Sp(0));
            asm.STR(X0, Sp(8));
            asm.MOVZ(X0, 0);
            if (cond)
            {
                MovConst(asm, X9, 0x60000000UL); // Z=1, C=1
                asm.MSR(Sys(Arm64SystemRegisterKnownId.NZCV), X9);
            }
            else
            {
                asm.MOVZ(X1, 0); // CBZ/TBZ use a register value, not NZCV.
            }

            asm.LabelForward(out var taken, "taken");
            asm.Label("site", out var site);
            if (kind == "b.cond") asm.B(Arm64ConditionalKind.EQ, taken);
            else if (kind == "cbz") asm.CBZ(X1, taken);
            else asm.TBZ(X1, 3, taken);

            var increment = kind == "b.cond" ? 20 : kind == "cbz" ? 20 : 20;
            var takenIncrement = kind == "b.cond" ? 10 : kind == "cbz" ? 11 : 12;
            asm.ADD(X0, X0, (ushort)increment);
            asm.LabelForward(out var done, "done");
            asm.B(done);
            asm.Label(taken, true);
            asm.ADD(X0, X0, (ushort)takenIncrement);
            asm.Label(done, true);
            asm.LDR(X9, Sp(8));
            asm.STR(X0, Mem(X9, 0));
            asm.LDR(X30, Sp(0));
            asm.ADD(SP, SP, 0x10);
            asm.RET();
            return Finish(kind, baseAddress, asm, site, 0, kind);
        }

        private static TestFunction BuildLiteral(ulong page, int offset, string kind)
        {
            var baseAddress = page + (ulong)offset;
            var asm = new Arm64Assembler(baseAddress);
            asm.SUB(SP, SP, 0x10);
            asm.STR(X30, Sp(0));
            asm.STR(X0, Sp(8));
            asm.LabelForward(out var literal, "literal");
            asm.Label("site", out var site);
            if (kind == "s") asm.LDR(S0, literal);
            else if (kind == "d") asm.LDR(D0, literal);
            else asm.LDR(Q0, literal);
            asm.LDR(X9, Sp(8));
            if (kind == "s") asm.STR(S0, Mem(X9, 0));
            else if (kind == "d") asm.STR(D0, Mem(X9, 0));
            else asm.STR(Q0, Mem(X9, 0));
            asm.LDR(X30, Sp(0));
            asm.ADD(SP, SP, 0x10);
            asm.RET();

            var align = kind == "s" ? 4 : kind == "d" ? 8 : 16;
            while ((asm.CurrentAddress & (ulong)(align - 1)) != 0) asm.NOP();
            asm.Label(literal, true);
            if (kind == "s")
            {
                asm.AppendUInt32(0x3FC00000); // 1.5f
            }
            else if (kind == "d")
            {
                asm.AppendUInt32(0);          // 2.5
                asm.AppendUInt32(0x40040000);
            }
            else
            {
                asm.AppendUInt32(0xdeadbeef);
                asm.AppendUInt32(0x01234567);
                asm.AppendUInt32(0x89abcdef);
                asm.AppendUInt32(0xfedcba98);
            }
            var codeWords = (int)((literal.Address - baseAddress) / 4);
            return Finish("ldr-" + kind, baseAddress, asm, site, codeWords, "literal-inline");
        }

        private static byte[] BuildHelper(ulong baseAddress)
        {
            try
            {
                var asm = new Arm64Assembler(baseAddress);
                asm.MOV(X0, X30);
                asm.RET();
                asm.End();
                return asm.Buffer.ToArray();
            }
            catch
            {
                return null;
            }
        }

        private static TestFunction Finish(string name, ulong baseAddress, Arm64Assembler asm,
            Arm64Label site, int codeWordCount, string expectedStrategy)
        {
            try
            {
                asm.End();
            }
            catch (Exception)
            {
                return null;
            }
            var bytes = asm.Buffer.ToArray();
            if (bytes.Length == 0 || (bytes.Length & 3) != 0) return null;
            var siteOffset = (int)(site.Address - baseAddress);
            if (siteOffset < 0 || (siteOffset & 3) != 0 || siteOffset + 4 > bytes.Length)
                return null;
            var words = new uint[bytes.Length / 4];
            for (var i = 0; i < words.Length; i++)
                words[i] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(i * 4, 4));
            if (AsmInstruction.Decode(words[siteOffset / 4]).Id == Arm64InstructionId.Invalid)
                return null;
            var decodeCount = codeWordCount == 0 ? words.Length : codeWordCount;
            for (var i = 0; i < decodeCount; i++)
                if (AsmInstruction.Decode(words[i]).Id == Arm64InstructionId.Invalid)
                    return null;
            return new TestFunction
            {
                Name = name,
                Base = baseAddress,
                Site = site.Address,
                Bytes = bytes,
                Words = words,
                ExpectedStrategy = expectedStrategy
            };
        }

        private static bool RunFullFrame(TestFunction function, ulong page, IntPtr result,
            ModLogger log)
        {
            Clear(result);
            var calls = 0;
            string handlerError = null;
            InterceptHandler handler = frame =>
            {
                try
                {
                    calls++;
                    for (var i = 0; i < 18; i++)
                        ExpectGpr(frame, i, GprConstant(i), ref handlerError);
                    for (var i = 19; i < 30; i++)
                        ExpectGpr(frame, i, GprConstant(i), ref handlerError);
                    var preX18 = unchecked((ulong)Marshal.ReadInt64(result, PreX18Offset));
                    var preX30 = unchecked((ulong)Marshal.ReadInt64(result, PreX30Offset));
                    Expect(InterceptFrame.GetGpr(frame, 18) == preX18, "frame x18",
                        ref handlerError);
                    Expect(InterceptFrame.GetGpr(frame, 30) == preX30, "frame x30",
                        ref handlerError);
                    Expect(unchecked((ulong)Marshal.ReadInt64(frame, InterceptFrame.NzcvOffset))
                           == NzcvValue, "frame NZCV", ref handlerError);
                    Expect(Marshal.ReadInt64(frame, InterceptFrame.FpcrOffset) == 0,
                        "frame FPCR", ref handlerError);
                    Expect(Marshal.ReadInt64(frame, InterceptFrame.FpsrOffset) == 0,
                        "frame FPSR", ref handlerError);
                    for (var i = 0; i < 32; i++)
                    {
                        var lane = VectorLane(i);
                        Expect(InterceptFrame.GetVectorUInt64(frame, i, 0) == lane,
                            "frame q" + i + " lane0", ref handlerError);
                        Expect(InterceptFrame.GetVectorUInt64(frame, i, 1) == lane,
                            "frame q" + i + " lane1", ref handlerError);
                    }
                }
                catch (Exception exception)
                {
                    handlerError = exception.GetType().Name + ": " + exception.Message;
                }
                return false;
            };

            if (!TryInstall(function, page, "full-frame", handler, log, out var interception))
                return false;
            Invoke(function, result);

            var failure = handlerError;
            Expect(calls == 1, "handler calls=" + calls, ref failure);
            if (interception.RelocationStrategy != function.ExpectedStrategy)
                failure = failure ?? "strategy=" + interception.RelocationStrategy;
            for (var i = 0; i < 18; i++)
                Expect(ReadUInt64(result, i * 8) == GprConstant(i), "x" + i, ref failure);
            for (var i = 19; i < 30; i++)
                Expect(ReadUInt64(result, i * 8) == GprConstant(i), "x" + i, ref failure);
            var pre18 = ReadUInt64(result, PreX18Offset);
            var pre30 = ReadUInt64(result, PreX30Offset);
            Expect(ReadUInt64(result, 0x90) == pre18, "x18", ref failure);
            Expect(ReadUInt64(result, 0xF0) == pre30, "x30", ref failure);
            Expect(ReadUInt64(result, InterceptFrame.NzcvOffset) == NzcvValue, "NZCV",
                ref failure);
            Expect(ReadUInt64(result, InterceptFrame.FpcrOffset) == 0, "FPCR", ref failure);
            Expect(ReadUInt64(result, InterceptFrame.FpsrOffset) == 0, "FPSR", ref failure);
            for (var i = 0; i < 32; i++)
            {
                var lane = VectorLane(i);
                Expect(ReadUInt64(result, InterceptFrame.VectorOffset + i * 16) == lane,
                    "q" + i + " lane0", ref failure);
                Expect(ReadUInt64(result, InterceptFrame.VectorOffset + i * 16 + 8) == lane,
                    "q" + i + " lane1", ref failure);
            }
            return Report("full-frame", function, interception, failure, log);
        }

        private static bool RunSkipAndExecute(TestFunction function, ulong page, IntPtr result,
            ModLogger log)
        {
            Clear(result);
            var calls = 0;
            InterceptHandler handler = _ => calls++ == 0;
            if (!TryInstall(function, page, "skip-execute", handler, log, out var interception))
                return false;

            Invoke(function, result);
            var first = ReadUInt64(result, 0);
            Invoke(function, result);
            var second = ReadUInt64(result, 0);

            var failure = (string)null;
            Expect(calls == 2, "handler calls=" + calls, ref failure);
            Expect(first == 0, "skip result=" + first, ref failure);
            Expect(second == 100, "execute result=" + second, ref failure);
            if (interception.RelocationStrategy != function.ExpectedStrategy)
                failure = failure ?? "strategy=" + interception.RelocationStrategy;
            return Report("skip/execute", function, interception, failure, log);
        }

        private static bool RunLinkRegister(TestFunction function, ulong page, IntPtr result,
            ModLogger log, string kind)
        {
            Clear(result);
            var calls = 0;
            InterceptHandler handler = _ =>
            {
                calls++;
                return false;
            };
            if (!TryInstall(function, page, kind + "-lr", handler, log, out var interception))
                return false;
            Invoke(function, result);
            var observed = ReadUInt64(result, 0);
            var expected = function.Site + 4;
            var failure = (string)null;
            Expect(calls == 1, "handler calls=" + calls, ref failure);
            Expect(observed == expected, $"LR=0x{observed:x} expected=0x{expected:x}",
                ref failure);
            if (interception.RelocationStrategy != function.ExpectedStrategy)
                failure = failure ?? "strategy=" + interception.RelocationStrategy;
            return Report(kind + " LR", function, interception, failure, log);
        }

        private static bool RunConditional(TestFunction function, ulong page, IntPtr result,
            ModLogger log, string kind, ulong expected)
        {
            Clear(result);
            var calls = 0;
            InterceptHandler handler = _ =>
            {
                calls++;
                return false;
            };
            if (!TryInstall(function, page, kind + "-cond", handler, log, out var interception))
                return false;
            Invoke(function, result);
            var observed = ReadUInt64(result, 0);
            var failure = (string)null;
            Expect(calls == 1, "handler calls=" + calls, ref failure);
            Expect(observed == expected, $"result={observed} expected={expected}", ref failure);
            if (interception.RelocationStrategy != function.ExpectedStrategy)
                failure = failure ?? "strategy=" + interception.RelocationStrategy;
            return Report(kind + " after callback", function, interception, failure, log);
        }

        private static bool RunLiteral(TestFunction function, ulong page, IntPtr result,
            ModLogger log, string kind)
        {
            Clear(result);
            var calls = 0;
            InterceptHandler handler = _ =>
            {
                calls++;
                return false;
            };
            if (!TryInstall(function, page, "ldr-" + kind, handler, log, out var interception))
                return false;
            Invoke(function, result);
            var failure = (string)null;
            Expect(calls == 1, "handler calls=" + calls, ref failure);
            if (kind == "s")
            {
                Expect(unchecked((uint)Marshal.ReadInt32(result, 0)) == 0x3FC00000u,
                    "s0=0x" + Marshal.ReadInt32(result, 0).ToString("x8"), ref failure);
            }
            else if (kind == "d")
            {
                Expect(ReadUInt64(result, 0) == 0x4004000000000000UL,
                    "d0=0x" + ReadUInt64(result, 0).ToString("x16"), ref failure);
            }
            else
            {
                var bytes = new byte[16];
                Marshal.Copy(result, bytes, 0, 16);
                var expected = new byte[]
                {
                    0xef, 0xbe, 0xad, 0xde, 0x67, 0x45, 0x23, 0x01,
                    0xef, 0xcd, 0xab, 0x89, 0x98, 0xba, 0xdc, 0xfe
                };
                for (var i = 0; i < 16; i++)
                    Expect(bytes[i] == expected[i], "q0 byte" + i, ref failure);
            }
            if (interception.RelocationStrategy != function.ExpectedStrategy)
                failure = failure ?? "strategy=" + interception.RelocationStrategy;
            return Report("ldr " + kind, function, interception, failure, log);
        }

        private static bool TryInstall(TestFunction function, ulong page, string siteId,
            InterceptHandler handler, ModLogger log, out MidBodyInterception interception)
        {
            interception = null;
            var profile = new CodeSiteProfile(
                BuildId,
                ProfileName,
                function.Base,
                (uint)(function.Base - page),
                (uint)function.SiteOffset,
                (uint)function.Bytes.Length,
                Owner,
                siteId,
                Array.AsReadOnly(function.Words),
                Array.AsReadOnly(function.Bytes));
            if (!MidBodyIntercept.TryInstall(profile, BuildId, handler, true,
                    out interception, out var diagnostics, log))
            {
                log.Warn($"[p1] FAIL install {siteId}: " + string.Join("; ", diagnostics));
                return false;
            }
            log.Info($"[p1] installed {siteId}: site=0x{function.Site:x} " +
                     $"stub=0x{interception.StubPage:x} strategy={interception.RelocationStrategy}");
            return true;
        }

        private static bool Report(string name, TestFunction function,
            MidBodyInterception interception, string failure, ModLogger log)
        {
            if (failure == null)
            {
                log.Info($"[p1] PASS {name}: site=0x{function.Site:x} " +
                         $"strategy={interception.RelocationStrategy}");
                return true;
            }
            log.Warn($"[p1] FAIL {name}: {failure} (site=0x{function.Site:x} " +
                     $"strategy={interception.RelocationStrategy})");
            return false;
        }

        private static void Invoke(TestFunction function, IntPtr result)
        {
            var target = Marshal.GetDelegateForFunctionPointer<TargetFunction>(
                (IntPtr)unchecked((long)function.Base));
            target(result);
        }

        private static void Clear(IntPtr result) =>
            Marshal.Copy(ZeroBuffer, 0, result, ResultBytes);

        private static ulong ReadUInt64(IntPtr address, int offset) =>
            unchecked((ulong)Marshal.ReadInt64(address, offset));

        private static void ExpectGpr(IntPtr frame, int index, ulong expected, ref string error) =>
            Expect(InterceptFrame.GetGpr(frame, index) == expected, "frame x" + index,
                ref error);

        private static void Expect(bool condition, string message, ref string error)
        {
            if (!condition && error == null) error = message;
        }

        private static ulong GprConstant(int index) =>
            0x1111000000000000UL + (ulong)index * 0x10101010101UL;

        private static ulong VectorLane(int index)
        {
            var value = 0UL;
            var pattern = (ulong)(byte)(0xA0 + index);
            for (var shift = 0; shift < 64; shift += 8) value |= pattern << shift;
            return value;
        }

        private static Arm64ImmediateMemoryAccessor Sp(int offset) =>
            new Arm64ImmediateMemoryAccessor(SP, offset);

        private static Arm64ImmediateMemoryAccessor Mem(Arm64RegisterX register, int offset) =>
            new Arm64ImmediateMemoryAccessor(register, offset);

        private static Arm64SystemRegister Sys(Arm64SystemRegisterKnownId id) =>
            (Arm64SystemRegister)id;

        private static void MovConst(Arm64Assembler assembler, Arm64RegisterX register,
            ulong value)
        {
            assembler.MOVZ(register, (ushort)(value & 0xFFFF));
            assembler.MOVK(register, (ushort)((value >> 16) & 0xFFFF), default, 16);
            assembler.MOVK(register, (ushort)((value >> 32) & 0xFFFF), default, 32);
            assembler.MOVK(register, (ushort)((value >> 48) & 0xFFFF), default, 48);
        }
    }
}
