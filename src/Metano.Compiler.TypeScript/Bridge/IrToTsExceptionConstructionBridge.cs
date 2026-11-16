using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrExceptionConstruction"/> (a BCL exception <c>new</c>,
/// or the base call of a user exception deriving from one) to the construction
/// of its runtime class (spec 011 FR-014, <see cref="BclExceptionClasses"/>),
/// and keeps a class's <c>Message</c> override visible.
/// <para>
/// Arguments by class:
/// <list type="bullet">
///   <item>A mapped class receives the arguments by role, in its
///   constructor's order, and composes <c>Message</c> as .NET does itself
///   (default message, <c>" (Parameter 'name')"</c>,
///   <c>"\nActual value was X."</c>, <c>"\nObject name: 'x'."</c>):
///   <c>new ArgumentOutOfRangeException("p", undefined, "m")</c>.</item>
///   <item>The base call of a user exception deriving from
///   <c>System.Exception</c>, an unmapped exception (which constructs the
///   root <c>Exception</c>, its <c>new</c> through
///   <c>namedException("System.IO", "IOException", message, inner)</c>, so
///   the C# type survives as its identity), and a construction passing a role
///   its class does not take, pass a message this bridge composes and the
///   inner exception. <c>System.Exception</c>'s default message names the user
///   class, which the runtime cannot know.</item>
/// </list>
/// A composed message is the <c>message</c> argument, or the type's default
/// message when it is absent or a <c>null</c> literal — and, when nullable
/// analysis says the argument may be <c>null</c>, <c>message ?? default</c> —
/// followed by the suffixes of the roles the root class does not take, omitted
/// for a <c>null</c> (or, for names, empty) literal. A <c>null</c> literal
/// counts as an absent argument everywhere.
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
/// Evaluation order: the role-ordered arguments reorder the source ones and
/// drop unmapped ones. When that would change when or whether a
/// side-effecting argument runs
/// (<see cref="IrExceptionConstruction.RequiresArgumentTemporaries"/>), the
/// arguments are evaluated once, in source order, first: the extractor
/// let-binds them for <c>new</c>; a base call declares <c>const</c>
/// temporaries before <c>super</c>, which cannot move into a let IIFE.
/// </para>
/// <para>
/// Known deviations: in a composed message, a non-literal <c>paramName</c>,
/// <c>objectName</c> or <c>actualValue</c> that is <c>null</c> / empty at
/// runtime is not suppressed, and an actual value is formatted with JS (not
/// .NET) <c>ToString</c> rules, as the runtime classes also do. When a
/// <c>Message</c> override supplies the message, the <c>stack</c> header
/// (captured by the native constructor) keeps the base message, not the
/// overridden one.
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

    private const string SwitchExpressionExceptionTypeName =
        "System.Runtime.CompilerServices.SwitchExpressionException";

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
    /// Lowers <c>new BclException(...)</c> to the construction of its runtime
    /// class. The extractor has already let-bound any side-effecting arguments
    /// the role-ordered lowering would reorder or drop.
    /// </summary>
    internal static TsExpression MapNewException(
        IrNewExpression creation,
        IrExceptionConstruction construction,
        IrToTsBridgeContext context
    )
    {
        var operands = ErrorOperands.Bind(construction, creation.Arguments, context);
        var exceptionClass = ClassOf(construction);
        var plan = MessagePlan.Of(construction, operands, exceptionClass, ConstructionSite.New);
        var arguments = BuildArguments(construction, operands, exceptionClass, plan);

        if (!plan.IsMapped && creation.Type is IrNamedTypeRef unmapped)
            return NamedException(unmapped.Namespace ?? "", unmapped.Name, arguments);

        return new TsNewExpression(exceptionClass.ValueReference(), arguments);
    }

    /// <summary>
    /// The throw of a switch expression no arm matched: .NET's
    /// <c>SwitchExpressionException</c> with the unmatched value. The runtime
    /// class takes the value as its first role and names it in its message; a
    /// class of a project's own mapping, or the root an unmapped type lowers
    /// to, receives .NET's message with the value already written into it.
    /// </summary>
    internal static TsExpression MapNonExhaustiveSwitch(TsExpression unmatchedValue)
    {
        var classes = IrToTsTypeMapper.ExceptionClasses;
        var exceptionClass = classes.ClassOf(SwitchExpressionExceptionTypeName);

        if (exceptionClass.Takes(IrExceptionArgumentRole.UnmatchedValue))
        {
            return new TsNewExpression(
                exceptionClass.ValueReference(),
                PositionalArguments(
                    exceptionClass,
                    role => role is IrExceptionArgumentRole.UnmatchedValue ? unmatchedValue : null
                )
            );
        }

        var message = SwitchExpressionMessage(unmatchedValue);

        var arguments = PositionalArguments(
            exceptionClass,
            role => role is IrExceptionArgumentRole.Message ? message : null
        );

        if (!classes.IsMapped(SwitchExpressionExceptionTypeName))
            return NamedException(
                "System.Runtime.CompilerServices",
                "SwitchExpressionException",
                arguments
            );

        return new TsNewExpression(exceptionClass.ValueReference(), arguments);
    }

    /// <summary>
    /// .NET's message for an unmatched value: <c>Non-exhaustive switch
    /// expression failed to match its input.</c>, followed by
    /// <c>\nUnmatched value was X.</c> when the value is not <c>null</c>.
    /// </summary>
    private static TsExpression SwitchExpressionMessage(TsExpression unmatchedValue) =>
        new TsConditionalExpression(
            new TsBinaryExpression(unmatchedValue, "==", new TsLiteral("null")),
            new TsStringLiteral(IrExceptionConstructionExtractor.SwitchExpressionDefaultMessage),
            new TsTemplateLiteral(
                [
                    IrExceptionConstructionExtractor.SwitchExpressionDefaultMessage
                        + IrExceptionConstructionExtractor.UnmatchedValueLine,
                    ".",
                ],
                [unmatchedValue]
            )
        );

    /// <summary>
    /// <c>namedException("System.IO", "IOException", message, inner)</c>: the
    /// root <c>Exception</c> an unmapped exception lowers to, with the message
    /// C# passed and the C# type as its <c>name</c> and <c>toString()</c> prefix
    /// (spec 011 FR-014).
    /// </summary>
    private static TsExpression NamedException(
        string typeNamespace,
        string typeName,
        IReadOnlyList<TsExpression> rootArguments
    ) =>
        new TsCallExpression(
            new TsRuntimeHelperReference("namedException"),
            [new TsStringLiteral(typeNamespace), new TsStringLiteral(typeName), .. rootArguments]
        );

    /// <summary>
    /// The <c>super(...)</c> call of a constructor whose base is a BCL
    /// exception: the arguments of its runtime class (the class's
    /// <c>extends</c>), preceded by source-order temporaries when
    /// <see cref="IrExceptionConstruction.RequiresArgumentTemporaries"/>.
    /// </summary>
    internal static TsBaseConstructorCall MapBaseExceptionCall(
        IrConstructorDeclaration ctor,
        IrExceptionConstruction construction,
        IrToTsBridgeContext context
    )
    {
        var operands = ErrorOperands.Bind(construction, ctor.BaseArguments ?? [], context);
        var exceptionClass = ClassOf(construction);

        var plan = MessagePlan.Of(
            construction,
            operands,
            exceptionClass,
            ConstructionSite.BaseCall
        );

        return new TsBaseConstructorCall(
            operands.Temporaries,
            BuildArguments(construction, operands, exceptionClass, plan)
        );
    }

    private static TsExceptionClass ClassOf(IrExceptionConstruction construction) =>
        IrToTsTypeMapper.ExceptionClasses.ClassOf(construction.ExceptionTypeName);

    /// <summary>
    /// The constructor arguments of <paramref name="exceptionClass"/>: the
    /// supplied value of each of its roles, in order, with <c>undefined</c>
    /// for a role omitted ahead of a supplied one and nothing after the last.
    /// The message role carries the composed message when
    /// <paramref name="plan"/> says the bridge composes it.
    /// </summary>
    private static IReadOnlyList<TsExpression> BuildArguments(
        IrExceptionConstruction construction,
        ErrorOperands operands,
        TsExceptionClass exceptionClass,
        MessagePlan plan
    )
    {
        var message = plan.Composes ? ComposeMessage(construction, operands, plan) : null;

        return PositionalArguments(
            exceptionClass,
            role =>
                role is IrExceptionArgumentRole.Message && plan.Composes
                    ? message
                    : operands.SuppliedValueOf(role)?.Lowered
        );
    }

    /// <summary>
    /// The value of each of <paramref name="exceptionClass"/>'s roles, in its
    /// constructor's order, with <c>undefined</c> for a role omitted ahead of
    /// a passed one and nothing after the last.
    /// </summary>
    private static IReadOnlyList<TsExpression> PositionalArguments(
        TsExceptionClass exceptionClass,
        Func<IrExceptionArgumentRole, TsExpression?> valueOf
    )
    {
        var values = exceptionClass.ConstructorRoles.Select(valueOf).ToList();
        var passed = values.FindLastIndex(value => value is not null) + 1;

        return values.Take(passed).Select(value => value ?? new TsIdentifier("undefined")).ToList();
    }

    /// <summary>
    /// The message of a construction whose class does not compose it: the base
    /// message, then the .NET suffix of each <see cref="MessagePlan.Folded"/> role.
    /// </summary>
    private static TsExpression? ComposeMessage(
        IrExceptionConstruction construction,
        ErrorOperands operands,
        MessagePlan plan
    )
    {
        var composer = new ErrorMessageComposer();

        AppendBaseMessage(composer, construction, operands);

        AppendNamedSuffix(
            composer,
            plan.FoldedValueOf(IrExceptionArgumentRole.ObjectName, operands),
            "\nObject name: '",
            "'."
        );

        AppendNamedSuffix(
            composer,
            plan.FoldedValueOf(IrExceptionArgumentRole.ParamName, operands),
            " (Parameter '",
            "')"
        );

        AppendValueLine(
            composer,
            plan.FoldedValueOf(IrExceptionArgumentRole.ActualValue, operands),
            "\nActual value was "
        );

        AppendValueLine(
            composer,
            plan.FoldedValueOf(IrExceptionArgumentRole.UnmatchedValue, operands),
            IrExceptionConstructionExtractor.UnmatchedValueLine
        );

        return composer.Build();
    }

    /// <summary>Where an exception construction stands.</summary>
    private enum ConstructionSite
    {
        /// <summary><c>new X(...)</c>.</summary>
        New,

        /// <summary>The <c>: base(...)</c> call of a user exception deriving from <c>X</c>.</summary>
        BaseCall,
    }

    /// <summary>
    /// The one decision of who writes an exception's message, taken once per
    /// construction.
    /// </summary>
    /// <param name="Composes">Whether this bridge composes the message rather
    /// than the class: an unmapped exception (which constructs the root
    /// <c>Exception</c>), the base call of a user exception deriving from
    /// <c>System.Exception</c> (its default message names the user class,
    /// which the runtime cannot know), and a construction passing a role its
    /// mapped class does not take.</param>
    /// <param name="IsMapped">Whether the type has a mapped class of its own;
    /// an unmapped one constructs the root through <c>namedException</c>.</param>
    /// <param name="Folded">The suffix roles (<c>paramName</c>,
    /// <c>actualValue</c>, <c>objectName</c>, <c>unmatchedValue</c>) the
    /// construction supplies but the class does not take, which the composed
    /// message carries as .NET's text (<c>" (Parameter 'p')"</c>, …) instead
    /// of dropping them.</param>
    private sealed record MessagePlan(
        bool Composes,
        bool IsMapped,
        IReadOnlySet<IrExceptionArgumentRole> Folded
    )
    {
        private static readonly IrExceptionArgumentRole[] SuffixRoles =
        [
            IrExceptionArgumentRole.ObjectName,
            IrExceptionArgumentRole.ParamName,
            IrExceptionArgumentRole.ActualValue,
            IrExceptionArgumentRole.UnmatchedValue,
        ];

        public static MessagePlan Of(
            IrExceptionConstruction construction,
            ErrorOperands operands,
            TsExceptionClass exceptionClass,
            ConstructionSite site
        )
        {
            var typeName = construction.ExceptionTypeName;
            var isMapped = IrToTsTypeMapper.ExceptionClasses.IsMapped(typeName);

            var folded = SuffixRoles
                .Where(role =>
                    operands.SuppliedValueOf(role) is not null && !exceptionClass.Takes(role)
                )
                .ToHashSet();

            var namesUserClass =
                site is ConstructionSite.BaseCall
                && typeName == BclExceptionClasses.RootExceptionTypeName;

            return new MessagePlan(
                Composes: !isMapped || namesUserClass || folded.Count > 0,
                isMapped,
                folded
            );
        }

        /// <summary>The supplied operand of <paramref name="role"/> when it is folded.</summary>
        public ErrorOperand? FoldedValueOf(IrExceptionArgumentRole role, ErrorOperands operands) =>
            Folded.Contains(role) ? operands.SuppliedValueOf(role) : null;
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
            new TsStringLiteral(construction.DefaultMessage ?? "")
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

    /// <summary><paramref name="prefix"/>, the value and <c>.</c>, as .NET writes an actual or unmatched value.</summary>
    private static void AppendValueLine(
        ErrorMessageComposer composer,
        ErrorOperand? value,
        string prefix
    )
    {
        if (value is null)
            return;

        composer.AppendText(prefix);
        composer.AppendValue(value);
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
            IrToTsBridgeContext context
        )
        {
            var lowered = arguments
                .Select(argument => IrToTsExpressionBridge.Map(argument.Value, context))
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

            return new TsTemplateLiteral(_texts, _holes);
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
    }
}
