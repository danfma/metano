using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The members that re-publish a less accessible inherited member on the implementing
/// class (#822): the inherited one keeps its <c>@internal</c> tag, so a sibling subclass
/// gains no public member, and this class declares the member public without storage of
/// its own. It applies to explicit implementations of interfaces outside the transpiled
/// contract (BCL or third-party metadata); one of a project or ProjectReference interface
/// still reports MS0031.
/// <list type="bullet">
///   <item>An inherited auto-property is a field the base constructor stores: a
///     <c>declare</c> field re-types it public and emits no code, so the base value
///     stays (a class field would reset it, and <c>super</c> cannot read a field). A field
///     has no per-accessor visibility, so an inherited <c>internal</c> or
///     <c>private protected</c> setter the interface does not publish stays writable on
///     this class: the same public state the FR-111 path (the inherited member widened)
///     published.</item>
///   <item>Inherited accessors are overridden by accessors that reach them through
///     <c>super</c>; a setter the interface does not publish keeps the inherited
///     visibility, as a JavaScript accessor declaration replaces the base's pair.</item>
///   <item>An inherited event keeps its storage: <c>name$add</c> and
///     <c>name$remove</c> call the inherited ones through <c>super</c>.</item>
/// </list>
/// </summary>
internal static partial class IrToTsClassBridge
{
    private static IReadOnlyList<TsClassMember> BuildRepublishedProperty(
        IrPropertyDeclaration prop,
        IrRepublishedProperty republication,
        string name,
        TsType tsType
    ) =>
        republication is IrRepublishedProperty.SharedStorage
            ? [new TsFieldMember(name, tsType, Readonly: IsReadOnly(prop), Declare: true)]
            : BuildRepublishedAccessors(prop, name, tsType);

    private static List<TsClassMember> BuildRepublishedAccessors(
        IrPropertyDeclaration prop,
        string name,
        TsType tsType
    )
    {
        var inherited = SuperMember(name);

        var members = new List<TsClassMember>
        {
            new TsGetterMember(name, tsType, [new TsReturnStatement(inherited)]),
        };

        if (prop.Accessors is IrPropertyAccessors.GetOnly)
            return members;

        var (accessibility, jsdoc) = IrToTsVisibilityMapper.Map(
            prop.SetterVisibility ?? prop.Visibility
        );

        members.Add(
            new TsSetterMember(
                name,
                new TsParameter("value", tsType),
                [
                    new TsExpressionStatement(
                        new TsBinaryExpression(inherited, "=", new TsIdentifier("value"))
                    ),
                ],
                Accessibility: accessibility,
                JSDoc: jsdoc
            )
        );

        return members;
    }

    private static IReadOnlyList<TsClassMember> BuildRepublishedEvent(
        string name,
        TsParameter handlerParam,
        TsMemberVisibility visibility
    ) =>
        [
            BuildRepublishedEventAccessor($"{name}$add", handlerParam, visibility),
            BuildRepublishedEventAccessor($"{name}$remove", handlerParam, visibility),
        ];

    private static TsMethodMember BuildRepublishedEventAccessor(
        string accessorName,
        TsParameter handlerParam,
        TsMemberVisibility visibility
    ) =>
        new(
            accessorName,
            [handlerParam],
            new TsVoidType(),
            Body:
            [
                new TsExpressionStatement(
                    new TsCallExpression(SuperMember(accessorName), [new TsIdentifier("handler")])
                ),
            ],
            Accessibility: visibility.Accessibility,
            JSDoc: visibility.JSDoc
        );

    private static TsPropertyAccess SuperMember(string name) =>
        new(new TsIdentifier("super"), name);
}
