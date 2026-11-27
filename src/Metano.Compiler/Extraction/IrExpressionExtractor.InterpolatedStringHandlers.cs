using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
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

    /// <summary>
    /// The lowered handler conversion <paramref name="expression"/> performs,
    /// or <c>null</c> when it performs none or converts to a BCL handler.
    /// </summary>
    private IrExpression? InterpolatedStringHandlerOf(ExpressionSyntax expression)
    {
        if (!MayConvertToHandler(expression))
            return null;

        if (
            HandlerCreationOf(expression) is not { Type: INamedTypeSymbol handlerType } creation
            || handlerType.IsBclInterpolatedStringHandler()
        )
            return null;

        if (HandlerProblemOf(creation, handlerType) is { } problem)
            return ReportUnsupportedHandler(expression, handlerType, problem);

        var construction = (IObjectCreationOperation)creation.HandlerCreation;

        var placeholders = HandlerArgumentValuesOf(creation, construction);

        return placeholders.Problem is { } argumentProblem
            ? ReportUnsupportedHandler(expression, handlerType, argumentProblem)
            : LowerHandler(creation, construction, placeholders.Values);
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

        if (ConstructionProblemOf(handlerType, constructor) is { } constructionProblem)
            return constructionProblem;

        return
            AppendsOf(creation.Content).FirstOrDefault(part => !IsLowerableAppend(part))
                is { } unsupported
            ? $"the part '{unsupported.Syntax}' calls an [Emit] or [Inline] member or binds dynamically"
            : null;
    }

    /// <summary>
    /// Why the target cannot construct the handler and call it positionally,
    /// or <c>null</c>: its type must be emitted or imported, not a
    /// <c>[PlainObject]</c> (an object literal has no methods), and not
    /// constructed through an <c>[ObjectArgs]</c> factory.
    /// </summary>
    private string? ConstructionProblemOf(INamedTypeSymbol handlerType, IMethodSymbol constructor)
    {
        var definition = handlerType.OriginalDefinition;

        if (
            !IrTypeRefMapper.IsTranspilableType(definition, _target)
            && !SymbolHelper.HasImport(definition)
        )
            return "the handler type is neither transpiled nor imported";

        if (SymbolHelper.HasPlainObject(definition))
            return "the handler type is a [PlainObject], whose object literal has no methods";

        return IrConstructorExtractor.HasObjectArgsSurface(constructor)
            ? "the handler constructor takes [ObjectArgs], whose factory takes no counts"
            : null;
    }

    /// <summary>
    /// The <c>[InterpolatedStringHandlerArgument]</c> values of the
    /// construction (<see cref="HandlerArgumentReads"/>). Each receiver or
    /// argument is lowered again, its diagnostics already reported by the call.
    /// </summary>
    private HandlerArgumentValues HandlerArgumentValuesOf(
        IInterpolatedStringHandlerCreationOperation creation,
        IObjectCreationOperation construction
    )
    {
        using var _ = ExtractionDiagnostics.Suppress();

        return HandlerArgumentReads.Of(creation, construction, ExtractReferencedValue);
    }

    /// <summary>
    /// The IR of a referenced receiver or argument. A <c>this</c> or
    /// <c>base</c> receiver, implicit or written, is the instance itself: a
    /// <c>base</c> receiver only selects the member the call binds.
    /// </summary>
    private IrExpression ExtractReferencedValue(IOperation referenced) =>
        referenced switch
        {
            IInstanceReferenceOperation => new IrThisExpression(),
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
    /// The construction, asserted to the constructed handler type when that
    /// type is generic: the temporary that holds the handler has no declared
    /// type, so the constructed type is stated explicitly.
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
    /// A syntactic pre-test that spares the semantic query for every other
    /// expression: an interpolated string, a cast of one, or a <c>+</c> whose
    /// right operand is one (an interpolated-string addition only adds
    /// interpolated strings).
    /// </summary>
    private static bool MayConvertToHandler(ExpressionSyntax expression) =>
        expression switch
        {
            InterpolatedStringExpressionSyntax => true,
            CastExpressionSyntax cast => IsInterpolatedOperand(cast.Expression),
            BinaryExpressionSyntax addition when addition.IsKind(SyntaxKind.AddExpression) =>
                IsInterpolatedOperand(addition.Right),
            _ => false,
        };

    private static bool IsInterpolatedOperand(ExpressionSyntax operand) =>
        operand switch
        {
            ParenthesizedExpressionSyntax parenthesized => IsInterpolatedOperand(
                parenthesized.Expression
            ),
            BinaryExpressionSyntax addition when addition.IsKind(SyntaxKind.AddExpression) =>
                IsInterpolatedOperand(addition.Right),
            _ => operand is InterpolatedStringExpressionSyntax,
        };
}
