using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>What the slot a value fills does with a box (spec 011 FR-050 / FR-051).</summary>
internal enum BoxingSlot
{
    /// <summary>An ordinary slot: a boxing conversion boxes.</summary>
    Boxed,

    /// <summary>
    /// A member of a <c>[PlainObject]</c> or <c>[JsTuple]</c>, which JS reads
    /// directly: no box is created there.
    /// </summary>
    Raw,

    /// <summary>
    /// A value JS reads raw by number or identity: an argument of an
    /// <c>[Import]</c> / <c>[Emit]</c> member or of a <c>dynamic</c> operation, an
    /// <c>object</c>-typed parameter of a mapped BCL member, a value stored in or
    /// looked up from a BCL collection. No box is created, and a value that may
    /// hold one is unboxed.
    /// </summary>
    Unboxed,

    /// <summary>
    /// A parameter a BCL member formats through its <c>ToString()</c>
    /// (<c>Console.WriteLine(object)</c>): the box is kept, so its .NET text
    /// prints.
    /// </summary>
    Text,
}

/// <summary>
/// The one classification of the slot a value fills, for boxing (spec 011
/// FR-050 / FR-051): the runtime-question scan records a boxing conversion only
/// where this answers <see cref="BoxingSlot.Boxed"/> or
/// <see cref="BoxingSlot.Text"/>, and the extraction lowers every value by the
/// same answer, so the two never disagree. Whether a BCL member lowers through
/// a declarative mapping is the caller's lookup.
/// </summary>
internal static class BoxingSlots
{
    /// <summary>
    /// The slot <paramref name="value"/> fills, past the implicit conversions and
    /// parentheses around it.
    /// </summary>
    /// <param name="value">The value's operation (or its boxing conversion).</param>
    /// <param name="assembly">The compilation's assembly: its members are not BCL.</param>
    /// <param name="lowersByMapping">Whether a BCL member called with the given
    /// argument count lowers through a declarative mapping.</param>
    public static BoxingSlot SlotOf(
        IOperation value,
        IAssemblySymbol assembly,
        Func<IMethodSymbol, int, bool> lowersByMapping
    )
    {
        var child = value;

        while (child.Parent is IParenthesizedOperation or IConversionOperation { IsImplicit: true })
            child = child.Parent;

        var consumer = child.Parent;
        var inArray = false;

        while (consumer is IArrayInitializerOperation or IArrayCreationOperation)
        {
            consumer = consumer.Parent;
            inArray = true;
        }

        if (inArray && consumer is not IArgumentOperation { ArgumentKind: ArgumentKind.ParamArray })
            return BoxingSlot.Boxed;

        return consumer switch
        {
            IArgumentOperation argument => ArgumentSlotOf(argument, assembly, lowersByMapping),
            IDynamicInvocationOperation
            or IDynamicObjectCreationOperation
            or IDynamicIndexerAccessOperation => BoxingSlot.Unboxed,
            ISimpleAssignmentOperation { Target: IMemberReferenceOperation target } assignment
                when assignment.Value == child => StoredSlotOf(target, assembly),
            ICollectionExpressionOperation collection => IsBclCollection(collection.Type, assembly)
                ? BoxingSlot.Unboxed
                : BoxingSlot.Boxed,
            _ => BoxingSlot.Boxed,
        };
    }

    private static BoxingSlot ArgumentSlotOf(
        IArgumentOperation argument,
        IAssemblySymbol assembly,
        Func<IMethodSymbol, int, bool> lowersByMapping
    )
    {
        var parameter = argument.Parameter;
        var slotType = SlotTypeOf(argument);

        return parameter?.ContainingSymbol switch
        {
            IMethodSymbol method when IsInteropShape(method.ContainingType) => BoxingSlot.Raw,
            IMethodSymbol method when IsInteropMember(method) => BoxingSlot.Unboxed,
            IMethodSymbol method when CanHoldBox(slotType) => BclMethodSlotOf(
                method,
                argument,
                assembly,
                lowersByMapping
            ),
            IPropertySymbol indexer
                when CanHoldBox(slotType) && IsBclCollection(indexer.ContainingType, assembly) =>
                BoxingSlot.Unboxed,
            _ => BoxingSlot.Boxed,
        };
    }

    /// <summary>
    /// The type a value of <paramref name="argument"/> fills: the parameter's, or
    /// its element type for a <c>params</c> element.
    /// </summary>
    private static ITypeSymbol? SlotTypeOf(IArgumentOperation argument) =>
        argument
            is { ArgumentKind: ArgumentKind.ParamArray, Parameter.Type: IArrayTypeSymbol array }
            ? array.ElementType
            : argument.Parameter?.Type;

    /// <summary>
    /// An <c>object</c>-typed parameter of a BCL member: a member that formats it
    /// keeps the box (<see cref="BoxingSlot.Text"/>); a member of a collection or
    /// with a declarative mapping reads it raw. The members of <c>System.Object</c>
    /// keep the box, which their identity and equality read.
    /// </summary>
    private static BoxingSlot BclMethodSlotOf(
        IMethodSymbol method,
        IArgumentOperation argument,
        IAssemblySymbol assembly,
        Func<IMethodSymbol, int, bool> lowersByMapping
    )
    {
        if (
            IsOwnMember(method, assembly)
            || method.ContainingType.SpecialType is SpecialType.System_Object
        )
            return BoxingSlot.Boxed;

        if (FormatsItsArguments(method))
            return BoxingSlot.Text;

        if (IsBclCollection(method.ContainingType, assembly))
            return BoxingSlot.Unboxed;

        return lowersByMapping(method, ArgumentCountOf(argument))
            ? BoxingSlot.Unboxed
            : BoxingSlot.Boxed;
    }

    private static int ArgumentCountOf(IArgumentOperation argument) =>
        argument.Parent switch
        {
            IInvocationOperation invocation => invocation.Arguments.Count(IsWritten),
            IObjectCreationOperation creation => creation.Arguments.Count(IsWritten),
            _ => 1,
        };

    private static bool IsWritten(IArgumentOperation argument) =>
        argument.ArgumentKind is not ArgumentKind.DefaultValue;

    /// <summary>
    /// A value assigned to a member: a <c>[PlainObject]</c> / <c>[JsTuple]</c>
    /// member is read directly; a BCL collection's indexer stores it raw.
    /// </summary>
    private static BoxingSlot StoredSlotOf(
        IMemberReferenceOperation target,
        IAssemblySymbol assembly
    )
    {
        if (IsInteropShape(target.Member.ContainingType))
            return BoxingSlot.Raw;

        return
            target is IPropertyReferenceOperation { Property: { IsIndexer: true } indexer }
            && CanHoldBox(indexer.Type)
            && IsBclCollection(indexer.ContainingType, assembly)
            ? BoxingSlot.Unboxed
            : BoxingSlot.Boxed;
    }

    /// <summary>
    /// The BCL members that format an <c>object</c> argument through its
    /// <c>ToString()</c>: <c>Console</c>, <c>TextWriter</c> and <c>Debug</c>
    /// <c>Write</c> / <c>WriteLine</c>, <c>string.Concat</c>, and
    /// <c>StringBuilder.Append</c> / <c>AppendLine</c> / <c>Insert</c>.
    /// </summary>
    private static bool FormatsItsArguments(IMethodSymbol method) =>
        (method.ContainingType.ToDisplayString(), method.Name) switch
        {
            ("System.Console" or "System.IO.TextWriter" or "System.Diagnostics.Debug", var name) =>
                name is "Write" or "WriteLine",
            ("string", "Concat") => true,
            ("System.Text.StringBuilder", "Append" or "AppendLine" or "Insert") => true,
            _ => false,
        };

    /// <summary>
    /// A collection type of the BCL (<c>System.Collections.*</c>) whose storage
    /// compares by identity in TypeScript until stage 011c.
    /// </summary>
    private static bool IsBclCollection(ITypeSymbol? type, IAssemblySymbol assembly) =>
        type is INamedTypeSymbol named
        && !SymbolEqualityComparer.Default.Equals(named.ContainingAssembly, assembly)
        && named.OriginalDefinition.ContainingNamespace?.ToDisplayString() is { } ns
        && (
            ns == "System.Collections"
            || ns.StartsWith("System.Collections.", StringComparison.Ordinal)
        );

    private static bool IsOwnMember(IMethodSymbol method, IAssemblySymbol assembly) =>
        SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, assembly);

    private static bool IsInteropMember(IMethodSymbol method) =>
        SymbolHelper.HasEmit(method)
        || SymbolHelper.HasImport(method)
        || SymbolHelper.HasImport(method.ContainingType)
        || InteropMethods.KindOf(method) is not InteropMethodKind.None;

    private static bool IsInteropShape(INamedTypeSymbol? type) =>
        type is not null && (SymbolHelper.HasPlainObject(type) || SymbolHelper.HasJsTuple(type));

    /// <summary>
    /// Whether <paramref name="method"/> called on a receiver of
    /// <paramref name="receiverType"/> is an enum value's <c>Equals(object)</c>
    /// (<c>color.Equals(other)</c>): C# boxes the receiver to call it, so a
    /// target that boxes compares two boxes (FR-051).
    /// </summary>
    public static bool IsEnumEquals(IMethodSymbol method, ITypeSymbol? receiverType) =>
        receiverType is { TypeKind: TypeKind.Enum }
        && method
            is {
                Name: "Equals",
                IsStatic: false,
                Parameters: [{ Type.SpecialType: SpecialType.System_Object }],
            };

    /// <summary>
    /// Whether a value of <paramref name="type"/> may hold a box: <c>object</c>,
    /// <c>ValueType</c>, <c>Enum</c>, <c>dynamic</c> or a type parameter.
    /// </summary>
    public static bool CanHoldBox(ITypeSymbol? type) =>
        type is ITypeParameterSymbol or IDynamicTypeSymbol
        || type?.SpecialType
            is SpecialType.System_Object
                or SpecialType.System_ValueType
                or SpecialType.System_Enum;
}
