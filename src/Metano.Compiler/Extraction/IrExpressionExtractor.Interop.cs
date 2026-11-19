using Metano.Annotations;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The interop surface of the <c>Metano</c> package (spec 011 FR-031 – FR-036):
/// <c>Js.Code</c> inline target code, and the <c>UnsafeCast&lt;T&gt;()</c> /
/// <c>AsDynamic()</c> escape hatches. The core keeps the C# facts only: the split of
/// the <c>code</c> argument into verbatim text and transpiled holes, the checks of
/// <c>MS0052</c>, and the holes' source spans. What the text means in the target
/// language (string literals, statement endings) is the target's to decide.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string InvalidInlineCodeKind = "InvalidInlineCode";

    /// <summary>
    /// The lowering of an interop call, or <c>null</c> when <paramref name="method"/>
    /// is not one.
    /// </summary>
    internal IrExpression? RewriteInteropCall(
        InvocationExpressionSyntax call,
        IMethodSymbol? method
    ) =>
        InteropMethods.KindOf(method) switch
        {
            InteropMethodKind.Code => InlineCodeExpressionOf(call, method!),
            InteropMethodKind.UnsafeCast => new IrUnsafeCastExpression(
                WithoutBox(ExtractInteropOperand(call, method!)),
                IrTypeRefMapper.Map(method!.TypeArguments[0], _originResolver, _target)
            ),
            InteropMethodKind.AsDynamic => new IrAsDynamicExpression(
                InteropUnboxed(
                    InteropOperandType(call, method!),
                    ExtractInteropOperand(call, method!)
                )
            ),
            _ when method is null => UnsupportedLateBoundInteropCall(call),
            _ => null,
        };

    /// <summary>
    /// A <c>Js.Code</c> call C# binds at run time because its argument is
    /// <c>dynamic</c> (<c>Js.Code(d)</c>): its text is not a constant, so it reports
    /// <c>MS0052</c> instead of being emitted as a call. <c>null</c> for any other
    /// late-bound call. (The escape hatches take <c>object</c>, so a <c>dynamic</c>
    /// argument binds them statically.)
    /// </summary>
    private IrExpression? UnsupportedLateBoundInteropCall(InvocationExpressionSyntax call)
    {
        var bindsJsCode = _semantic
            .GetSymbolInfo(call)
            .CandidateSymbols.OfType<IMethodSymbol>()
            .Any(candidate => InteropMethods.KindOf(candidate) is InteropMethodKind.Code);

        if (!bindsJsCode)
            return null;

        ReportInvalidInlineCode(
            call.ArgumentList.Arguments.FirstOrDefault()?.Expression ?? (SyntaxNode)call,
            "The Js.Code argument is dynamic, so the call binds at run time and has no "
                + "constant text; pass a constant or an interpolated string."
        );

        return new IrUnsupportedExpression(InvalidInlineCodeKind);
    }

    /// <summary>
    /// The statement form of a <c>Js.Code</c> call whose value is discarded (an
    /// expression statement, the body of an expression-bodied <c>void</c> member or
    /// lambda); <c>null</c> for any other expression.
    /// </summary>
    public IrStatement? InlineCodeStatementOf(ExpressionSyntax expression)
    {
        if (DiscardedInlineCodeCallOf(expression) is not { } call)
            return null;

        return InlineCodePartsOf(call) is { } parts
            ? new IrInlineCodeStatement(parts, LanguageOf(call), SpanOf(call))
            : new IrExpressionStatement(new IrUnsupportedExpression(InvalidInlineCodeKind));
    }

    private InvocationExpressionSyntax? DiscardedInlineCodeCallOf(ExpressionSyntax expression)
    {
        if (
            expression is not InvocationExpressionSyntax call
            || !NamesInteropMethod(call.Expression)
        )
            return null;

        var method = _semantic.GetSymbolInfo(call).Symbol as IMethodSymbol;
        var isDiscarded = _semantic.GetOperation(call)?.Parent is IExpressionStatementOperation;

        return InteropMethods.KindOf(method) is InteropMethodKind.Code && isDiscarded ? call : null;
    }

    private IrExpression InlineCodeExpressionOf(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    )
    {
        if (InlineCodePartsOf(call) is not { } parts)
            return new IrUnsupportedExpression(InvalidInlineCodeKind);

        var resultType = method.IsGenericMethod
            ? IrTypeRefMapper.Map(method.TypeArguments[0], _originResolver, _target)
            : null;

        return new IrInlineCodeExpression(parts, resultType, LanguageOf(call), SpanOf(call));
    }

    /// <summary>
    /// The language the interop API of <paramref name="call"/> is bound to. Every
    /// inline-code API carries the marker, so a missing one is a broken package.
    /// </summary>
    private TargetLanguage LanguageOf(InvocationExpressionSyntax call) =>
        _semantic.GetSymbolInfo(call).Symbol is { } method
        && TargetSpecificInterop.LanguageOf(method) is { } language
            ? language
            : throw new InvalidOperationException(
                $"'{call.Expression}' has no [TargetSpecific] marker; the Metano package is "
                    + "inconsistent with this compiler."
            );

    /// <summary>
    /// The operand of <c>UnsafeCast</c> / <c>AsDynamic</c>: the receiver of the
    /// reduced call (<c>x.AsDynamic()</c>, <c>x?.AsDynamic()</c>), or the first
    /// argument of the static form (<c>InteropExtensions.AsDynamic(x)</c>).
    /// </summary>
    private IrExpression ExtractInteropOperand(
        InvocationExpressionSyntax call,
        IMethodSymbol method
    )
    {
        if (
            method.ReducedFrom is not null
            && QualifiedMemberSyntax.From(call.Expression) is { } access
        )
            return Extract(access.Receiver);

        return Extract(call.ArgumentList.Arguments[0].Expression);
    }

    /// <summary>The static type of the operand of <c>UnsafeCast</c> / <c>AsDynamic</c>.</summary>
    private ITypeSymbol? InteropOperandType(InvocationExpressionSyntax call, IMethodSymbol method)
    {
        var operand =
            method.ReducedFrom is not null
            && QualifiedMemberSyntax.From(call.Expression) is { } access
                ? access.Receiver
                : call.ArgumentList.Arguments[0].Expression;

        return _semantic.GetTypeInfo(operand).Type;
    }

    /// <summary>
    /// The text and holes of the <c>code</c> argument, or <c>null</c> after
    /// reporting <c>MS0052</c>. An interpolated string is always split, even when C#
    /// folds it to a constant, so a constant hole stays code; any other argument
    /// must be a constant string.
    /// </summary>
    private IReadOnlyList<IrInlineCodePart>? InlineCodePartsOf(InvocationExpressionSyntax call)
    {
        var argument = call.ArgumentList.Arguments[0].Expression;
        var code = Unparenthesized(argument);

        if (code is InterpolatedStringExpressionSyntax interpolated)
            return InterpolatedInlineCodePartsOf(interpolated);

        if (ContainsInterpolation(code))
        {
            ReportInvalidInlineCode(
                argument,
                "The Js.Code argument nests an interpolated string in an expression; pass the "
                    + "interpolated string itself, so its holes are transpiled as code."
            );

            return null;
        }

        if (_semantic.GetConstantValue(code) is { HasValue: true, Value: string text })
            return [new IrInlineCodeText(text)];

        ReportInvalidInlineCode(
            argument,
            "The Js.Code argument must be a constant string or an interpolated string with "
                + "constant literal parts; the text is emitted at compile time."
        );

        return null;
    }

    private static bool ContainsInterpolation(ExpressionSyntax code) =>
        code.DescendantNodesAndSelf().OfType<InterpolatedStringExpressionSyntax>().Any();

    private IReadOnlyList<IrInlineCodePart>? InterpolatedInlineCodePartsOf(
        InterpolatedStringExpressionSyntax interpolated
    )
    {
        if (_semantic.GetOperation(interpolated) is not IInterpolatedStringOperation operation)
        {
            ReportInvalidInlineCode(interpolated, "The Js.Code argument could not be read.");

            return null;
        }

        var parts = new List<IrInlineCodePart>();
        var valid = true;

        foreach (var content in operation.Parts)
        {
            switch (content)
            {
                case IInterpolatedStringTextOperation { Text.ConstantValue.Value: string text }:
                    parts.Add(new IrInlineCodeText(text));
                    break;
                case IInterpolationOperation { Syntax: InterpolationSyntax hole } interpolation:
                    valid &= IsValidHole(hole, interpolation);
                    parts.Add(InlineCodeHoleOf(hole));
                    break;
            }
        }

        return valid ? parts : null;
    }

    /// <summary>
    /// A hole of the inline code: JS reads its value raw, so a value that may
    /// hold a box is unboxed (spec 011 FR-051).
    /// </summary>
    private IrInlineCodeHole InlineCodeHoleOf(InterpolationSyntax hole)
    {
        var value = InteropUnboxed(
            _semantic.GetTypeInfo(hole.Expression).Type,
            Extract(hole.Expression)
        );

        return new IrInlineCodeHole(value, SpanOf(hole));
    }

    private bool IsValidHole(InterpolationSyntax hole, IInterpolationOperation interpolation)
    {
        if (interpolation.Alignment is not null || interpolation.FormatString is not null)
        {
            ReportInvalidInlineCode(
                hole,
                "A Js.Code hole is code, not formatted text: remove its alignment or format clause."
            );

            return false;
        }

        if (!HoleBindsOutsideItsText(hole.Expression))
            return true;

        ReportInvalidInlineCode(
            hole,
            "A Js.Code hole cannot await or pass a ref / out argument: compute the value into a "
                + "local first and use the local in the hole."
        );

        return false;
    }

    /// <summary>
    /// Whether the hole awaits or passes a <c>ref</c> / <c>out</c> argument, outside
    /// the nested functions it may hold (which bind their own).
    /// </summary>
    private static bool HoleBindsOutsideItsText(ExpressionSyntax hole) =>
        hole.DescendantNodesAndSelf(node => node is not AnonymousFunctionExpressionSyntax)
            .Any(node =>
                node is AwaitExpressionSyntax
                || node is ArgumentSyntax argument
                    && argument.RefKindKeyword.Kind()
                        is SyntaxKind.RefKeyword
                            or SyntaxKind.OutKeyword
            );

    /// <summary>
    /// A use of an interop method the targets have no function for, reported loudly:
    /// a method group of <c>Js.Code</c> (<c>MS0052</c>) or of <c>UnsafeCast</c> /
    /// <c>AsDynamic</c> (<c>MS0001</c>), and, for a target that keeps the
    /// <c>dynamic</c> source forms (Dart, D28), a member access on an
    /// <c>AsDynamic()</c> result (<c>MS0001</c>; its symbol-less extraction
    /// would apply the naming policy). Any other target lowers that access as
    /// a <c>dynamic</c> one (spec 011 FR-030, FR-036). <c>null</c> for any
    /// other expression.
    /// </summary>
    private IrExpression? UnsupportedInteropUse(ExpressionSyntax expression)
    {
        if (InteropMethodGroupOf(expression) is { } method)
            return UnsupportedInteropMethodGroup(expression, method);

        var keepsUnsupportedAccess =
            _context.KeepsDynamicSourceForms
            && AccessedReceiverOf(expression) is { } receiver
            && IsAsDynamicCall(receiver);

        return keepsUnsupportedAccess ? UnsupportedAsDynamicAccess(expression) : null;
    }

    /// <summary>The receiver an access reads from: <c>r.M</c>, <c>r[i]</c>, <c>r?.M</c>.</summary>
    private static ExpressionSyntax? AccessedReceiverOf(ExpressionSyntax expression) =>
        expression switch
        {
            MemberAccessExpressionSyntax access => access.Expression,
            ElementAccessExpressionSyntax element => element.Expression,
            ConditionalAccessExpressionSyntax conditional => conditional.Expression,
            _ => null,
        };

    private IMethodSymbol? InteropMethodGroupOf(ExpressionSyntax expression)
    {
        if (
            expression is not (SimpleNameSyntax or MemberAccessExpressionSyntax)
            || !NamesInteropMethod(expression)
            || IsInvokedExpression(expression)
        )
            return null;

        return
            _semantic.GetOperation(expression) is IMethodReferenceOperation { Method: var method }
            && InteropMethods.KindOf(method) is not InteropMethodKind.None
            ? method
            : null;
    }

    private static bool IsInvokedExpression(ExpressionSyntax expression) =>
        expression.Parent is InvocationExpressionSyntax invocation
        && invocation.Expression == expression;

    private static IrExpression UnsupportedInteropMethodGroup(
        ExpressionSyntax expression,
        IMethodSymbol method
    )
    {
        if (InteropMethods.KindOf(method) is not InteropMethodKind.Code)
        {
            return ReportUnsupported(
                expression,
                "InteropMethodGroup",
                $"'{method.Name}' is a compile-time escape hatch with no function behind it in the "
                    + "target; call it instead of passing it as a method group."
            );
        }

        ReportInvalidInlineCode(
            expression,
            "Js.Code has no function behind it in the target; call it with its text instead of "
                + "passing it as a method group."
        );

        return new IrUnsupportedExpression(InvalidInlineCodeKind);
    }

    private bool IsAsDynamicCall(ExpressionSyntax receiver) =>
        Unparenthesized(receiver) is InvocationExpressionSyntax call
        && NamesInteropMethod(call.Expression)
        && InteropMethods.KindOf(_semantic.GetSymbolInfo(call).Symbol as IMethodSymbol)
            is InteropMethodKind.AsDynamic;

    private static IrUnsupportedExpression UnsupportedAsDynamicAccess(ExpressionSyntax access) =>
        ReportUnsupported(
            access,
            "AsDynamicMemberAccess",
            "A member access on an AsDynamic() result is not supported yet: its name would be "
                + "emitted by the naming policy instead of verbatim. Use Js.Code for the access."
        );

    /// <summary>The cheap syntactic pre-filter: the expression's name can spell an interop method.</summary>
    private static bool NamesInteropMethod(ExpressionSyntax expression) =>
        expression switch
        {
            SimpleNameSyntax name => InteropMethods.MayName(name.Identifier.ValueText),
            MemberAccessExpressionSyntax access => InteropMethods.MayName(
                access.Name.Identifier.ValueText
            ),
            MemberBindingExpressionSyntax binding => InteropMethods.MayName(
                binding.Name.Identifier.ValueText
            ),
            _ => false,
        };

    private static IrSourceSpan? SpanOf(SyntaxNode node) =>
        IrSourceSpanLocations.From(node.GetLocation());

    private static void ReportInvalidInlineCode(SyntaxNode node, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.InvalidInlineCode,
                message,
                node.GetLocation()
            )
        );
}
