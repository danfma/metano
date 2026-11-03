using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The identifier walk of the primary-constructor capture scan: resolves
/// every identifier of a type's members (nested types excluded) and collects
/// the primary-constructor parameters the member bodies read — the ones
/// Roslyn captures into hidden backing fields.
/// <para>
/// Reads inside the primary constructor's own scopes (the type's parameter
/// list and base arguments) and inside member initializers that run in the
/// constructor body see the parameter as a plain local, so they capture
/// nothing. Parameters already captured by an explicit field initializer are
/// skipped: the existing <c>AnnotateCapturedParams</c> pass wires the user's
/// field.
/// </para>
/// </summary>
internal static class PrimaryCtorCaptureWalker
{
    public static HashSet<IParameterSymbol> CapturedParameters(
        INamedTypeSymbol type,
        Compilation compilation,
        IReadOnlyDictionary<ISymbol, IParameterSymbol> primaryParams,
        IReadOnlySet<IParameterSymbol> explicitlyCapturedParams,
        bool initializersRunInConstructor
    )
    {
        var captured = new HashSet<IParameterSymbol>(SymbolEqualityComparer.Default);
        var primaryCtorSyntaxNodes = CollectPrimaryCtorSyntaxNodes(type);

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

        return captured;
    }

    /// <summary>
    /// Returns the syntax nodes of every direct member of the type
    /// (fields, properties, methods, events, accessors) so the walk can
    /// iterate identifier references without descending into nested types —
    /// primary-ctor parameters are not in scope for a nested type's body.
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
    /// Returns the syntax nodes that delimit the primary constructor's body
    /// — the type's parameter list and any base initializer arguments, the
    /// only places where primary-ctor parameters are in scope as plain
    /// locals. Identifier references inside these nodes do not need
    /// rewriting.
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
}
