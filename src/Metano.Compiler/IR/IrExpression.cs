namespace Metano.Compiler.IR;

/// <summary>
/// A semantic expression in the IR. Represents what the source code *means*,
/// not how any target language renders it.
/// </summary>
public abstract record IrExpression;

// -- Literals --

/// <summary>
/// A literal value (string, number, boolean, null, etc.).
/// </summary>
public sealed record IrLiteral(object? Value, IrLiteralKind Kind) : IrExpression;

/// <summary>
/// Categories of literal values.
/// </summary>
public enum IrLiteralKind
{
    Null,
    Boolean,
    Int32,
    Int64,
    Float64,
    Decimal,
    BigInteger,
    String,
    Char,
    Default,
}

// -- References --

/// <summary>
/// A reference to a variable, parameter, or member by name. Member casing should be
/// normalized by the target backend (C#'s PascalCase properties become camelCase in
/// JS/Dart, but type references preserve PascalCase — see <see cref="IrTypeReference"/>).
/// </summary>
public sealed record IrIdentifier(string Name) : IrExpression;

/// <summary>
/// A reference to a <em>type</em> used as an expression (e.g., the <c>Counter</c> in
/// <c>Counter.Zero</c>). Distinct from <see cref="IrIdentifier"/> so backends can
/// preserve PascalCase for type names while still lower-camel-casing ordinary member
/// and variable references.
/// </summary>
public sealed record IrTypeReference(string Name) : IrExpression;

/// <summary>
/// A reference to <c>this</c> (the current instance).
/// </summary>
public sealed record IrThisExpression() : IrExpression;

/// <summary>
/// A reference to <c>base</c> (the parent class instance).
/// </summary>
public sealed record IrBaseExpression() : IrExpression;

// -- Member access --

/// <summary>
/// Declaring-type context for a member access or call. Carries the member's owner
/// type as a fully-qualified source-language name (e.g.,
/// <c>System.Collections.Generic.List&lt;T&gt;</c>) so backends can key BCL mapping
/// tables by (declaring type, member name). The name is the type's original
/// (open-generic) definition, so closed generics share a single key —
/// <c>List&lt;int&gt;.Add</c> and <c>List&lt;Money&gt;.Add</c> both map to
/// <c>System.Collections.Generic.List&lt;T&gt;</c>.
/// <para>
/// <see cref="MemberName"/> is on the record because call sites can have an
/// <c>IrIdentifier</c> target (unqualified calls) where the name is not
/// otherwise reachable from the parent node.
/// </para>
/// </summary>
public sealed record IrMemberOrigin(
    string DeclaringTypeFullName,
    string MemberName,
    bool IsStatic = false,
    bool IsEnumMember = false,
    bool IsInlineWrapperMember = false,
    string? EmittedName = null,
    bool IsPlainObjectInstanceMethod = false,
    bool IsStringEnumMember = false,
    bool IsDeclaringTypeExternal = false,
    bool IsDeclaringTypeErasable = false
);

/// <summary>
/// Member access: <c>target.memberName</c>. <see cref="Origin"/> is null for
/// synthetic or unresolved nodes.
/// </summary>
public sealed record IrMemberAccess(
    IrExpression Target,
    string MemberName,
    IrMemberOrigin? Origin = null
) : IrExpression;

/// <summary>
/// Element/index access: <c>target[index]</c>.
/// </summary>
public sealed record IrElementAccess(
    IrExpression Target,
    IrExpression Index,
    IrTypeRef? TargetType = null
) : IrExpression;

/// <summary>
/// Optional chaining member access: <c>target?.memberName</c>.
/// </summary>
public sealed record IrOptionalChain(IrExpression Target, string MemberName) : IrExpression;

// -- Invocations --

/// <summary>
/// A method/function call expression.
/// <para>
/// <see cref="Origin"/> carries the declaring type + method name resolved from the
/// source symbol; the declaring type can differ from <see cref="Target"/> (e.g.,
/// <c>myList.Add(x)</c> has target <c>myList</c> but origin declaring type
/// <c>List&lt;T&gt;</c>). Backends consult it to dispatch BCL mappings.
/// </para>
/// </summary>
public sealed record IrCallExpression(
    IrExpression Target,
    IReadOnlyList<IrArgument> Arguments,
    IReadOnlyList<IrTypeRef>? TypeArguments = null,
    IrMemberOrigin? Origin = null
) : IrExpression;

/// <summary>
/// A single argument in a call or object-creation expression.
/// <para>
/// <see cref="Name"/> is populated when the source used a named-argument
/// syntax (<c>new Foo(x, Priority: p)</c>). Positional arguments leave it
/// null. Backends that support named arguments (Dart) can render them
/// directly; backends that don't (TypeScript) consult the declaring
/// method's parameter list to reorder and fill in skipped defaults —
/// matching the legacy lowering the pipeline already produces.
/// </para>
/// </summary>
public sealed record IrArgument(IrExpression Value, string? Name = null);

/// <summary>
/// Object creation: <c>new T(args)</c>.
/// <para>
/// <paramref name="IsPlainObject"/> flags constructions where the target
/// type carries <c>[PlainObject]</c>: backends that emit plain object
/// literals (TS) read <paramref name="ParameterNames"/> to key the
/// properties by constructor parameter name, while backends that treat
/// every type as a real class (Dart) just render the normal
/// <c>new T(args)</c>.
/// </para>
/// </summary>
public sealed record IrNewExpression(
    IrTypeRef Type,
    IReadOnlyList<IrArgument> Arguments,
    bool IsPlainObject = false,
    IReadOnlyList<string>? ParameterNames = null
) : IrExpression;

// -- Operators --

/// <summary>
/// Binary expression: <c>left op right</c>.
/// </summary>
public sealed record IrBinaryExpression(IrExpression Left, IrBinaryOp Operator, IrExpression Right)
    : IrExpression;

/// <summary>
/// Unary expression: <c>op operand</c> or <c>operand op</c>.
/// </summary>
public sealed record IrUnaryExpression(
    IrUnaryOp Operator,
    IrExpression Operand,
    bool IsPrefix = true
) : IrExpression;

/// <summary>
/// Semantic binary operators.
/// </summary>
public enum IrBinaryOp
{
    Add,
    Subtract,
    Multiply,
    Divide,
    Modulo,
    Equal,
    NotEqual,
    LessThan,
    LessThanOrEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LogicalAnd,
    LogicalOr,
    BitwiseAnd,
    BitwiseOr,
    BitwiseXor,
    LeftShift,
    RightShift,
    UnsignedRightShift,
    NullCoalescing,
    Assign,
    AddAssign,
    SubtractAssign,
    MultiplyAssign,
    DivideAssign,
    ModuloAssign,
    BitwiseAndAssign,
    BitwiseOrAssign,
    BitwiseXorAssign,
    LeftShiftAssign,
    RightShiftAssign,
    UnsignedRightShiftAssign,
    NullCoalescingAssign,
}

/// <summary>
/// Semantic unary operators.
/// </summary>
public enum IrUnaryOp
{
    Negate,
    LogicalNot,
    BitwiseNot,
    Increment,
    Decrement,
}

// -- Conversions and checks --

/// <summary>
/// Type cast: <c>(T)expr</c>.
/// </summary>
public sealed record IrCastExpression(IrExpression Expression, IrTypeRef TargetType) : IrExpression;

/// <summary>
/// Type check: <c>expr is T</c>.
/// </summary>
public sealed record IrTypeCheck(IrExpression Expression, IrTypeRef Type) : IrExpression;

// -- Conditional --

/// <summary>
/// Ternary conditional: <c>condition ? whenTrue : whenFalse</c>.
/// </summary>
public sealed record IrConditionalExpression(
    IrExpression Condition,
    IrExpression WhenTrue,
    IrExpression WhenFalse
) : IrExpression;

// -- Async --

/// <summary>
/// <c>await</c> expression.
/// </summary>
public sealed record IrAwaitExpression(IrExpression Expression) : IrExpression;

/// <summary>
/// <c>yield</c> expression (iterator methods).
/// </summary>
public sealed record IrYieldExpression(IrExpression? Value) : IrExpression;

// -- Lambdas --

/// <summary>
/// Lambda/anonymous function expression. When the lambda targets a
/// delegate whose first parameter carries <c>[This]</c>, the
/// extractor sets <paramref name="UsesThis"/> to <c>true</c> and
/// records the receiver type under <paramref name="ThisType"/>; the
/// lambda's first parameter stays in <paramref name="Parameters"/>
/// so the TypeScript bridge can hand the arrow to the
/// <c>bindReceiver</c> runtime helper, which captures the
/// dispatcher's JS <c>this</c> and forwards it into that parameter.
/// The arrow itself keeps lexical <c>this</c>, so any reference to
/// the enclosing C# class's <c>this</c> inside the body resolves
/// through ordinary closure capture. Backends that do not have a
/// JS-style <c>this</c> rebinding (Dart, Kotlin) ignore the flag
/// and emit the positional parameters as written.
/// </summary>
public sealed record IrLambdaExpression(
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef? ReturnType,
    IReadOnlyList<IrStatement> Body,
    bool IsAsync = false,
    bool UsesThis = false,
    IrTypeRef? ThisType = null
) : IrExpression;

// -- Collection literals --

/// <summary>
/// Array/list literal: <c>[a, b, c]</c>.
/// </summary>
public sealed record IrArrayLiteral(IReadOnlyList<IrExpression> Elements) : IrExpression;

/// <summary>
/// Object literal / anonymous object: <c>{ prop1 = a, prop2 = b }</c>.
/// </summary>
public sealed record IrObjectLiteral(IReadOnlyList<(string Name, IrExpression Value)> Properties)
    : IrExpression;

/// <summary>
/// Spread expression: <c>...expr</c>.
/// </summary>
public sealed record IrSpreadExpression(IrExpression Expression) : IrExpression;

// -- String interpolation --

/// <summary>
/// String interpolation: <c>$"hello {name}"</c>.
/// </summary>
public sealed record IrStringInterpolation(IReadOnlyList<IrInterpolationPart> Parts) : IrExpression;

/// <summary>
/// A part of a string interpolation.
/// </summary>
public abstract record IrInterpolationPart;

/// <summary>
/// A literal text segment within an interpolated string.
/// </summary>
public sealed record IrInterpolationText(string Text) : IrInterpolationPart;

/// <summary>
/// An expression segment within an interpolated string.
/// </summary>
public sealed record IrInterpolationExpression(
    IrExpression Expression,
    string? FormatSpecifier = null
) : IrInterpolationPart;

// -- Pattern matching --

/// <summary>
/// <c>expr is pattern</c>. Evaluates to a boolean and — for type and var
/// patterns — binds a new variable in the true branch. Backends render the test
/// and the binding according to the pattern kind.
/// </summary>
public sealed record IrIsPatternExpression(IrExpression Expression, IrPattern Pattern)
    : IrExpression;

/// <summary>
/// C# <c>switch</c> expression: <c>value switch { pattern => result, ... }</c>.
/// Arms are tried in order; the first matching pattern's <see cref="IrSwitchArm.Result"/>
/// is the value of the expression. Arms may include a <c>when</c> guard that
/// refines the match. Backends decide how to render this — Dart 3 has native
/// switch expressions, TypeScript lowers to an IIFE with <c>if/else</c>.
/// </summary>
public sealed record IrSwitchExpression(IrExpression Scrutinee, IReadOnlyList<IrSwitchArm> Arms)
    : IrExpression;

/// <summary>
/// One arm of an <see cref="IrSwitchExpression"/>.
/// </summary>
/// <param name="Pattern">The pattern tested against the scrutinee.</param>
/// <param name="WhenClause">Optional <c>when</c> guard expression — evaluated
/// only after the pattern matches.</param>
/// <param name="Result">The expression the arm evaluates to when it wins.</param>
public sealed record IrSwitchArm(IrPattern Pattern, IrExpression? WhenClause, IrExpression Result);

// -- Non-destructive update --

/// <summary>
/// C# <c>source with { X = expr, Y = expr2 }</c> non-destructive update.
/// Produces a new record-like value where each named member is replaced by
/// the given expression; all other members are copied from <see cref="Source"/>.
/// Backends decide the lowering — TypeScript maps to
/// <c>source.with({ x: expr })</c> (or an object spread literal for
/// <c>[PlainObject]</c> shapes), Dart maps to <c>source.copyWith(x: expr)</c>.
/// </summary>
public sealed record IrWithExpression(
    IrExpression Source,
    IReadOnlyList<IrWithAssignment> Assignments,
    bool IsPlainObjectSource = false
) : IrExpression;

/// <summary>
/// A single <c>MemberName = value</c> entry inside an
/// <see cref="IrWithExpression"/>.
/// </summary>
public sealed record IrWithAssignment(string MemberName, IrExpression Value);

// -- Throw --

/// <summary>
/// Throw expression (C# 7+): <c>throw new Exception()</c> as an expression.
/// </summary>
public sealed record IrThrowExpression(IrExpression Expression) : IrExpression;

// -- Runtime helpers --

/// <summary>
/// A call to a runtime helper function. The <see cref="HelperName"/> is a semantic
/// identifier that each backend maps to its own runtime library.
/// </summary>
public sealed record IrRuntimeHelperCall(string HelperName, IReadOnlyList<IrExpression> Arguments)
    : IrExpression;

/// <summary>
/// A template-based expression expansion. Used for declarative BCL mappings
/// where the lowering is expressed as a template string with argument placeholders.
/// </summary>
/// <param name="Template">Template string with <c>$0</c>, <c>$1</c>, etc. placeholders.</param>
/// <param name="Receiver">The receiver expression (<c>$0</c>), if any.</param>
/// <param name="Arguments">Arguments filling <c>$1</c>, <c>$2</c>, etc.</param>
/// <param name="RequiredImports">Runtime imports needed for this template
/// (e.g., helper functions from the target's runtime library).</param>
public sealed record IrTemplateExpression(
    string Template,
    IrExpression? Receiver,
    IReadOnlyList<IrExpression> Arguments,
    IReadOnlyList<string>? RequiredImports = null
) : IrExpression;
