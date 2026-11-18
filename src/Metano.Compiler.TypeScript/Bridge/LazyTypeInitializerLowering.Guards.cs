using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The guard half of the lazy type initializer: which members call
/// <c>#$init</c> first, and how a struct's constructor runs it before its
/// field initializers (FR-066).
/// </summary>
internal sealed partial class LazyTypeInitializerLowering
{
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
}
