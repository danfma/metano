using Metano.Compiler.IR;
using Metano.Compiler.Mappings;

namespace Metano.Dart.Bridge;

/// <summary>
/// Resolves a C# method/property reference against the declarative mapping
/// registry and produces a Dart-side IR rewrite when an entry exists.
/// <para>
/// Returning <c>null</c> means "no mapping applies — emit the call/access as-is".
/// Templates (<c>DartTemplate = "$0.foo($1)"</c>) are not yet handled here; only
/// simple <see cref="DeclarativeMappingEntry.DartName"/> renames are honored.
/// Template support lands when the first BCL mapping needs it.
/// </para>
/// </summary>
public static class IrToDartBclMapper
{
    public static IrExpression? TryMapCall(
        IrCallExpression call,
        DeclarativeMappingRegistry registry
    )
    {
        if (call.Origin is null)
            return null;
        if (
            !registry.TryGetMethodsByFullName(
                call.Origin.DeclaringTypeFullName,
                call.Origin.MemberName,
                out var candidates
            )
        )
            return null;

        var match = candidates.FirstOrDefault(c =>
            c.HasDartMapping && (!c.HasArgCountFilter || c.WhenArgCount == call.Arguments.Count)
        );
        if (match is null)
            return null;
        if (match.DartName is not null)
            return RenameCall(call, match.DartName);
        // DartTemplate parsed but not yet rendered. Tracking issue: emit
        // an MS0008 "feature not yet supported" diagnostic here once the
        // first BCL mapping needs the template form.
        return null;
    }

    public static IrExpression? TryMapMemberAccess(
        IrMemberAccess access,
        DeclarativeMappingRegistry registry
    )
    {
        if (access.Origin is null)
            return null;
        if (
            !registry.TryGetPropertyByFullName(
                access.Origin.DeclaringTypeFullName,
                access.Origin.MemberName,
                out var entry
            )
        )
            return null;
        if (!entry.HasDartMapping || entry.DartName is null)
            return null;

        return access.Origin.IsStatic
            ? new IrIdentifier(entry.DartName)
            : access with
            {
                MemberName = entry.DartName,
            };
    }

    /// <summary>
    /// Replaces the call's target with one that renders the Dart-mapped name.
    /// Static calls drop the receiver entirely (the C# qualifier becomes
    /// irrelevant once renamed); instance calls keep the receiver and only
    /// rewrite the member name.
    /// </summary>
    private static IrCallExpression RenameCall(IrCallExpression call, string dartName)
    {
        IrExpression callee = call.Origin!.IsStatic
            ? new IrIdentifier(dartName)
            : call.Target switch
            {
                IrMemberAccess access => access with { MemberName = dartName },
                IrOptionalChain chain => chain with { MemberName = dartName },
                _ => call.Target,
            };
        return call with { Target = callee };
    }
}
