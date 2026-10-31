using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers the arguments of an <see cref="IrExceptionConstruction"/> (a BCL
/// exception <c>new</c>, or the base call of a user exception deriving from
/// one) into the arguments of the JS <c>Error(message, { cause })</c>
/// constructor, and builds the <c>super(...)</c> call of any constructor.
/// <para>
/// The message emulates how .NET composes <c>Exception.Message</c>:
/// <list type="bullet">
///   <item>the <c>message</c> argument, or the type's default message when it
///     is absent or a <c>null</c> literal — and, when nullable analysis says
///     the argument may be <c>null</c>, <c>message ?? default</c>;</item>
///   <item><c>"\nObject name: 'x'."</c> for an <c>objectName</c>;</item>
///   <item><c>" (Parameter 'name')"</c> for a <c>paramName</c>;</item>
///   <item><c>"\nActual value was X."</c> for an <c>actualValue</c>.</item>
/// </list>
/// Suffixes are omitted for a <c>null</c> (or, for names, empty) literal. The
/// <c>innerException</c> becomes <c>{ cause }</c>. When the constructed class
/// overrides <c>Message</c>, no message is passed, so the native <c>Error</c>
/// installs no own <c>message</c> property that would shadow the override.
/// </para>
/// <para>
/// Evaluation order: the message composition reads the arguments in role
/// order and ignores unmapped ones. That is only safe when at most one
/// argument has side effects and it is still used exactly once; otherwise all
/// arguments are first evaluated once, in source order, into temporaries (an
/// IIFE for <c>new</c>, <c>const</c> declarations before <c>super</c>).
/// </para>
/// <para>
/// Known deviations: a non-literal <c>paramName</c>, <c>objectName</c> or
/// <c>actualValue</c> that is <c>null</c> / empty at runtime is not
/// suppressed, and an actual value is formatted with JS (not .NET)
/// <c>ToString</c> rules. Type tests stay coarse: every BCL exception type is
/// <c>Error</c> on the TS side, so <c>o is ArgumentException</c> lowers to
/// <c>instanceof Error</c> and typed <c>catch</c> clauses ignore their filter.
/// </para>
/// </summary>
public static class IrToTsExceptionConstructionBridge
{
    /// <summary>
    /// The ES private field holding the message a class that overrides
    /// <c>Message</c> composed for its native base (see
    /// <see cref="IrExceptionBaseMessage"/>).
    /// </summary>
    public const string BaseMessageField = "#baseMessage";

    private const string TemporaryPrefix = "__exceptionArg";

    /// <summary><c>this.#baseMessage</c>: the lowering of <c>base.Message</c>
    /// in a class that keeps its base message.</summary>
    public static TsExpression BaseMessageAccess() =>
        new TsPropertyAccess(new TsIdentifier("this"), BaseMessageField);

    /// <summary>
    /// The <c>#baseMessage</c> field declaration a class needs when any of its
    /// constructors keeps the base message; <c>null</c> otherwise.
    /// </summary>
    public static TsFieldMember? BaseMessageFieldFor(IrConstructorDeclaration? primary)
    {
        var constructors = primary is null ? [] : (primary.Overloads ?? []).Prepend(primary);

        var keepsBaseMessage = constructors.Any(ctor =>
            ctor.BaseExceptionConstruction is { IsMessageOverridden: true }
        );

        return keepsBaseMessage
            ? new TsFieldMember(
                BaseMessageField,
                new TsStringType(),
                Initializer: null,
                Readonly: true,
                Static: false,
                Accessibility: TsAccessibility.None
            )
            : null;
    }

    /// <summary>
    /// Lowers <c>new BclException(...)</c> to <c>new Error(...)</c>, wrapped in
    /// an IIFE that evaluates the arguments in source order when any of them
    /// may have side effects.
    /// </summary>
    public static TsExpression MapNewError(
        IrNewExpression creation,
        IrExceptionConstruction construction,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var operands = ErrorOperands.Bind(construction, creation.Arguments, bclRegistry);

        var newError = new TsNewExpression(
            new TsIdentifier("Error"),
            BuildErrorArguments(construction, operands)
        );

        if (operands.Temporaries.Count == 0)
            return newError;

        var evaluator = new TsArrowFunction(
            operands
                .Temporaries.Select(temporary => new TsParameter(temporary.Name, null))
                .ToList(),
            [new TsReturnStatement(newError)]
        );

        return new TsCallExpression(
            evaluator,
            operands.Temporaries.Select(temporary => temporary.Initializer).ToList()
        );
    }

    /// <summary>
    /// The base-constructor call of <paramref name="ctor"/>: role-mapped
    /// <c>Error</c> arguments when the base is a BCL exception, the plain base
    /// arguments otherwise. Returns <c>null</c> when the constructor passes
    /// nothing to its base.
    /// </summary>
    public static TsBaseConstructorCall? MapBaseArguments(
        IrConstructorDeclaration ctor,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var baseArguments = ctor.BaseArguments ?? [];

        if (ctor.BaseExceptionConstruction is { } construction)
        {
            var operands = ErrorOperands.Bind(construction, baseArguments, bclRegistry);

            var keptBaseMessage = construction.IsMessageOverridden
                ? ComposeMessage(construction, operands) ?? new TsStringLiteral("")
                : null;

            return new TsBaseConstructorCall(
                operands.Temporaries,
                BuildErrorArguments(construction, operands),
                keptBaseMessage
            );
        }

        if (baseArguments.Count == 0)
            return null;

        return new TsBaseConstructorCall(
            [],
            baseArguments.Select(a => IrToTsExpressionBridge.MapArgument(a, bclRegistry)).ToList()
        );
    }

    private static IReadOnlyList<TsExpression> BuildErrorArguments(
        IrExceptionConstruction construction,
        ErrorOperands operands
    )
    {
        var message = construction.IsMessageOverridden
            ? null
            : ComposeMessage(construction, operands);

        var cause = operands.SuppliedValueOf(IrExceptionArgumentRole.InnerException);

        if (cause is null)
            return message is null ? [] : [message];

        var options = new TsObjectLiteral([new TsObjectProperty("cause", cause.Lowered)]);

        return [message ?? new TsIdentifier("undefined"), options];
    }

    private static TsExpression? ComposeMessage(
        IrExceptionConstruction construction,
        ErrorOperands operands
    )
    {
        var composer = new ErrorMessageComposer();
        var objectName = operands.SuppliedValueOf(IrExceptionArgumentRole.ObjectName);
        var paramName = operands.SuppliedValueOf(IrExceptionArgumentRole.ParamName);
        var actualValue = operands.SuppliedValueOf(IrExceptionArgumentRole.ActualValue);

        AppendBaseMessage(composer, construction, operands);
        AppendNamedSuffix(composer, objectName, "\nObject name: '", "'.");
        AppendNamedSuffix(composer, paramName, " (Parameter '", "')");
        AppendActualValue(composer, actualValue);

        return composer.Build();
    }

    private static void AppendBaseMessage(
        ErrorMessageComposer composer,
        IrExceptionConstruction construction,
        ErrorOperands operands
    )
    {
        var message = operands.SuppliedValueOf(IrExceptionArgumentRole.Message);

        if (message is null)
        {
            if (construction.DefaultMessage is { } defaultMessage)
                composer.AppendText(defaultMessage);

            return;
        }

        if (construction.MessageMayBeNull)
        {
            composer.AppendValue(message with { Lowered = WithDefault(message, construction) });

            return;
        }

        composer.AppendValue(message);
    }

    private static TsExpression WithDefault(
        ErrorOperand message,
        IrExceptionConstruction construction
    ) =>
        new TsBinaryExpression(
            message.Lowered,
            "??",
            construction.DefaultMessage is { } defaultMessage
                ? new TsStringLiteral(defaultMessage)
                : new TsIdentifier("undefined")
        );

    private static void AppendNamedSuffix(
        ErrorMessageComposer composer,
        ErrorOperand? name,
        string prefix,
        string suffix
    )
    {
        if (name is null || ErrorOperands.Unwrapped(name.Source) is IrLiteral { Value: "" })
            return;

        composer.AppendText(prefix);
        composer.AppendValue(name);
        composer.AppendText(suffix);
    }

    private static void AppendActualValue(ErrorMessageComposer composer, ErrorOperand? actualValue)
    {
        if (actualValue is null)
            return;

        composer.AppendText("\nActual value was ");
        composer.AppendValue(actualValue);
        composer.AppendText(".");
    }

    /// <summary>
    /// The arguments of one exception construction, each lowered either
    /// inline (every argument side-effect free) or to a temporary holding its
    /// source-order evaluation.
    /// </summary>
    private sealed class ErrorOperands
    {
        private readonly IrExceptionConstruction _construction;
        private readonly IReadOnlyList<ErrorOperand> _operands;

        private ErrorOperands(
            IrExceptionConstruction construction,
            IReadOnlyList<ErrorOperand> operands,
            IReadOnlyList<TsVariableDeclaration> temporaries
        )
        {
            _construction = construction;
            _operands = operands;
            Temporaries = temporaries;
        }

        public IReadOnlyList<TsVariableDeclaration> Temporaries { get; }

        public static ErrorOperands Bind(
            IrExceptionConstruction construction,
            IReadOnlyList<IrArgument> arguments,
            DeclarativeMappingRegistry? bclRegistry
        )
        {
            var lowered = arguments
                .Select(argument => IrToTsExpressionBridge.Map(argument.Value, bclRegistry))
                .ToList();

            if (CanEvaluateInline(construction, arguments))
            {
                var inline = arguments
                    .Select((argument, index) => new ErrorOperand(argument.Value, lowered[index]))
                    .ToList();

                return new ErrorOperands(construction, inline, []);
            }

            var temporaries = lowered
                .Select((value, index) => new TsVariableDeclaration(TemporaryPrefix + index, value))
                .ToList();

            var viaTemporaries = arguments
                .Select(
                    (argument, index) =>
                        new ErrorOperand(argument.Value, new TsIdentifier(temporaries[index].Name))
                )
                .ToList();

            return new ErrorOperands(construction, viaTemporaries, temporaries);
        }

        /// <summary>
        /// The operand playing <paramref name="role"/>, or <c>null</c> when the
        /// construction does not supply it or supplies a <c>null</c> literal
        /// (which .NET treats as absent).
        /// </summary>
        public ErrorOperand? SuppliedValueOf(IrExceptionArgumentRole role)
        {
            for (var index = 0; index < _operands.Count; index++)
            {
                if (_construction.ArgumentRoles.ElementAtOrDefault(index) != role)
                    continue;

                return IsNullLiteral(Unwrapped(_operands[index].Source)) ? null : _operands[index];
            }

            return null;
        }

        /// <summary>The expression under any casts (<c>(string?)null</c>).</summary>
        public static IrExpression Unwrapped(IrExpression expression) =>
            expression is IrCastExpression cast ? Unwrapped(cast.Expression) : expression;

        private static bool IsNullLiteral(IrExpression expression) =>
            expression is IrLiteral { Kind: IrLiteralKind.Null or IrLiteralKind.Default };

        /// <summary>
        /// Inline lowering reorders arguments into role order and drops the
        /// unmapped ones (and the message, when overridden). That is
        /// unobservable when at most one argument has side effects and that
        /// argument is still evaluated exactly once.
        /// </summary>
        private static bool CanEvaluateInline(
            IrExceptionConstruction construction,
            IReadOnlyList<IrArgument> arguments
        )
        {
            var effectfulIndices = Enumerable
                .Range(0, arguments.Count)
                .Where(index => !IsSideEffectFree(arguments[index].Value))
                .ToList();

            return effectfulIndices switch
            {
                [] => true,
                [var index] => IsEvaluatedOnce(
                    construction,
                    construction.ArgumentRoles.ElementAtOrDefault(index)
                ),
                _ => false,
            };
        }

        private static bool IsEvaluatedOnce(
            IrExceptionConstruction construction,
            IrExceptionArgumentRole role
        ) =>
            role switch
            {
                IrExceptionArgumentRole.Unmapped => false,
                IrExceptionArgumentRole.Message => !construction.IsMessageOverridden,
                _ => true,
            };

        private static bool IsSideEffectFree(IrExpression expression) =>
            expression switch
            {
                IrLiteral or IrIdentifier or IrThisExpression or IrTypeReference => true,
                IrMemberAccess access => IsSideEffectFree(access.Target),
                IrCastExpression cast => IsSideEffectFree(cast.Expression),
                IrStringInterpolation interpolation => interpolation
                    .Parts.OfType<IrInterpolationExpression>()
                    .All(hole => IsSideEffectFree(hole.Expression)),
                _ => false,
            };
    }

    /// <summary>An argument's IR source (for literal folding) and its TS lowering.</summary>
    private sealed record ErrorOperand(IrExpression Source, TsExpression Lowered);

    /// <summary>
    /// Accumulates constant text and runtime holes, then emits the smallest
    /// equivalent TS expression: nothing, a string literal, the lone hole, or
    /// a template literal.
    /// </summary>
    private sealed class ErrorMessageComposer
    {
        // Template-literal invariant: a text segment surrounds every hole, so
        // _texts.Count == _holes.Count + 1 at all times.
        private readonly List<string> _texts = [""];
        private readonly List<TsExpression> _holes = [];
        private bool _hasContent;

        public void AppendText(string text)
        {
            _texts[^1] += text;
            _hasContent = true;
        }

        public void AppendValue(ErrorOperand operand)
        {
            if (FoldableText(operand) is { } text)
            {
                AppendText(text);

                return;
            }

            _holes.Add(operand.Lowered);
            _texts.Add("");
            _hasContent = true;
        }

        public TsExpression? Build()
        {
            if (!_hasContent)
                return null;

            if (IsPlainText())
                return new TsStringLiteral(_texts[0]);

            if (IsLoneHole())
                return _holes[0];

            return new TsTemplateLiteral(_texts.Select(EscapeTemplateText).ToList(), _holes);
        }

        /// <summary>The text of an inline string-literal operand, folded
        /// into the surrounding constant text; <c>null</c> otherwise.</summary>
        private static string? FoldableText(ErrorOperand operand) =>
            operand
                is {
                    Source: IrLiteral { Kind: IrLiteralKind.String, Value: string text },
                    Lowered: TsStringLiteral,
                }
                ? text
                : null;

        private bool IsPlainText() => _holes.Count == 0 && !ContainsLineBreak(_texts[0]);

        private bool IsLoneHole() => _holes.Count == 1 && _texts.All(text => text.Length == 0);

        private static bool ContainsLineBreak(string text) =>
            text.Contains('\n') || text.Contains('\r');

        private static string EscapeTemplateText(string text) =>
            text.Replace("\\", "\\\\")
                .Replace("`", "\\`")
                .Replace("${", "\\${")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r");
    }
}

/// <summary>
/// A lowered base-constructor call: <paramref name="Temporaries"/> to declare
/// before <c>super(...)</c> (arguments evaluated in source order), the
/// <c>super</c> <paramref name="Arguments"/>, and — for a class that overrides
/// <c>Message</c> — the <paramref name="KeptBaseMessage"/> stored in
/// <c>#baseMessage</c> right after <c>super</c>.
/// </summary>
public sealed record TsBaseConstructorCall(
    IReadOnlyList<TsVariableDeclaration> Temporaries,
    IReadOnlyList<TsExpression> Arguments,
    TsExpression? KeptBaseMessage = null
)
{
    /// <summary>The temporaries, the <c>super(...)</c> statement and the
    /// kept-base-message assignment, if any.</summary>
    public IReadOnlyList<TsStatement> ToStatements()
    {
        var superCall = new TsExpressionStatement(
            new TsCallExpression(new TsIdentifier("super"), Arguments)
        );

        if (KeptBaseMessage is null)
            return [.. Temporaries, superCall];

        var keepBaseMessage = new TsExpressionStatement(
            new TsBinaryExpression(
                IrToTsExceptionConstructionBridge.BaseMessageAccess(),
                "=",
                KeptBaseMessage
            )
        );

        return [.. Temporaries, superCall, keepBaseMessage];
    }
}
