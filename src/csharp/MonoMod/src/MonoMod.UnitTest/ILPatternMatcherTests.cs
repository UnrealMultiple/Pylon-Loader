using MonoMod.Cil;
using MonoMod.Utils;
using System.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace MonoMod.UnitTest
{
    public sealed class ILPatternMatcherTests : TestBase
    {
        public ILPatternMatcherTests(ITestOutputHelper helper) : base(helper)
        {
        }

        [Fact]
        public void ILMatcherDoesNotThrowMatchingDynamicMethodRef()
        {
            MethodInfo dm;
            using (var dmd1 = new DynamicMethodDefinition("Test DM 1", typeof(void), []))
            {
                using var ilctx = new ILContext(dmd1.Definition);
                var il = new ILCursor(ilctx);

                il.EmitRet();

                dm = dmd1.Generate();
            }

            using (var dmd2 = new DynamicMethodDefinition("Test DM 2", typeof(void), []))
            {
                using var ilctx = new ILContext(dmd2.Definition);
                var il = new ILCursor(ilctx);

                il.EmitCall(dm);
                // also emit with ilprocessor directly
                ilctx.IL.Emit(Mono.Cecil.Cil.OpCodes.Call, dm);
                il.EmitRet();

                // now for the actual test: lets try to match that call against a Console.WriteLine
                il.Goto(0);
                Assert.False(il.TryGotoNext(i => i.MatchCallOrCallvirt(typeof(System.Console), "WriteLine")));

                // DM should also be compilable
                _ = dmd2.Generate();
            }
        }
    }
}
