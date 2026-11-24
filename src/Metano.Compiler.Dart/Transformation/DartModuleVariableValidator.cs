using Metano.Annotations;
using Metano.Compiler.Dart.Bridge;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Dart.Transformation;

/// <summary>
/// Rejects containerless static class module-variable shapes that Dart cannot
/// preserve (#327):
/// <list type="bullet">
///   <item>An explicit static constructor on a containerless static class (no
///     top-level type initialization hook in Dart).</item>
///   <item>A read or write of a module variable whose emitted bare name is
///     shadowed by a parameter, local, range variable, or local function in
///     scope.</item>
/// </list>
/// </summary>
internal sealed class DartModuleVariableValidator(
    Compilation compilation,
    IReadOnlyList<INamedTypeSymbol> transpilableTypes,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    public void Validate()
    {
        foreach (var type in transpilableTypes)
        {
            if (DartTypeShapes.LowersToTopLevelFunctions(type))
                RejectStaticConstructors(type);

            foreach (var root in MemberSyntaxRoots(type))
                ValidateAccessSites(root);
        }
    }

    private void RejectStaticConstructors(INamedTypeSymbol type)
    {
        foreach (var constructor in type.StaticConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
                continue;

            Report(
                $"Containerless static class '{type.Name}' declares a static constructor, "
                    + "which has no module form in Dart: use field or property initializers instead.",
                constructor.Locations.FirstOrDefault()
            );
        }
    }

    private void ValidateAccessSites(SyntaxNode root)
    {
        var model = ModelFor(root.SyntaxTree);

        foreach (var identifier in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (IsInsideNameOf(identifier))
                continue;

            if (
                model.GetSymbolInfo(identifier).Symbol is not { } symbol
                || !IsModuleVariable(symbol)
            )
                continue;

            var emittedName = EmittedName(symbol);

            var shadow = model
                .LookupSymbols(identifier.SpanStart, name: null)
                .Where(IsLocalBinding)
                .FirstOrDefault(local =>
                    IrToDartNamingPolicy.ToParameterName(local.Name) == emittedName
                );

            if (shadow is null)
                continue;

            Report(
                $"'{symbol.ContainingType.Name}.{symbol.Name}' is emitted as the module variable "
                    + $"'{emittedName}', which the local '{shadow.Name}' shadows here. Rename "
                    + "the local or add a [Name(\"...\")] override on the member.",
                identifier.GetLocation()
            );
        }
    }

    private bool IsModuleVariable(ISymbol symbol) =>
        symbol is IFieldSymbol or IPropertySymbol
        && symbol.ContainingType is { } owner
        && DartTypeShapes.LowersToTopLevelFunctions(owner)
        && SymbolHelper.IsToolchainDeclared(owner, compilation.Assembly)
        && IrModuleVariableExtractor.IsModuleState(symbol.OriginalDefinition, TargetLanguage.Dart);

    internal static string EmittedName(ISymbol member)
    {
        var nameOverride = member.GetNameOverride(TargetLanguage.Dart);

        if (nameOverride is not null)
            return nameOverride;

        var camel = IrToDartNamingPolicy.ToMemberName(member.Name, attributes: null);

        return member.DeclaredAccessibility == Accessibility.Private && !camel.StartsWith('_')
            ? "_" + camel
            : camel;
    }

    private static IEnumerable<SyntaxNode> MemberSyntaxRoots(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(member =>
                !member.IsImplicitlyDeclared
                && member is not INamedTypeSymbol
                && member is not IMethodSymbol { AssociatedSymbol: not null }
                && !SymbolHelper.HasIgnore(member, TargetLanguage.Dart)
            )
            .SelectMany(member => member.DeclaringSyntaxReferences)
            .Select(reference => reference.GetSyntax())
            .SelectMany<SyntaxNode, SyntaxNode>(syntax =>
                syntax is TypeDeclarationSyntax typeDecl
                    ? new SyntaxNode?[]
                    {
                        typeDecl.ParameterList,
                        typeDecl.BaseList,
                    }.OfType<SyntaxNode>()
                    : [syntax]
            );

    private static bool IsLocalBinding(ISymbol symbol) =>
        symbol
            is ILocalSymbol
                or IParameterSymbol
                or IRangeVariableSymbol
                or IMethodSymbol { MethodKind: MethodKind.LocalFunction };

    private static bool IsInsideNameOf(SyntaxNode node) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            );

    private SemanticModel ModelFor(SyntaxTree tree)
    {
        if (!_models.TryGetValue(tree, out var model))
        {
            model = compilation.GetSemanticModel(tree);
            _models[tree] = model;
        }

        return model;
    }

    private void Report(string message, Location? location) =>
        reportDiagnostic(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location
            )
        );
}
