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
internal sealed partial class LazyTypeInitializerLowering(
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
}
