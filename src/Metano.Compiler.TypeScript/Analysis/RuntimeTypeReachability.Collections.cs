using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.Bridge;
using Microsoft.CodeAnalysis;

namespace Metano.Compiler.TypeScript.Analysis;

/// <summary>
/// The collection half of the reachability phase (spec 011 FR-008, D31): which
/// runtime collection classes carry type arguments, which argument types that
/// reaches, <c>MS0046</c> reason 2, and the collection-interface tests that
/// report <c>MS0001</c> (W4-17).
/// </summary>
internal sealed partial class RuntimeTypeReachability
{
    private readonly HashSet<CollectionTestKey> _unanswerableCollectionTests = [];
    private IReadOnlyList<INamedTypeSymbol>? _implementerTypes;

    /// <summary>
    /// Reaches the transpiled JSON contract classes of every serializer context
    /// (FR-028): their specs link the descriptor.
    /// </summary>
    private void ReachJsonRoots(IReadOnlyList<INamedTypeSymbol> roots)
    {
        foreach (var root in roots)
        {
            if (DescribableType.Of(root) is { } described && IsEmittedJsonClass(described))
                Reach(described);
        }
    }

    private bool IsEmittedJsonClass(DescribableType type) =>
        type.Kind is DescriptorKind.Class && _typesByKey.ContainsKey(type.Key);

    /// <summary>Whether <paramref name="type"/> is a public class of a referenced <c>[TranspileAssembly]</c> library.</summary>
    internal static bool IsLibraryClassWithDescriptor(INamedTypeSymbol type) =>
        type.ContainingAssembly.HasTranspileAssembly()
        && IsPublic(type)
        && DescribableType.Of(type) is { Kind: DescriptorKind.Class };

    /// <summary>
    /// A construction of a questioned collection class passes descriptors for its
    /// type arguments, so every described class among them is reached (D31).
    /// </summary>
    private void ReachCollectionConstructions(
        IrCompilation ir,
        IReadOnlyList<ITypeSymbol> jsonCollections
    )
    {
        var questioned = QuestionedCollectionClassesOf(ir);

        if (questioned.Count == 0)
            return;

        foreach (
            var question in ir.RuntimeQuestions.Where(question => IsAsked(question, questioned))
        )
            ReachTypeArguments(question.Type);

        foreach (var collection in jsonCollections.OfType<INamedTypeSymbol>())
        {
            if (IsJsonCollectionAsked(collection, questioned))
                ReachArgumentSymbols(collection);
        }
    }

    /// <summary>
    /// A JSON contract's collection is built through the runtime class of its kind, which
    /// carries its type arguments when that class is questioned.
    /// </summary>
    private static bool IsJsonCollectionAsked(
        INamedTypeSymbol collection,
        IReadOnlySet<IrCollectionKind> questioned
    ) =>
        RuntimeCollections.KindOfClrName(ClrNameOf(collection.OriginalDefinition)) is { } kind
        && RuntimeCollections.ClassesOf(kind).Any(questioned.Contains);

    /// <summary>Reaches every described type among the arguments of <paramref name="type"/>, nested ones included.</summary>
    private void ReachArgumentSymbols(INamedTypeSymbol type)
    {
        foreach (var argument in type.TypeArguments.OfType<INamedTypeSymbol>())
        {
            var unwrapped =
                argument.OriginalDefinition.SpecialType is SpecialType.System_Nullable_T
                    ? argument.TypeArguments[0] as INamedTypeSymbol
                    : argument;

            if (unwrapped is null)
                continue;

            if (DescribableOf(unwrapped) is { } describable)
                Reach(describable);

            ReachArgumentSymbols(unwrapped);
        }
    }

    private static bool IsAsked(
        IrRuntimeQuestion question,
        IReadOnlySet<IrCollectionKind> questioned
    ) =>
        question
            is {
                Kind: IrRuntimeQuestionKind.CollectionConstruction,
                Type.CollectionDefinitionName: { } name,
            }
        && RuntimeCollections.KindOfClrName(name) is { } kind
        && questioned.Contains(RuntimeCollections.ArrayBackedKindOf(kind));

    private static IReadOnlySet<IrCollectionKind> QuestionedCollectionClassesOf(IrCompilation ir) =>
        ir
            .QuestionedCollectionTypes.Select(RuntimeCollections.KindOfClrName)
            .OfType<IrCollectionKind>()
            .SelectMany(RuntimeCollections.ClassesOf)
            .ToHashSet();

    private void AnswerCollectionTest(IrRuntimeQuestion question)
    {
        if (question.Type.CollectionDefinitionName is not { } name)
            return;

        ReachTypeArguments(question.Type);
        ReportArgumentWithoutDescriptor(question);
        CollectUnanswerableInterfaceTest(question, name);
    }

    /// <summary>
    /// <c>MS0046</c> reason 2: a tested argument with no descriptor (a type
    /// parameter that is not reified, a type without one) that is not
    /// <c>object</c> tests as <c>UnknownType</c>, which matches every value.
    /// </summary>
    private void ReportArgumentWithoutDescriptor(IrRuntimeQuestion question)
    {
        if (!question.Type.TypeArguments.Any(LacksDescriptor))
            return;

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.ErasedTypeArgumentTest,
                $"'{question.Text}' tests a collection whose type argument has no runtime "
                    + "descriptor: that argument is not checked (spec 011 FR-008).",
                LocationOf(question)
            )
        );
    }

    private bool LacksDescriptor(IrQuestionedType argument) =>
        argument switch
        {
            { Shape: IrQuestionedTypeShape.TypeParameter } => true,
            { Shape: IrQuestionedTypeShape.Named, CollectionDefinitionName: not null } =>
                argument.TypeArguments.Any(LacksDescriptor),
            { Shape: IrQuestionedTypeShape.Named, Key: { } key } => DescribableOf(argument) is null
                && IrTypeKeys.Find(key, _compilation) is { } symbol
                && !HasRuntimeDescriptor(symbol),
            _ => false,
        };

    /// <summary>
    /// W4-17: a test against a collection interface that some value without a
    /// collection descriptor implements (a type that is not a runtime collection,
    /// a native array, a dictionary view) and that can be the operand would answer
    /// <c>false</c> for it. The test is recorded here; <see cref="ReportUnanswerableInterfaceTests"/>
    /// reports it.
    /// </summary>
    private void CollectUnanswerableInterfaceTest(IrRuntimeQuestion question, string name)
    {
        if (
            RuntimeCollections.KindOfClrName(name) is not { } tested
            || !RuntimeCollections.IsInterface(tested)
            || !UnanswerableValueMayBeOperand(tested, name, question.InputType)
        )
            return;

        _unanswerableCollectionTests.Add(
            CollectionTestKey.From(tested, OperandKeyOf(question.InputType))
        );
    }

    /// <summary>
    /// <c>MS0001</c> for every test the bridge lowers to the throwing placeholder: the
    /// bridge reads the operand's key only coarsely (an array, a runtime collection, any
    /// other type), so a test whose key was recorded is reported even where its own
    /// operand could not hold such a value. Run once every question has been answered.
    /// </summary>
    private void ReportUnanswerableInterfaceTests(IrCompilation ir)
    {
        foreach (var question in ir.RuntimeQuestions.Where(IsUnanswerableInterfaceTest))
        {
            _report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{question.Text}' cannot be lowered: a value that is not a runtime collection "
                        + "(a native array, or a type implementing the collection "
                        + "interface) may be the operand and has no collection descriptor, so the test "
                        + "would answer false for it (spec 011 W4-17).",
                    LocationOf(question)
                )
            );
        }
    }

    private bool IsUnanswerableInterfaceTest(IrRuntimeQuestion question) =>
        question
            is {
                Kind: IrRuntimeQuestionKind.Test or IrRuntimeQuestionKind.As,
                Type.CollectionDefinitionName: { } name,
            }
        && RuntimeCollections.KindOfClrName(name) is { } tested
        && RuntimeCollections.IsInterface(tested)
        && _unanswerableCollectionTests.Contains(
            CollectionTestKey.From(tested, OperandKeyOf(question.InputType))
        );

    /// <summary>
    /// The operand's key as the bridge reads it from the static type
    /// (<see cref="RuntimeTypeReachabilityResult.IsUnanswerable"/>): an array, a runtime
    /// collection kind, else open.
    /// </summary>
    private static string OperandKeyOf(IrQuestionedType? operand) =>
        operand switch
        {
            { Shape: IrQuestionedTypeShape.Array } => CollectionTestKey.ArrayOperand,
            { CollectionDefinitionName: { } name } => CollectionTestKey.OperandOfClrName(name),
            { Shape: IrQuestionedTypeShape.Named, Key: { MetadataName: var metadataName } }
                when metadataName.StartsWith(
                    "System.Collections.Generic.Dictionary`2",
                    StringComparison.Ordinal
                ) => metadataName.EndsWith("KeyCollection", StringComparison.Ordinal)
                ? nameof(IrCollectionKind.KeyCollection)
            : metadataName.EndsWith("ValueCollection", StringComparison.Ordinal)
                ? nameof(IrCollectionKind.ValueCollection)
            : CollectionTestKey.OpenOperand,
            _ => CollectionTestKey.OpenOperand,
        };

    private static bool IsOpenOperand(IrQuestionedType? operand) =>
        operand is null
        || operand.AcceptsBoxedValues
        || operand.Shape
            is IrQuestionedTypeShape.Object
                or IrQuestionedTypeShape.TypeParameter
                or IrQuestionedTypeShape.Dynamic;

    /// <summary>
    /// Whether a value no collection descriptor answers for can be the operand: a
    /// native array behind an array-backed list interface, a dictionary view behind
    /// <c>ICollection</c> / <c>IReadOnlyCollection</c>, or a type that implements the
    /// interface itself (<see cref="ImplementerMayBeOperand"/>).
    /// </summary>
    private bool UnanswerableValueMayBeOperand(
        IrCollectionKind tested,
        string testedName,
        IrQuestionedType? operand
    ) =>
        NativeValueMayBeOperand(tested, operand)
        || ImplementerMayBeOperand(tested, testedName, operand);

    private static bool NativeValueMayBeOperand(IrCollectionKind tested, IrQuestionedType? operand)
    {
        var open = IsOpenOperand(operand);

        return RuntimeCollections.IsImplementedByArrays(tested) && (open || operand!.AcceptsArrays);
    }

    private bool ImplementerMayBeOperand(
        IrCollectionKind tested,
        string testedName,
        IrQuestionedType? operand
    )
    {
        var open = IsOpenOperand(operand);

        if (!open && operand is not { Shape: IrQuestionedTypeShape.Named, Key: { } })
            return false;

        var holder = open ? null : IrTypeKeys.Find(operand!.Key!, _compilation);

        if (!open && holder is null)
            return false;

        return ImplementerTypes()
            .Any(type =>
                ImplementsDefinition(type, testedName) && (open || CanBeHeldBy(type, holder!))
            );
    }

    private static string ClrNameOf(INamedTypeSymbol definition) =>
        IrTypeKeys.Of(definition).MetadataName;

    private static bool ImplementsDefinition(INamedTypeSymbol type, string clrName) =>
        type.AllInterfaces.Any(implemented => ClrNameOf(implemented.OriginalDefinition) == clrName);

    /// <summary>Whether a value of <paramref name="type"/> can be stored in <paramref name="holder"/>.</summary>
    private static bool CanBeHeldBy(INamedTypeSymbol type, INamedTypeSymbol holder)
    {
        var definition = holder.OriginalDefinition;

        for (var current = type; current is not null; current = current.BaseType)
        {
            if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, definition))
                return true;
        }

        return type.AllInterfaces.Any(implemented =>
            SymbolEqualityComparer.Default.Equals(implemented.OriginalDefinition, definition)
        );
    }

    /// <summary>
    /// The classes, structs and records that have no collection descriptor: this
    /// compilation's transpiled types and the public types of a referenced
    /// <c>[TranspileAssembly]</c> library.
    /// </summary>
    private IReadOnlyList<INamedTypeSymbol> ImplementerTypes() =>
        _implementerTypes ??= [
            .. _types
                .Where(type => type.Kind is DescriptorKind.Class)
                .Select(type => type.Symbol)
                .Where(type => !DerivesFromRuntimeCollection(type)),
            .. _compilation
                .SourceModule.ReferencedAssemblySymbols.Where(assembly =>
                    assembly.HasTranspileAssembly()
                )
                .SelectMany(assembly => TypesOf(assembly.GlobalNamespace))
                .Where(IsLibraryImplementerWithoutCollectionDescriptor),
        ];

    private static bool IsLibraryImplementerWithoutCollectionDescriptor(INamedTypeSymbol type) =>
        IsPublic(type)
        && DescribableType.Of(type) is { Kind: DescriptorKind.Class }
        && !DerivesFromRuntimeCollection(type);

    /// <summary>
    /// A class deriving from a runtime collection class answers the collection
    /// tests through that class's descriptor (the constructor walk reaches it).
    /// </summary>
    private static bool DerivesFromRuntimeCollection(INamedTypeSymbol type)
    {
        for (var current = type.BaseType; current is not null; current = current.BaseType)
        {
            if (
                RuntimeCollections.KindOfClrName(ClrNameOf(current.OriginalDefinition)) is { } kind
                && !RuntimeCollections.IsInterface(kind)
            )
                return true;
        }

        return false;
    }

    private static IEnumerable<INamedTypeSymbol> TypesOf(INamespaceSymbol container) =>
        container
            .GetMembers()
            .SelectMany(member =>
                member switch
                {
                    INamespaceSymbol nested => TypesOf(nested),
                    INamedTypeSymbol type => WithNested(type),
                    _ => [],
                }
            );

    private static IEnumerable<INamedTypeSymbol> WithNested(INamedTypeSymbol type) =>
        type.GetTypeMembers().SelectMany(WithNested).Prepend(type);
}
