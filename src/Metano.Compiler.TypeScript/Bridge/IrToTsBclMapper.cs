using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Parenthesization.Templates;
using Metano.Compiler.TypeScript.Transformation;

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
        if (access.Origin is null || JsPropertyMappingOf(access.Origin, registry) is not { } entry)
            return null;

        var receiver = access.Origin.IsStatic ? null : loweredTarget;

        return ApplyPropertyMapping(entry, receiver, ReceiverTypeOf(access.Target, receiver));
    }

    /// <summary>
    /// Tries to map an instance member read whose receiver is already lowered
    /// and has no IR node of its own, such as the member a property pattern
    /// entry reads (<c>e is { InnerException: null }</c>).
    /// </summary>
    public static TsExpression? TryMapInstanceRead(
        IrMemberOrigin origin,
        TsExpression receiver,
        DeclarativeMappingRegistry registry
    ) =>
        origin.IsStatic || JsPropertyMappingOf(origin, registry) is not { } entry
            ? null
            : ApplyPropertyMapping(entry, receiver, receiverType: null);

    // Dart-only entries share the registry but carry no JS data; skip them so
    // the dereferences in ApplyPropertyMapping never see nulls.
    private static DeclarativeMappingEntry? JsPropertyMappingOf(
        IrMemberOrigin origin,
        DeclarativeMappingRegistry registry
    ) =>
        registry.TryGetPropertyByFullName(
            origin.DeclaringTypeFullName,
            origin.MemberName,
            out var entry
        ) && entry.HasJsMapping
            ? entry
            : null;

    /// <summary>
    /// Tries to map a method call. <paramref name="typeArgumentNames"/> feeds
    /// <c>$T0</c>, <c>$T1</c>, … template placeholders; pass an empty list for
    /// non-generic calls. <paramref name="typeArguments"/>, when given, are the
    /// call's type arguments as types, which the placeholders print in
    /// preference to the names (a primitive or a constructed type has no name).
    /// </summary>
    public static TsExpression? TryMapCall(
        IrCallExpression call,
        TsExpression? loweredReceiver,
        IReadOnlyList<TsExpression> loweredArgs,
        IReadOnlyList<string> typeArgumentNames,
        DeclarativeMappingRegistry registry,
        IReadOnlyList<TsType>? typeArguments = null
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

            // TODO(#657): the extractor's `LowersByDeclarativeMapping` port
            // applies this overload filter to the same argument count but
            // not the `WhenArg0StringEquals` filter below; one shared
            // per-overload predicate should answer both. When the extractor
            // drops a null comparer, the port receives the comparer-less
            // argument count but the parameter types of the overload that
            // still has the comparer; one per-overload predicate (count,
            // parameter types and `WhenArg0StringEquals`) must replace both.
            if (!candidate.MatchesOverload(loweredArgs.Count, call.Origin.ParameterTypes))
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

        var receiverType = call.Target is IrMemberAccess { Target: var irReceiver }
            ? ReceiverTypeOf(irReceiver, loweredReceiver)
            : null;

        return ApplyMethodMapping(
            match,
            receiver,
            receiverType,
            loweredArgs,
            typeArgumentNames,
            typeArguments ?? []
        );
    }

    /// <summary>
    /// Tries to map <c>new T(args)</c> through a declarative constructor
    /// mapping (<c>[MapMethod(typeof(T), ".ctor", JsTemplate = …)]</c>),
    /// selecting the overload by its argument count and parameter types.
    /// <paramref name="typeArguments"/> feed the <c>$T0</c>, <c>$T1</c>, …
    /// placeholders with the constructed type's type arguments. Only
    /// <c>JsTemplate</c> entries apply (a constructor has no member to rename),
    /// and an entry whose template reads an argument the call site does not
    /// supply is skipped rather than printing a raw <c>$N</c>. Returns
    /// <c>null</c> when no JS-side entry matches.
    /// </summary>
    public static TsTemplate? TryMapConstruction(
        IrNewExpression creation,
        IReadOnlyList<TsExpression> loweredArgs,
        IReadOnlyList<TsType> typeArguments,
        DeclarativeMappingRegistry registry
    )
    {
        if (creation.Origin is not { } origin)
            return null;

        if (
            !registry.TryGetMethodsByFullName(
                origin.DeclaringTypeFullName,
                origin.MemberName,
                out var candidates
            )
        )
            return null;

        var match = candidates.FirstOrDefault(candidate =>
            IsApplicableConstruction(candidate, loweredArgs.Count, origin.ParameterTypes)
        );

        return match is null
            ? null
            : new TsTemplate(match.JsTemplate!, Receiver: null, loweredArgs)
            {
                TypeArguments = typeArguments,
                RuntimeImports = match.RuntimeImportsList,
            };
    }

    private static bool IsApplicableConstruction(
        DeclarativeMappingEntry candidate,
        int argumentCount,
        IReadOnlyList<string>? parameterTypes
    ) =>
        candidate.HasTemplate
        && candidate.HasJsMapping
        && candidate.MatchesOverload(argumentCount, parameterTypes)
        && ReadsOnlySuppliedArguments(candidate.JsTemplate!, argumentCount);

    private static bool ReadsOnlySuppliedArguments(string template, int argumentCount) =>
        TsTemplatePlaceholder
            .ReferencedArgumentIndices(template)
            .All(index => index < argumentCount);

    /// <summary>
    /// The type a static mapping's receiver names, as the template's <c>$Type</c>
    /// placeholder prints it: the exported, canonical name, never a <c>using</c> alias
    /// of it. <c>null</c> unless the IR receiver is a type reference (the same fact an
    /// <c>[Emit]</c> template's <c>$Type</c> reads).
    /// </summary>
    private static TsExpression? ReceiverTypeOf(IrExpression irReceiver, TsExpression? receiver) =>
        (irReceiver, receiver) switch
        {
            (IrTypeReference, TsTypeReference reference) => new TsTypeReference(
                CanonicalTypeName(reference.Name),
                reference.Origin
            ),
            (IrTypeReference, TsIdentifier identifier) => new TsIdentifier(
                CanonicalTypeName(identifier.Name)
            ),
            _ => null,
        };

    private static string CanonicalTypeName(string name) =>
        IrToTsTypeMapper.UsingAliases?.AliasToCanonical.GetValueOrDefault(name) ?? name;

    private static TsExpression ApplyPropertyMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        TsExpression? receiverType
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(mapping.JsTemplate!, receiver, Arguments: [])
            {
                RuntimeImports = mapping.RuntimeImportsList,
                ReceiverType = receiverType,
            };

        var name = mapping.JsName!;
        return receiver is not null ? new TsPropertyAccess(receiver, name) : new TsIdentifier(name);
    }

    private static TsExpression ApplyMethodMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        TsExpression? receiverType,
        IReadOnlyList<TsExpression> args,
        IReadOnlyList<string> typeArgumentNames,
        IReadOnlyList<TsType> typeArguments
    )
    {
        if (mapping.HasTemplate)
            return new TsTemplate(mapping.JsTemplate!, receiver, args)
            {
                TypeArguments = typeArguments,
                TypeArgumentNames = typeArgumentNames,
                RuntimeImports = mapping.RuntimeImportsList,
                ReceiverType = receiverType,
            };

        var name = mapping.JsName!;
        var callee = receiver is not null
            ? (TsExpression)new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
        return new TsCallExpression(callee, args);
    }
}
