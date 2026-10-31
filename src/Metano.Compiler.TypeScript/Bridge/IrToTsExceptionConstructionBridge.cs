using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers an <see cref="IrExceptionConstruction"/> (a BCL exception
/// construction, or the base call of a user exception deriving from one) into
/// the arguments of the JS <c>Error(message, { cause })</c> constructor.
/// <para>
/// The message emulates how .NET composes <c>Exception.Message</c>:
/// <list type="bullet">
///   <item>the <c>message</c> argument, or the type's default message when it
///     is absent or a <c>null</c> literal;</item>
///   <item><c>" (Parameter 'name')"</c> appended for a <c>paramName</c>
///     (omitted for a <c>null</c> / empty literal);</item>
///   <item><c>"\nActual value was X."</c> appended for an <c>actualValue</c>
///     (omitted for a <c>null</c> literal).</item>
/// </list>
/// The <c>innerException</c> becomes <c>{ cause }</c>. Constant parts fold
/// into a string literal; runtime parts become template-literal holes.
/// Known deviations: a non-literal <c>message</c>, <c>paramName</c> or
/// <c>actualValue</c> that is <c>null</c> at runtime is not substituted /
/// suppressed, and an actual value is formatted with JS (not .NET)
/// <c>ToString</c> rules.
/// </para>
/// </summary>
public static class IrToTsExceptionConstructionBridge
{
    public static IReadOnlyList<TsExpression> MapErrorArguments(
        IrExceptionConstruction construction,
        IReadOnlyList<IrArgument> arguments,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var message = ComposeMessage(construction, arguments, bclRegistry);
        var innerException = construction.ValueOf(
            IrExceptionArgumentRole.InnerException,
            arguments
        );

        if (innerException is null || IsNullLiteral(innerException))
            return message is null ? [] : [message];

        var options = new TsObjectLiteral([
            new TsObjectProperty("cause", IrToTsExpressionBridge.Map(innerException, bclRegistry)),
        ]);

        return [message ?? new TsIdentifier("undefined"), options];
    }

    private static TsExpression? ComposeMessage(
        IrExceptionConstruction construction,
        IReadOnlyList<IrArgument> arguments,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var composer = new ErrorMessageComposer(bclRegistry);

        AppendBaseMessage(composer, construction, arguments);
        AppendParamName(
            composer,
            construction.ValueOf(IrExceptionArgumentRole.ParamName, arguments)
        );
        AppendActualValue(
            composer,
            construction.ValueOf(IrExceptionArgumentRole.ActualValue, arguments)
        );

        return composer.Build();
    }

    private static void AppendBaseMessage(
        ErrorMessageComposer composer,
        IrExceptionConstruction construction,
        IReadOnlyList<IrArgument> arguments
    )
    {
        var message = construction.ValueOf(IrExceptionArgumentRole.Message, arguments);

        if (message is not null && !IsNullLiteral(message))
            composer.AppendValue(message);
        else if (construction.DefaultMessage is { } defaultMessage)
            composer.AppendText(defaultMessage);
    }

    private static void AppendParamName(ErrorMessageComposer composer, IrExpression? paramName)
    {
        if (paramName is null || IsNullOrEmptyStringLiteral(paramName))
            return;

        composer.AppendText(" (Parameter '");
        composer.AppendValue(paramName);
        composer.AppendText("')");
    }

    private static void AppendActualValue(ErrorMessageComposer composer, IrExpression? actualValue)
    {
        if (actualValue is null || IsNullLiteral(actualValue))
            return;

        composer.AppendText("\nActual value was ");
        composer.AppendValue(actualValue);
        composer.AppendText(".");
    }

    private static bool IsNullLiteral(IrExpression expression) =>
        expression is IrLiteral { Kind: IrLiteralKind.Null or IrLiteralKind.Default };

    private static bool IsNullOrEmptyStringLiteral(IrExpression expression) =>
        IsNullLiteral(expression) || expression is IrLiteral { Value: "" };

    /// <summary>
    /// Accumulates constant text and runtime holes, then emits the smallest
    /// equivalent TS expression: nothing, a string literal, the lone hole, or
    /// a template literal.
    /// </summary>
    private sealed class ErrorMessageComposer(DeclarativeMappingRegistry? bclRegistry)
    {
        private readonly List<string> _texts = [""];
        private readonly List<TsExpression> _holes = [];
        private bool _hasContent;

        public void AppendText(string text)
        {
            _texts[^1] += text;
            _hasContent = true;
        }

        public void AppendValue(IrExpression value)
        {
            if (value is IrLiteral { Kind: IrLiteralKind.String, Value: string text })
            {
                AppendText(text);

                return;
            }

            _holes.Add(IrToTsExpressionBridge.Map(value, bclRegistry));
            _texts.Add("");
            _hasContent = true;
        }

        public TsExpression? Build()
        {
            if (!_hasContent)
                return null;

            if (_holes.Count == 0 && !ContainsLineBreak(_texts[0]))
                return new TsStringLiteral(_texts[0]);

            if (IsLoneHole())
                return _holes[0];

            return new TsTemplateLiteral(_texts.Select(EscapeTemplateText).ToList(), _holes);
        }

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
