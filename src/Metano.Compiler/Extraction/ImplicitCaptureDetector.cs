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
/// This detector walks the class's syntax once before member extraction
/// runs, semantically resolves identifier references inside non-ctor
/// members, and produces:
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
    /// <param name="ParamFieldMap">Captured parameters keyed to their synthesized backing field.</param>
    /// <param name="SynthesizedFields">The backing fields to append to the class's members.</param>
    /// <param name="PromotedParams">The promoted parameters whose member-body reads go through their property (<see cref="PrimaryConstructorPromotion.ReadThroughProperties"/>), keyed to it. Any other parameter a member body reads is captured into <paramref name="ParamFieldMap"/>.</param>
    internal sealed record Result(
        IReadOnlyDictionary<ISymbol, string> ParamFieldMap,
        IReadOnlyList<IrFieldDeclaration> SynthesizedFields,
        IReadOnlyDictionary<ISymbol, IPropertySymbol> PromotedParams
    )
    {
        /// <summary>No parameter read needs rewriting in the member bodies.</summary>
        public bool HasNoRewrites => ParamFieldMap.Count == 0 && PromotedParams.Count == 0;
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

        var primaryCtor = type.Constructors.FirstOrDefault(IsPrimaryConstructor);
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

        var promotedOnly = Empty with { PromotedParams = readThroughProperties };

        if (primaryParams.Count == 0)
            return promotedOnly;

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

        var primaryCtorSyntaxNodes = CollectPrimaryCtorSyntaxNodes(type);
        // TypeScript shares one namespace across fields, properties and
        // methods, so the collision check has to consider every
        // member's name — not just fields — or the synthesizer would
        // happily emit a private field that shadows a public method.
        var existingMemberNames = type.GetMembers()
            .Select(m => m.Name)
            .ToHashSet(StringComparer.Ordinal);
        var captured = new HashSet<ISymbol>(SymbolEqualityComparer.Default);

        // Walk only the members of THIS type (skip nested types); C# 12
        // primary-ctor parameters are not in scope for a nested type,
        // so an identifier inside a nested type that happens to share a
        // parameter name must not be misclassified as a capture.
        foreach (var memberSyntax in EnumerateMemberSyntaxNodes(type))
        {
            var model = compilation.GetSemanticModel(memberSyntax.SyntaxTree);
            foreach (
                var identifier in memberSyntax.DescendantNodes().OfType<IdentifierNameSyntax>()
            )
            {
                if (IsInsidePrimaryCtor(identifier, primaryCtorSyntaxNodes))
                    continue;

                if (initializersRunInConstructor && IsInsideMemberInitializer(identifier))
                    continue;

                var resolved = model.GetSymbolInfo(identifier).Symbol;

                if (
                    resolved is IParameterSymbol paramSymbol
                    && primaryParams.ContainsKey(paramSymbol)
                    && !explicitlyCapturedParams.Contains(paramSymbol)
                )
                {
                    captured.Add(paramSymbol);
                }
            }
        }

        if (captured.Count == 0)
            return promotedOnly;

        var paramFieldMap = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var synthesized = new List<IrFieldDeclaration>(captured.Count);
        foreach (var symbol in captured)
        {
            if (symbol is not IParameterSymbol paramSymbol)
                continue;

            // A member of the same name (a hand-written `_a`) moves the
            // synthesized field to the next free name (`_a2`, `_a3`, …).
            var fieldName = FreeFieldName(paramSymbol.Name, existingMemberNames);

            existingMemberNames.Add(fieldName);
            paramFieldMap[paramSymbol] = fieldName;
            synthesized.Add(
                new IrFieldDeclaration(
                    Name: fieldName,
                    Visibility: IrVisibility.Private,
                    IsStatic: false,
                    Type: IrTypeRefMapper.Map(paramSymbol.Type, originResolver),
                    IsReadonly: !promotion.IsWritten(paramSymbol),
                    Initializer: null,
                    IsCapturedByCtor: true
                )
            );
        }

        return new Result(paramFieldMap, synthesized, readThroughProperties);
    }

    /// <summary>
    /// True for the synthesized C# 12+ primary constructor — the
    /// instance constructor whose declaring syntax is the type
    /// declaration itself rather than a separate
    /// <c>ConstructorDeclarationSyntax</c>.
    /// </summary>
    private static bool IsPrimaryConstructor(IMethodSymbol ctor)
    {
        if (ctor.MethodKind != MethodKind.Constructor || ctor.IsStatic)
            return false;

        var syntax = ctor.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax();
        return syntax
            is ClassDeclarationSyntax
                or StructDeclarationSyntax
                or RecordDeclarationSyntax;
    }

    /// <summary>
    /// Collects parameters already captured by an explicit field
    /// initializer of the form <c>= paramName</c>, so the detector
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

    /// <summary>
    /// Returns the syntax nodes of every direct member of the type
    /// (fields, properties, methods, events, accessors) so the
    /// detector can iterate identifier references without descending
    /// into nested types — primary-ctor parameters are not in scope
    /// for a nested type's body.
    /// </summary>
    private static IEnumerable<SyntaxNode> EnumerateMemberSyntaxNodes(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers())
        {
            if (member is INamedTypeSymbol)
                continue;
            foreach (var declaringRef in member.DeclaringSyntaxReferences)
                yield return declaringRef.GetSyntax();
        }
    }

    /// <summary>
    /// Returns the syntax nodes that delimit the primary
    /// constructor's body — the type's parameter list and any base
    /// initializer arguments, the only places where primary-ctor
    /// parameters are in scope as plain locals. Identifier
    /// references inside these nodes do not need rewriting.
    /// </summary>
    private static IReadOnlyList<SyntaxNode> CollectPrimaryCtorSyntaxNodes(INamedTypeSymbol type)
    {
        var nodes = new List<SyntaxNode>();
        foreach (var declaringRef in type.DeclaringSyntaxReferences)
        {
            var syntax = declaringRef.GetSyntax();
            switch (syntax)
            {
                case ClassDeclarationSyntax cls:
                    if (cls.ParameterList is not null)
                        nodes.Add(cls.ParameterList);
                    AddPrimaryBaseArguments(cls.BaseList, nodes);
                    break;
                case StructDeclarationSyntax st:
                    if (st.ParameterList is not null)
                        nodes.Add(st.ParameterList);
                    AddPrimaryBaseArguments(st.BaseList, nodes);
                    break;
                case RecordDeclarationSyntax rec:
                    if (rec.ParameterList is not null)
                        nodes.Add(rec.ParameterList);
                    AddPrimaryBaseArguments(rec.BaseList, nodes);
                    break;
            }
        }
        return nodes;
    }

    private static void AddPrimaryBaseArguments(BaseListSyntax? baseList, List<SyntaxNode> nodes)
    {
        var primaryBase = baseList
            ?.Types.OfType<PrimaryConstructorBaseTypeSyntax>()
            .FirstOrDefault();
        if (primaryBase?.ArgumentList is not null)
            nodes.Add(primaryBase.ArgumentList);
    }

    private static bool IsInsidePrimaryCtor(
        SyntaxNode identifier,
        IReadOnlyList<SyntaxNode> primaryCtorScopes
    )
    {
        // `Span.Contains` only makes sense within a single `SyntaxTree`
        // — partial types declared across multiple files share the same
        // INamedTypeSymbol but live in distinct trees, so an identifier
        // in file A could spuriously match a primary-ctor scope in
        // file B. Compare both span and tree identity to avoid the
        // false positive.
        foreach (var scope in primaryCtorScopes)
        {
            if (scope.SyntaxTree == identifier.SyntaxTree && scope.Span.Contains(identifier.Span))
                return true;
        }
        return false;
    }

    /// <summary>
    /// True inside a field or property initializer. When the class extractor
    /// moves the initializers into the constructor body
    /// (<see cref="PrimaryConstructorInitializerDeferral.AppliesTo"/>), the
    /// parameter is in scope there, so a read captures nothing (as in C#,
    /// which only captures parameters read by members that outlive
    /// construction).
    /// </summary>
    private static bool IsInsideMemberInitializer(SyntaxNode identifier) =>
        MemberInitializerSyntax.Contains(identifier, stopAtExecutableBodies: false);

    /// <summary>
    /// <see cref="SynthesizeFieldName"/>'s name for <paramref name="paramName"/>,
    /// or, when a member already has it, the first free one with a numeric
    /// suffix, so the synthesized field never shadows a member.
    /// </summary>
    private static string FreeFieldName(string paramName, IReadOnlySet<string> takenNames)
    {
        var baseName = SynthesizeFieldName(paramName);

        return Enumerable
            .Range(2, int.MaxValue - 2)
            .Select(suffix => baseName + suffix)
            .Prepend(baseName)
            .First(name => !takenNames.Contains(name));
    }

    /// <summary>
    /// Picks the synthesized field name from a primary-ctor param's
    /// source name. The convention matches the rest of the code base:
    /// camelCase parameter prefixed with an underscore
    /// (<c>view</c> → <c>_view</c>, <c>onClick</c> → <c>_onClick</c>).
    /// Reserved JS keywords already get their reserved-word treatment
    /// downstream when the backend renders the field, so the leading
    /// underscore is enough here.
    /// </summary>
    private static string SynthesizeFieldName(string paramName)
    {
        if (string.IsNullOrEmpty(paramName))
            return "_value";
        var first = char.ToLowerInvariant(paramName[0]);
        return paramName.Length == 1 ? "_" + first : "_" + first + paramName[1..];
    }
}
