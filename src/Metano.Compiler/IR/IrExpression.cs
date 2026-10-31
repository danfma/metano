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
/// <para>
/// <see cref="Origin"/> is the cross-assembly origin of the referenced type when it
/// comes from another <c>[EmitPackage]</c> package — the same value
/// <see cref="IrNamedTypeRef.Origin"/> carries in type positions — so backends can
/// import a type that appears only in a value position. Null for intra-project,
/// BCL and synthesized references.
/// </para>
/// </summary>
public sealed record IrTypeReference(string Name, IrTypeOrigin? Origin = null) : IrExpression;

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
/// <param name="IsJsCallableInvoke">The call targets <c>Invoke</c> on a <c>[JsCallable]</c> interface; the receiver IS the JS function, so <c>recv.Invoke(args)</c> lowers to <c>recv(args)</c>.</param>
/// <param name="IsJsTupleElement">The member access reads a positional element of a <c>[JsTuple]</c> value, lowering to array-index access <c>recv[i]</c>.</param>
/// <param name="TupleIndex">The positional index (declaration order) when <see cref="IsJsTupleElement"/> is true; <c>-1</c> otherwise.</param>
/// <param name="IsTranspiledDataMember">The member is a field or property (not an enum member or indexer) declared on a type whose declaration the toolchain emits — a type of the current compilation or of a transpiled package, neither <c>[External]</c> nor <c>[Import]</c>. A backend that renames data members at their declaration (TypeScript escapes reserved words: <c>Default</c> → <c>default_</c>) applies the same rename at the access; a foreign member keeps the name its real declaration has.</param>
/// <param name="IsBrandedValueMember">The member access reads the single wrapped value member of a <c>[Branded]</c> struct (<c>id.Value</c>). The branded value IS the primitive at runtime, so backends that lower the wrapper to a branded primitive collapse the read to the receiver.</param>
/// <param name="ParameterTypes">For a method or constructor, the stable full names of the bound overload's parameter types (open definitions), so a declarative mapping can tell same-arity overloads apart; <c>null</c> for other members.</param>
/// <param name="IsInitOnly">The member is a property whose setter is an <c>init</c> accessor. C# writes such a property only from the declaring type's constructors and accessors (through <c>this</c> / <c>base</c>) and from object initializers, so a write through any other receiver is an object-initializer write; a backend that declares init-only properties read-only (TypeScript <c>readonly</c>) lowers that write to a form its type checker accepts.</param>
public sealed record IrMemberOrigin(
    string DeclaringTypeFullName,
    string MemberName,
    bool IsStatic = false,
    bool IsEnumMember = false,
    bool IsBrandedMember = false,
    string? EmittedName = null,
    bool IsPlainObjectInstanceMethod = false,
    bool IsStringEnumMember = false,
    bool IsDeclaringTypeExternal = false,
    bool IsDeclaringTypeNoContainer = false,
    bool IsJsCallableInvoke = false,
    bool IsJsTupleElement = false,
    int TupleIndex = -1,
    bool IsBrandedValueMember = false,
    bool IsTranspiledDataMember = false,
    IReadOnlyList<string>? ParameterTypes = null,
    bool IsInitOnly = false
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
/// A copy of the half-open element range <c>[Start, End)</c> of a sequence
/// (array, list or string): the lowering of a C# range access
/// <c>target[start..end]</c>. Both bounds are plain zero-based offsets — the
/// extractor already resolved any from-end bound (<c>^n</c>) to
/// <c>length - n</c>. A <c>null</c> <see cref="Start"/> means the sequence
/// start and a <c>null</c> <see cref="End"/> means the sequence end.
/// <see cref="TargetType"/> is the receiver's type, so a backend can tell a
/// string from a list when the two slice differently.
/// </summary>
public sealed record IrSliceExpression(
    IrExpression Target,
    IrExpression? Start,
    IrExpression? End,
    IrTypeRef? TargetType = null
) : IrExpression;

/// <summary>
/// A null-conditional access (<c>receiver?.A</c>, <c>receiver?.A.B(x)</c>,
/// <c>receiver?[i]</c>), shaped like Roslyn's
/// <c>IConditionalAccessOperation</c>: <see cref="Receiver"/> is evaluated
/// once; the whole expression yields <c>null</c> when it is null and
/// <see cref="WhenNotNull"/> otherwise. <see cref="WhenNotNull"/> is the rest
/// of the chain extracted as an ordinary expression, so every step keeps its
/// own lowering (member mappings, extension rewrites, LINQ); it reads the
/// receiver through <see cref="IrConditionalReceiver"/> placeholders. How the
/// receiver is evaluated once (native optional chaining, repeating a
/// side-effect-free receiver, or binding it) is the backend's decision.
/// </summary>
public sealed record IrNullConditionalAccess(IrExpression Receiver, IrExpression WhenNotNull)
    : IrExpression;

/// <summary>
/// The value of the nearest enclosing <see cref="IrNullConditionalAccess"/>'s
/// receiver, read inside that access's
/// <see cref="IrNullConditionalAccess.WhenNotNull"/> (Roslyn's
/// <c>IConditionalAccessInstanceOperation</c>). It is never null there and
/// reading it has no side effects. A placeholder inside a nested access's
/// <see cref="IrNullConditionalAccess.Receiver"/> still refers to the
/// enclosing access; one inside a nested access's
/// <see cref="IrNullConditionalAccess.WhenNotNull"/> refers to the nested one.
/// A backend that binds receivers to a reused name must therefore evaluate a
/// receiver that reads a placeholder in the enclosing scope (bind it, never
/// repeat it inside the nested guard).
/// </summary>
public sealed record IrConditionalReceiver : IrExpression;

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
    IrMemberOrigin? Origin = null,
    bool IsOptional = false
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
/// <para>
/// <see cref="IsSpread"/> tags arguments that flow directly into a rest /
/// <c>params</c> slot as a single array, so backends emit them with the
/// spread prefix.
/// </para>
/// </summary>
public sealed record IrArgument(IrExpression Value, string? Name = null, bool IsSpread = false);

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
/// <para>
/// <paramref name="IsJsTuple"/> flags constructions where the target type
/// carries <c>[JsTuple]</c>: backends that emit array tuples (TS) lower
/// <c>new T(a, b)</c> to a positional array literal <c>[a, b]</c>, mirroring
/// the <c>[PlainObject]</c> object-literal lowering.
/// </para>
/// <para>
/// <paramref name="Initializers"/> carries the <c>Member = value</c> clauses
/// of an object-initializer construction (<c>new T { … }</c>). A JSX backend
/// reads them to populate element attributes / children.
/// </para>
/// <para>
/// <paramref name="ExternalImports"/> carries the <c>[Import]</c> module
/// dependency of an imported JSX-renderable type (a <c>solid-router</c>
/// <c>Route</c>, etc.) so a backend that lowers the <c>new</c> to a JSX
/// element can resolve the capitalized tag to its npm module instead of an
/// intra-project component file. Null for every other construction; the value
/// import is otherwise resolved from the declaring type.
/// </para>
/// <para>
/// <paramref name="ExceptionConstruction"/> is set when the constructed type is
/// an exception with no emitted target declaration (a BCL exception): it
/// classifies <paramref name="Arguments"/> by constructor-parameter role so a
/// backend can lower the construction to its native error shape. Null for
/// every other construction.
/// </para>
/// <para>
/// <paramref name="Origin"/> identifies the bound constructor (member name
/// <c>.ctor</c>, with its overload's parameter types) so a backend can apply a
/// declarative constructor mapping (<c>[MapMethod(typeof(List&lt;&gt;), ".ctor")]</c>)
/// to a type that has no emitted declaration; <paramref name="TypeArguments"/>
/// are the constructed type's type arguments, which such a mapping can splice
/// into its template. <paramref name="Origin"/> is null when the constructor
/// did not resolve; <paramref name="TypeArguments"/> is null for a non-generic
/// type. Constructor mappings are applied by the TypeScript backend only today.
/// </para>
/// </summary>
public sealed record IrNewExpression(
    IrTypeRef Type,
    IReadOnlyList<IrArgument> Arguments,
    bool IsPlainObject = false,
    IReadOnlyList<string>? ParameterNames = null,
    bool IsObjectArgsCtor = false,
    bool IsJsTuple = false,
    IReadOnlyList<IrMemberInit>? Initializers = null,
    IReadOnlyList<IrExternalImport>? ExternalImports = null,
    IrExceptionConstruction? ExceptionConstruction = null,
    IrMemberOrigin? Origin = null,
    IReadOnlyList<IrTypeRef>? TypeArguments = null
) : IrExpression;

/// <summary>
/// A single <c>Member = value</c> assignment inside an object-initializer
/// clause (<c>new T { Member = value, ... }</c>).
/// <para>
/// <see cref="MemberName"/> is the original C# member name;
/// <see cref="EmittedName"/> is the resolved <c>[Name]</c> override for the
/// active target (or <c>null</c>, in which case the backend camelCases the
/// member name itself). <see cref="Value"/> is the lowered right-hand
/// expression. Order is source order — backends that care about stable child
/// / attribute ordering (JSX) rely on it.
/// <para>
/// <see cref="IsChildrenSlot"/> is set when the assigned member is the element
/// base's children-collection slot (a member typed as an array of the marked
/// JSX element type). A JSX backend routes such an assignment to the element's
/// children rather than to an attribute — by this resolved flag, never by a
/// hard-coded member name, so any binding's children slot works regardless of
/// what it is named.
/// </para>
/// </summary>
public sealed record IrMemberInit(
    string MemberName,
    string? EmittedName,
    IrExpression Value,
    bool IsChildrenSlot = false
);

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

    /// <summary>
    /// C#'s built-in <c>/</c> over integral operands (<c>int</c>, <c>long</c>,
    /// <c>uint</c>, <c>short</c>, <c>byte</c>, <c>char</c>, …, and their lifted
    /// nullable forms): the quotient truncated toward zero, throwing on a zero
    /// divisor. <see cref="Divide"/> keeps the floating-point and decimal forms.
    /// A compound <c>/=</c> over integral operands lowers to an assignment of
    /// this operator, so there is no matching compound member.
    /// </summary>
    IntegerDivide,
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
    IrTypeRef? ThisType = null,
    string? Name = null
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
/// <param name="MemberName">The C# member name.</param>
/// <param name="Value">The new value.</param>
/// <param name="EmittedName">Target-resolved <c>[Name]</c> override of the
/// member, or <c>null</c> to apply the backend's naming policy.</param>
public sealed record IrWithAssignment(
    string MemberName,
    IrExpression Value,
    string? EmittedName = null
);

// -- Throw --

/// <summary>
/// Throw expression (C# 7+): <c>throw new Exception()</c> as an expression.
/// </summary>
public sealed record IrThrowExpression(IrExpression Expression) : IrExpression;

// -- Let binding --

/// <summary>
/// Evaluates <see cref="Value"/> once, binds it to <see cref="Name"/>, and
/// returns <see cref="Body"/> with that binding in scope. Used to hoist a
/// shared receiver so a rewrite that needs to reference it more than once
/// (extension property compound assignment, increment/decrement,
/// null-conditional writes) can do so without re-evaluating the source
/// expression. Backends lower the node according to their idioms — the
/// TypeScript bridge emits an IIFE
/// (<c>((Name) =&gt; Body)(Value)</c>) so the binding scopes only the
/// body expression and the surrounding statement remains an expression.
/// </summary>
public sealed record IrLetExpression(string Name, IrExpression Value, IrExpression Body)
    : IrExpression;

/// <summary>
/// Evaluates every expression of <see cref="Effects"/> in order for its side
/// effects, then evaluates and yields <see cref="Result"/> — the comma
/// operator of C-family languages, as an expression. An object or collection
/// initializer lowers to one: <c>new T(args) { X = x, Y = { Z = z } }</c>
/// binds the new instance with an <see cref="IrLetExpression"/> whose body is
/// the sequence of the initializer's member assignments and <c>Add</c> calls
/// against that binding, ending in the binding itself. Backends without a
/// sequencing operator lower it to a block-bodied immediately invoked
/// function.
/// </summary>
public sealed record IrSequenceExpression(IReadOnlyList<IrExpression> Effects, IrExpression Result)
    : IrExpression;

// -- Calls with out arguments --

/// <summary>
/// A call that passes one or more arguments as C# <c>out</c>. Neither
/// TypeScript nor Dart has by-reference arguments, so the call is modelled as
/// a multi-value result: <see cref="Call"/> is the invocation without its
/// <c>out</c> arguments, and evaluating it yields the ordered values
/// <c>(result, out1, out2, …)</c> — the callee's return value (omitted when
/// <see cref="ResultType"/> is <c>null</c>, a <c>void</c> callee) followed by
/// one value per <see cref="OutArguments"/> entry, in the callee's parameter
/// order. Each backend lowers the callee to return that shape and the call
/// site to unpack it.
/// <para>
/// In statement position the extractor binds the values with an
/// <see cref="IrTupleDeconstruction"/> whose initializer is this node (see
/// <c>OutArgumentCallLowering</c>); anywhere else (a short-circuit operand, a
/// conditional branch, a loop condition the extractor could not restructure)
/// the node stays in place, its value is the callee's result, and the
/// <c>out</c> values are stored into each target as a side effect.
/// </para>
/// </summary>
/// <param name="Call">The invocation without its <c>out</c> arguments; its
/// origin still names the C# method, so declarative mappings match it.</param>
/// <param name="OutArguments">The <c>out</c> arguments, in the callee's
/// parameter order.</param>
/// <param name="ResultType">The callee's return type, or <c>null</c> when it
/// returns <c>void</c>.</param>
/// <param name="IsExternalCallee">Whether the callee is neither in this
/// compilation nor in a transpiled assembly (a BCL or third-party method): no
/// backend lowers its declaration, so only a declarative mapping can give the
/// call its multi-value result.</param>
public sealed record IrOutArgumentCall(
    IrCallExpression Call,
    IReadOnlyList<IrOutArgument> OutArguments,
    IrTypeRef? ResultType,
    bool IsExternalCallee = false
) : IrExpression
{
    /// <summary>
    /// The call with <paramref name="rewrite"/> applied to its invocation and
    /// to each existing <c>out</c> destination, for IR rewriters. The
    /// invocation is kept as is when the rewrite turns it into anything other
    /// than a call.
    /// </summary>
    public IrOutArgumentCall MapExpressions(Func<IrExpression, IrExpression> rewrite) =>
        this with
        {
            Call = rewrite(Call) as IrCallExpression ?? Call,
            OutArguments = OutArguments
                .Select(o =>
                    o.Target is IrOutAssignment assignment
                        ? o with
                        {
                            Target = new IrOutAssignment(rewrite(assignment.Destination)),
                        }
                        : o
                )
                .ToList(),
        };
}

/// <summary>
/// One <c>out</c> argument of an <see cref="IrOutArgumentCall"/>.
/// </summary>
/// <param name="ParameterOrdinal">The position of the bound parameter in the
/// callee's parameter list as written at the call site (the receiver of a
/// reduced extension call excluded), so a backend that keeps the argument
/// slot — a declarative mapping of a BCL method — can put
/// <see cref="DefaultValue"/> back in place.</param>
/// <param name="ParameterName">The bound parameter's name.</param>
/// <param name="Target">Where the callee's <c>out</c> value goes.</param>
/// <param name="DefaultValue">C#'s <c>default(T)</c> for the parameter type
/// (<see cref="IrLiteralKind.Default"/> when it has no literal).</param>
public sealed record IrOutArgument(
    int ParameterOrdinal,
    string ParameterName,
    IrOutTarget Target,
    IrExpression DefaultValue
);

/// <summary>Where an <c>out</c> argument stores the callee's value.</summary>
public abstract record IrOutTarget;

/// <summary>
/// <c>out var name</c> / <c>out T name</c>: declares a local. When the call
/// stays in expression position, the extractor declares the local (uninitialized,
/// typed <see cref="Type"/>) ahead of the enclosing statement and the call
/// assigns it.
/// </summary>
/// <param name="Type">The local's type: the explicit <c>T</c>, or for
/// <c>var</c> the bound parameter's type, widened to its nullable form when the
/// parameter carries <c>[MaybeNullWhen]</c>.</param>
public sealed record IrOutLocalDeclaration(string Name, IrTypeRef Type) : IrOutTarget;

/// <summary><c>out x</c>, <c>out this.field</c>: an existing variable or member.</summary>
public sealed record IrOutAssignment(IrExpression Destination) : IrOutTarget;

/// <summary><c>out _</c> / <c>out var _</c>: the value is dropped.</summary>
public sealed record IrOutDiscard : IrOutTarget;

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
/// <param name="TypeArguments">Generic type arguments at the call site —
/// substituted into <c>$T0</c>/<c>$T1</c> placeholders when the template
/// expands. Carries the IR type ref so the backend can resolve the emitted
/// name (and the import collector can pull in the corresponding type).</param>
/// <param name="ExternalImports">External JS modules the template body
/// references textually (e.g., a literal <c>createElement</c> in the
/// template paired with an <c>[Import]</c> on the source method). The
/// walker can't see these inside the opaque template text, so the extractor
/// threads them through as a separate field.</param>
public sealed record IrTemplateExpression(
    string Template,
    IrExpression? Receiver,
    IReadOnlyList<IrExpression> Arguments,
    IReadOnlyList<string>? RequiredImports = null,
    IReadOnlyList<IrTypeRef>? TypeArguments = null,
    IReadOnlyList<IrExternalImport>? ExternalImports = null
) : IrExpression;
