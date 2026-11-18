using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The storage half of the lazy type initializer: each static field and
/// auto-property becomes a <c>#name$</c> slot behind a guarded accessor pair,
/// and a struct's instance field keeps its default (FR-066).
/// </summary>
internal sealed partial class LazyTypeInitializerLowering
{
    /// <summary>
    /// An instance field of a struct keeps its default; its C# initializer runs in
    /// the constructor, after the guard, as C# runs it only in a declared
    /// constructor. A class's instance field is unchanged.
    /// </summary>
    private TsFieldMember LowerInstanceStorage(
        TsFieldMember built,
        IrTypeRef type,
        bool hasInitializer
    )
    {
        if (!target.IsStruct || !hasInitializer || built.Initializer is not { } initializer)
            return built;

        _structFieldInitializers.Add(
            Assignment(new TsPropertyAccess(new TsIdentifier("this"), built.Name), initializer)
        );

        var defaultValue = IrToTsClassBridge.ComputeDefaultInitializer(type);

        return built with
        {
            Initializer = defaultValue,
            DefinitelyAssigned = defaultValue is null,
        };
    }

    private IReadOnlyList<TsClassMember> LowerStaticStorage(
        TsFieldMember built,
        StaticStorage storage
    )
    {
        var slot = Access($"#{built.Name}$");
        var defaultValue = IrToTsClassBridge.ComputeDefaultInitializer(storage.Type);
        var widensToNull = defaultValue is null && !IsNullable(built.Type);

        var slotType = widensToNull
            ? new TsUnionType([built.Type, new TsNamedType("null")])
            : built.Type;

        var slotDefault = SlotDefault(defaultValue, slot);

        if (storage.HasInitializer && built.Initializer is { } initializer)
            _staticInitializers.Add(Assignment(slot, initializer));

        var members = new List<TsClassMember>
        {
            new TsFieldMember($"#{built.Name}$", slotType, slotDefault, Static: true),
            new TsGetterMember(
                built.Name,
                built.Type,
                [
                    Guard(),
                    new TsReturnStatement(
                        widensToNull ? new TsCastExpression(slot, built.Type) : slot
                    ),
                ],
                Static: true,
                Accessibility: built.Accessibility,
                JSDoc: built.JSDoc
            ),
        };

        if (!built.Readonly || storage.IsAssignedByStaticConstructor)
            members.Add(Setter(built, slot));

        _slotOwners.TryAdd(built.Name, storage.Owner);

        return members;
    }

    /// <summary>
    /// The value a slot holds at class definition: a literal token, or <c>null</c>
    /// for a reference type. Any other default (a struct, an enum's zero member,
    /// <c>UUID.empty</c>) reads or constructs something, so the initializer assigns
    /// it first and the slot is declared without one.
    /// </summary>
    private TsExpression? SlotDefault(TsExpression? defaultValue, TsExpression slot)
    {
        if (defaultValue is null)
            return new TsLiteral("null");

        if (IsLiteralToken(defaultValue))
            return defaultValue;

        _slotDefaults.Add(Assignment(slot, defaultValue));

        return null;
    }

    private static bool IsLiteralToken(TsExpression expression) =>
        expression
            is TsLiteral
                or TsStringLiteral
                or TsCastExpression { Expression: TsLiteral or TsStringLiteral };

    private static bool IsNullable(TsType type) =>
        type is TsUnionType union
        && union.Types.Any(member => member is TsNamedType { Name: "null" });

    /// <summary>
    /// A writable member keeps its accessibility; a read-only one the static
    /// constructor writes gets a private setter, so it stays read-only outside.
    /// </summary>
    private TsSetterMember Setter(TsFieldMember built, TsExpression slot)
    {
        var accessibility = built.Readonly ? TsAccessibility.Private : built.Accessibility;

        return new TsSetterMember(
            built.Name,
            new TsParameter("value", built.Type),
            [Guard(), Assignment(slot, new TsIdentifier("value"))],
            Static: true,
            Accessibility: accessibility,
            JSDoc: built.JSDoc
        );
    }

    /// <summary>The facts a static field or auto-property brings to its slot.</summary>
    private sealed record StaticStorage(
        IrMemberDeclaration Owner,
        IrTypeRef Type,
        bool HasInitializer,
        bool IsAssignedByStaticConstructor
    );
}
