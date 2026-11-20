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
    private readonly HashSet<string> _unanswerableCollectionTests = new(StringComparer.Ordinal);
    private IReadOnlyList<INamedTypeSymbol>? _implementerTypes;

    /// <summary>
    /// Reaches the transpiled JSON contract classes of every serializer context
    /// (FR-028): their specs link the descriptor.
    /// </summary>
    private void ReachJsonRoots(IReadOnlyList<INamedTypeSymbol> roots)
    {
        foreach (var root in roots)
        {
            if (
                DescribableType.Of(root) is { Kind: DescriptorKind.Class } described
                && _typesByKey.ContainsKey(described.Key)
            )
                Reach(described);
        }
    }

    /// <summary>Whether <paramref name="type"/> is a public class of a referenced <c>[TranspileAssembly]</c> library.</summary>
    internal static bool IsLibraryClassWithDescriptor(INamedTypeSymbol type) =>
        type.ContainingAssembly.HasTranspileAssembly()
        && IsPublic(type)
        && DescribableType.Of(type) is { Kind: DescriptorKind.Class };

    /// <summary>
    /// A construction of a questioned collection class passes descriptors for its
    /// type arguments, so every described class among them is reached (D31).
    /// </summary>
    private void ReachCollectionConstructions(IrCompilation ir)
    {
        var questioned = QuestionedCollectionClassesOf(ir);

        if (questioned.Count == 0)
            return;

        foreach (
            var question in ir.RuntimeQuestions.Where(question => IsAsked(question, questioned))
        )
            ReachTypeArguments(question.Type);
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
        && questioned.Contains(kind);

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
        ReportUnanswerableInterfaceTest(question, name);
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
    /// W4-17: a test against a collection interface some type that is not a
    /// runtime collection implements, and that can be the operand, would answer
    /// <c>false</c> for it: <c>MS0001</c> instead, and the site lowers to the
    /// throwing placeholder.
    /// </summary>
    private void ReportUnanswerableInterfaceTest(IrRuntimeQuestion question, string name)
    {
        if (
            RuntimeCollections.KindOfClrName(name) is not { } tested
            || !RuntimeCollections.IsInterface(tested)
            || !ImplementerMayBeOperand(tested, name, question.InputType)
        )
            return;

        _unanswerableCollectionTests.Add(
            RuntimeTypeReachabilityResult.UnanswerableKey(name, OperandKeyOf(question.InputType))
        );

        _report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                $"'{question.Text}' cannot be lowered: a type that is not a runtime collection "
                    + "implements the collection interface and may be the value, and it has no "
                    + "collection descriptor, so the test would answer false for it (spec 011 W4-17).",
                LocationOf(question)
            )
        );
    }

    private string OperandKeyOf(IrQuestionedType? operand)
    {
        if (IsOpenOperand(operand))
            return "*";

        if (operand!.CollectionDefinitionName is { } collection)
            return collection;

        return operand.Key is { } key && IrTypeKeys.Find(key, _compilation) is { } symbol
            ? $"{symbol.ContainingNamespace.ToDisplayString()}.{symbol.Name}"
            : "*";
    }

    private static bool IsOpenOperand(IrQuestionedType? operand) =>
        operand is null
        || operand.AcceptsBoxedValues
        || operand.Shape
            is IrQuestionedTypeShape.Object
                or IrQuestionedTypeShape.TypeParameter
                or IrQuestionedTypeShape.Dynamic;

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
                )
            || RuntimeKindsWithoutMutableShape()
                .Any(kind =>
                    RuntimeCollections.InterfacesImplementedBy(kind).Contains(tested)
                    && (open || RuntimeKindCanBeHeldBy(kind, holder!))
                );
    }

    private static IEnumerable<IrCollectionKind> RuntimeKindsWithoutMutableShape() =>
        RuntimeCollections
            .ClassKinds()
            .Where(kind => !RuntimeCollections.ShapeImplementsMutableInterface(kind));

    private static bool RuntimeKindCanBeHeldBy(IrCollectionKind kind, INamedTypeSymbol holder) =>
        RuntimeCollections.KindOfClrName(ClrNameOf(holder.OriginalDefinition)) is { } held
        && (held == kind || RuntimeCollections.InterfacesImplementedBy(kind).Contains(held));

    private static string ClrNameOf(INamedTypeSymbol definition) =>
        $"{definition.ContainingNamespace.ToDisplayString()}.{definition.MetadataName}";

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
                .Where(type =>
                    IsPublic(type)
                    && DescribableType.Of(type) is { Kind: DescriptorKind.Class }
                    && !DerivesFromRuntimeCollection(type)
                ),
        ];

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
