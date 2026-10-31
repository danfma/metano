using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Raises <c>MS0013</c>: the <c>[Ignore]</c> attribute means ".NET-only", so any
/// reference to such a type from a transpilable type's signature OR body is an
/// error. The scan covers every reference position the IR/bridges traverse later:
/// signatures (params, return, fields, props, base, generic constraints) and bodies
/// (locals, <c>new</c>, casts, <c>typeof</c>, generic args, member access). A
/// containing type or member that is itself <c>[Ignore]</c> stays silent, and the
/// scan is target-aware: a <c>[Ignore(TargetLanguage.Dart)]</c> type does not light
/// up under a TypeScript run.
/// <para>
/// Exempt cases (#247), since nothing from them reaches the output:
/// <list type="bullet">
///   <item>source-generator output (<see cref="GeneratedSourceTrees"/>);</item>
///   <item>an interface ignored for the active target listed in a base list — the
///   class/interface extractors erase it (see
///   <see cref="SymbolHelper.EmittedInterfaces"/>);</item>
///   <item>attribute applications, which are never emitted — except the ones the
///   compiler reads to produce output (<see cref="EmittingAttributeNames"/>).</item>
/// </list>
/// </para>
/// </summary>
internal sealed class IgnoreReferenceValidator
{
    /// <summary>
    /// Attributes whose <c>typeof</c> arguments reach the generated code, so they stay
    /// policed: <c>[JsonSerializable(typeof(T))]</c> makes the JSON-context transformer
    /// emit a type spec for <c>T</c>.
    /// </summary>
    private static readonly IReadOnlySet<string> EmittingAttributeNames = new HashSet<string>(
        StringComparer.Ordinal
    )
    {
        "System.Text.Json.Serialization.JsonSerializableAttribute",
    };

    private readonly Compilation _compilation;
    private readonly TargetLanguage _target;
    private readonly GeneratedSourceTrees _generatedSources;
    private readonly List<MetanoDiagnostic> _diagnostics;
    private readonly HashSet<Location> _reportedLocations = [];

    private IgnoreReferenceValidator(
        Compilation compilation,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        _compilation = compilation;
        _target = target;
        _generatedSources = generatedSources;
        _diagnostics = diagnostics;
    }

    public static void Validate(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var validator = new IgnoreReferenceValidator(
            compilation,
            target,
            generatedSources,
            diagnostics
        );

        CSharpSourceFrontend.CollectTopLevelTypes(
            compilation.Assembly.GlobalNamespace,
            type =>
            {
                if (validator.IsPolicedType(type, assemblyWideTranspile))
                    validator.ScanType(type);
            }
        );
    }

    /// <summary>
    /// A transpilable, user-authored type that is not itself ignored for the target.
    /// </summary>
    private bool IsPolicedType(INamedTypeSymbol type, bool assemblyWideTranspile) =>
        SymbolHelper.IsTranspilable(type, assemblyWideTranspile, _compilation.Assembly)
        && !SymbolHelper.HasIgnore(type, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(type);

    /// <summary>
    /// Members marked <c>[Ignore]</c> are not emitted, so references from their
    /// signature or body never reach the target; members a source generator added
    /// to a partial type are not user-authored. Neither is policed.
    /// </summary>
    private bool IsPolicedMember(ISymbol member) =>
        !SymbolHelper.HasIgnore(member, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(member);

    private void ScanType(INamedTypeSymbol type)
    {
        var typeLocation = type.Locations.FirstOrDefault();

        if (type.BaseType is { } baseType)
            CheckType(baseType, typeLocation);

        foreach (var iface in SymbolHelper.EmittedInterfaces(type, _target))
            CheckType(iface, typeLocation);

        CheckConstraints(type.TypeParameters);
        ScanEmittingAttributes(type);

        foreach (var member in type.GetMembers().Where(IsPolicedMember))
            ScanMember(member);

        foreach (var nested in type.GetTypeMembers())
        {
            if (IsPolicedType(nested, assemblyWideTranspile: false))
                ScanType(nested);
        }
    }

    private void CheckConstraints(IEnumerable<ITypeParameterSymbol> typeParameters)
    {
        foreach (var typeParameter in typeParameters)
        foreach (var constraint in typeParameter.ConstraintTypes)
            CheckType(constraint, typeParameter.Locations.FirstOrDefault());
    }

    private void ScanEmittingAttributes(INamedTypeSymbol type)
    {
        foreach (var attribute in type.GetAttributes().Where(IsEmittingAttribute))
        {
            var application = attribute.ApplicationSyntaxReference;

            if (application is null || _generatedSources.Contains(application.SyntaxTree))
                continue;

            var location = application.GetSyntax().GetLocation();

            foreach (var argument in attribute.ConstructorArguments)
            {
                if (argument is { Kind: TypedConstantKind.Type, Value: ITypeSymbol referenced })
                    CheckType(referenced, location);
            }
        }
    }

    private static bool IsEmittingAttribute(AttributeData attribute) =>
        attribute.AttributeClass is { } attributeClass
        && EmittingAttributeNames.Contains(attributeClass.ToDisplayString());

    private void ScanMember(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol field when !field.IsImplicitlyDeclared:
                CheckType(field.Type, field.Locations.FirstOrDefault());
                break;
            case IPropertySymbol prop when !prop.IsImplicitlyDeclared:
                CheckType(prop.Type, prop.Locations.FirstOrDefault());
                break;
            case IMethodSymbol method when IsScannedMethod(method):
                ScanMethod(method);
                break;
        }
    }

    private static bool IsScannedMethod(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared
        && method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.Constructor
                or MethodKind.PropertyGet
                or MethodKind.PropertySet;

    private void ScanMethod(IMethodSymbol method)
    {
        CheckType(method.ReturnType, method.Locations.FirstOrDefault());

        foreach (var parameter in method.Parameters)
            CheckType(parameter.Type, parameter.Locations.FirstOrDefault());

        CheckConstraints(method.TypeParameters);
        ScanMethodBody(method);
    }

    private void ScanMethodBody(IMethodSymbol method)
    {
        foreach (var reference in method.DeclaringSyntaxReferences)
        {
            if (_generatedSources.Contains(reference.SyntaxTree))
                continue;

            var syntax = reference.GetSyntax();
            var semantic = _compilation.GetSemanticModel(syntax.SyntaxTree);

            // The walk does not descend into attribute applications: they are never
            // emitted, whether on the method, its parameters, or — through a record's
            // primary constructor, whose syntax is the whole declaration — the type.
            // Type-level attributes that do reach the output are policed separately
            // by ScanEmittingAttributes.
            foreach (var node in syntax.DescendantNodesAndSelf(IsOutsideAttributeApplication))
                InspectNode(node, semantic);
        }
    }

    private static bool IsOutsideAttributeApplication(SyntaxNode node) =>
        node is not AttributeListSyntax;

    private void InspectNode(SyntaxNode node, SemanticModel semantic)
    {
        switch (node)
        {
            case ObjectCreationExpressionSyntax creation:
                CheckType(semantic.GetTypeInfo(creation).Type, node.GetLocation());
                break;
            case ImplicitObjectCreationExpressionSyntax creation:
                CheckType(semantic.GetTypeInfo(creation).Type, node.GetLocation());
                break;
            case CastExpressionSyntax cast:
                CheckType(semantic.GetTypeInfo(cast.Type).Type, node.GetLocation());
                break;
            case TypeOfExpressionSyntax typeOf:
                CheckType(semantic.GetTypeInfo(typeOf.Type).Type, node.GetLocation());
                break;
            case BinaryExpressionSyntax binary when IsTypeTest(binary):
                CheckType(semantic.GetTypeInfo(binary.Right).Type, node.GetLocation());
                break;
            case VariableDeclarationSyntax declaration:
                CheckType(semantic.GetTypeInfo(declaration.Type).Type, node.GetLocation());
                break;
            case ParameterSyntax { Type: { } parameterType }:
                CheckType(semantic.GetTypeInfo(parameterType).Type, node.GetLocation());
                break;
            case GenericNameSyntax generic:
                foreach (var argument in generic.TypeArgumentList.Arguments)
                    CheckType(semantic.GetTypeInfo(argument).Type, argument.GetLocation());
                break;
            case MemberAccessExpressionSyntax access:
                var accessed = semantic.GetSymbolInfo(access).Symbol;

                if (accessed?.ContainingType is { } owner)
                    CheckType(owner, access.Expression.GetLocation());

                CheckMember(accessed, access.Name.GetLocation());
                break;
            case InvocationExpressionSyntax invocation:
                CheckMember(
                    semantic.GetSymbolInfo(invocation).Symbol,
                    invocation.Expression.GetLocation()
                );
                break;
        }
    }

    private static bool IsTypeTest(BinaryExpressionSyntax binary) =>
        binary.IsKind(SyntaxKind.IsExpression) || binary.IsKind(SyntaxKind.AsExpression);

    private void CheckType(ITypeSymbol? candidate, Location? location)
    {
        if (candidate is null || location is null)
            return;

        // Walk into generic args / array elements so `List<IgnoredType>`
        // and `IgnoredType[]` light up the same way bare references do.
        switch (candidate)
        {
            case IArrayTypeSymbol array:
                CheckType(array.ElementType, location);
                return;
            case INamedTypeSymbol named:
                foreach (var argument in named.TypeArguments)
                    CheckType(argument, location);

                if (SymbolHelper.HasIgnore(named, _target) && _reportedLocations.Add(location))
                    ReportType(named, location);
                return;
        }
    }

    private void CheckMember(ISymbol? candidate, Location location)
    {
        if (candidate is null || !IsMemberKind(candidate))
            return;

        if (SymbolHelper.HasIgnore(candidate, _target) && _reportedLocations.Add(location))
            ReportMember(candidate, location);
    }

    private static bool IsMemberKind(ISymbol symbol) =>
        symbol.Kind
            is SymbolKind.Method
                or SymbolKind.Property
                or SymbolKind.Field
                or SymbolKind.Event;

    private void ReportType(INamedTypeSymbol type, Location location) =>
        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.IgnoreReferencedByTranspiledCode,
                $"Transpilable code references '{type.Name}', which is marked [Ignore] "
                    + "(.NET-only). Migrate the type to [External] (ambient TS shape) "
                    + "or remove the dependency from transpiled code.",
                location
            )
        );

    private void ReportMember(ISymbol member, Location location)
    {
        var owner = member.ContainingType?.Name ?? "<global>";

        _diagnostics.Add(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.IgnoreReferencedByTranspiledCode,
                $"Transpilable code references '{owner}.{member.Name}', which is marked [Ignore] "
                    + "(.NET-only). The member is not emitted; remove the reference or drop "
                    + "[Ignore] from the member.",
                location
            )
        );
    }
}
