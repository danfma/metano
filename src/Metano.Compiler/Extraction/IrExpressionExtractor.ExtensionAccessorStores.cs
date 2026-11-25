using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Stores through the accessor helpers of a C# 14 extension property or
/// indexer (#156): <c>r.P = v</c> and <c>r[i] = v</c> lower to
/// <c>p$set(r, v)</c> / <c>item$set(r, i, v)</c>, and every compound form,
/// <c>??=</c> and <c>++</c> / <c>--</c> read the current value through the
/// matching <c>$get</c> helper. The receiver and the indices are evaluated
/// once, as C# evaluates them.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// Builds <c>receiver.Prop op= value</c> (any assignment form) against an
    /// extension property. A static extension property has no receiver: its
    /// helpers take only the value.
    /// </summary>
    private IrExpression BuildExtensionPropertyAssignment(
        MemberAccessExpressionSyntax targetSyntax,
        IPropertySymbol property,
        ExtensionPropertyLowering lowering,
        AssignmentExpressionSyntax assign
    )
    {
        var setterName = IrExtensionConventions.SetterName(property);
        var emittedSetterName = ResolvePropertySetterEmittedName(property);
        var isValueUsed = !IsValueUnused(assign);

        IrExpression Accessor(string name, string? emittedName, IReadOnlyList<IrExpression> args) =>
            BuildExtensionHelperCall(
                lowering.HelperContainer,
                name,
                emittedName,
                args.Select(arg => new IrArgument(arg)).ToList(),
                typeArguments: null
            );

        IrExpression Store(IReadOnlyList<IrExpression> receiver) =>
            BuildExtensionAccessorStore(
                assign,
                property,
                () => Accessor(lowering.HelperName, lowering.EmittedName, receiver),
                value => Accessor(setterName, emittedSetterName, [.. receiver, value]),
                isValueUsed
            );

        if (lowering.IsStatic)
            return Store([]);

        var receiver = Extract(targetSyntax.Expression);

        return ReadsCurrentValue(assign)
            ? BindReceiverOnce(receiver, bound => Store([bound]))
            : Store([receiver]);
    }

    /// <summary>
    /// The inner write of a null-conditional assignment (<c>r?.Prop op= v</c>)
    /// against an extension property, or <c>null</c> when the target is not
    /// one. The receiver is already reusable (the null test repeats it), and
    /// the value of the whole conditional access is not the stored one, so
    /// the store yields nothing.
    /// </summary>
    private IrExpression? TryBuildExtensionPropertyAssignmentForNullConditional(
        IrExpression receiver,
        AssignmentExpressionSyntax assign
    )
    {
        if (
            assign.Left is not MemberBindingExpressionSyntax binding
            || _semantic.GetSymbolInfo(binding).Symbol is not IPropertySymbol property
            || TryResolveExtensionPropertyLoweringForSymbol(property) is not { } lowering
        )
            return null;

        var setterName = IrExtensionConventions.SetterName(property);
        var emittedSetterName = ResolvePropertySetterEmittedName(property);

        return BuildExtensionAccessorStore(
            assign,
            property,
            () =>
                BuildExtensionHelperCall(
                    lowering.HelperContainer,
                    lowering.HelperName,
                    lowering.EmittedName,
                    [new IrArgument(receiver)],
                    typeArguments: null
                ),
            value =>
                BuildExtensionHelperCall(
                    lowering.HelperContainer,
                    setterName,
                    emittedSetterName,
                    [new IrArgument(receiver), new IrArgument(value)],
                    typeArguments: null
                ),
            isValueUsed: false
        );
    }

    /// <summary>
    /// Builds <c>receiver[i] op= value</c> (any assignment form) against an
    /// extension indexer: <c>item$set(r, i, value)</c>, with the slot read
    /// through <c>item$get(r, i)</c> when the store needs its current value.
    /// </summary>
    private IrExpression BuildExtensionIndexerAssignment(
        ElementAccessExpressionSyntax targetSyntax,
        IPropertySymbol indexer,
        ExtensionPropertyLowering lowering,
        AssignmentExpressionSyntax assign
    )
    {
        var accessors = ExtensionIndexerAccessors.Of(indexer, lowering, this);
        var isValueUsed = !IsValueUnused(assign);

        IrExpression Store(IrExpression receiver, IReadOnlyList<IrExpression> indices) =>
            BuildExtensionAccessorStore(
                assign,
                indexer,
                () => accessors.Read(receiver, indices),
                value => accessors.Write(receiver, indices, value),
                isValueUsed
            );

        var receiver = Extract(targetSyntax.Expression);
        var indices = ExtractIndices(targetSyntax);

        return ReadsCurrentValue(assign)
            ? BindIndexerTargetOnce(receiver, indices, Store)
            : Store(receiver, indices);
    }

    /// <summary>
    /// Lowers <c>receiver[i]++</c> / <c>receiver[i]--</c> against an
    /// extension indexer to <c>item$set(r, i, item$get(r, i) ± 1)</c>, lifted
    /// for a <c>Nullable&lt;T&gt;</c> slot (see
    /// <see cref="ExtensionIncrementValue"/>), yielding the stored (prefix) or
    /// previous (postfix) value when it is used
    /// (see <see cref="StoreIncrementThroughSetter"/>).
    /// </summary>
    private IrExpression BuildExtensionIndexerIncrement(
        ExpressionSyntax node,
        ElementAccessExpressionSyntax targetSyntax,
        IPropertySymbol indexer,
        ExtensionPropertyLowering lowering,
        IrUnaryOp op
    )
    {
        if (indexer.SetMethod is null)
            return new IrUnsupportedExpression("ExtensionIndexerIncrementWithoutSetter");

        var accessors = ExtensionIndexerAccessors.Of(indexer, lowering, this);
        var binaryOp = StepOperatorOf(op);
        var one = OneOf(targetSyntax);
        var receiver = Extract(targetSyntax.Expression);
        var indices = ExtractIndices(targetSyntax);

        return BindIndexerTargetOnce(receiver, indices, (boundReceiver, boundIndices) =>
            StoreIncrementThroughSetter(
                node,
                accessors.Read(boundReceiver, boundIndices),
                current => ExtensionIncrementValue(node, indexer, current, binaryOp, one),
                value => accessors.Write(boundReceiver, boundIndices, value)
            )
        );
    }

    /// <summary>
    /// The store <paramref name="assign"/> performs through an extension
    /// accessor pair: <c>write(value)</c> for <c>=</c>, the compound value
    /// over <c>read()</c> for <c>op=</c>, and the coalesce
    /// <c>read() ?? write(value)</c> for <c>??=</c>, which, as in C#, calls
    /// the setter only when the current value is <c>null</c>. When the
    /// assignment's value is used, the store yields the stored value.
    /// </summary>
    private IrExpression BuildExtensionAccessorStore(
        AssignmentExpressionSyntax assign,
        IPropertySymbol property,
        Func<IrExpression> read,
        Func<IrExpression, IrExpression> write,
        bool isValueUsed
    )
    {
        if (assign.IsKind(SyntaxKind.CoalesceAssignmentExpression))
            return new IrBinaryExpression(
                read(),
                IrBinaryOp.NullCoalescing,
                StoreThroughSetter(Extract(assign.Right), write, isValueUsed)
            );

        return StoreThroughSetter(ExtensionStoredValue(assign, property, read), write, isValueUsed);
    }

    /// <summary>
    /// The value an extension accessor store passes to the setter: the
    /// right-hand side for <c>=</c>; the three-valued result for a
    /// <c>bool?</c> <c>&amp;=</c> / <c>|=</c> / <c>^=</c>, as a <c>bool?</c>
    /// field stores it; otherwise the compound value, lifted for a
    /// <c>Nullable&lt;T&gt;</c> accessor (see <see cref="ExtensionCompoundValue"/>).
    /// </summary>
    private IrExpression ExtensionStoredValue(
        AssignmentExpressionSyntax assign,
        IPropertySymbol property,
        Func<IrExpression> read
    )
    {
        if (TryMapCompoundAssignmentToBinary(assign.Kind()) is not { } op)
            return Extract(assign.Right);

        if (IsNullableBooleanLogic(_semantic.GetOperation(assign), op))
            return LiftBooleanLogic(
                new OperatorOperand(read(), IsNullableValueOperand(assign.Left), assign.Left),
                OperandOf(assign.Right),
                op
            );

        var current = read();

        return ExtensionCompoundValue(
            assign,
            property,
            current,
            NarrowBuiltinOperator(op, assign),
            Extract(assign.Right)
        );
    }

    /// <summary>
    /// <c>write(value)</c>, followed by the stored value when the store's
    /// value is used: the setter helper returns nothing, so
    /// <c>(write(v), v)</c> yields what C# yields.
    /// </summary>
    private IrExpression StoreThroughSetter(
        IrExpression value,
        Func<IrExpression, IrExpression> write,
        bool isValueUsed
    ) =>
        isValueUsed
            ? BindValueOnce(value, stored => new IrSequenceExpression([write(stored)], stored))
            : write(value);

    /// <summary>
    /// Whether <paramref name="assign"/> reads its target before storing:
    /// every form but a plain <c>=</c>.
    /// </summary>
    private static bool ReadsCurrentValue(AssignmentExpressionSyntax assign) =>
        !assign.IsKind(SyntaxKind.SimpleAssignmentExpression);

    private List<IrExpression> ExtractIndices(ElementAccessExpressionSyntax access) =>
        access.ArgumentList.Arguments.Select(argument => Extract(argument.Expression)).ToList();

    /// <summary>
    /// Evaluates an indexer target's receiver and then each index once, in
    /// source order, when <paramref name="build"/> reads and writes the slot:
    /// a reusable one is repeated, anything else is let-bound
    /// (see <see cref="BindOnce"/>).
    /// </summary>
    private IrExpression BindIndexerTargetOnce(
        IrExpression receiver,
        IReadOnlyList<IrExpression> indices,
        Func<IrExpression, IReadOnlyList<IrExpression>, IrExpression> build
    ) =>
        BindReceiverOnce(receiver, boundReceiver =>
            BindEachValueOnce(indices, boundIndices => build(boundReceiver, boundIndices))
        );

    private IrExpression BindEachValueOnce(
        IReadOnlyList<IrExpression> values,
        Func<IReadOnlyList<IrExpression>, IrExpression> build
    )
    {
        if (values.Count == 0)
            return build([]);

        return BindValueOnce(values[0], first =>
            BindEachValueOnce(values.Skip(1).ToList(), rest => build([first, .. rest]))
        );
    }

    /// <summary>
    /// The <c>item$get</c> / <c>item$set</c> helper calls of one extension
    /// indexer, named by <see cref="IrExtensionConventions"/>.
    /// </summary>
    private sealed record ExtensionIndexerAccessors(
        IrExpressionExtractor Extractor,
        ExtensionPropertyLowering Lowering,
        string SetterName,
        string? EmittedSetterName
    )
    {
        public static ExtensionIndexerAccessors Of(
            IPropertySymbol indexer,
            ExtensionPropertyLowering lowering,
            IrExpressionExtractor extractor
        ) =>
            new(
                extractor,
                lowering,
                IrExtensionConventions.SetterName(indexer),
                extractor.ResolvePropertySetterEmittedName(indexer)
            );

        public IrExpression Read(IrExpression receiver, IReadOnlyList<IrExpression> indices) =>
            Call(Lowering.HelperName, Lowering.EmittedName, [receiver, .. indices]);

        public IrExpression Write(
            IrExpression receiver,
            IReadOnlyList<IrExpression> indices,
            IrExpression value
        ) => Call(SetterName, EmittedSetterName, [receiver, .. indices, value]);

        private IrCallExpression Call(
            string name,
            string? emittedName,
            IReadOnlyList<IrExpression> arguments
        ) =>
            Extractor.BuildExtensionHelperCall(
                Lowering.HelperContainer,
                name,
                emittedName,
                arguments.Select(argument => new IrArgument(argument)).ToList(),
                typeArguments: null
            );
    }
}
