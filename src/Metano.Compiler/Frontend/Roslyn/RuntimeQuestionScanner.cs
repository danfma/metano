using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Frontend.Roslyn;

/// <summary>
/// Walks the code every transpilable type contributes to the output (bodies,
/// lambdas, local functions, field / property / constructor initializers,
/// parameter defaults, switch expressions and patterns), as
/// <see cref="TranspiledReferenceWalker"/> does, and records each runtime
/// question it asks: a type test or pattern, <c>as</c>, an explicit cast,
/// <c>typeof</c>, <c>GetType()</c>, and the parameter types of an overload
/// group a target may dispatch at runtime. It reports the reflection no target
/// answers as <c>MS0049</c> (spec 011 FR-025). Which descriptor a question
/// reaches is the target's decision: the scan records facts only.
/// </summary>
internal sealed class RuntimeQuestionScanner
{
    private readonly Compilation _compilation;
    private readonly TargetLanguage _target;
    private readonly GeneratedSourceTrees _generatedSources;

    private readonly IReadOnlyDictionary<
        (string DeclaringTypeFullName, string MemberName),
        IReadOnlyList<DeclarativeMappingEntry>
    > _methodMappings;

    private readonly List<MetanoDiagnostic> _diagnostics;
    private readonly List<IrRuntimeQuestion> _questions = [];

    private RuntimeQuestionScanner(
        Compilation compilation,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            IReadOnlyList<DeclarativeMappingEntry>
        > methodMappings,
        List<MetanoDiagnostic> diagnostics
    )
    {
        _compilation = compilation;
        _target = target;
        _generatedSources = generatedSources;
        _methodMappings = methodMappings;
        _diagnostics = diagnostics;
    }

    public static IReadOnlyList<IrRuntimeQuestion> Scan(
        Compilation compilation,
        bool assemblyWideTranspile,
        TargetLanguage target,
        GeneratedSourceTrees generatedSources,
        IReadOnlyDictionary<
            (string DeclaringTypeFullName, string MemberName),
            IReadOnlyList<DeclarativeMappingEntry>
        > methodMappings,
        List<MetanoDiagnostic> diagnostics
    )
    {
        var scanner = new RuntimeQuestionScanner(
            compilation,
            target,
            generatedSources,
            methodMappings,
            diagnostics
        );

        CSharpSourceFrontend.CollectTopLevelTypes(compilation.Assembly.GlobalNamespace, type =>
        {
            if (scanner.IsScannedType(type, assemblyWideTranspile))
                scanner.ScanType(type);
        });

        return scanner._questions;
    }

    private bool IsScannedType(INamedTypeSymbol type, bool assemblyWideTranspile) =>
        SymbolHelper.IsTranspilable(type, assemblyWideTranspile, _compilation.Assembly)
        && !SymbolHelper.HasIgnore(type, _target)
        && !_generatedSources.DeclaresOnlyGeneratedCode(type);

    private bool IsScannedMember(ISymbol member) =>
        !SymbolHelper.HasIgnore(member, _target)
        && !(
            member is IMethodSymbol { AssociatedSymbol: { } owner }
            && SymbolHelper.HasIgnore(owner, _target)
        )
        && !_generatedSources.DeclaresOnlyGeneratedCode(member);

    private void ScanType(INamedTypeSymbol type)
    {
        foreach (var member in type.GetMembers().Where(IsScannedMember))
            ScanMember(member);

        RecordOverloadParameters(type);

        foreach (var nested in type.GetTypeMembers())
        {
            if (IsScannedType(nested, assemblyWideTranspile: false))
                ScanType(nested);
        }
    }

    private void ScanMember(ISymbol member)
    {
        switch (member)
        {
            case IFieldSymbol or IPropertySymbol or IEventSymbol when !member.IsImplicitlyDeclared:
                ScanCode(member, MemberContributedCode.Initializers);
                break;
            case IMethodSymbol method when !method.IsImplicitlyDeclared:
                ScanCode(method, MemberContributedCode.Executable);
                break;
        }
    }

    private void ScanCode(ISymbol member, Func<SyntaxNode, IEnumerable<SyntaxNode?>> codeOf)
    {
        foreach (var reference in member.DeclaringSyntaxReferences)
        {
            if (_generatedSources.Contains(reference.SyntaxTree))
                continue;

            var declaration = reference.GetSyntax();
            var semantic = _compilation.GetSemanticModel(declaration.SyntaxTree);

            foreach (var code in codeOf(declaration).OfType<SyntaxNode>())
            foreach (var root in OperationRootsOf(code, semantic))
            foreach (var operation in root.DescendantsAndSelf())
                Inspect(operation);
        }
    }

    /// <summary>
    /// The outermost operations under <paramref name="code"/>: the node's own
    /// when it has one, else those of its children (attribute lists, which
    /// never reach the output, are skipped).
    /// </summary>
    private static IEnumerable<IOperation> OperationRootsOf(SyntaxNode code, SemanticModel semantic)
    {
        if (code is AttributeListSyntax)
            yield break;

        if (semantic.GetOperation(code) is { } operation)
        {
            yield return operation;
            yield break;
        }

        foreach (var child in code.ChildNodes())
        foreach (var root in OperationRootsOf(child, semantic))
            yield return root;
    }

    private void Inspect(IOperation operation)
    {
        switch (operation)
        {
            case IIsTypeOperation isType:
                Record(
                    IrRuntimeQuestionKind.Test,
                    isType.TypeOperand,
                    isType.Syntax,
                    isType.ValueOperand.Type
                );

                break;
            case ITypePatternOperation typePattern:
                Record(
                    IrRuntimeQuestionKind.Test,
                    typePattern.MatchedType,
                    typePattern.Syntax,
                    typePattern.InputType
                );

                break;
            case IDeclarationPatternOperation { MatchesNull: false } declaration:
                Record(
                    IrRuntimeQuestionKind.Test,
                    declaration.MatchedType,
                    declaration.Syntax,
                    declaration.InputType
                );

                break;
            case IRecursivePatternOperation recursive:
                Record(
                    IrRuntimeQuestionKind.Test,
                    recursive.MatchedType,
                    recursive.Syntax,
                    recursive.InputType
                );

                break;
            case IConversionOperation { IsTryCast: true } asCast:
                Record(IrRuntimeQuestionKind.As, asCast.Type, asCast.Syntax, asCast.Operand.Type);
                break;
            case IConversionOperation { Syntax: CastExpressionSyntax } cast
                when IsCheckedCast(cast, _compilation):
                Record(IrRuntimeQuestionKind.Cast, cast.Type, cast.Syntax, cast.Operand.Type);
                break;
            case ITypeOfOperation typeOf:
                InspectTypeOf(typeOf);
                break;
            case IInvocationOperation invocation:
                InspectInvocation(invocation);
                break;
            case IDynamicInvocationOperation dynamicCall when IsDynamicGetType(dynamicCall):
                InspectDynamicGetType(dynamicCall);
                break;
            case IPropertyReferenceOperation property
                when RuntimeQuestionFacts.IsUnsupportedTypeMember(property.Property):
                ReportUnsupportedTypeMember(property.Property, property.Syntax);
                break;
        }
    }

    /// <summary>A late-bound <c>d.GetType()</c> on a <c>dynamic</c> receiver.</summary>
    private static bool IsDynamicGetType(IDynamicInvocationOperation call) =>
        call
            is {
                Operation: IDynamicMemberReferenceOperation { MemberName: "GetType" },
                Arguments.Length: 0,
            };

    private void InspectDynamicGetType(IDynamicInvocationOperation call)
    {
        var receiver = ((IDynamicMemberReferenceOperation)call.Operation).Instance;

        Record(IrRuntimeQuestionKind.GetType, receiver?.Type, call.Syntax);
    }

    /// <summary>
    /// An explicit reference, unboxing or type-parameter conversion the source
    /// wrote, which .NET checks at runtime; numeric, user-defined, nullable and
    /// identity conversions are not questions.
    /// </summary>
    private static bool IsCheckedCast(IConversionOperation cast, Compilation compilation)
    {
        var conversion = ConversionOf(cast, compilation);

        return !cast.IsImplicit
            && !conversion.IsIdentity
            && !conversion.IsUserDefined
            && (
                conversion.IsReference || conversion.IsUnboxing || cast.Type is ITypeParameterSymbol
            );
    }

    /// <summary>
    /// The conversion a cast performs; a cast of a <c>dynamic</c> value is the
    /// same cast of an <c>object</c> one (spec 011 FR-030), as extraction
    /// classifies it.
    /// </summary>
    private static Conversion ConversionOf(IConversionOperation cast, Compilation compilation)
    {
        if (
            cast.Operand.Type is not IDynamicTypeSymbol
            || cast.Type is null
            || compilation is not CSharpCompilation csharp
        )
            return cast.GetConversion();

        return csharp.ClassifyConversion(
            csharp.GetSpecialType(SpecialType.System_Object),
            cast.Type
        );
    }

    private void InspectTypeOf(ITypeOfOperation typeOf)
    {
        if (RuntimeQuestionFacts.IsUnsupportedTypeOf(typeOf.TypeOperand))
        {
            ReportUnsupportedReflection(
                typeOf.Syntax,
                $"'{typeOf.Syntax}' cannot be lowered: '{typeOf.TypeOperand.ToDisplayString()}' "
                    + "has no runtime type descriptor (void, pointer and open generic types have "
                    + "none; spec 011 FR-025)."
            );

            return;
        }

        Record(IrRuntimeQuestionKind.TypeOf, typeOf.TypeOperand, typeOf.Syntax);
    }

    private void InspectInvocation(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;

        if (IrExpressionExtractor.IsObjectGetType(method))
        {
            InspectGetType(invocation);

            return;
        }

        if (RuntimeQuestionFacts.IsReflectiveLookup(method))
        {
            ReportUnsupportedReflection(
                invocation.Syntax,
                $"'{invocation.Syntax}' cannot be lowered: "
                    + $"'{method.ContainingType.ToDisplayString()}.{method.Name}' finds or creates a "
                    + "type by reflection, which runtime type descriptors do not support (spec 011 "
                    + "FR-025)."
            );

            return;
        }

        if (RuntimeQuestionFacts.IsUnsupportedTypeMember(method))
            ReportUnsupportedTypeMember(method, invocation.Syntax);

        RecordDescribedTypeArguments(invocation);
    }

    /// <summary>
    /// A mapped generic method whose template reads a type argument's
    /// descriptor (<c>Enum.Parse&lt;E&gt;</c> → <c>enumFromRaw(E$runtimeType, …)</c>)
    /// asks for it as <c>typeof(E)</c> does. Every overload's template counts,
    /// so the scan reaches a superset of what the bridge reads.
    /// </summary>
    private void RecordDescribedTypeArguments(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;

        if (
            !method.IsGenericMethod
            || !_methodMappings.TryGetValue(
                (method.ContainingType.GetStableFullName(), method.Name),
                out var mappings
            )
        )
            return;

        var indices = mappings.SelectMany(mapping => mapping.DescribedTypeArguments).ToHashSet();

        foreach (var index in indices.Where(index => index < method.TypeArguments.Length))
            Record(IrRuntimeQuestionKind.TypeOf, method.TypeArguments[index], invocation.Syntax);
    }

    private void InspectGetType(IInvocationOperation invocation)
    {
        var receiver = invocation.Instance;

        if (IsNullLiteral(receiver))
        {
            ReportUnsupportedReflection(
                invocation.Syntax,
                $"'{invocation.Syntax}' calls GetType() on a null literal, which always throws "
                    + "NullReferenceException (spec 011 FR-025)."
            );

            return;
        }

        Record(IrRuntimeQuestionKind.GetType, StaticTypeOf(receiver), invocation.Syntax);
    }

    /// <summary>
    /// The receiver's own static type: <c>GetType</c> is declared on
    /// <c>object</c>, so a value-type receiver arrives boxed.
    /// </summary>
    private static ITypeSymbol? StaticTypeOf(IOperation? receiver) =>
        receiver is IConversionOperation { IsImplicit: true } conversion
            ? StaticTypeOf(conversion.Operand)
            : receiver?.Type;

    private static bool IsNullLiteral(IOperation? receiver) =>
        receiver switch
        {
            IConversionOperation conversion => IsNullLiteral(conversion.Operand),
            IParenthesizedOperation parenthesized => IsNullLiteral(parenthesized.Operand),
            ILiteralOperation { ConstantValue: { HasValue: true, Value: null } } => true,
            _ => false,
        };

    /// <summary>
    /// The parameter types of every overload group a target may dispatch at
    /// runtime: method groups with more than one member (declared or
    /// inherited), a dispatched constructor set and user-defined operators that
    /// share a name, with the element type of a <c>params</c> array.
    /// </summary>
    private void RecordOverloadParameters(INamedTypeSymbol type)
    {
        var policy = OverloadGroupingPolicy.For(_target);

        foreach (var group in MethodOverloadGroups.Of(type, policy))
        {
            var members = group
                .Members.Concat(group.InheritedOverloads ?? [])
                .Select(member => member.Declaration)
                .OfType<IMethodSymbol>()
                .ToList();

            if (members.Count > 1)
                RecordParameters(members);
        }

        if (IrConstructorExtractor.IsDispatchedSet(type))
            RecordParameters(IrConstructorExtractor.AllExplicitConstructors(type));

        var operatorGroups = type.GetMembers()
            .OfType<IMethodSymbol>()
            .Where(method => method.MethodKind is MethodKind.UserDefinedOperator)
            .GroupBy(method => method.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1);

        foreach (var operators in operatorGroups)
            RecordParameters([.. operators]);
    }

    private void RecordParameters(IReadOnlyList<IMethodSymbol> overloads)
    {
        foreach (var parameter in overloads.SelectMany(overload => overload.Parameters))
        {
            var location = parameter.Locations.FirstOrDefault();

            AddQuestion(IrRuntimeQuestionKind.OverloadParameter, parameter.Type, location);

            var elementType = (parameter.Type as IArrayTypeSymbol)?.ElementType;

            if (elementType is not null)
                AddQuestion(IrRuntimeQuestionKind.OverloadParameter, elementType, location);

            var typeParameter = (elementType ?? parameter.Type) as ITypeParameterSymbol;

            foreach (var constraint in typeParameter?.ConstraintTypes ?? [])
                AddQuestion(IrRuntimeQuestionKind.OverloadParameter, constraint, location);
        }
    }

    private void Record(
        IrRuntimeQuestionKind kind,
        ITypeSymbol? type,
        SyntaxNode site,
        ITypeSymbol? inputType = null
    ) =>
        _questions.Add(
            Question(kind, type, site.GetLocation()) with
            {
                InputType = inputType is null
                    ? null
                    : RuntimeQuestionFacts.QuestionedTypeOf(inputType),
                Text = site.ToString(),
            }
        );

    private void AddQuestion(IrRuntimeQuestionKind kind, ITypeSymbol? type, Location? location) =>
        _questions.Add(Question(kind, type, location));

    private static IrRuntimeQuestion Question(
        IrRuntimeQuestionKind kind,
        ITypeSymbol? type,
        Location? location
    ) =>
        new()
        {
            Kind = kind,
            Type = RuntimeQuestionFacts.QuestionedTypeOf(type),
            Span = location is null ? null : IrSourceSpanLocations.From(location),
        };

    private void ReportUnsupportedTypeMember(ISymbol member, SyntaxNode site) =>
        ReportUnsupportedReflection(
            site,
            $"'{site}' uses 'System.Type.{member.Name}', which runtime type descriptors do not "
                + "support: they answer Name, FullName, Namespace, IsInterface, IsClass, "
                + "IsInstanceOfType, IsAssignableFrom, equality and ToString, with no member "
                + "metadata (spec 011 FR-025)."
        );

    private void ReportUnsupportedReflection(SyntaxNode site, string message)
    {
        var diagnostic = new MetanoDiagnostic(
            MetanoDiagnosticSeverity.Error,
            DiagnosticCodes.UnsupportedReflection,
            message,
            site.GetLocation()
        );

        if (!_diagnostics.Contains(diagnostic))
            _diagnostics.Add(diagnostic);
    }
}
