using Metano.Compiler.Diagnostics;
using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Operations;

namespace Metano.Compiler.Extraction;

/// <summary>
/// The operations C# binds at run time because a value is <c>dynamic</c>
/// (spec 011 FR-030, research R6):
/// <list type="bullet">
///   <item>a member access, call or indexer of a <c>dynamic</c> receiver
///   lowers to <see cref="IrDynamicMemberAccess"/> /
///   <see cref="IrDynamicInvocation"/> / <see cref="IrDynamicIndexerAccess"/>,
///   names verbatim, except the four <c>System.Object</c> members, which lower
///   as on an <c>object</c> receiver;</item>
///   <item>a call, <c>new</c> or indexer of a statically typed receiver with a
///   <c>dynamic</c> argument binds to its single candidate through the ordinary
///   path, or, when its candidates are one overload group
///   (<see cref="LateBoundOverloadGroups"/>), to the group bound to no
///   overload;</item>
///   <item>an operator with a <c>dynamic</c> operand is the plain operator,
///   none of the typed lowerings;</item>
///   <item>a numeric cast becomes an <see cref="IrDynamicNumericConversion"/>;
///   any other cast is the cast from <c>object</c>.</item>
/// </list>
/// What cannot pass through reports <c>MS0053</c>. A target that keeps the
/// source forms (<see cref="IrExtractionContext.KeepsDynamicSourceForms"/>)
/// gets none of this.
/// </summary>
public sealed partial class IrExpressionExtractor
{
    private const string ObjectCastHint =
        "cast the dynamic argument to its parameter type, or to object, to bind the call statically";

    private static readonly IReadOnlyDictionary<string, int> ObjectMemberArities = new Dictionary<
        string,
        int
    >(StringComparer.Ordinal)
    {
        ["ToString"] = 0,
        ["GetHashCode"] = 0,
        ["GetType"] = 0,
        ["Equals"] = 1,
    };

    private static readonly IReadOnlyDictionary<BinaryOperatorKind, string> OperatorMethodNames =
        new Dictionary<BinaryOperatorKind, string>
        {
            [BinaryOperatorKind.Add] = WellKnownMemberNames.AdditionOperatorName,
            [BinaryOperatorKind.Subtract] = WellKnownMemberNames.SubtractionOperatorName,
            [BinaryOperatorKind.Multiply] = WellKnownMemberNames.MultiplyOperatorName,
            [BinaryOperatorKind.Divide] = WellKnownMemberNames.DivisionOperatorName,
            [BinaryOperatorKind.Remainder] = WellKnownMemberNames.ModulusOperatorName,
            [BinaryOperatorKind.LeftShift] = WellKnownMemberNames.LeftShiftOperatorName,
            [BinaryOperatorKind.RightShift] = WellKnownMemberNames.RightShiftOperatorName,
            [BinaryOperatorKind.UnsignedRightShift] =
                WellKnownMemberNames.UnsignedRightShiftOperatorName,
            [BinaryOperatorKind.And] = WellKnownMemberNames.BitwiseAndOperatorName,
            [BinaryOperatorKind.Or] = WellKnownMemberNames.BitwiseOrOperatorName,
            [BinaryOperatorKind.ExclusiveOr] = WellKnownMemberNames.ExclusiveOrOperatorName,
            [BinaryOperatorKind.Equals] = WellKnownMemberNames.EqualityOperatorName,
            [BinaryOperatorKind.NotEquals] = WellKnownMemberNames.InequalityOperatorName,
            [BinaryOperatorKind.LessThan] = WellKnownMemberNames.LessThanOperatorName,
            [BinaryOperatorKind.LessThanOrEqual] = WellKnownMemberNames.LessThanOrEqualOperatorName,
            [BinaryOperatorKind.GreaterThan] = WellKnownMemberNames.GreaterThanOperatorName,
            [BinaryOperatorKind.GreaterThanOrEqual] =
                WellKnownMemberNames.GreaterThanOrEqualOperatorName,
        };

    /// <summary>Whether the target keeps the forms it had before FR-030 (Dart, D28).</summary>
    private bool KeepsDynamicSourceForms => _context.KeepsDynamicSourceForms;

    /// <summary>
    /// <c>d.Name</c> on a <c>dynamic</c> receiver: the name verbatim.
    /// <c>null</c> for any other member access.
    /// </summary>
    private IrExpression? TryExtractDynamicMemberAccess(QualifiedMemberSyntax member)
    {
        if (KeepsDynamicSourceForms || !IsDynamic(member.Receiver))
            return null;

        ReportRenamedMemberAccess(member);

        return new IrDynamicMemberAccess(
            Extract(member.Receiver),
            member.Name.Identifier.ValueText
        );
    }

    /// <summary>
    /// <c>MS0057</c> for <c>x.AsDynamic().Member</c> where the static type of
    /// <c>x</c> declares <c>Member</c> under another emitted name: the
    /// verbatim access misses it at run time (spec 011 FR-036, OD-22).
    /// </summary>
    private void ReportRenamedMemberAccess(QualifiedMemberSyntax member)
    {
        if (_context.EmittedMemberName is not { } emittedNameOf)
            return;

        if (AsDynamicOperandType(member.Receiver) is not { } operandType)
            return;

        var name = member.Name.Identifier.ValueText;

        if (DeclaredMemberNamed(operandType, name) is not { } declared)
            return;

        if (emittedNameOf(declared) == name)
            return;

        // The port's name is a best effort (an overload suffix, an [Ignore] or a
        // BCL mapping can change it), so the message does not quote it.
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Warning,
                DiagnosticCodes.DynamicAccessToRenamedMember,
                $"'{member.Node}' reads '{name}' verbatim, but '{operandType.ToDisplayString()}' emits its member '{name}' under a different name, so the access does not reach it at run time; access it on the typed value instead.",
                member.Node.GetLocation()
            )
        );
    }

    /// <summary>
    /// The static type of <c>x</c> when <paramref name="receiver"/> is
    /// <c>x.AsDynamic()</c> (or <c>InteropExtensions.AsDynamic(x)</c>);
    /// <c>null</c> for any other receiver.
    /// </summary>
    private ITypeSymbol? AsDynamicOperandType(ExpressionSyntax receiver)
    {
        if (!IsAsDynamicCall(receiver))
            return null;

        var call = (InvocationExpressionSyntax)Unparenthesized(receiver);

        var operand =
            call.Expression is MemberAccessExpressionSyntax access
            && call.ArgumentList.Arguments.Count == 0
                ? access.Expression
                : call.ArgumentList.Arguments.FirstOrDefault()?.Expression;

        return operand is null ? null : _semantic.GetTypeInfo(operand).Type;
    }

    /// <summary>
    /// The member named <paramref name="name"/> that <paramref name="type"/>
    /// declares or inherits (its base classes, then its interfaces).
    /// </summary>
    private static ISymbol? DeclaredMemberNamed(ITypeSymbol type, string name)
    {
        IEnumerable<ITypeSymbol> declaringTypes = [.. BaseTypesOf(type), .. type.AllInterfaces];

        return declaringTypes
            .SelectMany(declaring => declaring.GetMembers(name))
            .FirstOrDefault(symbol =>
                symbol is IPropertySymbol or IFieldSymbol or IMethodSymbol or IEventSymbol
                && !symbol.IsImplicitlyDeclared
            );
    }

    private static IEnumerable<ITypeSymbol> BaseTypesOf(ITypeSymbol type)
    {
        for (ITypeSymbol? current = type; current is not null; current = current.BaseType)
            yield return current;
    }

    /// <summary>
    /// A call Roslyn bound at run time: on a <c>dynamic</c> target, or on a
    /// statically typed one whose <c>dynamic</c> argument left several
    /// candidates. <c>null</c> for any other call, which includes a late-bound
    /// call with a single candidate: the ordinary path binds it.
    /// </summary>
    private IrExpression? TryExtractDynamicInvocation(InvocationExpressionSyntax inv)
    {
        if (KeepsDynamicSourceForms)
            return null;

        if (_semantic.GetOperation(inv) is not IDynamicInvocationOperation operation)
            return null;

        if (operation.Operation.Type is IDynamicTypeSymbol)
            return ExtractDynamicTargetCall(inv, operation);

        if (_semantic.GetSymbolInfo(inv).Symbol is not null)
            return null;

        return UnsupportedLateBoundInteropCall(inv) ?? ExtractLateBoundCall(inv, operation);
    }

    private IrExpression ExtractDynamicTargetCall(
        InvocationExpressionSyntax inv,
        IDynamicInvocationOperation operation
    )
    {
        if (TryExtractDynamicObjectMemberCall(inv, operation) is { } objectMemberCall)
            return objectMemberCall;

        if (HasNamedOrByReferenceArgument(operation))
            return ReportByNameOrReferenceArgument(inv);

        return new IrDynamicInvocation(
            Extract(inv.Expression),
            [.. inv.ArgumentList.Arguments.Select(argument => Extract(argument.Expression))]
        );
    }

    /// <summary>
    /// <c>d.ToString()</c>, <c>d.GetHashCode()</c>, <c>d.Equals(x)</c> and
    /// <c>d.GetType()</c> on a <c>dynamic</c> receiver: every value has them,
    /// so they lower as the same call on an <c>object</c> receiver (#506)
    /// rather than verbatim, which no JavaScript value answers.
    /// </summary>
    private IrExpression? TryExtractDynamicObjectMemberCall(
        InvocationExpressionSyntax inv,
        IDynamicInvocationOperation operation
    )
    {
        if (
            operation.Operation
                is not IDynamicMemberReferenceOperation { TypeArguments.Length: 0 } member
            || inv.Expression is not MemberAccessExpressionSyntax access
        )
            return null;

        var isObjectMember =
            ObjectMemberArities.TryGetValue(member.MemberName, out var arity)
            && arity == operation.Arguments.Length
            && !HasNamedOrByReferenceArgument(operation);

        if (!isObjectMember)
            return null;

        var method = _semantic
            .Compilation.GetSpecialType(SpecialType.System_Object)
            .GetMembers(member.MemberName)
            .OfType<IMethodSymbol>()
            .First(candidate => !candidate.IsStatic && candidate.Parameters.Length == arity);

        var origin = BuildOrigin(method);

        return new IrCallExpression(
            new IrMemberAccess(Extract(access.Expression), method.Name, origin),
            [.. inv.ArgumentList.Arguments.Select(ExtractArgument)],
            Origin: origin
        );
    }

    /// <summary>
    /// A call of a statically typed receiver whose <c>dynamic</c> argument
    /// left several candidates: when the candidates are one overload group,
    /// a call of the group bound to no overload, <c>MS0053</c> otherwise. The
    /// call carries no <see cref="IrCallExpression.Overload"/> and no
    /// <see cref="IrMemberAccess.Overload"/>, so a target names the group
    /// itself, as for a call it cannot bind (TypeScript: the runtime
    /// dispatcher, which chooses on the arguments' runtime values).
    /// </summary>
    private IrExpression ExtractLateBoundCall(
        InvocationExpressionSyntax inv,
        IDynamicInvocationOperation operation
    )
    {
        var candidates = _semantic
            .GetSymbolInfo(inv)
            .CandidateSymbols.OfType<IMethodSymbol>()
            .ToList();

        var argumentCount = operation.Arguments.Length;

        if (
            LateBoundOverloadGroups.DispatcherOwnerOf(candidates, argumentCount, _context)
            is not { } owner
        )
            return ReportUnboundCandidates(inv);

        if (HasNamedOrByReferenceArgument(operation))
            return ReportByNameOrReferenceArgument(inv);

        var origin = BuildOrigin(owner);

        return new IrCallExpression(
            new IrMemberAccess(LateBoundReceiverOf(inv, owner), owner.Name, origin),
            [.. inv.ArgumentList.Arguments.Select(ExtractArgument)],
            Origin: origin
        );
    }

    /// <summary>
    /// The receiver of a late-bound call: the written one, or the implicit
    /// <c>this</c> / declaring type of a call written by its bare name.
    /// </summary>
    private IrExpression LateBoundReceiverOf(InvocationExpressionSyntax inv, IMethodSymbol owner) =>
        inv.Expression switch
        {
            MemberAccessExpressionSyntax access => Extract(access.Expression),
            _ when owner.IsStatic => TypeReferenceTo(owner.ContainingType),
            _ => new IrThisExpression(),
        };

    /// <summary>
    /// A <c>new</c> Roslyn bound at run time with several constructor
    /// candidates: when they are the constructors of one dispatched set, the
    /// construction bound to no overload (TypeScript: the constructor
    /// dispatcher), <c>MS0053</c> otherwise. An
    /// initializer on such a <c>new</c> reports <c>MS0053</c> too. <c>null</c>
    /// for any other construction, which includes a late-bound one with a
    /// single candidate.
    /// </summary>
    private IrExpression? TryExtractDynamicCreation(BaseObjectCreationExpressionSyntax creation)
    {
        if (KeepsDynamicSourceForms)
            return null;

        if (_semantic.GetOperation(creation) is not IDynamicObjectCreationOperation operation)
            return null;

        if (operation.Initializer is not null)
            return ReportUnsupportedDynamic(
                creation,
                $"The initializer of '{creation}' cannot run on a construction bound at run time by a dynamic argument."
            );

        var symbolInfo = _semantic.GetSymbolInfo(creation);

        if (symbolInfo.Symbol is not null)
            return null;

        var candidates = symbolInfo.CandidateSymbols.OfType<IMethodSymbol>().ToList();

        if (
            !LateBoundOverloadGroups.IsDispatchedConstructorSet(
                candidates,
                operation.Arguments.Length,
                _context
            )
        )
            return ReportUnboundCandidates(creation);

        if (HasNamedOrByReferenceArgument(operation))
            return ReportByNameOrReferenceArgument(creation);

        return ExtractTypedCreation(creation);
    }

    /// <summary>
    /// <c>d[i]</c> on a <c>dynamic</c> receiver, or an indexer of a statically
    /// typed receiver that a <c>dynamic</c> index left with several candidates
    /// (<c>MS0053</c>). <c>null</c> for any other indexer.
    /// </summary>
    private IrExpression? TryExtractDynamicIndexer(
        ExpressionSyntax node,
        ExpressionSyntax receiverSyntax,
        BracketedArgumentListSyntax arguments
    )
    {
        if (KeepsDynamicSourceForms)
            return null;

        if (_semantic.GetOperation(node) is not IDynamicIndexerAccessOperation operation)
            return null;

        if (!IsDynamic(receiverSyntax))
        {
            return _semantic.GetSymbolInfo(node).Symbol is null
                ? ReportUnboundCandidates(node)
                : null;
        }

        if (HasNamedOrByReferenceArgument(operation))
            return ReportByNameOrReferenceArgument(node);

        if (arguments.Arguments is not [var index])
            return ReportUnsupported(
                node,
                node.Kind().ToString(),
                $"Indexer '{node}' on a dynamic receiver takes {arguments.Arguments.Count} indices; JavaScript indexes with one."
            );

        return new IrDynamicIndexerAccess(Extract(receiverSyntax), Extract(index.Expression));
    }

    /// <summary>
    /// <c>left op right</c> where an operand is <c>dynamic</c>: the plain
    /// operator over the operands as written, with none of the typed
    /// lowerings (<c>int</c> wrapping, <c>bigint</c>, <c>Decimal</c>,
    /// <c>char</c> codes, record equality, string concatenation); a 64-bit
    /// literal operand is a <c>number</c>. A user-defined operator of the
    /// statically typed operand reports <c>MS0053</c>. <c>null</c> when no
    /// operand is <c>dynamic</c>.
    /// </summary>
    private IrExpression? TryExtractDynamicBinary(BinaryExpressionSyntax bin, IrBinaryOp op)
    {
        if (KeepsDynamicSourceForms)
            return null;

        if (_semantic.GetOperation(bin) is not IBinaryOperation operation)
            return null;

        var hasDynamicOperand =
            operation.LeftOperand.Type is IDynamicTypeSymbol
            || operation.RightOperand.Type is IDynamicTypeSymbol;

        if (!hasDynamicOperand)
            return null;

        if (
            DeclaresOperator(operation.LeftOperand.Type, operation.OperatorKind)
            || DeclaresOperator(operation.RightOperand.Type, operation.OperatorKind)
        )
            return ReportUserOperator(bin, bin.OperatorToken);

        return new IrBinaryExpression(DynamicOperand(bin.Left), op, DynamicOperand(bin.Right));
    }

    /// <summary>
    /// <c>target op= value</c> where either side is <c>dynamic</c>, once the
    /// target-shape lowerings (extension property and indexer setters, events)
    /// have declined it. A <c>dynamic</c> target is the plain compound
    /// operator, with none of the typed lowerings of
    /// <see cref="TryExtractDynamicBinary"/> (<c>d += 1L</c> adds a
    /// <c>number</c>). A typed target keeps the ordinary compound lowering,
    /// which converts the result back to the target's type as C# does
    /// (<c>i += d</c> wraps to 32 bits, <c>c += d</c> stores a code unit). A
    /// user-defined operator of the statically typed side reports
    /// <c>MS0053</c>. <c>null</c> for any other assignment.
    /// </summary>
    private IrExpression? TryExtractDynamicCompoundAssignment(AssignmentExpressionSyntax assign)
    {
        if (KeepsDynamicSourceForms)
            return null;

        if (_semantic.GetOperation(assign) is not ICompoundAssignmentOperation operation)
            return null;

        var hasDynamicSide =
            operation.Target.Type is IDynamicTypeSymbol
            || operation.Value.Type is IDynamicTypeSymbol;

        if (!hasDynamicSide)
            return null;

        if (
            DeclaresOperator(operation.Target.Type, operation.OperatorKind)
            || DeclaresOperator(operation.Value.Type, operation.OperatorKind)
        )
            return ReportUserOperator(assign, assign.OperatorToken);

        if (operation.Target.Type is not IDynamicTypeSymbol)
            return null;

        return new IrBinaryExpression(
            Extract(assign.Left),
            MapAssignmentOp(assign.Kind()),
            DynamicOperand(assign.Right)
        );
    }

    /// <summary>
    /// An operand of an operator bound at run time: a 64-bit literal is the
    /// JavaScript <c>number</c> a <c>dynamic</c> integer holds, so
    /// <c>d + 1L</c> is <c>d + 1</c>.
    /// </summary>
    private IrExpression DynamicOperand(ExpressionSyntax operand) =>
        _semantic.GetConstantValue(operand) is { HasValue: true, Value: long or ulong } constant
            ? new IrLiteral(Convert.ToDouble(constant.Value), IrLiteralKind.Float64)
            : Extract(operand);

    /// <summary>
    /// <c>(T)d</c>: a numeric <c>T</c> converts the runtime value
    /// (<see cref="IrDynamicNumericConversion"/>); a <c>T</c> that declares a
    /// user-defined conversion reports <c>MS0053</c>; a reference type or type
    /// parameter is the cast from <c>object</c>; any other value type keeps
    /// the bare operand. <c>null</c> when the operand is not <c>dynamic</c>.
    /// </summary>
    private IrExpression? TryExtractDynamicCast(
        CastExpressionSyntax cast,
        IrExpression inner,
        ITypeSymbol sourceType,
        ITypeSymbol targetType
    )
    {
        if (KeepsDynamicSourceForms || sourceType is not IDynamicTypeSymbol)
            return null;

        if (DynamicNumericTarget(targetType) is { } kind)
        {
            return new IrDynamicNumericConversion(
                inner,
                kind,
                CheckedContexts.IsChecked(cast, _semantic.Compilation),
                targetType.IsNullableValueType()
            );
        }

        if (DeclaresUserConversion(targetType))
            return ReportUserConversion(cast, targetType);

        return new IrCastExpression(
            inner,
            ExtractTargetType(cast),
            ReferenceConversion: ReferenceConversionOf(cast, targetType)
        );
    }

    /// <summary>
    /// An implicit conversion of a <c>dynamic</c> value to a type that declares
    /// a user-defined conversion (<c>Money m = d;</c>): the runtime binder
    /// would call the operator, which the target cannot choose, so it reports
    /// <c>MS0053</c>. <c>null</c> for any other expression.
    /// </summary>
    private IrExpression? UnsupportedDynamicUserConversion(ExpressionSyntax expression)
    {
        if (KeepsDynamicSourceForms)
            return null;

        var typeInfo = _semantic.GetTypeInfo(expression);

        var convertsDynamic =
            typeInfo.Type is IDynamicTypeSymbol
            && typeInfo.ConvertedType is { } converted
            && converted is not IDynamicTypeSymbol
            && DynamicNumericTarget(converted) is null
            && DeclaresUserConversion(converted);

        return convertsDynamic ? ReportUserConversion(expression, typeInfo.ConvertedType!) : null;
    }

    private bool IsDynamic(ExpressionSyntax expression) =>
        _semantic.GetTypeInfo(expression).Type is IDynamicTypeSymbol;

    /// <summary>The numeric kind a cast of a <c>dynamic</c> value converts to; <c>null</c> for an enum or a non-numeric type.</summary>
    private static IrNumericKind? DynamicNumericTarget(ITypeSymbol type) =>
        NumericEndpoints.Of(type) is { IsEnum: false } endpoint ? endpoint.Kind : null;

    /// <summary>
    /// Whether <paramref name="type"/> (or its nullable underlying type)
    /// declares a user-defined conversion into itself.
    /// </summary>
    private static bool DeclaresUserConversion(ITypeSymbol type)
    {
        if (type.UnwrapNullableValueType() is not INamedTypeSymbol named)
            return false;

        return named
            .GetMembers()
            .OfType<IMethodSymbol>()
            .Any(method =>
                method.MethodKind == MethodKind.Conversion
                && SymbolEqualityComparer.Default.Equals(
                    method.ReturnType.UnwrapNullableValueType(),
                    named
                )
            );
    }

    /// <summary>
    /// Whether <paramref name="type"/> declares the user-defined operator of
    /// <paramref name="kind"/>. The operators C# predefines are built in: a
    /// <c>string</c>'s equality and the numeric operators (<c>double</c> and
    /// <c>float</c> also declare them as members), except <c>decimal</c>'s,
    /// whose operators are methods of its runtime class.
    /// </summary>
    private static bool DeclaresOperator(ITypeSymbol? type, BinaryOperatorKind kind)
    {
        if (type is null or IDynamicTypeSymbol || HasPredefinedOperators(type))
            return false;

        if (!OperatorMethodNames.TryGetValue(kind, out var name))
            return false;

        return type.UnwrapNullableValueType().GetMembers(name).OfType<IMethodSymbol>().Any();
    }

    private static bool HasPredefinedOperators(ITypeSymbol type) =>
        type.SpecialType == SpecialType.System_String
        || NumericEndpoints.Of(type) is { IsEnum: false, Kind: not IrNumericKind.Decimal };

    private static bool HasNamedOrByReferenceArgument(IDynamicInvocationOperation operation) =>
        HasNamedOrByReferenceArgument(
            operation.Arguments.Length,
            operation.GetArgumentName,
            operation.GetArgumentRefKind
        );

    private static bool HasNamedOrByReferenceArgument(IDynamicIndexerAccessOperation operation) =>
        HasNamedOrByReferenceArgument(
            operation.Arguments.Length,
            operation.GetArgumentName,
            operation.GetArgumentRefKind
        );

    private static bool HasNamedOrByReferenceArgument(IDynamicObjectCreationOperation operation) =>
        HasNamedOrByReferenceArgument(
            operation.Arguments.Length,
            operation.GetArgumentName,
            operation.GetArgumentRefKind
        );

    private static bool HasNamedOrByReferenceArgument(
        int count,
        Func<int, string?> nameOf,
        Func<int, RefKind?> refKindOf
    ) =>
        Enumerable
            .Range(0, count)
            .Any(index =>
                nameOf(index) is not null || refKindOf(index) is not (null or RefKind.None)
            );

    private IrUnsupportedExpression ReportByNameOrReferenceArgument(SyntaxNode node) =>
        ReportUnsupportedDynamic(
            node,
            $"'{node}' is bound at run time by a dynamic value, so its named or ref / out / in arguments cannot be matched to parameters."
        );

    private IrUnsupportedExpression ReportUnboundCandidates(SyntaxNode node) =>
        ReportUnsupportedDynamic(
            node,
            $"'{node}' is bound at run time by a dynamic argument among candidates that are not one overload group; {ObjectCastHint}."
        );

    private static IrUnsupportedExpression ReportUserOperator(SyntaxNode node, SyntaxToken token) =>
        ReportUnsupportedDynamic(
            node,
            $"Operator '{token.ValueText}' in '{node}' would bind a user-defined operator at run time; cast the dynamic operand to the operand type."
        );

    private IrUnsupportedExpression ReportUserConversion(SyntaxNode node, ITypeSymbol type) =>
        ReportUnsupportedDynamic(
            node,
            $"Converting the dynamic value of '{node}' to '{type.ToDisplayString()}' would run a user-defined conversion at run time; convert the value explicitly."
        );

    private static IrUnsupportedExpression ReportUnsupportedDynamic(SyntaxNode node, string message)
    {
        ExtractionDiagnostics.Report(
            new MetanoDiagnostic(
                MetanoDiagnosticSeverity.Error,
                DiagnosticCodes.UnsupportedDynamicOperation,
                message,
                node.GetLocation()
            )
        );

        return new IrUnsupportedExpression(node.Kind().ToString());
    }
}
