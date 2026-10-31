using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Raises <c>MS0013</c>: the <c>[Ignore]</c> attribute means ".NET-only", so any
/// reference to such a type from a transpilable type's signature OR body is an
/// error. Signatures (params, return, fields, props, events, base, generic
/// constraints) are checked from their symbols. The code each member contributes
/// (bodies, field/property/event initializers, parameter defaults,
/// constructor-initializer arguments) is walked name by name, so a type or member
/// spelled anywhere in it raises (#277). Only emitted code is walked, so static
/// constructors, explicit interface implementations and custom event accessors are
/// not scanned — the IR does not emit their bodies. A containing type or member that
/// is itself <c>[Ignore]</c> stays silent, and the scan is target-aware: a
/// <c>[Ignore(TargetLanguage.Dart)]</c> type does not light up under a TypeScript
/// run.
/// <para>
/// Exempt cases (#247), since nothing from them reaches the output:
/// <list type="bullet">
///   <item>source-generator output (<see cref="GeneratedSourceTrees"/>);</item>
///   <item>an interface ignored for the active target listed in a base list — the
///   class/interface extractors erase it (see
///   <see cref="SymbolHelper.EmittedInterfaces"/>);</item>
///   <item>attribute applications, which are never emitted — except the ones the
///   compiler reads to produce output (<see cref="EmittingAttributeNames"/>);</item>
///   <item><c>nameof</c> arguments, which fold to a string constant (#277).</item>
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
        && !IsAccessorOfIgnoredMember(member)
        && !_generatedSources.DeclaresOnlyGeneratedCode(member);

    /// <summary>
    /// Accessors are members of their own, so an <c>[Ignore]</c> on the property
    /// has to be read from the associated symbol.
    /// </summary>
    private bool IsAccessorOfIgnoredMember(ISymbol member) =>
        member is IMethodSymbol { AssociatedSymbol: { } owner }
        && SymbolHelper.HasIgnore(owner, _target);

    private void ScanType(INamedTypeSymbol type)
    {
        var typeLocation = type.Locations.FirstOrDefault();

        if (type.BaseType is { } baseType)
            CheckType(baseType, typeLocation);

        foreach (var iface in SymbolHelper.EmittedInterfaces(type, _target))
            CheckType(iface, typeLocation);

        CheckConstraints(type.TypeParameters);
        ScanEmittingAttributes(type);

        if (type.DelegateInvokeMethod is { } signature)
            CheckSignature(signature);

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
                ScanCode(field, MemberContributedCode.Initializers);
                break;
            case IPropertySymbol property when !property.IsImplicitlyDeclared:
                ScanProperty(property);
                break;
            case IEventSymbol @event when !@event.IsImplicitlyDeclared:
                CheckType(@event.Type, @event.Locations.FirstOrDefault());
                ScanCode(@event, MemberContributedCode.Initializers);
                break;
            case IMethodSymbol
            {
                MethodKind: MethodKind.PropertyGet or MethodKind.PropertySet
            } accessor when !accessor.IsImplicitlyDeclared:
                // The signature belongs to the property (see ScanProperty).
                ScanCode(accessor, MemberContributedCode.Executable);
                break;
            case IMethodSymbol method when IsScannedMethod(method):
                ScanMethod(method);
                break;
        }
    }

    /// <summary>
    /// The property owns the signature its accessors share (type and indexer
    /// parameters), so it is checked once here and the accessors only scan their
    /// bodies — otherwise each reference would report at both locations.
    /// </summary>
    private void ScanProperty(IPropertySymbol property)
    {
        CheckType(property.Type, property.Locations.FirstOrDefault());
        CheckParameters(property.Parameters);
        ScanCode(property, MemberContributedCode.Initializers);
    }

    private static bool IsScannedMethod(IMethodSymbol method) =>
        !method.IsImplicitlyDeclared
        && method.MethodKind
            is MethodKind.Ordinary
                or MethodKind.Constructor
                or MethodKind.UserDefinedOperator
                or MethodKind.Conversion;

    private void ScanMethod(IMethodSymbol method)
    {
        CheckSignature(method);
        ScanCode(method, MemberContributedCode.Executable);
    }

    private void CheckSignature(IMethodSymbol method)
    {
        CheckType(method.ReturnType, method.Locations.FirstOrDefault());
        CheckParameters(method.Parameters);
        CheckConstraints(method.TypeParameters);
    }

    private void CheckParameters(IEnumerable<IParameterSymbol> parameters)
    {
        foreach (var parameter in parameters)
            CheckType(parameter.Type, parameter.Locations.FirstOrDefault());
    }

    /// <summary>
    /// Walks the code a member contributes to the output — bodies, initializers,
    /// default values, constructor-initializer arguments — but not its signature
    /// syntax, which <see cref="ScanMember"/> checks from the symbols (walking it would
    /// double-report, and would police the explicit-interface specifiers and base
    /// lists that #247 exempts).
    /// </summary>
    private void ScanCode(ISymbol member, Func<SyntaxNode, IEnumerable<SyntaxNode?>> codeOf)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            if (_generatedSources.Contains(reference.SyntaxTree))
                continue;

            var declaration = reference.GetSyntax();
            var semantic = _compilation.GetSemanticModel(declaration.SyntaxTree);

            foreach (var code in codeOf(declaration).OfType<SyntaxNode>())
                ScanSyntax(code, semantic);
        }
    }

    /// <summary>
    /// The walk does not descend into what never reaches the output: attribute
    /// applications (on locals, lambdas, local functions or their parameters;
    /// type-level attributes that do reach the output are policed by
    /// <see cref="ScanEmittingAttributes"/>) and <c>nameof</c> arguments, which
    /// fold to a string constant.
    /// </summary>
    private void ScanSyntax(SyntaxNode code, SemanticModel semantic)
    {
        foreach (var node in code.DescendantNodesAndSelf(node => MayReachOutput(node, semantic)))
            InspectNode(node, semantic);
    }

    private static bool MayReachOutput(SyntaxNode node, SemanticModel semantic) =>
        node is not AttributeListSyntax && !IsNameOf(node, semantic);

    private static bool IsNameOf(SyntaxNode node, SemanticModel semantic) =>
        node is InvocationExpressionSyntax { Expression: IdentifierNameSyntax callee }
        && callee.Identifier.ValueText == "nameof"
        && semantic.GetOperation(node) is INameOfOperation;

    /// <summary>
    /// Every simple name is resolved, so a type is caught in any position it can be
    /// spelled — patterns, catch declarations, local functions, lambda parameters,
    /// constraints, <c>typeof</c>, <c>default</c>, casts, <c>new</c> — and a member in
    /// any position it can be read or called from. Type arguments are names of their
    /// own, so a name checks only the symbol it binds to.
    /// </summary>
    private void InspectNode(SyntaxNode node, SemanticModel semantic)
    {
        switch (node)
        {
            case SimpleNameSyntax name:
                InspectName(name, semantic.GetSymbolInfo(name).Symbol);
                break;
            case ImplicitObjectCreationExpressionSyntax creation:
                CheckType(semantic.GetTypeInfo(creation).Type, node.GetLocation());
                break;
            case MemberAccessExpressionSyntax access:
                InspectMemberReceiver(access, semantic);
                break;
        }
    }

    /// <summary>
    /// The receiver of an instance access spells no type (<c>m.Value</c>), so the
    /// accessed member's containing type is checked at the receiver. For a static
    /// access (<c>Marker.Value</c>) the name walk reports the same location, and
    /// <see cref="_reportedLocations"/> keeps it to one diagnostic.
    /// </summary>
    private void InspectMemberReceiver(MemberAccessExpressionSyntax access, SemanticModel semantic)
    {
        if (semantic.GetSymbolInfo(access).Symbol?.ContainingType is { } owner)
            CheckType(owner, access.Expression.GetLocation());
    }

    /// <summary>
    /// <c>var</c> spells no type, so the inferred type is checked whole (type
    /// arguments included) at the keyword.
    /// </summary>
    private void InspectName(SimpleNameSyntax name, ISymbol? symbol)
    {
        var location = name.GetLocation();

        switch (symbol)
        {
            case ITypeSymbol inferred when name.IsVar:
                CheckType(inferred, location);
                break;
            case INamedTypeSymbol named:
                CheckNamedType(named, location);
                break;
            default:
                CheckMember(symbol, location);
                break;
        }
    }

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

                CheckNamedType(named, location);
                return;
        }
    }

    private void CheckNamedType(INamedTypeSymbol type, Location location)
    {
        if (SymbolHelper.HasIgnore(type, _target) && _reportedLocations.Add(location))
            ReportType(type, location);
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
