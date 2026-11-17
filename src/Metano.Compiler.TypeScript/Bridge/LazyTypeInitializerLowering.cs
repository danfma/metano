using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Runs a type's static constructor when .NET does: once, right before the
/// first instance creation or static member access (FR-066, #681).
/// <list type="bullet">
///   <item><c>static readonly #$init = typeInitializer("Name", () => { … })</c>, the
///     class's first member, assigns the non-literal slot defaults, then the static
///     field and auto-property initializers in textual order, then runs the
///     static constructor body.</item>
///   <item>Each static field and auto-property becomes a <c>#name$</c> slot holding a
///     literal default, behind a <c>static get</c> / <c>static set</c> pair that calls
///     <c>Name.#$init()</c> first.</item>
///   <item>Every static user member, and every instance user member of a struct,
///     starts with the guard; the constructor runs it before anything the instance
///     runs.</item>
/// </list>
/// Every access spells the class name, never <c>this</c>: <c>Derived.x</c> reaches
/// the declaring class's accessor with <c>this === Derived</c>.
/// </summary>
internal sealed class LazyTypeInitializerLowering(
    TypeInitializerTarget target,
    IrClassDeclaration ir,
    IReadOnlyList<IrStatement> staticConstructorBody,
    IrToTsBridgeContext lowering
) : TypeInitializerLowering
{
    private const string GuardName = "#$init";
    private const string RuntimeHelper = "typeInitializer";

    private readonly List<TsStatement> _slotDefaults = [];
    private readonly List<TsStatement> _staticInitializers = [];
    private readonly List<TsStatement> _structFieldInitializers = [];

    private readonly Dictionary<string, IrMemberDeclaration> _slotOwners = new(
        StringComparer.Ordinal
    );

    public override IReadOnlyList<TsClassMember> LowerField(
        IrFieldDeclaration field,
        TsFieldMember built
    )
    {
        if (field.IsCompileTimeConstant || field.IsConstant)
            return [built];

        if (!field.IsStatic)
            return [LowerInstanceStorage(built, field.Type, field.Initializer is not null)];

        return LowerStaticStorage(
            built,
            new StaticStorage(
                field,
                field.Type,
                field.Initializer is not null,
                field.IsAssignedByStaticConstructor
            )
        );
    }

    public override IReadOnlyList<TsClassMember> LowerProperty(
        IrPropertyDeclaration property,
        IReadOnlyList<TsClassMember> built
    )
    {
        if (built is not [TsFieldMember { Abstract: false } storage])
            return built;

        if (!property.IsStatic)
            return [LowerInstanceStorage(storage, property.Type, property.Initializer is not null)];

        return LowerStaticStorage(
            storage,
            new StaticStorage(
                property,
                property.Type,
                property.Initializer is not null,
                property.Semantics?.IsAssignedByStaticConstructor == true
            )
        );
    }

    public override TsClassBody Complete(TsClassBody body, IReadOnlySet<TsClassMember> userMembers)
    {
        var members = body
            .Members.Select(member =>
                userMembers.Contains(member) && RunsTheGuard(member) ? Guarded(member) : member
            )
            .ToList();

        var constructor = GuardConstructor(body.Constructor, members);

        members.Insert(0, InitializerField());

        return new TsClassBody(constructor, members);
    }

    /// <summary>
    /// Every static member whose TypeScript name is the name of another member's
    /// slot accessors (a private field <c>count</c> beside a property <c>Count</c>,
    /// both <c>count</c>): the two would declare one name twice, so the class
    /// emitter reports each as an error asking for a <c>[Name]</c>.
    /// </summary>
    public override IReadOnlyList<SlotNameClash> SlotNameClashes() =>
        (ir.Members ?? [])
            .Where(member => member.IsStatic)
            .Select(ClashOf)
            .OfType<SlotNameClash>()
            .ToList();

    private SlotNameClash? ClashOf(IrMemberDeclaration member) =>
        TsNameOf(member) is { } name
        && _slotOwners.TryGetValue(name, out var owner)
        && !ReferenceEquals(owner, member)
            ? new SlotNameClash(member, name, owner)
            : null;

    private static string? TsNameOf(IrMemberDeclaration member) =>
        member switch
        {
            IrFieldDeclaration or IrPropertyDeclaration =>
                IrToTsNamingPolicy.ToDeclaredDataMemberName(member.Name, member.Attributes),
            IrMethodDeclaration method => IrToTsNamingPolicy.ToMethodName(method),
            _ => null,
        };

    /// <summary>
    /// A static user member always runs the guard; an instance one only on a
    /// struct, whose default value exists without a constructor call (ECMA-335
    /// I.8.9.5: the first instance method call triggers the initializer).
    /// </summary>
    private bool RunsTheGuard(TsClassMember member) =>
        member switch
        {
            TsMethodMember { Abstract: true } => false,
            TsMethodMember method => method.Static || target.IsStruct,
            TsGetterMember getter => getter.Static || target.IsStruct,
            TsSetterMember setter => setter.Static || target.IsStruct,
            _ => false,
        };

    private TsClassMember Guarded(TsClassMember member) =>
        member switch
        {
            TsMethodMember method => method with { Body = WithGuard(method.Body, Guard()) },
            TsGetterMember getter => getter with { Body = WithGuard(getter.Body, Guard()) },
            TsSetterMember setter => setter with { Body = WithGuard(setter.Body, Guard()) },
            _ => member,
        };

    private static IReadOnlyList<TsStatement> WithGuard(
        IReadOnlyList<TsStatement> body,
        TsStatement guard
    ) => body is [var first, ..] && IsGuard(first) ? body : [guard, .. body];

    private static bool IsGuard(TsStatement statement) =>
        statement
            is TsExpressionStatement
            {
                Expression: TsCallExpression { Callee: TsPropertyAccess { Property: GuardName } },
            };

    /// <summary>
    /// The constructor runs the guard before anything the instance runs. A derived
    /// class runs it before <c>super(...)</c>; a base class's instance field
    /// initializers run before its constructor body, so the guard folds into the
    /// first one. A struct runs it, then its instance field initializers, on every
    /// path that runs a declared constructor, also one called with no arguments
    /// (a <c>params</c> array left empty); the path that builds the implicit
    /// default value runs neither, as <c>default(S)</c> runs no constructor.
    /// </summary>
    private TsConstructor GuardConstructor(TsConstructor constructor, List<TsClassMember> members)
    {
        if (target.IsStruct)
            return constructor with { Body = WithStructPrologue(constructor.Body) };

        if (!target.HasBaseClass && FoldIntoFirstFieldInitializer(members))
            return constructor;

        return constructor with
        {
            Body = WithGuard(constructor.Body, Guard()),
        };
    }

    private IReadOnlyList<TsStatement> WithStructPrologue(IReadOnlyList<TsStatement> body)
    {
        TsStatement[] prologue = [Guard(), .. _structFieldInitializers];

        return target.ImplicitDefault switch
        {
            ImplicitDefaultConstruction.WholeConstructor => body,
            ImplicitDefaultConstruction.LeadingBranch => [body[0], .. prologue, .. body.Skip(1)],
            _ => [.. prologue, .. body],
        };
    }

    private bool FoldIntoFirstFieldInitializer(List<TsClassMember> members)
    {
        var index = members.FindIndex(member =>
            member is TsFieldMember { Static: false, Initializer: not null }
        );

        if (index < 0)
            return false;

        var field = (TsFieldMember)members[index];

        members[index] = field with
        {
            Initializer = new TsParenthesized(
                new TsBinaryExpression(GuardCall(), ",", field.Initializer!)
            ),
        };

        return true;
    }

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

    private TsFieldMember InitializerField()
    {
        TsStatement[] body =
        [
            .. _slotDefaults,
            .. _staticInitializers,
            .. IrToTsStatementBridge.MapBody(staticConstructorBody, lowering),
        ];

        var initializer = new TsCallExpression(
            new TsRuntimeHelperReference(RuntimeHelper),
            [new TsStringLiteral(target.TypeName), new TsArrowFunction([], body)]
        );

        return new TsFieldMember(
            GuardName,
            new TsFunctionType([], new TsVoidType()),
            initializer,
            Readonly: true,
            Static: true
        );
    }

    private TsStatement Guard() => new TsExpressionStatement(GuardCall());

    private TsExpression GuardCall() => new TsCallExpression(Access(GuardName), []);

    private TsExpression Access(string privateName) =>
        new TsPropertyAccess(new TsIdentifier(target.ClassName), privateName);

    private static TsStatement Assignment(TsExpression target, TsExpression value) =>
        new TsExpressionStatement(new TsBinaryExpression(target, "=", value));

    /// <summary>The facts a static field or auto-property brings to its slot.</summary>
    private sealed record StaticStorage(
        IrMemberDeclaration Owner,
        IrTypeRef Type,
        bool HasInitializer,
        bool IsAssignedByStaticConstructor
    );
}
