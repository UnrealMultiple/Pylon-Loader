using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Operations;
using System.Collections.Immutable;

namespace MonoMod.SourceGen.Internal.Analyzers
{
    [DiagnosticAnalyzer(LanguageNames.CSharp)]
    public sealed class DoNotSizeofGenerics : DiagnosticAnalyzer
    {
        private static readonly DiagnosticDescriptor DoNotSizeofGeneric = new(
            "MMA002",
            "Do not use the sizeof() operator on a generic parameter",
            "On some old Mono runtimes, sizeof(T) always returns sizeof(IntPtr). See docs/RuntimeIssueNotes.md.",
            "RuntimeIssues",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        private static readonly DiagnosticDescriptor DoNotUseUnsafeSizeOf = new(
            "MMA003",
            "Do not use Unsafe.SizeOf<T>()",
            "On some old Mono runtimes, the sizeof opcode always returns sizeof(IntPtr) on generic parameters, " +
                "which Unsafe.SizeOf<T>() always has.. See docs/RuntimeIssueNotes.md.",
            "RuntimeIssues",
            DiagnosticSeverity.Warning,
            isEnabledByDefault: true);

        public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics
            => ImmutableArray.Create(DoNotSizeofGeneric, DoNotUseUnsafeSizeOf);

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1062:Validate arguments of public methods",
            Justification = "Roslyn always passes a non-null context")]
        public override void Initialize(AnalysisContext context)
        {
            context.EnableConcurrentExecution();
            context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.ReportDiagnostics);

            // normal sizeof operator
            context.RegisterOperationAction(ctx =>
            {
                var sizeofOp = (ISizeOfOperation)ctx.Operation;

                if (sizeofOp.TypeOperand.TypeKind is TypeKind.TypeParameter)
                {
                    ctx.ReportDiagnostic(Diagnostic.Create(DoNotSizeofGeneric, sizeofOp.Syntax.GetLocation()));
                }
            }, OperationKind.SizeOf);
        }
    }
}
