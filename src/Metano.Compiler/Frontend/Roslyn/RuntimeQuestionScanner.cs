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

    private readonly DeclarativeMappingRegistry _mappings;

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
        _mappings = DeclarativeMappingRegistry.FromMethods(methodMappings);
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
        if (type.BaseType is not null)
        {
            var location = type.Locations.FirstOrDefault();

            if (location is not null && type.BaseType.IsGenericType)
                RecordCollectionBase(type.BaseType, location);
        }

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
        InspectEnumText(operation);

        if (operation is IConversionOperation conversion)
            InspectBoxing(conversion);

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
            case IConstantPatternOperation constant
                when UnconvertedType(constant.Value) is { TypeKind: TypeKind.Enum } constantType:
                Record(
                    IrRuntimeQuestionKind.Test,
                    constantType,
                    constant.Syntax,
                    constant.InputType
                );

                break;
            case ISingleValueCaseClauseOperation
            {
                Parent: ISwitchCaseOperation { Parent: ISwitchOperation { Value: var subject } },
            } label when UnconvertedType(label.Value) is { TypeKind: TypeKind.Enum } labelType:
                Record(
                    IrRuntimeQuestionKind.Test,
                    labelType,
                    label.Syntax,
                    UnconvertedType(subject)
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
            case IConversionOperation { Operand.Type: IArrayTypeSymbol source } arrayConversion:
                RecordCollectionConstruction(arrayConversion.Type, arrayConversion.Syntax);
                RecordAdoptedArray(source, arrayConversion.Syntax);
                break;
            case IObjectCreationOperation or ICollectionExpressionOperation:
                RecordCollectionConstruction(operation.Type, operation.Syntax);
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

    private void RecordCollectionBase(INamedTypeSymbol baseType, Location location)
    {
        if (RuntimeQuestionFacts.QuestionedTypeOf(baseType).CollectionDefinitionName is not null)
            AddQuestion(IrRuntimeQuestionKind.CollectionConstruction, baseType, location);
    }

    /// <summary>
    /// Records the construction of a collection type (<see cref="IrRuntimeQuestionKind.CollectionConstruction"/>);
    /// any other type is not recorded.
    /// </summary>
    private void RecordCollectionConstruction(ITypeSymbol? type, SyntaxNode syntax)
    {
        if (
            type is not INamedTypeSymbol { IsGenericType: true } named
            || RuntimeQuestionFacts.QuestionedTypeOf(named).CollectionDefinitionName is null
        )
            return;

        _questions.Add(
            Question(IrRuntimeQuestionKind.CollectionConstruction, named, syntax.GetLocation())
        );
    }

    /// <summary>
    /// An array converted to a collection interface becomes a <c>List&lt;E&gt;</c> of its
    /// own element type <c>E</c>, which can differ from the target's argument by array
    /// covariance: that list is the construction a target reaches the element's
    /// descriptor from.
    /// </summary>
    private void RecordAdoptedArray(IArrayTypeSymbol source, SyntaxNode syntax)
    {
        if (_compilation.GetTypeByMetadataName("System.Collections.Generic.List`1") is { } list)
            RecordCollectionConstruction(list.Construct(source.ElementType), syntax);
    }

    /// <summary>
    /// A boxing conversion of an enum or <c>[InlineWrapper]</c> value, to
    /// <c>object</c>, <c>ValueType</c>, <c>Enum</c> or an interface (spec 011
    /// FR-050), outside an interop-bound slot.
    /// </summary>
    private void InspectBoxing(IConversionOperation conversion)
    {
        if (
            !conversion.GetConversion().IsBoxing
            || conversion.Operand.Type is not { } source
            || BoxingSlots.SlotOf(conversion, _compilation.Assembly, _mappings.HasJsMappingFor)
                is not (BoxingSlot.Boxed or BoxingSlot.Text)
        )
            return;

        var boxed = source.UnwrapNullableValueType();

        if (boxed.TypeKind is not TypeKind.Enum && !SymbolHelper.HasInlineWrapper(boxed))
            return;

        _questions.Add(
            Question(IrRuntimeQuestionKind.Box, boxed, conversion.Syntax.GetLocation()) with
            {
                InputType = RuntimeQuestionFacts.QuestionedTypeOf(conversion.Type),
                Text = conversion.Syntax.ToString(),
                FeedsTypeQuestion = BoxingFacts.FeedsTypeQuestion(conversion),
            }
        );
    }

    /// <summary>The type of <paramref name="operation"/> before its implicit conversions.</summary>
    private static ITypeSymbol? UnconvertedType(IOperation? operation) =>
        operation is IConversionOperation { IsImplicit: true } conversion
            ? UnconvertedType(conversion.Operand)
            : operation?.Type;

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

        if (method.Name is "ToList" or "ToDictionary" or "ToHashSet")
            RecordCollectionConstruction(invocation.Type, invocation.Syntax);

        if (RuntimeQuestionFacts.IsUnsupportedTypeMember(method))
            ReportUnsupportedTypeMember(method, invocation.Syntax);

        InspectEnumEquals(invocation);
        RecordDescribedTypeArguments(invocation);
        InspectEnumJoin(invocation);
    }

    /// <summary>
    /// An enum value's <c>Equals(object)</c> boxes its receiver (FR-051), as a
    /// boxing conversion to <c>object</c> does.
    /// </summary>
    private void InspectEnumEquals(IInvocationOperation invocation)
    {
        var receiver = invocation.Instance?.Type;

        if (!BoxingSlots.IsEnumEquals(invocation.TargetMethod, receiver))
            return;

        _questions.Add(
            Question(IrRuntimeQuestionKind.Box, receiver, invocation.Syntax.GetLocation()) with
            {
                InputType = RuntimeQuestionFacts.QuestionedTypeOf(_compilation.ObjectType),
                Text = invocation.Instance!.Syntax.ToString(),
            }
        );
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
            || !_mappings.TryGetMethodsByFullName(
                method.ContainingType.GetStableFullName(),
                method.Name,
                out var mappings
            )
        )
            return;

        var indices = mappings.SelectMany(mapping => mapping.DescribedTypeArguments).ToHashSet();

        foreach (var index in indices.Where(index => index < method.TypeArguments.Length))
            Record(IrRuntimeQuestionKind.TypeOf, method.TypeArguments[index], invocation.Syntax);
    }

    /// <summary>
    /// Default text of an enum can declaratively read its descriptor, just as a mapped
    /// generic call can. Record the enum at each text site, including implicit calls.
    /// </summary>
    private void InspectEnumText(IOperation operation)
    {
        RecordEnumText(operation.Type, DefaultTextSites.ForOperand(operation), operation.Syntax);
    }

    private void InspectEnumJoin(IInvocationOperation invocation)
    {
        var method = invocation.TargetMethod;

        if (!DefaultTextSites.IsJoin(method) || method.TypeArguments.Length != 1)
            return;

        RecordEnumText(method.TypeArguments[0], DefaultTextSite.Implicit, invocation.Syntax);
    }

    private void RecordEnumText(ITypeSymbol? type, DefaultTextSite site, SyntaxNode syntax)
    {
        if (
            type?.UnwrapNullableValueType() is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType
            && DefaultTextConversion.MappedToString(
                enumType,
                new(_target, _mappings.HasJsMappingFor, site)
            )
                is { } method
            && _mappings.TryGetMethodsByFullName(
                method.ContainingType.GetStableFullName(),
                method.Name,
                out var mappings
            )
            && mappings.Any(mapping => mapping.DescribedTypeArguments.Contains(0))
        )
            Record(IrRuntimeQuestionKind.TypeOf, enumType, syntax);
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
                InputType = inputType is null ? null : QuestionedInputOf(inputType),
                Text = site.ToString(),
            }
        );

    /// <summary>
    /// The static type of a tested value, with what its values can be at run time
    /// beyond the type's own shape: a native array or a dictionary view, which a
    /// target that has no descriptor for them must not answer a collection test for.
    /// </summary>
    private IrQuestionedType QuestionedInputOf(ITypeSymbol inputType) =>
        RuntimeQuestionFacts.QuestionedTypeOf(inputType) with
        {
            AcceptsArrays = CanHoldArray(inputType),
            AcceptsDictionaryViews = CanHoldDictionaryView(inputType),
        };

    /// <summary>
    /// Whether some array converts implicitly to <paramref name="type"/>: an array of
    /// the type's own single type argument (<c>IList&lt;int&gt;</c> holds an
    /// <c>int[]</c>), else an <c>object[]</c> (<c>object</c>, <c>Array</c>).
    /// </summary>
    private bool CanHoldArray(ITypeSymbol type)
    {
        if (type is IArrayTypeSymbol)
            return true;

        var element = ElementOf(type);

        return _compilation.ClassifyConversion(_compilation.CreateArrayTypeSymbol(element), type)
            is { IsImplicit: true };
    }

    private bool CanHoldDictionaryView(ITypeSymbol type)
    {
        if (
            _compilation.GetTypeByMetadataName("System.Collections.Generic.Dictionary`2")
            is not { } dictionary
        )
            return false;

        var element = ElementOf(type);
        var anything = _compilation.ObjectType;

        return ViewOf(dictionary.Construct(element, anything), "KeyCollection") is { } keys
                && _compilation.ClassifyConversion(keys, type) is { IsImplicit: true }
            || ViewOf(dictionary.Construct(anything, element), "ValueCollection") is { } values
                && _compilation.ClassifyConversion(values, type) is { IsImplicit: true };
    }

    private static INamedTypeSymbol? ViewOf(INamedTypeSymbol dictionary, string name) =>
        dictionary.GetTypeMembers(name).FirstOrDefault();

    private ITypeSymbol ElementOf(ITypeSymbol type) =>
        type is INamedTypeSymbol { TypeArguments: [var single] } ? single : _compilation.ObjectType;

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
