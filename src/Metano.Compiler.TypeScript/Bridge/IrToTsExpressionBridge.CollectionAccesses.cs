using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The stage 011c collection accesses that have their own IR node (spec 011
/// contracts/runtime-collections.md § Indexer compound assignments, § Interface
/// receivers, § Compiler contract): element updates, <c>r[^n]</c> and the
/// array-to-interface conversion. Never an IIFE (FR-009): a part that must be
/// evaluated once is assigned inline to a temporary declared at the head of
/// the enclosing statement list (<see cref="TsTemporaries"/>), so C#'s
/// evaluation order (receiver, index, read, right-hand side, write) and every
/// enclosing short-circuit are kept.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    private const string CollectionsGuardMessage =
        "is not supported here: it needs temporaries, and this position has no statement "
        + "list to declare them in (a field or static initializer, a constructor-initializer "
        + "argument).";

    private static TsRuntimeHelperReference ListClass =>
        new(RuntimeCollections.NameOf(IrCollectionKind.List), RuntimeCollections.Module);

    /// <summary>
    /// The receiver and index of one collection access as the rendered shape
    /// reads them: each one either the original (reusable) expression or the
    /// temporary <see cref="Effects"/> assign it to, in source order.
    /// </summary>
    private sealed record BoundCollectionAccess(
        TsExpression Receiver,
        TsExpression Index,
        IReadOnlyList<TsExpression> Effects
    );

    /// <summary>
    /// <see cref="IrIndexerUpdateExpression"/>: in statement position with a
    /// reusable receiver and index, the direct form <c>r.set(i, r.get(i) op v)</c>;
    /// in expression position on a class receiver, <c>r.update(i, f)</c> /
    /// <c>r.updateReturningOld(i, f)</c> (never for <c>??=</c>, which must not
    /// write a non-null element, nor when the value awaits or yields, which a
    /// closure cannot hold); everywhere else the comma shapes over temporaries.
    /// </summary>
    internal static TsExpression MapIndexerUpdate(
        IrIndexerUpdateExpression update,
        IrToTsBridgeContext context,
        bool isStatement
    )
    {
        if (isStatement && IsDirectUpdate(update))
        {
            var receiver = Map(update.Receiver, context);
            var index = UpdateIndexOf(update, receiver, Map(update.Index, context));

            return Set(receiver, index, NewValueOver(update, Get(receiver, index), context));
        }

        if (!isStatement && UpdatesThroughClosure(update))
            return UpdateCall(update, context);

        if (context.Temporaries is not { } temporaries)
            return UnsupportedCollectionAccess("This element update");

        var shape = CommaUpdate(update, temporaries, context, isStatement);

        return isStatement ? shape : new TsParenthesized(shape);
    }

    /// <summary>
    /// <c>r[^n]</c> read: <c>r.fromEnd(n)</c> on a class, <c>r.get(r.count - n)</c>
    /// on an interface (no <c>fromEnd</c> member, W4-13), <c>fromEnd(arr, n)</c>
    /// on an array.
    /// </summary>
    private static TsExpression MapFromEndRead(
        IrFromEndElementAccess access,
        IrToTsBridgeContext context
    )
    {
        var offset = Map(access.Offset, context);

        if (access.ReceiverType is IrArrayTypeRef)
            return new TsCallExpression(
                new TsRuntimeHelperReference("fromEnd", RuntimeCollections.Module),
                [Map(access.Receiver, context), offset]
            );

        if (
            access.ReceiverType is IrCollectionTypeRef { Kind: var kind }
            && !RuntimeCollections.IsInterface(kind)
        )
            return new TsCallExpression(
                new TsPropertyAccess(Map(access.Receiver, context), "fromEnd"),
                [offset]
            );

        return WithBoundFromEnd(access, context, bound => Get(bound.Receiver, bound.Index));
    }

    /// <summary><c>r[^n] = v</c>: <c>r.set(r.count - n, v)</c>, the receiver bound once.</summary>
    private static TsExpression MapFromEndWrite(
        IrFromEndElementAccess access,
        IrExpression value,
        IrToTsBridgeContext context
    ) =>
        WithBoundFromEnd(access, context, bound =>
            Set(bound.Receiver, bound.Index, Map(value, context))
        );

    /// <summary>
    /// An array viewed as one of the list interfaces (FR-003):
    /// <c>List.adopt(arr, { fixedSize: true })</c>.
    /// </summary>
    private static TsExpression MapAdoptedArray(
        IrArrayToCollectionConversion conversion,
        IrToTsBridgeContext context
    ) => AdoptedFixedSize(Map(conversion.Value, context));

    /// <summary><c>List.adopt(array, { fixedSize: true })</c>.</summary>
    internal static TsExpression AdoptedFixedSize(TsExpression array) =>
        new TsCallExpression(
            new TsPropertyAccess(ListClass, "adopt"),
            [array, new TsObjectLiteral([new TsObjectProperty("fixedSize", new TsLiteral("true"))])]
        );

    private static TsExpression WithBoundFromEnd(
        IrFromEndElementAccess access,
        IrToTsBridgeContext context,
        Func<BoundCollectionAccess, TsExpression> render
    )
    {
        var receiverIsReusable = IrEvaluationTraits.IsReusableReceiver(access.Receiver);
        var offsetIsReusable = IrEvaluationTraits.IsSideEffectFree(access.Offset);

        if (receiverIsReusable && offsetIsReusable)
        {
            var receiver = Map(access.Receiver, context);

            return render(
                new BoundCollectionAccess(
                    receiver,
                    CountMinus(receiver, Map(access.Offset, context)),
                    []
                )
            );
        }

        if (context.Temporaries is not { } temporaries)
            return UnsupportedCollectionAccess("This index-from-end access");

        var bound = BindFromEnd(access.Receiver, access.Offset, temporaries, context);

        return new TsParenthesized(Comma([.. bound.Effects, render(bound)]));
    }

    private static bool IsDirectUpdate(IrIndexerUpdateExpression update) =>
        update.Result is not IrIndexerUpdateResult.Coalesce
        && IrEvaluationTraits.IsReusableReceiver(update.Receiver)
        && IrEvaluationTraits.IsSideEffectFree(update.Index)
        && ReadsOfCurrent(update) <= 1;

    private static bool UpdatesThroughClosure(IrIndexerUpdateExpression update) =>
        update.Result is not IrIndexerUpdateResult.Coalesce
        && !RuntimeCollections.IsInterface(update.ReceiverType.Kind)
        && !SuspendsInPlace(update.NewValue)
        && (
            !update.FromEnd
            || IrEvaluationTraits.IsReusableReceiver(update.Receiver)
                && IrEvaluationTraits.IsSideEffectFree(update.Index)
        );

    /// <summary><c>r.update(i, (v$) =&gt; v$ op v)</c> / <c>r.updateReturningOld(…)</c>.</summary>
    private static TsExpression UpdateCall(
        IrIndexerUpdateExpression update,
        IrToTsBridgeContext context
    )
    {
        var receiver = Map(update.Receiver, context);
        var index = UpdateIndexOf(update, receiver, Map(update.Index, context));

        var member =
            update.Result is IrIndexerUpdateResult.OldValue ? "updateReturningOld" : "update";

        var current = IrToTsNamingPolicy.ToLocalName(update.CurrentName);

        var step = new TsArrowFunction(
            [new TsParameter(current, Type: null)],
            [new TsReturnStatement(Map(update.NewValue, context))]
        );

        return new TsCallExpression(new TsPropertyAccess(receiver, member), [index, step]);
    }

    /// <summary>
    /// The comma shapes of contracts/runtime-collections.md § Indexer compound
    /// assignments, the value of the expression last (dropped in statement
    /// position).
    /// </summary>
    private static TsExpression CommaUpdate(
        IrIndexerUpdateExpression update,
        TsTemporaries temporaries,
        IrToTsBridgeContext context,
        bool isStatement
    )
    {
        var bound = update.FromEnd
            ? BindFromEnd(update.Receiver, update.Index, temporaries, context)
            : BindElement(update, temporaries, context);

        var effects = bound.Effects.ToList();
        var read = Get(bound.Receiver, bound.Index);

        if (update.Result is IrIndexerUpdateResult.Coalesce)
        {
            var written = temporaries.Declare("$t");

            var store = Comma([
                Set(bound.Receiver, bound.Index, Assign(written, Map(update.NewValue, context))),
                written,
            ]);

            return Comma([.. effects, new TsBinaryExpression(read, "??", store)]);
        }

        var readsOnce = ReadsOfCurrent(update) <= 1;

        if (isStatement && readsOnce)
            return Comma([
                .. effects,
                Set(bound.Receiver, bound.Index, NewValueOver(update, read, context)),
            ]);

        var old = temporaries.Declare("$o");

        var bindsOld = update.Result is IrIndexerUpdateResult.OldValue || !readsOnce;

        if (bindsOld)
            effects.Add(Assign(old, read));

        TsExpression current = bindsOld ? old : read;
        var newValue = NewValueOver(update, current, context);

        if (update.Result is IrIndexerUpdateResult.OldValue)
            return Comma([
                .. effects,
                Set(bound.Receiver, bound.Index, newValue),
                .. isStatement ? Array.Empty<TsExpression>() : [old],
            ]);

        effects.Add(
            Set(bound.Receiver, bound.Index, isStatement ? newValue : Assign(old, newValue))
        );

        return Comma([.. effects, .. isStatement ? Array.Empty<TsExpression>() : [old]]);
    }

    private static BoundCollectionAccess BindElement(
        IrIndexerUpdateExpression update,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var effects = new List<TsExpression>();

        var receiver = Bound(
            update.Receiver,
            IrEvaluationTraits.IsReusableReceiver(update.Receiver),
            "$r",
            temporaries,
            effects,
            context
        );

        var key = RuntimeCollections.IsKeyed(update.ReceiverType.Kind) ? "$k" : "$i";

        var index = Bound(
            update.Index,
            IrEvaluationTraits.IsSideEffectFree(update.Index),
            key,
            temporaries,
            effects,
            context
        );

        return new BoundCollectionAccess(receiver, index, effects);
    }

    /// <summary>
    /// The receiver and the absolute index <c>count - n</c> of <c>r[^n]</c>:
    /// C# evaluates the receiver, then <c>n</c>, then reads the count, so a
    /// non-reusable <c>n</c> is bound before the count is read, and the index
    /// is computed once.
    /// </summary>
    private static BoundCollectionAccess BindFromEnd(
        IrExpression receiverExpression,
        IrExpression offsetExpression,
        TsTemporaries temporaries,
        IrToTsBridgeContext context
    )
    {
        var effects = new List<TsExpression>();

        var receiver = Bound(
            receiverExpression,
            IrEvaluationTraits.IsReusableReceiver(receiverExpression),
            "$r",
            temporaries,
            effects,
            context
        );

        if (IrEvaluationTraits.IsSideEffectFree(offsetExpression))
            return new BoundCollectionAccess(
                receiver,
                CountMinus(receiver, Map(offsetExpression, context)),
                effects
            );

        var index = temporaries.Declare("$i");

        effects.Add(Assign(index, Map(offsetExpression, context)));
        effects.Add(Assign(index, CountMinus(receiver, index)));

        return new BoundCollectionAccess(receiver, index, effects);
    }

    private static TsExpression Bound(
        IrExpression value,
        bool isReusable,
        string stem,
        TsTemporaries temporaries,
        List<TsExpression> effects,
        IrToTsBridgeContext context
    )
    {
        var mapped = Map(value, context);

        if (isReusable)
            return mapped;

        var temporary = temporaries.Declare(stem);

        effects.Add(Assign(temporary, mapped));

        return temporary;
    }

    private static TsExpression UpdateIndexOf(
        IrIndexerUpdateExpression update,
        TsExpression receiver,
        TsExpression index
    ) => update.FromEnd ? CountMinus(receiver, index) : index;

    private static TsExpression NewValueOver(
        IrIndexerUpdateExpression update,
        TsExpression current,
        IrToTsBridgeContext context
    ) =>
        Map(
            update.NewValue,
            context with
            {
                BoundElements = context.BoundElements.SetItem(update.CurrentName, current),
            }
        );

    private static int ReadsOfCurrent(IrIndexerUpdateExpression update) =>
        CountIdentifier(update.NewValue, update.CurrentName);

    private static int CountIdentifier(IrExpression expression, string name) =>
        (expression is IrIdentifier identifier && identifier.Name == name ? 1 : 0)
        + IrNodeChildren.ExpressionsOf(expression).Sum(child => CountIdentifier(child, name));

    /// <summary>
    /// Whether <paramref name="expression"/> awaits or yields outside a nested
    /// lambda: a closure cannot hold it, so the update cannot be a callback.
    /// </summary>
    private static bool SuspendsInPlace(IrExpression expression) =>
        expression is IrAwaitExpression or IrYieldExpression
        || IrNodeChildren.ExpressionsOf(expression).Any(SuspendsInPlace);

    private static TsExpression CountMinus(TsExpression receiver, TsExpression offset) =>
        new TsBinaryExpression(new TsPropertyAccess(receiver, "count"), "-", offset);

    private static TsCallExpression Get(TsExpression receiver, TsExpression index) =>
        new(new TsPropertyAccess(receiver, "get"), [index]);

    private static TsCallExpression Set(
        TsExpression receiver,
        TsExpression index,
        TsExpression value
    ) => new(new TsPropertyAccess(receiver, "set"), [index, value]);

    private static TsBinaryExpression Assign(TsIdentifier target, TsExpression value) =>
        new(target, "=", value);

    private static TsExpression Comma(IReadOnlyList<TsExpression> parts) =>
        parts.Aggregate((left, right) => new TsBinaryExpression(left, ",", right));

    private static TsExpression UnsupportedCollectionAccess(string what)
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"{what} {CollectionsGuardMessage}"
            )
        );

        return MapUnsupported(new IrUnsupportedExpression("CollectionAccessWithoutStatementList"));
    }

    /// <summary>
    /// A collection initializer (FR-007): after the parameterless or the
    /// comparer constructor, the factory of the form (<c>List.of([a, b])</c>,
    /// <c>Dictionary.of([[k, v]], cmp)</c>, <c>Dictionary.assign([[k, v]])</c>);
    /// after any other constructor, the construction followed by
    /// <c>initAdd</c> / <c>initAssign</c>.
    /// </summary>
    private static TsExpression MapCollectionInitialization(
        IrCollectionInitialization initialization,
        IrToTsBridgeContext context
    )
    {
        var items = new TsArrayLiteral(
            initialization
                .Elements.Select(element =>
                    element is [var single]
                        ? Map(single, context)
                        : new TsArrayLiteral(element.Select(value => Map(value, context)).ToList())
                )
                .ToList()
        );

        var typedItems = initialization.ElementType is { } elementType
            ? new TsCastExpression(
                items,
                new TsArrayType(IrToTsTypeMapper.Map(elementType, context.TypeOverrides))
            )
            : (TsExpression)items;

        var isAssign = initialization.Form is IrCollectionInitializerForm.Assign;

        if (initialization.Construction is IrCollectionConstruction.Other)
            return new TsCallExpression(
                new TsPropertyAccess(
                    Map(initialization.Creation, context),
                    isAssign ? "initAssign" : "initAdd"
                ),
                [typedItems]
            );

        var factory = new TsPropertyAccess(
            new TsRuntimeHelperReference(
                RuntimeCollections.NameOf(initialization.Type.Kind),
                RuntimeCollections.Module
            ),
            isAssign ? "assign" : "of"
        );

        return new TsCallExpression(
            factory,
            initialization.Comparer is { } comparer
                ? [typedItems, Map(comparer, context)]
                : [typedItems]
        );
    }

    /// <summary>
    /// The literal a set collection expression or a <c>params</c>
    /// <c>HashSet&lt;T&gt;</c> argument builds its set from
    /// (<c>new HashSet(literal)</c> with no constructor symbol); <c>null</c>
    /// for any other construction.
    /// </summary>
    private static IrArrayLiteral? SetLiteralOf(IrNewExpression creation) =>
        creation
            is {
                Type: IrCollectionTypeRef { Kind: IrCollectionKind.HashSet },
                Origin: null,
                Arguments: [{ Value: IrArrayLiteral literal }],
            }
            ? literal
            : null;

    /// <summary><c>HashSet.of([a, b])</c>: duplicates collapse, as C# adds each element.</summary>
    private static TsExpression SetFactory(IrArrayLiteral literal, IrToTsBridgeContext context) =>
        new TsCallExpression(
            new TsPropertyAccess(
                new TsRuntimeHelperReference(
                    RuntimeCollections.NameOf(IrCollectionKind.HashSet),
                    RuntimeCollections.Module
                ),
                "of"
            ),
            [MapTypedArrayLiteral(literal, context)]
        );

    /// <summary>
    /// <c>r[a..^n]</c> on a runtime collection or an array: <c>r.slice(a, r.count - n)</c>
    /// (<c>length</c> for an array). C# evaluates the receiver, each bound, then
    /// reads the length, so a part that is not reusable is assigned to a
    /// temporary first, in that order, and no IIFE is emitted (FR-009).
    /// </summary>
    private static TsExpression MapFromEndSlice(
        IrSliceExpression slice,
        IrToTsBridgeContext context
    )
    {
        var parts = new IrExpression?[] { slice.Target, slice.Start, slice.End };

        var allReusable =
            IrEvaluationTraits.IsReusableReceiver(slice.Target)
            && parts.Skip(1).All(part => part is null || IrEvaluationTraits.IsSideEffectFree(part));

        if (!allReusable && context.Temporaries is null)
            return UnsupportedCollectionAccess("This range access");

        var effects = new List<TsExpression>();

        TsExpression BoundPart(IrExpression value, bool isReusable, string stem) =>
            allReusable
                ? Map(value, context)
                : Bound(value, isReusable, stem, context.Temporaries!, effects, context);

        var receiver = BoundPart(
            slice.Target,
            IrEvaluationTraits.IsReusableReceiver(slice.Target),
            "$r"
        );

        var length = slice.TargetType is IrArrayTypeRef ? "length" : "count";

        TsExpression? ResolvedBound(IrExpression? bound, bool fromEnd, string stem)
        {
            if (bound is null)
                return null;

            var value = BoundPart(bound, IrEvaluationTraits.IsSideEffectFree(bound), stem);

            return fromEnd
                ? new TsBinaryExpression(new TsPropertyAccess(receiver, length), "-", value)
                : value;
        }

        var start = ResolvedBound(slice.Start, slice.StartFromEnd, "$s");
        var end = ResolvedBound(slice.End, slice.EndFromEnd, "$e");

        var call = new TsCallExpression(
            new TsPropertyAccess(receiver, "slice"),
            (start, end) switch
            {
                (null, null) => [],
                ({ } from, null) => [from],
                (null, { } to) => [new TsLiteral("0"), to],
                ({ } from, { } to) => [from, to],
            }
        );

        return effects.Count == 0 ? call : new TsParenthesized(Comma([.. effects, call]));
    }
}
