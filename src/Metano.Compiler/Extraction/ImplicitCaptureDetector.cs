using Metano.Annotations;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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
    /// <param name="PromotedParams">Parameters folded into the property that only exposes them (<see cref="PrimaryConstructorPropertyAlias"/>), keyed to that property. That property is the only storage the backends emit for the parameter, so a member body that reads the parameter reads the property.</param>
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
        TargetLanguage? target,
        bool initializersRunInConstructor
    )
    {
        if (compilation is null)
            return Empty;

        var primaryCtor = type.Constructors.FirstOrDefault(IsPrimaryConstructor);
        if (primaryCtor is null || primaryCtor.Parameters.Length == 0)
            return Empty;

        var primaryParams = new Dictionary<ISymbol, IParameterSymbol>(
            SymbolEqualityComparer.Default
        );

        var promotionCandidates = new Dictionary<ISymbol, IPropertySymbol>(
            SymbolEqualityComparer.Default
        );

        foreach (var param in primaryCtor.Parameters)
        {
            // Parameters that match a public property of the type are
            // already promoted to that property by C# 12's positional-
            // record / primary-ctor surface; the existing constructor
            // bridge handles them end-to-end. Synthesizing a private
            // backing field would duplicate state and surface as
            // double-emitted fields in regenerated samples, so a member
            // body reads the property instead — when the two can never
            // diverge (see PromotedReads).
            if (FindPromotedProperty(param, type, compilation, target) is { } property)
                promotionCandidates[param] = property;
            else
                primaryParams[param] = param;
        }

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
        var promotedReads = new PromotedReads(promotionCandidates);

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

                var insideInitializer = IsInsideMemberInitializer(identifier);

                if (initializersRunInConstructor && insideInitializer)
                    continue;

                var resolved = model.GetSymbolInfo(identifier).Symbol;

                if (!insideInitializer)
                    promotedReads.Record(resolved, identifier);

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

        // A promoted parameter whose property can diverge from it (the
        // property has a setter, or a member writes the parameter) keeps its
        // own storage, as C# does: it is captured like any other parameter.
        captured.UnionWith(promotedReads.Diverging);

        var promotedParams = promotedReads.Aliased();

        if (captured.Count == 0)
            return Empty with { PromotedParams = promotedParams };

        var paramFieldMap = new Dictionary<ISymbol, string>(SymbolEqualityComparer.Default);
        var synthesized = new List<IrFieldDeclaration>(captured.Count);
        foreach (var symbol in captured)
        {
            if (symbol is not IParameterSymbol paramSymbol)
                continue;

            var fieldName = SynthesizeFieldName(paramSymbol.Name);

            // Hand-written field with the same name preempts the
            // synthesized backing field — silently skip the
            // synthesis. The bare param reference inside member
            // bodies stays unrewritten and the consumer's TS
            // compiler surfaces the missing identifier. Surfacing a
            // dedicated diagnostic would require threading
            // <see cref="MetanoDiagnostic"/> up from the extractor;
            // tracked as a follow-up once the extraction pipeline
            // gains a diagnostic sink.
            if (existingMemberNames.Contains(fieldName))
                continue;

            existingMemberNames.Add(fieldName);
            paramFieldMap[paramSymbol] = fieldName;
            synthesized.Add(
                new IrFieldDeclaration(
                    Name: fieldName,
                    Visibility: IrVisibility.Private,
                    IsStatic: false,
                    Type: IrTypeRefMapper.Map(paramSymbol.Type, originResolver),
                    IsReadonly: true,
                    Initializer: null,
                    IsCapturedByCtor: true
                )
            );
        }

        return new Result(paramFieldMap, synthesized, promotedParams);
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
    /// The property a primary-ctor parameter is promoted to, if any: a
    /// property of the type — the C# 12 positional-record /
    /// primary-ctor rule that pairs <c>(string name)</c> with the
    /// type's <c>Name</c> property. Promoted params already have a
    /// public storage slot; synthesizing a separate private field
    /// would duplicate state. Match is case-insensitive to mirror
    /// <c>IrConstructorExtractor.ResolvePromotionInfo</c>, which
    /// also pairs camelCase params with PascalCase properties, and — like
    /// it — only an emitted property
    /// (<see cref="PrimaryConstructorPropertyAlias.IsPromotionCandidate"/>)
    /// that merely exposes the parameter
    /// (<see cref="PrimaryConstructorPropertyAlias.IsAliasOf"/>) counts.
    /// </summary>
    private static IPropertySymbol? FindPromotedProperty(
        IParameterSymbol param,
        INamedTypeSymbol type,
        Compilation compilation,
        TargetLanguage? target
    ) =>
        type.GetMembers()
            .OfType<IPropertySymbol>()
            .FirstOrDefault(property =>
                string.Equals(property.Name, param.Name, StringComparison.OrdinalIgnoreCase)
                && PrimaryConstructorPropertyAlias.IsPromotionCandidate(property, target)
                && PrimaryConstructorPropertyAlias.IsAliasOf(property, param, compilation)
            );

    /// <summary>
    /// The member-body references (outside initializers) to the promoted
    /// parameters, sorted by whether a read may go through the promoted
    /// property. It may when the two can never diverge: the property has no
    /// setter (an <c>init</c> one counts, since an object initializer changes
    /// the property but not the parameter) and no member writes the
    /// parameter. Otherwise C# keeps separate storage for the parameter
    /// (warning CS9124), and so must the emitted code.
    /// </summary>
    private sealed class PromotedReads(IReadOnlyDictionary<ISymbol, IPropertySymbol> candidates)
    {
        private readonly HashSet<ISymbol> _read = new(SymbolEqualityComparer.Default);
        private readonly HashSet<ISymbol> _written = new(SymbolEqualityComparer.Default);

        /// <summary>The referenced parameters that need their own storage.</summary>
        public IEnumerable<ISymbol> Diverging => _read.Where(param => !CanReadProperty(param));

        public void Record(ISymbol? resolved, IdentifierNameSyntax identifier)
        {
            if (resolved is not IParameterSymbol param || !candidates.ContainsKey(param))
                return;

            _read.Add(param);

            if (IsWriteTarget(identifier))
                _written.Add(param);
        }

        /// <summary>The parameters whose reads go through the promoted property.</summary>
        public IReadOnlyDictionary<ISymbol, IPropertySymbol> Aliased() =>
            candidates
                .Where(entry => _read.Contains(entry.Key) && CanReadProperty(entry.Key))
                .ToDictionary(
                    entry => entry.Key,
                    entry => entry.Value,
                    (IEqualityComparer<ISymbol>)SymbolEqualityComparer.Default
                );

        private bool CanReadProperty(ISymbol param) =>
            candidates[param].SetMethod is null && !_written.Contains(param);

        private static bool IsWriteTarget(IdentifierNameSyntax identifier) =>
            identifier.Parent switch
            {
                AssignmentExpressionSyntax assignment => assignment.Left == identifier,
                PrefixUnaryExpressionSyntax prefix => prefix.IsKind(
                    SyntaxKind.PreIncrementExpression
                ) || prefix.IsKind(SyntaxKind.PreDecrementExpression),
                PostfixUnaryExpressionSyntax postfix => postfix.IsKind(
                    SyntaxKind.PostIncrementExpression
                ) || postfix.IsKind(SyntaxKind.PostDecrementExpression),
                ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                    && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword),
                _ => false,
            };
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
