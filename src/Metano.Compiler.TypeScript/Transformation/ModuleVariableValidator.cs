using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.TypeScript.Transformation;

/// <summary>
/// Rejects the uses of a containerless static class's module state (#314) whose
/// C# meaning the emitted ES module cannot keep. Each one reports MS0001 at the
/// offending C# syntax instead of emitting code that computes another value or
/// does not compile:
/// <list type="bullet">
///   <item>An explicit static constructor: its body has no module form, and the
///     variables it assigns would keep their defaults.</item>
///   <item>An initializer that reads a module variable declared at or after its
///     own member: ES evaluates the declarations eagerly in order, so the read
///     throws a TDZ <c>ReferenceError</c> where C# reads the default.</item>
///   <item>A read whose emitted bare name is shadowed by a parameter, local,
///     range variable or local function of the same TypeScript name.</item>
///   <item>A write from a file other than the declaring one: an ES import binding
///     is read-only (TS2632).</item>
/// </list>
/// Limitation: a module function called from an initializer that reads a later
/// variable, and an import cycle between two containerless classes' initializers,
/// still hit the TDZ at load time; neither is detected.
/// </summary>
public sealed class ModuleVariableValidator(
    Compilation compilation,
    IReadOnlyList<INamedTypeSymbol> transpilableTypes,
    IReadOnlyDictionary<string, IrTranspilableTypeRef> transpilableTypesByName,
    Action<MetanoDiagnostic> reportDiagnostic
)
{
    private readonly Dictionary<SyntaxTree, SemanticModel> _models = [];

    public void Validate()
    {
        foreach (var type in transpilableTypes)
        {
            if (type.IsContainerless())
            {
                RejectStaticConstructors(type);
                RejectForwardReferences(type);
            }

            foreach (var root in MemberSyntaxRoots(type))
            {
                ValidateAccessSites(root);
            }
        }
    }

    private void RejectStaticConstructors(INamedTypeSymbol type)
    {
        foreach (var constructor in type.StaticConstructors)
        {
            if (constructor.IsImplicitlyDeclared)
            {
                continue;
            }

            Report(
                $"Containerless static class '{type.Name}' declares a static constructor, "
                    + "which has no module form: the module variables it assigns would keep "
                    + "their defaults. Use field or property initializers instead.",
                constructor.Locations.FirstOrDefault()
            );
        }
    }

    private void RejectForwardReferences(INamedTypeSymbol type)
    {
        var moduleState = type.GetMembers().Where(IsModuleState).ToList();
        var declarationOrder = moduleState
            .Select((member, index) => (member, index))
            .ToDictionary(pair => pair.member, pair => pair.index, SymbolEqualityComparer.Default);

        for (var index = 0; index < moduleState.Count; index++)
        {
            foreach (var initializer in InitializerValues(moduleState[index]))
            {
                RejectForwardReferences(initializer, index, declarationOrder);
            }
        }
    }

    private void RejectForwardReferences(
        ExpressionSyntax initializer,
        int index,
        Dictionary<ISymbol, int> declarationOrder
    )
    {
        var model = ModelFor(initializer.SyntaxTree);
        var eagerReads = initializer
            .DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax)
            .OfType<IdentifierNameSyntax>();

        foreach (var read in eagerReads)
        {
            if (
                model.GetSymbolInfo(read).Symbol?.OriginalDefinition is { } symbol
                && declarationOrder.TryGetValue(symbol, out var readIndex)
                && readIndex >= index
                && !IsInsideNameOf(read)
            )
            {
                Report(
                    $"The initializer reads '{symbol.ContainingType.Name}.{symbol.Name}' before "
                        + "it is declared. A module variable cannot be read before its "
                        + "declaration runs, so the module would fail to load. Declare "
                        + $"'{symbol.Name}' first.",
                    read.GetLocation()
                );
            }
        }
    }

    private void ValidateAccessSites(SyntaxNode root)
    {
        var model = ModelFor(root.SyntaxTree);

        foreach (var identifier in root.DescendantNodesAndSelf().OfType<IdentifierNameSyntax>())
        {
            if (
                model.GetSymbolInfo(identifier).Symbol is not { } symbol
                || !IsModuleVariable(symbol)
                || IsInsideNameOf(identifier)
            )
            {
                continue;
            }

            var emittedName = EmittedName(symbol);

            RejectShadowedRead(model, identifier, symbol, emittedName);
            RejectImportedWrite(model, identifier, symbol, emittedName);
        }
    }

    private void RejectShadowedRead(
        SemanticModel model,
        IdentifierNameSyntax identifier,
        ISymbol symbol,
        string emittedName
    )
    {
        var shadow = model
            .LookupSymbols(identifier.SpanStart)
            .FirstOrDefault(candidate =>
                IsLocalBinding(candidate)
                && TypeScriptNaming.ToCamelCase(candidate.Name) == emittedName
            );

        if (shadow is null)
        {
            return;
        }

        Report(
            $"'{symbol.ContainingType.Name}.{symbol.Name}' is emitted as the module variable "
                + $"'{emittedName}', which the {Describe(shadow)} '{shadow.Name}' shadows here, "
                + "so the read would return the local value. Rename the local or add a "
                + "[Name(\"...\")] override on the member.",
            identifier.GetLocation()
        );
    }

    private void RejectImportedWrite(
        SemanticModel model,
        IdentifierNameSyntax identifier,
        ISymbol symbol,
        string emittedName
    )
    {
        if (
            !IsWriteTarget(identifier)
            || IsSameFile(symbol.ContainingType, Accessor(model, identifier))
        )
        {
            return;
        }

        Report(
            $"Module variable '{emittedName}' of '{symbol.ContainingType.Name}' is assigned "
                + "outside the file it is declared in. A static field or property of a "
                + "containerless static class becomes a module variable, and an imported "
                + "binding is read-only in TypeScript. Assign it through a method of the "
                + "declaring class instead.",
            identifier.GetLocation()
        );
    }

    private static bool IsModuleState(ISymbol member) =>
        IrModuleVariableExtractor.IsModuleState(member, TargetLanguage.TypeScript);

    /// <summary>
    /// A module variable the toolchain declares: the reads the expression bridge
    /// lowers to a <c>TsModuleExportReference</c> (an <c>[External]</c> or
    /// <c>[Import]</c> container declares its own names).
    /// </summary>
    private bool IsModuleVariable(ISymbol symbol) =>
        symbol is IFieldSymbol or IPropertySymbol
        && symbol.ContainingType is { } owner
        && owner.IsContainerless()
        && SymbolHelper.IsToolchainDeclared(owner, compilation.Assembly)
        && IsModuleState(symbol.OriginalDefinition);

    private static string EmittedName(ISymbol member) =>
        IrToTsNamingPolicy.ToDataMemberName(
            member.Name,
            member.GetNameOverride(TargetLanguage.TypeScript)
        );

    private static bool IsLocalBinding(ISymbol symbol) =>
        symbol
            is ILocalSymbol
                or IParameterSymbol
                or IRangeVariableSymbol
                or IMethodSymbol { MethodKind: MethodKind.LocalFunction };

    private static string Describe(ISymbol shadow) =>
        shadow switch
        {
            IParameterSymbol => "parameter",
            IMethodSymbol => "local function",
            IRangeVariableSymbol => "range variable",
            _ => "local",
        };

    private static bool IsInsideNameOf(SyntaxNode node) =>
        node.Ancestors()
            .OfType<InvocationExpressionSyntax>()
            .Any(invocation =>
                invocation.Expression is IdentifierNameSyntax { Identifier.ValueText: "nameof" }
            );

    /// <summary>
    /// True when the member read through <paramref name="identifier"/> is the
    /// target of an assignment (simple or compound), an increment or decrement,
    /// or a <c>ref</c> / <c>out</c> argument.
    /// </summary>
    private static bool IsWriteTarget(IdentifierNameSyntax identifier)
    {
        var access =
            identifier.Parent is MemberAccessExpressionSyntax member && member.Name == identifier
                ? (ExpressionSyntax)member
                : identifier;

        return access.Parent switch
        {
            AssignmentExpressionSyntax assignment => assignment.Left == access,
            PrefixUnaryExpressionSyntax prefix => prefix.Kind()
                is SyntaxKind.PreIncrementExpression
                    or SyntaxKind.PreDecrementExpression,
            PostfixUnaryExpressionSyntax postfix => postfix.Kind()
                is SyntaxKind.PostIncrementExpression
                    or SyntaxKind.PostDecrementExpression,
            ArgumentSyntax argument => !argument.RefKindKeyword.IsKind(SyntaxKind.None)
                && !argument.RefKindKeyword.IsKind(SyntaxKind.InKeyword),
            _ => false,
        };
    }

    private static INamedTypeSymbol? Accessor(SemanticModel model, SyntaxNode node) =>
        node.Ancestors().OfType<BaseTypeDeclarationSyntax>().FirstOrDefault() is { } declaration
            ? model.GetDeclaredSymbol(declaration)
            : null;

    /// <summary>
    /// True when both types emit into the same TypeScript file (the same type,
    /// or types co-located by <c>[EmitInFile]</c>). A type of another assembly
    /// is never in the current file.
    /// </summary>
    private bool IsSameFile(INamedTypeSymbol owner, INamedTypeSymbol? accessor) =>
        accessor is not null
        && OutputFileOf(owner) is { } ownerFile
        && OutputFileOf(accessor) is { } accessorFile
        && ownerFile == accessorFile;

    private (string Namespace, string FileName)? OutputFileOf(INamedTypeSymbol type)
    {
        var topLevel = type;

        while (topLevel.ContainingType is { } container)
        {
            topLevel = container;
        }

        return
            SymbolEqualityComparer.Default.Equals(topLevel.ContainingAssembly, compilation.Assembly)
            && transpilableTypesByName.TryGetValue(topLevel.Name, out var typeRef)
            ? (typeRef.Namespace, typeRef.FileName)
            : null;
    }

    /// <summary>
    /// The syntax of each emitted member of <paramref name="type"/>: its fields,
    /// properties, methods and constructors, without accessors (covered by their
    /// property), nested types (validated as types of their own) and
    /// <c>[Ignore]</c>d members.
    /// </summary>
    private static IEnumerable<SyntaxNode> MemberSyntaxRoots(INamedTypeSymbol type) =>
        type.GetMembers()
            .Where(member =>
                !member.IsImplicitlyDeclared
                && member is not INamedTypeSymbol
                && member is not IMethodSymbol { AssociatedSymbol: not null }
                && !SymbolHelper.HasIgnore(member, TargetLanguage.TypeScript)
            )
            .SelectMany(member => member.DeclaringSyntaxReferences)
            .Select(reference => reference.GetSyntax());

    private static IEnumerable<ExpressionSyntax> InitializerValues(ISymbol member) =>
        member
            .DeclaringSyntaxReferences.Select(reference => reference.GetSyntax())
            .Select(syntax =>
                syntax switch
                {
                    VariableDeclaratorSyntax field => field.Initializer?.Value,
                    PropertyDeclarationSyntax property => property.Initializer?.Value,
                    _ => null,
                }
            )
            .OfType<ExpressionSyntax>();

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
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                message,
                location
            )
        );
}
