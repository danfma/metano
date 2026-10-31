using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// By-reference arguments. A call that passes <c>out</c> arguments becomes an
/// <see cref="IrOutArgumentCall"/>: the invocation is extracted as usual with
/// each bound <c>out</c> argument standing as an <see cref="OutArgumentSlot"/>,
/// the slots are then dropped from the argument list and described as
/// <see cref="IrOutArgument"/>s instead. <c>ref</c> arguments, and <c>out</c>
/// arguments of a call shape the backends cannot return values from (a
/// delegate, a constructor, a null-conditional call, a local function, an
/// <c>[Emit]</c> template), are reported as MS0001 and passed by value.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    /// <summary>
    /// The <c>out</c> arguments of the invocations being extracted, which
    /// <see cref="ExtractArgument"/> turns into slots. Keyed by syntax node, so
    /// nested invocations and constructor calls in between are unaffected.
    /// </summary>
    private readonly HashSet<ArgumentSyntax> _slottedOutArguments = [];

    /// <summary>
    /// Places the results of <c>out</c>-argument calls; shared with the
    /// statement extractor so temporaries stay unique across one body.
    /// </summary>
    internal OutArgumentCallLowering OutArgumentCalls { get; } = new();

    /// <summary>
    /// Extracts an expression that has no enclosing statement list (a field or
    /// property initializer, a base-constructor argument). When it makes a
    /// call with <c>out</c> arguments, it is wrapped in an immediately-invoked
    /// arrow whose body binds them: <c>(() =&gt; { const [$found0, v] = …;
    /// return …; })()</c>. A local read outside its own expression (a later
    /// base-constructor argument) is not covered.
    /// </summary>
    public IrExpression ExtractStandalone(ExpressionSyntax expression)
    {
        var lowered = LowerOutArgumentCalls(new IrReturnStatement(Extract(expression)));

        if (lowered is [IrReturnStatement { Value: { } value }])
            return value;

        return new IrCallExpression(new IrLambdaExpression([], ReturnType: null, lowered), []);
    }

    /// <summary>
    /// The statements that replace <paramref name="statement"/> once the
    /// results of its <c>out</c>-argument calls are bound, with the bindings
    /// promoted to <c>const</c> when nothing reassigns them.
    /// </summary>
    internal IReadOnlyList<IrStatement> LowerOutArgumentCalls(IrStatement statement) =>
        IrStatementExtractor.PromoteEffectiveConsts(OutArgumentCalls.Lower(statement));

    private IrExpression ExtractInvocation(InvocationExpressionSyntax inv)
    {
        var outArguments = inv
            .ArgumentList.Arguments.Where(a => a.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            .ToList();

        if (outArguments.Count == 0)
            return ExtractInvocationCore(inv);

        if (OutReturningMethodOf(inv) is not { } method)
            return ExtractInvocationCore(inv);

        _slottedOutArguments.UnionWith(outArguments);

        try
        {
            return WithOutArguments(ExtractInvocationCore(inv), method, outArguments);
        }
        finally
        {
            _slottedOutArguments.ExceptWith(outArguments);
        }
    }

    private IMethodSymbol? OutReturningMethodOf(InvocationExpressionSyntax inv) =>
        _semantic.GetSymbolInfo(inv).Symbol is IMethodSymbol method && ReturnsOutValues(method)
            ? method
            : null;

    /// <summary>
    /// Whether calls to <paramref name="method"/> can return their
    /// <c>out</c> values: an ordinary or extension method whose own
    /// declaration the backends lower the same way (not a delegate, a local
    /// function or an inline template).
    /// </summary>
    private static bool ReturnsOutValues(IMethodSymbol method) =>
        method.MethodKind is MethodKind.Ordinary or MethodKind.ReducedExtension
        && !SymbolHelper.HasEmit(method)
        && !SymbolHelper.HasInline(method);

    private IrExpression WithOutArguments(
        IrExpression extracted,
        IMethodSymbol method,
        IReadOnlyList<ArgumentSyntax> outArguments
    )
    {
        if (extracted is not IrCallExpression call)
        {
            foreach (var argument in outArguments)
                ReportUnsupportedOutArgument(argument);

            return extracted;
        }

        var outBindings = outArguments
            .Select(argument => ExtractOutArgument(argument, method))
            .OrderBy(o => o.ParameterOrdinal)
            .ToList();

        var withoutSlots = call with
        {
            Arguments = call.Arguments.Where(a => a.Value is not OutArgumentSlot).ToList(),
        };

        return new IrOutArgumentCall(
            withoutSlots,
            outBindings,
            method.ReturnsVoid
                ? null
                : IrTypeRefMapper.Map(method.ReturnType, _originResolver, _target),
            IsOutsideTranspilation(method)
        );
    }

    private IrOutArgument ExtractOutArgument(ArgumentSyntax argument, IMethodSymbol method)
    {
        var parameter = BoundParameterOf(argument, method);
        var defaultValue = DefaultValueLiterals.For(parameter.Type, BuildOrigin);

        if (
            defaultValue is IrLiteral { Kind: IrLiteralKind.Default }
            && IsOutsideTranspilation(method)
        )
            ReportUnrepresentableDefault(argument, parameter.Type);

        return new IrOutArgument(
            method.Parameters.IndexOf(parameter, 0, SymbolEqualityComparer.Default),
            parameter.Name,
            OutTargetOf(argument.Expression, parameter),
            defaultValue
        );
    }

    /// <summary>
    /// The parameter <paramref name="argument"/> binds to, taken from
    /// <paramref name="method"/> as seen at the call site (a reduced extension
    /// method's list excludes the receiver) and with its type substituted for
    /// the call (<c>TValue</c> becomes <c>int</c>).
    /// </summary>
    private IParameterSymbol BoundParameterOf(ArgumentSyntax argument, IMethodSymbol method)
    {
        var name = _semantic.GetOperation(argument) is IArgumentOperation { Parameter: { } bound }
            ? bound.Name
            : argument.NameColon?.Name.Identifier.ValueText;

        if (name is not null && method.Parameters.FirstOrDefault(p => p.Name == name) is { } byName)
            return byName;

        return method.Parameters[PositionOf(argument)];
    }

    /// <summary>
    /// The position of a positional <paramref name="argument"/> in its list.
    /// An <c>out</c> argument never binds to a <c>params</c> parameter, so the
    /// position is a parameter index.
    /// </summary>
    private static int PositionOf(ArgumentSyntax argument) =>
        argument.Parent is ArgumentListSyntax list ? list.Arguments.IndexOf(argument) : 0;

    private IrOutTarget OutTargetOf(ExpressionSyntax target, IParameterSymbol parameter) =>
        target switch
        {
            DeclarationExpressionSyntax
            {
                Designation: SingleVariableDesignationSyntax designation
            } declaration => new IrOutLocalDeclaration(
                designation.Identifier.ValueText,
                IrTypeRefMapper.Map(
                    DeclaredLocalType(declaration, designation, parameter),
                    _originResolver,
                    _target
                )
            ),
            DeclarationExpressionSyntax => new IrOutDiscard(),
            _ when _semantic.GetSymbolInfo(target).Symbol is IDiscardSymbol => new IrOutDiscard(),
            _ => new IrOutAssignment(Extract(target)),
        };

    /// <summary>
    /// The type of the local an <c>out</c> declaration introduces. An explicit
    /// <c>out T x</c> keeps <c>T</c>. Roslyn types every <c>var</c> local as
    /// nullable and tracks the real state through flow analysis, so an
    /// <c>out var</c> takes the bound parameter's type instead, made nullable
    /// when <c>[MaybeNullWhen]</c> lets the callee store <c>null</c>.
    /// </summary>
    private ITypeSymbol DeclaredLocalType(
        DeclarationExpressionSyntax declaration,
        SingleVariableDesignationSyntax designation,
        IParameterSymbol parameter
    ) =>
        !declaration.Type.IsVar && _semantic.GetDeclaredSymbol(designation) is ILocalSymbol local
            ? local.Type
            : OutParameterFlow.WidestType(parameter);

    /// <summary>
    /// A <c>ref</c>, <c>in</c> or <c>out</c> argument. <c>in</c> is passed by
    /// value (the callee cannot write through it); an <c>out</c> argument of
    /// the invocation being extracted becomes a slot; any other <c>out</c>
    /// and every <c>ref</c> argument is reported and passed by value.
    /// </summary>
    private IrArgument ExtractByReferenceArgument(ArgumentSyntax argument)
    {
        var name = argument.NameColon?.Name.Identifier.ValueText;

        if (argument.RefOrOutKeyword.IsKind(SyntaxKind.InKeyword))
            return new IrArgument(Extract(argument.Expression), name);

        if (_slottedOutArguments.Contains(argument))
            return new IrArgument(new OutArgumentSlot(), name);

        if (argument.RefOrOutKeyword.IsKind(SyntaxKind.OutKeyword))
            ReportUnsupportedOutArgument(argument);
        else
            ReportUnsupportedRefArgument(argument);

        return new IrArgument(PassedByValue(argument.Expression), name);
    }

    private IrExpression PassedByValue(ExpressionSyntax expression) =>
        expression is DeclarationExpressionSyntax
        || _semantic.GetSymbolInfo(expression).Symbol is IDiscardSymbol
            ? new IrLiteral(null, IrLiteralKind.Default)
            : Extract(expression);

    /// <summary>
    /// Whether <paramref name="method"/> is neither in this compilation nor in
    /// a referenced assembly Metano transpiles — a BCL or third-party method —
    /// or is declared by JavaScript (<c>[Import]</c> / <c>[External]</c>): it
    /// only runs through a declarative mapping that receives the <c>out</c>
    /// defaults.
    /// </summary>
    private bool IsOutsideTranspilation(IMethodSymbol method) =>
        IsJavaScriptDeclared(method)
        || !SymbolEqualityComparer.Default.Equals(
            method.ContainingAssembly,
            _semantic.Compilation.Assembly
        ) && !HasAssemblyTranspileAttribute(method.ContainingAssembly);

    /// <summary>
    /// A method whose JavaScript comes from elsewhere (<c>[Import]</c>,
    /// <c>[External]</c>), so no lowered declaration returns its <c>out</c> values.
    /// </summary>
    private static bool IsJavaScriptDeclared(IMethodSymbol method) =>
        SymbolHelper.HasImport(method)
        || SymbolHelper.HasImport(method.ContainingType)
        || SymbolHelper.HasExternal(method.ContainingType);

    private static void ReportUnsupportedOutArgument(ArgumentSyntax argument) =>
        ReportByReference(
            argument,
            $"'out' argument '{argument.Expression}' is not supported in this call: it is "
                + "passed by value, so the callee's result does not reach it."
        );

    private static void ReportUnsupportedRefArgument(ArgumentSyntax argument) =>
        ReportByReference(
            argument,
            $"'ref' argument '{argument.Expression}' is not supported: it is passed by value, so "
                + "the callee's writes do not reach it."
        );

    private static void ReportUnrepresentableDefault(ArgumentSyntax argument, ITypeSymbol type) =>
        ReportByReference(
            argument,
            $"'default({type.ToDisplayString()})' has no literal in the target: when the call "
                + $"fails, '{argument.Expression}' holds no value instead of the zeroed struct."
        );

    private static void ReportByReference(ArgumentSyntax argument, string message) =>
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.UnsupportedFeature,
                message,
                argument.GetLocation()
            )
        );
}

/// <summary>
/// Stands for a bound <c>out</c> argument while its invocation is extracted;
/// <see cref="IrExpressionExtractor"/> removes it before the call leaves the
/// extractor.
/// </summary>
internal sealed record OutArgumentSlot : IrExpression;
