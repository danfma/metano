using Metano.Annotations;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Which primary-constructor parameters of a type are promoted to the
/// property that only exposes them (<c>constructor(readonly a: number)</c>),
/// decided once per type and consumed by both the constructor extraction
/// (which folds the pair) and the capture scan (which rewrites member-body
/// reads), so the two never disagree.
/// <para>
/// A parameter is promoted when it has an alias property
/// (<see cref="PrimaryConstructorPropertyAlias.AliasPropertyOf"/>) and nothing
/// in the type writes it (<see cref="WriteAccess"/>). A written parameter is
/// not promoted: C# keeps its storage apart from the property's, so the
/// property keeps its own declaration and the parameter is captured into a
/// mutable field. The exception is a record's positional property, which
/// C# assigns before any initializer can write the parameter
/// (<see cref="IsPromotable"/>).
/// </para>
/// <para>
/// Known gap: a write inside the base-constructor arguments
/// (<c>: B(a++)</c>) runs after the derived initializers in C#, which run
/// before the base constructor; TypeScript must call <c>super(...)</c>
/// first, so the order differs.
/// </para>
/// </summary>
internal sealed class PrimaryConstructorPromotion
{
    private static readonly PrimaryConstructorPromotion None = new(
        new Dictionary<ISymbol, IPropertySymbol>(SymbolEqualityComparer.Default),
        new HashSet<ISymbol>(SymbolEqualityComparer.Default)
    );

    private readonly IReadOnlyDictionary<ISymbol, IPropertySymbol> _promoted;
    private readonly IReadOnlySet<ISymbol> _written;

    private PrimaryConstructorPromotion(
        IReadOnlyDictionary<ISymbol, IPropertySymbol> promoted,
        IReadOnlySet<ISymbol> written
    )
    {
        _promoted = promoted;
        _written = written;

        ReadThroughProperties = promoted
            .Where(entry => CannotDiverge(entry.Value))
            .ToDictionary(
                entry => entry.Key,
                entry => entry.Value,
                (IEqualityComparer<ISymbol>)SymbolEqualityComparer.Default
            );
    }

    /// <summary>
    /// The promoted parameters whose member-body reads may go through the
    /// property, keyed to it: the property can never diverge from the
    /// parameter, because it has no setter (an <c>init</c> accessor counts,
    /// since an object initializer changes the property but not the
    /// parameter) and no override can replace its getter (it is neither
    /// <c>virtual</c> nor <c>abstract</c>). Every other read parameter keeps
    /// its own storage, as C# does (warning CS9124).
    /// </summary>
    public IReadOnlyDictionary<ISymbol, IPropertySymbol> ReadThroughProperties { get; }

    /// <summary>The property <paramref name="parameter"/> is promoted to, if any.</summary>
    public IPropertySymbol? PromotedPropertyOf(IParameterSymbol parameter) =>
        _promoted.GetValueOrDefault(parameter);

    /// <summary>Something in the type writes <paramref name="parameter"/>.</summary>
    public bool IsWritten(IParameterSymbol parameter) => _written.Contains(parameter);

    public static PrimaryConstructorPromotion For(
        INamedTypeSymbol type,
        Compilation? compilation,
        TargetLanguage? target
    )
    {
        var primaryCtor = type.Constructors.FirstOrDefault(
            IrConstructorExtractor.IsPrimaryConstructor
        );

        if (compilation is null || primaryCtor is null || primaryCtor.Parameters.IsEmpty)
            return None;

        var written = WrittenParameters(type, primaryCtor, compilation);
        var promoted = new Dictionary<ISymbol, IPropertySymbol>(SymbolEqualityComparer.Default);

        foreach (var parameter in primaryCtor.Parameters)
        {
            if (
                PrimaryConstructorPropertyAlias.AliasPropertyOf(parameter, compilation, target)
                    is { } property
                && IsPromotable(property, written.Contains(parameter))
            )
                promoted[parameter] = property;
        }

        return new PrimaryConstructorPromotion(promoted, written);
    }

    /// <summary>
    /// An alias property is promoted unless the parameter is written, so the
    /// two may diverge. A record's positional property is promoted even then:
    /// C# assigns it the original argument before any initializer runs (the
    /// parameter property does the same), and only an initializer can write
    /// the parameter, since a member body's <c>X</c> is the property. That
    /// initializer keeps the bare constructor parameter.
    /// </summary>
    private static bool IsPromotable(IPropertySymbol aliasProperty, bool parameterWritten) =>
        !parameterWritten
        || PrimaryConstructorPropertyAlias.IsPositionalRecordProperty(aliasProperty);

    private static bool CannotDiverge(IPropertySymbol property) =>
        property is { SetMethod: null, IsVirtual: false, IsAbstract: false };

    /// <summary>
    /// The primary-constructor parameters written anywhere in the type's
    /// declarations: member bodies, initializers (and their lambdas) and the
    /// base-constructor arguments.
    /// </summary>
    private static HashSet<ISymbol> WrittenParameters(
        INamedTypeSymbol type,
        IMethodSymbol primaryCtor,
        Compilation compilation
    )
    {
        var written = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        foreach (var declaration in type.DeclaringSyntaxReferences.Select(r => r.GetSyntax()))
        {
            var model = compilation.GetSemanticModel(declaration.SyntaxTree);

            var writes = declaration
                .DescendantNodes()
                .OfType<IdentifierNameSyntax>()
                .Where(identifier => IsWriteOfParameterOf(primaryCtor, identifier, model))
                .Select(identifier => model.GetSymbolInfo(identifier).Symbol)
                .OfType<IParameterSymbol>();

            written.UnionWith(writes);
        }

        return written;
    }

    private static bool IsWriteOfParameterOf(
        IMethodSymbol constructor,
        IdentifierNameSyntax identifier,
        SemanticModel model
    ) =>
        model.GetSymbolInfo(identifier).Symbol is IParameterSymbol parameter
        && SymbolEqualityComparer.Default.Equals(parameter.ContainingSymbol, constructor)
        && WriteAccess.IsWriteTarget(identifier, model);
}
