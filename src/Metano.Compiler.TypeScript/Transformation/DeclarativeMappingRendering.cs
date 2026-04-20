using Metano.Compiler.IR;
using Metano.TypeScript.AST;

namespace Metano.Transformation;

/// <summary>
/// Pure-TS rendering helpers shared by <see cref="BclMapper"/> (legacy Roslyn-driven
/// path) and <see cref="Metano.TypeScript.Bridge.IrToTsBclMapper"/> (IR-driven path).
/// Everything here operates only on <see cref="TsExpression"/>s, strings, and
/// <see cref="DeclarativeMappingRegistry"/>; it is free of any Roslyn or IR
/// dependency, so both mappers can share the same behavior for arg-literal
/// matching, wrap-receiver expansion and runtime-import parsing.
/// </summary>
internal static class DeclarativeMappingRendering
{
    /// <summary>
    /// Whether a mapping entry's optional arg-literal filter matches the given call
    /// site. An entry without a filter matches anything; an entry with
    /// <see cref="DeclarativeMappingEntry.WhenArg0StringEquals"/> only matches when
    /// arg 0 is a TS string literal whose value equals the filter.
    /// </summary>
    internal static bool MatchesArgFilter(
        DeclarativeMappingEntry entry,
        IReadOnlyList<TsExpression> args
    )
    {
        if (!entry.HasArgFilter)
            return true;
        if (args.Count < 1)
            return false;
        return args[0] is TsStringLiteral str && str.Value == entry.WhenArg0StringEquals;
    }

    internal static TsExpression WrapReceiverIfNeeded(
        TsExpression receiver,
        string wrapReceiver,
        DeclarativeMappingRegistry registry
    ) =>
        IsAlreadyWrappedBy(receiver, wrapReceiver, registry)
            ? receiver
            : BuildWrapCall(wrapReceiver, receiver);

    internal static TsCallExpression BuildWrapCall(string wrapReceiver, TsExpression source)
    {
        var dot = wrapReceiver.IndexOf('.');
        if (dot < 0)
            return new TsCallExpression(new TsIdentifier(wrapReceiver), [source]);
        var root = wrapReceiver[..dot];
        var member = wrapReceiver[(dot + 1)..];
        return new TsCallExpression(new TsPropertyAccess(new TsIdentifier(root), member), [source]);
    }

    /// <summary>
    /// Detects "already wrapped" in two ways: the receiver's callee is a property
    /// access on the wrapper's root identifier (e.g., <c>Enumerable.range(0, 10)</c>
    /// for wrapper <c>Enumerable.from</c>), or the callee property name is
    /// registered as a chain method for that wrapper (e.g., <c>arr.where(p)</c> when
    /// <c>where</c> is a known chained method of <c>Enumerable.from</c>).
    /// </summary>
    internal static bool IsAlreadyWrappedBy(
        TsExpression receiver,
        string wrapReceiver,
        DeclarativeMappingRegistry registry
    )
    {
        if (receiver is not TsCallExpression call)
            return false;
        if (call.Callee is not TsPropertyAccess access)
            return false;

        var dot = wrapReceiver.IndexOf('.');
        var root = dot < 0 ? wrapReceiver : wrapReceiver[..dot];
        if (access.Object is TsIdentifier id && id.Name == root)
            return true;

        var chainMethods = registry.GetChainMethodNames(wrapReceiver);
        return chainMethods.Contains(access.Property);
    }
}
