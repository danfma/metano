using Metano.Annotations;
using Metano.Compiler.Extraction;
using Metano.Compiler.TypeScript.Analysis;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Transformation.Json;

/// <summary>
/// The <c>test</c> a polymorphic descriptor stores for a structural
/// <c>[PlainObject]</c> case (FR-019 rule 1, FR-041): the runtime-test
/// bridge's test of the type as a function value (spec 011 NFR-004), so a
/// JSON case, a type pattern and a dispatcher branch test the same way. For
/// a plain object that is its synthesized guard: <c>isPoint</c>, imported
/// from the type's module or package; <c>Shape.isCircle</c> for a nested
/// type, whose guard is declared in the companion namespace.
/// </summary>
internal sealed class JsonGuardReferences(TypeScriptTransformContext context)
{
    private readonly TypeScriptTransformContext _context = context;

    /// <summary>
    /// The JSON contract's structural guards are emitted by their own set
    /// (<see cref="TypeScriptTransformContext.StructuralGuardTypes"/>), not
    /// asked of the runtime type reachability, so the test reads them unchecked.
    /// </summary>
    public TsExpression GuardReferenceOf(INamedTypeSymbol type) =>
        IrToTsRuntimeTestBridge.TestFunction(
            IrTypeRefMapper.Map(type, _context.OriginResolver, TargetLanguage.TypeScript),
            RuntimeTypeReachabilityResult.Unchecked
        )
        ?? throw new InvalidOperationException(
            $"Internal error: the structural JSON case '{type.ToDisplayString()}' has no "
                + "runtime type test (spec 011 NFR-004)."
        );
}
