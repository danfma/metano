using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Detects primary-constructor parameters that are captured implicitly
/// by a class's member bodies. C# 12 primary constructors expose their
/// parameters to every member in the type; when a non-constructor
/// member references one, Roslyn synthesizes a hidden backing field
/// to hold the value. Without the same synthesis on our side the
/// emitted TypeScript drops the parameter from the ctor signature
/// and references an out-of-scope identifier inside member bodies.
/// <para>
/// This detector orchestrates the scan: it decides the primary
/// constructor, applies the promotion read-through map and the
/// explicit-capture exemption, and delegates the identifier walk to
/// <see cref="PrimaryCtorCaptureWalker"/> and the field synthesis to
/// <see cref="CapturedFieldSynthesizer"/>. The result is:
/// </para>
/// <list type="bullet">
///   <item>A <c>param-symbol → field-name</c> map that the
///   <see cref="IrExpressionExtractor"/> consults to rewrite captured
///   identifier references into <c>this._field</c> accesses.</item>
///   <item>A list of synthesized <see cref="IrFieldDeclaration"/>
///   entries to append to the class's member list, one per captured
///   param that lacks an explicit user-written field initializer.</item>
/// </list>
/// <para>
/// Parameters captured by an explicit field initializer
/// (<c>private readonly Foo _foo = foo;</c>) keep going through the
/// existing <c>AnnotateCapturedParams</c> pass so this detector
/// reuses the user's field name and does not synthesize a duplicate.
/// </para>
/// </summary>
internal static class ImplicitCaptureDetector
{
    /// <param name="CapturedParams">Captured parameters keyed to their synthesized backing field.</param>
    /// <param name="SynthesizedFields">The backing fields to append to the class's members.</param>
    /// <param name="AliasedParams">The parameters whose member-body reads go through the property they alias (<see cref="PrimaryConstructorPromotion.ReadThroughProperties"/>), keyed to it. Any other parameter a member body reads is captured into <paramref name="CapturedParams"/>.</param>
    internal sealed record Result(
        IReadOnlyDictionary<ISymbol, string> CapturedParams,
        IReadOnlyList<IrFieldDeclaration> SynthesizedFields,
        IReadOnlyDictionary<ISymbol, IPropertySymbol> AliasedParams
    )
    {
        /// <summary>Any parameter read needs rewriting in the member bodies.</summary>
        public bool HasRewrites => CapturedParams.Count > 0 || AliasedParams.Count > 0;
    }

    private static readonly Result Empty = new(
        new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default),
        Array.Empty<IrFieldDeclaration>(),
        new Dictionary<ISymbol, IPropertySymbol>(SymbolEqualityComparer.Default)
    );

    public static Result Scan(
        INamedTypeSymbol type,
        Compilation? compilation,
        IrTypeOriginResolver? originResolver,
        PrimaryConstructorPromotion promotion,
        bool initializersRunInConstructor
    )
    {
        if (compilation is null)
            return Empty;

        var primaryCtor = type.Constructors.FirstOrDefault(
            IrConstructorExtractor.IsPrimaryConstructor
        );
        if (primaryCtor is null || primaryCtor.Parameters.Length == 0)
            return Empty;

        // A promoted parameter whose property can never diverge from it is
        // read through that property; synthesizing a private backing field
        // would duplicate state and surface as double-emitted fields in
        // regenerated samples. Every other parameter a member body reads is
        // captured, a promoted one included (C# keeps its storage apart).
        var readThroughProperties = promotion.ReadThroughProperties;

        var primaryParams = primaryCtor
            .Parameters.Where(param => !readThroughProperties.ContainsKey(param))
            .ToDictionary(
                param => (ISymbol)param,
                param => param,
                (IEqualityComparer<ISymbol>)SymbolEqualityComparer.Default
            );

        var aliasedOnly = Empty with { AliasedParams = readThroughProperties };

        if (primaryParams.Count == 0)
            return aliasedOnly;

        // Parameters captured by an explicit field initializer get their
        // synthesis suppressed early — the existing AnnotateCapturedParams
        // pass already wires the field/param pair end-to-end. Symbol-
        // identity match guards against the false positive where an
        // initializer references a static / base member that happens to
        // share the parameter's name.
        var explicitlyCapturedParams = CollectExplicitlyCapturedParams(
            type,
            compilation,
            primaryParams
        );

        var capturedParams = PrimaryCtorCaptureWalker.CapturedParameters(
            type,
            compilation,
            primaryParams,
            explicitlyCapturedParams,
            initializersRunInConstructor
        );

        if (capturedParams.Count == 0)
            return aliasedOnly;

        var (captured, synthesizedFields) = CapturedFieldSynthesizer.Synthesize(
            capturedParams,
            type,
            originResolver,
            promotion
        );

        return new Result(captured, synthesizedFields, readThroughProperties);
    }

    /// <summary>
    /// Collects parameters already captured by an explicit field
    /// initializer of the form <c>= paramName</c>, so the scan
    /// does not synthesize a second backing field for them. Symbol-
    /// identity match (via <see cref="SemanticModel.GetSymbolInfo"/>)
    /// guards against the false positive where the initializer
    /// references a static / base member that happens to share the
    /// parameter's name.
    /// </summary>
    private static HashSet<IParameterSymbol> CollectExplicitlyCapturedParams(
        INamedTypeSymbol type,
        Compilation compilation,
        Dictionary<ISymbol, IParameterSymbol> primaryParams
    )
    {
        var result = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
        {
            if (
                field.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                is not VariableDeclaratorSyntax declaration
            )
                continue;
            if (declaration.Initializer?.Value is not IdentifierNameSyntax initializerId)
                continue;

            var model = compilation.GetSemanticModel(declaration.SyntaxTree);
            if (
                model.GetSymbolInfo(initializerId).Symbol is IParameterSymbol param
                && primaryParams.ContainsKey(param)
            )
                result.Add(param);
        }
        return result;
    }
}
