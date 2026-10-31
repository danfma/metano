using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers the arguments of an <see cref="IrExceptionConstruction"/> (a BCL
/// exception <c>new</c>, or the base call of a user exception deriving from
/// one) into the arguments of the JS <c>Error(message, { cause })</c>
/// constructor, and keeps a class's <c>Message</c> override visible.
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
/// <c>innerException</c> becomes <c>{ cause }</c>.
/// </para>
/// <para>
/// <c>Message</c> overrides: the native <c>Error</c> constructor installs an own
/// <c>message</c> data property that would shadow a <c>get message()</c>
/// override — whichever ancestor makes the BCL base call. A class that
/// overrides <c>Message</c> therefore moves that own property, right after
/// <c>super(...)</c>, into its private <c>#baseMessage</c> field (or, when an
/// ancestor already did so, reads the ancestor's <c>super.message</c>), and its
/// <c>base.Message</c> reads lower to that field.
/// </para>
/// <para>
/// Evaluation order: the message composition reads the arguments in role
/// order and ignores unmapped ones. When that would change when or whether a
/// side-effecting argument runs
/// (<see cref="IrExceptionConstruction.RequiresArgumentTemporaries"/>), the
/// arguments are evaluated once, in source order, first: the extractor
/// let-binds them for <c>new</c>; a base call declares <c>const</c>
/// temporaries before <c>super</c>, which cannot move into a let IIFE.
/// </para>
/// <para>
/// Known deviations: a non-literal <c>paramName</c>, <c>objectName</c> or
/// <c>actualValue</c> that is <c>null</c> / empty at runtime is not
/// suppressed, and an actual value is formatted with JS (not .NET)
/// <c>ToString</c> rules. When a <c>Message</c> override supplies the message,
/// the <c>stack</c> header (captured by the native constructor) keeps the base
/// message, not the overridden one. Type tests stay coarse: every BCL exception type is
/// <c>Error</c> on the TS side, so <c>o is ArgumentException</c> lowers to
/// <c>instanceof Error</c> and typed <c>catch</c> clauses ignore their filter.
/// </para>
/// </summary>
public static class IrToTsExceptionConstructionBridge
{
    /// <summary>
    /// The ES private field holding the base message of a class that overrides
    /// <c>Message</c>; its <c>base.Message</c> reads lower to it.
    /// </summary>
    public const string BaseMessageField = "#baseMessage";

    private const string TemporaryPrefix = "__baseArg";

    /// <summary>
    /// Whether the class currently being emitted overrides <c>Message</c> —
    /// set per class by <see cref="EnterClass"/>. <see cref="AsyncLocal{T}"/>
    /// scopes it to the emitting execution context, like the type mapper's
    /// per-file scopes.
    /// </summary>
    private static readonly AsyncLocal<bool> _keepsBaseMessage = new();

    /// <summary>
    /// Whether <paramref name="ir"/> is an exception class that itself
    /// declares a (non-ignored) <c>Message</c> override.
    /// </summary>
    public static bool OverridesMessage(IrClassDeclaration ir) =>
        ir.Semantics.IsException
        && (ir.Members ?? []).Any(member =>
            member
                is IrPropertyDeclaration
                {
                    Name: "Message",
                    IsStatic: false,
                    Semantics.IsOverride: true,
                }
        );

    /// <summary>
    /// Scopes the emission of one class: while the returned handle is alive,
    /// <c>base.Message</c> reads (see <see cref="TryMapKeptBaseMessageRead"/>)
    /// redirect to <c>#baseMessage</c> when <paramref name="keepsBaseMessage"/>
    /// (the class overrides <c>Message</c>).
    /// </summary>
    public static IDisposable EnterClass(bool keepsBaseMessage)
    {
        var scope = new ClassScope(_keepsBaseMessage.Value);

        _keepsBaseMessage.Value = keepsBaseMessage;

        return scope;
    }

    /// <summary>
    /// <c>this.#baseMessage</c> for a <c>base.Message</c> read inside a class
    /// that keeps its base message; <c>null</c> for every other access.
    /// </summary>
    public static TsExpression? TryMapKeptBaseMessageRead(IrMemberAccess access) =>
        _keepsBaseMessage.Value && access is { Target: IrBaseExpression, MemberName: "Message" }
            ? BaseMessageAccess()
            : null;

    /// <summary>
    /// Adds the <c>override</c> modifier to the <c>get message()</c> lowered
    /// from a <c>Message</c> override (<paramref name="property"/>): the JS
    /// <c>Error</c> base declares <c>message</c>, so <c>noImplicitOverride</c>
    /// requires it. Other members pass through unchanged.
    /// </summary>
    public static IReadOnlyList<TsClassMember> MarkMessageOverride(
        IrPropertyDeclaration property,
        IReadOnlyList<TsClassMember> members
    ) =>
        property is { Name: "Message", Semantics.IsOverride: true }
            ? members
                .Select(member =>
                    member is TsGetterMember getter ? getter with { Override = true } : member
                )
                .ToList()
            : members;

    /// <summary>The <c>#baseMessage</c> field declaration.</summary>
    public static TsFieldMember BaseMessageFieldDeclaration() =>
        new(
            BaseMessageField,
            new TsStringType(),
            Initializer: null,
            Readonly: true,
            Static: false,
            Accessibility: TsAccessibility.None
        );

    /// <summary>
    /// The statements that follow <c>super(...)</c> in a class that overrides
    /// <c>Message</c>: keep the base message — the own <c>message</c> property
    /// the native constructor installed, or the ancestor's
    /// <c>super.message</c> when an ancestor already moved it — and delete the
    /// own property so the override getter is visible.
    /// <code>
    /// this.#baseMessage = Object.hasOwn(this, "message") ? this.message : super.message;
    /// Reflect.deleteProperty(this, "message");
    /// </code>
    /// </summary>
    public static IReadOnlyList<TsStatement> CaptureBaseMessage()
    {
        var self = new TsIdentifier("this");
        var messageKey = new TsStringLiteral("message");

        var hasOwnMessage = new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Object"), "hasOwn"),
            [self, messageKey]
        );

        var baseMessage = new TsConditionalExpression(
            hasOwnMessage,
            new TsPropertyAccess(self, "message"),
            new TsPropertyAccess(new TsIdentifier("super"), "message")
        );

        var deleteOwnMessage = new TsCallExpression(
            new TsPropertyAccess(new TsIdentifier("Reflect"), "deleteProperty"),
            [self, messageKey]
        );

        return
        [
            new TsExpressionStatement(
                new TsBinaryExpression(BaseMessageAccess(), "=", baseMessage)
            ),
            new TsExpressionStatement(deleteOwnMessage),
        ];
    }

    private static TsExpression BaseMessageAccess() =>
        new TsPropertyAccess(new TsIdentifier("this"), BaseMessageField);

    /// <summary>Restores the enclosing emission's kept-base-message state.</summary>
    private sealed class ClassScope(bool previous) : IDisposable
    {
        public void Dispose() => _keepsBaseMessage.Value = previous;
    }

    /// <summary>
    /// Lowers <c>new BclException(...)</c> to <c>new Error(...)</c>. The
    /// extractor has already let-bound any side-effecting arguments the
    /// role-ordered lowering would reorder or drop.
    /// </summary>
    public static TsExpression MapNewError(
        IrNewExpression creation,
        IrExceptionConstruction construction,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var operands = ErrorOperands.Bind(construction, creation.Arguments, bclRegistry);

        return new TsNewExpression(
            new TsIdentifier("Error"),
            BuildErrorArguments(construction, operands)
        );
    }

    /// <summary>
    /// The <c>super(...)</c> call of a constructor whose base is a BCL
    /// exception: role-mapped <c>Error</c> arguments, preceded by source-order
    /// temporaries when <see cref="IrExceptionConstruction.RequiresArgumentTemporaries"/>.
    /// </summary>
    public static TsBaseConstructorCall MapBaseExceptionCall(
        IrConstructorDeclaration ctor,
        IrExceptionConstruction construction,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var operands = ErrorOperands.Bind(construction, ctor.BaseArguments ?? [], bclRegistry);

        return new TsBaseConstructorCall(
            operands.Temporaries,
            BuildErrorArguments(construction, operands)
        );
    }

    private static IReadOnlyList<TsExpression> BuildErrorArguments(
        IrExceptionConstruction construction,
        ErrorOperands operands
    )
    {
        var message = ComposeMessage(construction, operands);

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
    /// inline or — when the construction requires argument temporaries — to a
    /// temporary holding its source-order evaluation.
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

            if (!construction.RequiresArgumentTemporaries)
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
