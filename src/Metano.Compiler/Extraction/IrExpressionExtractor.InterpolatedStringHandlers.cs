using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// Lowers an interpolated string (or a <c>+</c> chain of them) that C#
/// converts to a custom interpolated-string handler (FR-112) to the pattern
/// Roslyn bound: the handler construction with Roslyn's
/// <c>literalLength</c> / <c>formattedCount</c> constants and the
/// <c>[InterpolatedStringHandlerArgument]</c> values, then one call per part,
/// in source order, to the exact <c>AppendLiteral</c> / <c>AppendFormatted</c>
/// overload Roslyn bound, then the handler. It is built from existing IR
/// only: an <see cref="IrLetExpression"/> holds the handler and an
/// <see cref="IrSequenceExpression"/> runs the appends and yields it, so a
/// target hoists it where the conversion is the statement's own value and
/// keeps it in place everywhere else. <c>bool</c>-returning appends form one
/// <c>&amp;&amp;</c> chain, so a <c>false</c> result skips the remaining parts.
/// A BCL handler keeps the template-literal lowering (FR-051).
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string HandlerConversionKind = "InterpolatedStringHandler";

    private const string NonRepeatableHandlerArgument =
        "a handler argument is not a repeatable read (a local, a parameter, 'this', a constant "
        + "or a field of those), or an argument with a side effect runs between it and the "
        + "handler; bind it to a local first";

    /// <summary>
    /// The lowered handler conversion <paramref name="expression"/> performs,
    /// or <c>null</c> when it performs none or converts to a BCL handler.
    /// </summary>
    private IrExpression? InterpolatedStringHandlerOf(ExpressionSyntax expression)
    {
        if (
            expression
            is not (
                InterpolatedStringExpressionSyntax
                or BinaryExpressionSyntax
                or CastExpressionSyntax
            )
        )
            return null;

        if (
            HandlerCreationOf(expression) is not { Type: INamedTypeSymbol handlerType } creation
            || handlerType.ContainingAssembly.IsBclAssembly()
        )
            return null;

        if (HandlerProblemOf(creation, handlerType) is { } problem)
            return ReportUnsupportedHandler(expression, handlerType, problem);

        var construction = (IObjectCreationOperation)creation.HandlerCreation;

        return PlaceholderValuesOf(creation, construction) is { } placeholders
            ? LowerHandler(creation, construction, placeholders)
            : ReportUnsupportedHandler(expression, handlerType, NonRepeatableHandlerArgument);
    }

    /// <summary>
    /// The handler creation <paramref name="expression"/> is: an explicit cast
    /// binds it to the cast, an implicit conversion is the parent of the
    /// outermost interpolated string or <c>+</c> chain.
    /// </summary>
    private IInterpolatedStringHandlerCreationOperation? HandlerCreationOf(
        ExpressionSyntax expression
    ) =>
        _semantic.GetOperation(expression) switch
        {
            IInterpolatedStringHandlerCreationOperation creation => creation,
            (IInterpolatedStringOperation or IInterpolatedStringAdditionOperation)
            and { Parent: IInterpolatedStringHandlerCreationOperation creation } => creation,
            _ => null,
        };

    /// <summary>
    /// Why the handler conversion cannot be lowered, or <c>null</c> when its
    /// construction and every append can (its placeholders aside).
    /// </summary>
    private string? HandlerProblemOf(
        IInterpolatedStringHandlerCreationOperation creation,
        INamedTypeSymbol handlerType
    )
    {
        if (!_context.LowersInterpolatedStringHandlers)
            return $"the {TargetName} target does not lower handlers";

        if (
            creation.HandlerCreation
            is not IObjectCreationOperation { Constructor: { } constructor }
        )
            return "its construction is not a constructor call";

        if (creation.HandlerCreationHasSuccessParameter)
        {
            return "its constructor takes the trailing 'out bool' parameter, which a constructor "
                + "cannot lower";
        }

        if (!IsLowerableConstruction(handlerType, constructor))
            return "the handler type is not emitted or imported";

        return
            AppendsOf(creation.Content).FirstOrDefault(part => !IsLowerableAppend(part))
                is { } unsupported
            ? $"the part '{unsupported.Syntax}' calls an [Emit] or [Inline] member or binds dynamically"
            : null;
    }

    /// <summary>
    /// A handler the target constructs and calls positionally: its type is
    /// emitted or imported, and it is neither a <c>[PlainObject]</c> (an object
    /// literal has no methods) nor constructed through an <c>[ObjectArgs]</c>
    /// factory.
    /// </summary>
    private bool IsLowerableConstruction(INamedTypeSymbol handlerType, IMethodSymbol constructor)
    {
        var definition = handlerType.OriginalDefinition;

        return (
                IrTypeRefMapper.IsTranspilableType(definition, _target)
                || SymbolHelper.HasImport(definition)
            )
            && !SymbolHelper.HasPlainObject(definition)
            && !IrConstructorExtractor.HasObjectArgsSurface(constructor);
    }

    /// <summary>The parts of the handler content, in source order, across a <c>+</c> chain.</summary>
    private static IEnumerable<IOperation> AppendsOf(IOperation content) =>
        content switch
        {
            IInterpolatedStringAdditionOperation addition => AppendsOf(addition.Left)
                .Concat(AppendsOf(addition.Right)),
            IInterpolatedStringOperation interpolation => interpolation.Parts,
            _ => [content],
        };

    /// <summary>
    /// An append the lowering calls as written: bound statically, to a method
    /// without a call-site template. Roslyn binds an append only to an instance
    /// method of the handler, never to an extension (CS1061).
    /// </summary>
    private static bool IsLowerableAppend(IOperation part) =>
        part is IInterpolatedStringAppendOperation { AppendCall: IInvocationOperation append }
        && !SymbolHelper.HasEmit(append.TargetMethod)
        && !SymbolHelper.HasInline(append.TargetMethod);

    /// <summary>
    /// The value each <c>[InterpolatedStringHandlerArgument]</c> placeholder of
    /// <paramref name="construction"/> passes, keyed by the placeholder, or
    /// <c>null</c> when one cannot be read again at the handler: C# evaluates
    /// the receiver and the arguments once, before the handler, so the lowering
    /// reads each referenced value again, which is only sound for a repeatable
    /// read with no side effect between its evaluation and the handler.
    /// </summary>
    private Dictionary<IOperation, IrExpression>? PlaceholderValuesOf(
        IInterpolatedStringHandlerCreationOperation creation,
        IObjectCreationOperation construction
    )
    {
        var values = new Dictionary<IOperation, IrExpression>();
        var handlerArgument = creation.Parent as IArgumentOperation;
        var call = handlerArgument?.Parent;
        var callArguments = WrittenArgumentsOf(call) ?? [];
        var handlerIndex = handlerArgument is null ? -1 : callArguments.IndexOf(handlerArgument);
        var firstReadIndex = handlerIndex;

        foreach (var argument in construction.Arguments)
        {
            if (PlaceholderOf(argument) is not { } placeholder)
                continue;

            if (
                handlerIndex < 0
                || !IsPassedAsIs(argument.Value)
                || ReferencedOperationOf(placeholder, call!, callArguments) is not { } referenced
                || !IsRepeatableOperation(referenced)
            )
                return null;

            firstReadIndex = Math.Min(firstReadIndex, ReadIndexOf(placeholder));
            values[argument.Value] = ExtractReferencedValue(referenced);
        }

        var between = callArguments.Skip(firstReadIndex).Take(handlerIndex - firstReadIndex);

        return between.All(argument => IsRepeatableOperation(argument.Value)) ? values : null;
    }

    private static IInterpolatedStringHandlerArgumentPlaceholderOperation? PlaceholderOf(
        IArgumentOperation argument
    ) =>
        argument.Value switch
        {
            IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder => placeholder,
            IConversionOperation
            {
                Operand: IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder,
            } => placeholder,
            _ => null,
        };

    /// <summary>
    /// Whether the constructor receives the placeholder's value unchanged: no
    /// conversion, or one that keeps the value (identity, reference).
    /// </summary>
    private static bool IsPassedAsIs(IOperation value) =>
        value is not IConversionOperation conversion
        || conversion.Conversion is { IsIdentity: true } or { IsReference: true };

    /// <summary>
    /// The call's receiver or argument a placeholder stands for, or <c>null</c>
    /// for the trailing validity placeholder and a receiver the call has not.
    /// </summary>
    private static IOperation? ReferencedOperationOf(
        IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder,
        IOperation call,
        System.Collections.Immutable.ImmutableArray<IArgumentOperation> callArguments
    ) =>
        placeholder.PlaceholderKind switch
        {
            InterpolatedStringArgumentPlaceholderKind.CallsiteReceiver => (
                call as IInvocationOperation
            )?.Instance,
            InterpolatedStringArgumentPlaceholderKind.CallsiteArgument
                when placeholder.ArgumentIndex < callArguments.Length => callArguments[
                placeholder.ArgumentIndex
            ].Value,
            _ => null,
        };

    /// <summary>
    /// The position in the call's evaluation order after which the referenced
    /// value is read: the receiver runs before every argument.
    /// </summary>
    private static int ReadIndexOf(
        IInterpolatedStringHandlerArgumentPlaceholderOperation placeholder
    ) =>
        placeholder.PlaceholderKind is InterpolatedStringArgumentPlaceholderKind.CallsiteArgument
            ? placeholder.ArgumentIndex + 1
            : 0;

    /// <summary>
    /// An evaluation that reads the same value again and runs no code: a
    /// constant, a local, a parameter, <c>this</c>, a <c>default</c>, a field of
    /// a static type or of a repeatable receiver, and a conversion with no
    /// user-defined operator over one of those.
    /// </summary>
    private static bool IsRepeatableOperation(IOperation operation) =>
        operation.ConstantValue.HasValue
        || operation switch
        {
            ILocalReferenceOperation
            or IParameterReferenceOperation
            or IInstanceReferenceOperation
            or IDefaultValueOperation => true,
            IFieldReferenceOperation field => field.Instance is null
                || IsRepeatableOperation(field.Instance),
            IConversionOperation { OperatorMethod: null } conversion => IsRepeatableOperation(
                conversion.Operand
            ),
            _ => false,
        };

    /// <summary>
    /// The IR of a referenced receiver or argument: an implicit <c>this</c>
    /// receiver has no syntax of its own.
    /// </summary>
    private IrExpression ExtractReferencedValue(IOperation referenced) =>
        referenced switch
        {
            IInstanceReferenceOperation { IsImplicit: true } => new IrThisExpression(),
            { Syntax: ExpressionSyntax syntax } => Extract(syntax),
            _ => throw new InvalidOperationException(
                $"A handler argument has unexpected syntax '{referenced.Syntax}'."
            ),
        };

    /// <summary>
    /// <c>let h = new H(…) in (h.append…(…), …, h)</c>, the appends chained
    /// with <c>&amp;&amp;</c> when they return <c>bool</c>.
    /// </summary>
    private IrLetExpression LowerHandler(
        IInterpolatedStringHandlerCreationOperation creation,
        IObjectCreationOperation construction,
        IReadOnlyDictionary<IOperation, IrExpression> placeholders
    )
    {
        var name = _temporaries.Next("handler$temp$");
        var handler = new IrIdentifier(name);

        List<IrExpression> appends =
        [
            .. AppendsOf(creation.Content)
                .Cast<IInterpolatedStringAppendOperation>()
                .Select(append =>
                    HandlerAppendCall(handler, (IInvocationOperation)append.AppendCall)
                ),
        ];

        IReadOnlyList<IrExpression> effects =
            creation.HandlerAppendCallsReturnBool && appends.Count > 0
                ?
                [
                    appends.Aggregate(
                        (chain, next) => new IrBinaryExpression(chain, IrBinaryOp.LogicalAnd, next)
                    ),
                ]
                : appends;

        return new IrLetExpression(
            name,
            TypedHandlerConstruction(construction, placeholders),
            new IrSequenceExpression(effects, handler)
        );
    }

    /// <summary>
    /// The construction asserted to the constructed handler type when that
    /// type is generic: the constructor parameters (the counts, the handler
    /// arguments) seldom mention a type parameter, so a target that infers type
    /// arguments from the arguments could not, and the temporary holding the
    /// handler has no declared type to infer them from.
    /// </summary>
    private IrExpression TypedHandlerConstruction(
        IObjectCreationOperation construction,
        IReadOnlyDictionary<IOperation, IrExpression> placeholders
    )
    {
        var creation = HandlerConstruction(construction, placeholders);

        return creation.TypeArguments is { Count: > 0 }
            ? new IrCastExpression(creation, creation.Type, IsAssertion: true)
            : creation;
    }

    /// <summary>
    /// <c>new H(literalLength, formattedCount, …)</c> with Roslyn's bound
    /// arguments in parameter order, through the resolved overload's factory
    /// when the handler's constructors are dispatched.
    /// </summary>
    private IrNewExpression HandlerConstruction(
        IObjectCreationOperation construction,
        IReadOnlyDictionary<IOperation, IrExpression> placeholders
    )
    {
        var constructor = construction.Constructor!;
        var handlerType = constructor.ContainingType;
        var site = construction.Syntax;

        var arguments = construction
            .Arguments.OrderBy(argument => argument.Parameter!.Ordinal)
            .Select(argument => new IrArgument(
                placeholders.GetValueOrDefault(argument.Value) ?? HandlerArgumentValueOf(argument),
                Type: MapTypeWithReifiedScope(argument.Parameter!.Type, site)
            ))
            .ToList();

        var overload = DispatchedConstructorCalls.ConstructionTarget(constructor, _context)
            is { } target
            ? new IrSelectedOverload(target)
            : null;

        return new IrNewExpression(
            IrTypeRefMapper.Map(handlerType, _originResolver, _target),
            arguments,
            Origin: BuildOrigin(constructor),
            TypeArguments: ConstructedTypeArguments(handlerType),
            Overload: overload
        )
        {
            ReifiedTypeArguments = _context.ReifiedTypeAnalysis.ReifiedTypeArgumentsForNew(
                handlerType,
                type => MapTypeWithReifiedScope(type, site)
            ),
            ReifiedArgumentPosition = constructor.Parameters.Length,
        };
    }

    /// <summary>
    /// <c>handler.Append…(…)</c> bound to the overload Roslyn picked, with its
    /// arguments in parameter order: the literal text, the hole value, the
    /// alignment and the format as Roslyn bound them, an omitted optional
    /// parameter as its default.
    /// </summary>
    private IrCallExpression HandlerAppendCall(IrIdentifier handler, IInvocationOperation append)
    {
        var method = append.TargetMethod;
        var origin = BuildOrigin(method);

        var overload = DispatchedMethodCalls.RefOf(method, _context) is { } reference
            ? MarkSynthesized(new IrSelectedOverload(reference), method)
            : null;

        var arguments = append
            .Arguments.OrderBy(argument => argument.Parameter!.Ordinal)
            .Select(argument => new IrArgument(
                HandlerArgumentValueOf(argument),
                Type: MapTypeWithReifiedScope(argument.Parameter!.Type, append.Syntax)
            ))
            .ToList();

        var reified = _context.ReifiedTypeAnalysis.ReifiedTypeArgumentsForCall(method, type =>
            MapTypeWithReifiedScope(type, append.Syntax)
        );

        return new IrCallExpression(
            new IrMemberAccess(handler, method.Name, origin) { Overload = overload },
            arguments,
            MethodTypeArgumentsOf(method),
            origin
        )
        {
            Overload = overload,
            ReifiedTypeArguments = reified,
            ReifiedArgumentPosition = reified is { Count: > 0 }
                ? _context.ReifiedTypeAnalysis.ReifiedParametersOf(method)[0].Position
                : 0,
        };
    }

    /// <summary>
    /// The value Roslyn bound to one argument of the handler construction or
    /// of an append: an omitted optional parameter's default, a constant with
    /// no expression of its own (the counts, the literal text, the format),
    /// else the argument's expression (the hole value, the alignment).
    /// </summary>
    private IrExpression HandlerArgumentValueOf(IArgumentOperation argument)
    {
        if (argument.ArgumentKind == ArgumentKind.DefaultValue)
            return BoundDefaultOf(argument);

        var constant = argument.Value.ConstantValue;

        if (
            constant.HasValue
            && (argument.Value.IsImplicit || argument.Syntax is not ExpressionSyntax)
        )
            return BuildLiteralForDefault(constant.Value, argument.Parameter!.Type);

        return argument.Syntax is ExpressionSyntax expression
            ? Extract(expression)
            : ReportUnsupported(argument.Syntax, HandlerConversionKind);
    }

    private IrUnsupportedExpression ReportUnsupportedHandler(
        ExpressionSyntax expression,
        INamedTypeSymbol handlerType,
        string reason
    )
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedFeature,
                $"Interpolated string converted to the handler '{handlerType.ToDisplayString()}' "
                    + $"is not supported: {reason} (FR-112).",
                expression.GetLocation()
            )
        );

        return new IrUnsupportedExpression(HandlerConversionKind)
        {
            ResultType = IrTypeRefMapper.Map(handlerType, _originResolver, _target),
        };
    }
}
