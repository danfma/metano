using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Transformation;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// IR-driven BCL mapping: given an <see cref="IrMemberAccess"/> or
/// <see cref="IrCallExpression"/> (with its <see cref="IrMemberOrigin"/>) plus
/// already-lowered TypeScript receiver/arguments, returns the mapped
/// <see cref="TsExpression"/> when the declarative registry has an entry, or
/// <c>null</c> otherwise. The caller renders the raw form on <c>null</c>.
/// </summary>
public static class IrToTsBclMapper
{
    public static TsExpression? TryMapMemberAccess(
        IrMemberAccess access,
        TsExpression loweredTarget,
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
        // Dart-only entries share the registry but carry no JS data; skip
        // them so the dereferences in ApplyPropertyMapping never see nulls.
        if (!entry.HasJsMapping)
            return null;
        var receiver = access.Origin.IsStatic ? null : loweredTarget;
        return ApplyPropertyMapping(entry, receiver);
    }

    /// <summary>
    /// Tries to map a method call. <paramref name="typeArgumentNames"/> feeds
    /// <c>$T0</c>, <c>$T1</c>, … template placeholders; pass an empty list for
    /// non-generic calls.
    /// </summary>
    public static TsExpression? TryMapCall(
        IrCallExpression call,
        TsExpression? loweredReceiver,
        IReadOnlyList<TsExpression> loweredArgs,
        IReadOnlyList<string> typeArgumentNames,
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

        DeclarativeMappingEntry? match = null;
        foreach (var candidate in candidates)
        {
            // Multi-target attributes can carry only Dart-side data; those
            // entries must be invisible to the TS pipeline because the call
            // site below dereferences `JsName!` / `JsTemplate!` and would
            // crash otherwise.
            if (!candidate.HasJsMapping)
                continue;
            if (candidate.HasArgCountFilter && candidate.WhenArgCount != loweredArgs.Count)
                continue;
            if (DeclarativeMappingRendering.MatchesArgFilter(candidate, loweredArgs))
            {
                match = candidate;
                break;
            }
        }
        if (match is null)
            return null;

        var receiver =
            match.HasWrapReceiver && loweredReceiver is not null
                ? DeclarativeMappingRendering.WrapReceiverIfNeeded(
                    loweredReceiver,
                    match.WrapReceiver!,
                    registry
                )
                : loweredReceiver;

        return ApplyMethodMapping(match, receiver, loweredArgs, typeArgumentNames);
    }

    private static TsExpression ApplyPropertyMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(mapping.JsTemplate!, receiver, Arguments: [])
            {
                RuntimeImports = mapping.RuntimeImportsList,
            };

        var name = mapping.JsName!;
        return receiver is not null ? new TsPropertyAccess(receiver, name) : new TsIdentifier(name);
    }

    private static TsExpression ApplyMethodMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args,
        IReadOnlyList<string> typeArgumentNames
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(mapping.JsTemplate!, receiver, args)
            {
                TypeArgumentNames = typeArgumentNames,
                RuntimeImports = mapping.RuntimeImportsList,
            };

        var name = mapping.JsName!;
        var callee = receiver is not null
            ? (TsExpression)new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
        return new TsCallExpression(callee, args);
    }
}
