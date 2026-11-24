namespace Metano.Compiler.IR;

/// <summary>
/// A semantic expression in the IR. Represents what the source code *means*,
/// not how any target language renders it.
/// </summary>
public abstract record IrExpression
{
    /// <summary>
    /// The static source call retained when a lowering erases its callee (an
    /// inline body, template, mapping or synthesized equality operator).
    /// Targets with lazy type initialization consume it; other targets may ignore it.
    /// </summary>
    public IrBoundStaticCall? BoundStaticCall { get; init; }
}

/// <summary>
/// Source facts about a call to a type with an explicit static constructor.
/// <paramref name="MaterializedCall"/> retains argument evaluation for textual
/// inline substitution without changing that substitution on other targets.
/// A lifted value-type operator invokes no method when an operand is missing.
/// </summary>
public sealed record IrBoundStaticCall(
    IrTypeReference Owner,
    IrNamedTypeRef DeclaringType,
    bool IsContainerless,
    bool HasExtensionMembers,
    bool IsLifted = false,
    IrCallExpression? MaterializedCall = null,
    IReadOnlyList<int>? EvaluationOrder = null
);

// -- Literals --

/// <summary>
/// A literal value (string, number, boolean, null, etc.).
/// </summary>
public sealed record IrLiteral(object? Value, IrLiteralKind Kind) : IrExpression
{
    /// <summary>
    /// The <see cref="Value"/> of the <see cref="IrLiteralKind.BigInteger"/>
    /// literal that spells <c>default(BigInteger)</c>, for a target that
    /// prints that zero its own way.
    /// </summary>
    public const string BigIntegerZeroDigits = "0";

    /// <summary>
    /// The enum a constant <c>0</c> converts to implicitly (C#'s enumeration
    /// conversion: <c>E e = 0;</c>, <c>return 0;</c>, <c>Take(0)</c>), for a
    /// target whose enum type does not take the bare number; <c>null</c> for
    /// every other literal.
    /// </summary>
    public IrTypeRef? EnumConversionType { get; init; }
}

/// <summary>
/// Categories of literal values.
/// </summary>
public enum IrLiteralKind
{
    Null,
    Boolean,
    Int32,
    Int64,

    /// <summary>A C# <c>ulong</c> constant; <see cref="IrLiteral.Value"/> is a <see cref="ulong"/>.</summary>
    UInt64,
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
/// <para>
/// <see cref="PrivateNestedSegments"/> mirrors <see cref="IrNamedTypeRef.PrivateNestedSegments"/>:
/// how many leading segments of the dotted <see cref="Name"/> lead to a <c>private</c>
/// nested declaration (<c>0</c> when none is private).
/// </para>
/// </summary>
public sealed record IrTypeReference(
    string Name,
    IrTypeOrigin? Origin = null,
    int PrivateNestedSegments = 0
) : IrExpression;

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
/// <param name="TupleElement">The kind of positional element the member access reads: a <c>[JsTuple]</c> record's positional member, or a C# tuple element read by name or as <c>ItemN</c>. Each backend reads the element at <see cref="TupleIndex"/> its own way (TypeScript indexes the array, <c>recv[i]</c>; Dart reads a C# tuple element as the record field <c>recv.$N</c> and a <c>[JsTuple]</c> member by name).</param>
/// <param name="TupleIndex">The zero-based element position when <see cref="IsTupleElement"/> is true; <c>-1</c> otherwise.</param>
/// <param name="IsTranspiledDataMember">The member is a field or property (not an enum member or indexer) declared on a type whose declaration the toolchain emits — a type of the current compilation or of a transpiled package, neither <c>[External]</c> nor <c>[Import]</c>. A backend that renames data members at their declaration (TypeScript escapes reserved words: <c>Default</c> → <c>default_</c>) applies the same rename at the access; a foreign member keeps the name its real declaration has.</param>
/// <param name="IsBrandedValueMember">The member access reads the single wrapped value member of a <c>[Branded]</c> struct (<c>id.Value</c>). The branded value IS the primitive at runtime, so backends that lower the wrapper to a branded primitive collapse the read to the receiver.</param>
/// <param name="ParameterTypes">For a method or constructor, the stable full names of the bound overload's parameter types (open definitions), so a declarative mapping can tell same-arity overloads apart; <c>null</c> for other members.</param>
/// <param name="IsInitOnly">The member is a property whose setter is an <c>init</c> accessor. C# writes such a property only from the declaring type's constructors and accessors (through <c>this</c> / <c>base</c>) and from object initializers, so a write through any other receiver is an object-initializer write; a backend that declares init-only properties read-only (TypeScript <c>readonly</c>) lowers that write to a form its type checker accepts.</param>
/// <param name="CompanionOwner">For a <c>[PlainObject]</c> instance method (<see cref="IsPlainObjectInstanceMethod"/>), the declaring type as a value reference, with its cross-package origin: the method lowers to a standalone helper exported from that type's module, so a backend can import the helper from there. <c>null</c> for other members.</param>
/// <param name="IsInitializerWrite">The access is the target of a member initializer a backend moved into the constructor body (<c>int P { get; set; } = x;</c>). C# runs it as a store to the member's own backing storage, never through a (possibly overridden) accessor, so a backend that keeps storage apart from an accessor writes the storage.</param>
/// <param name="IsForeignMember">The member comes from a referenced .NET assembly (the BCL or a third-party library) that the toolchain neither transpiles nor binds — no <c>[External]</c>, <c>[Import]</c>, <c>[Emit]</c> or <c>[Name]</c> binding on the member, no <c>[External]</c>, <c>[Import]</c>, <c>[NoContainer]</c> or <c>[ExportFromBcl]</c> binding for its type: the target has no declaration of it, so a backend can lower it only through a declarative mapping.</param>
/// <param name="IsDeclaringTypeExtensionContainer">The member is a static method of a static class that declares extension members (classic or in a C# 14 block) and is not containerless. A backend that emits such a class as a module of functions (TypeScript) flattens the call like a <c>[NoContainer]</c> one; a backend that keeps the class (Dart) ignores the flag.</param>
/// <param name="IsCompileTimeConstant">The member is a field declared with the C# <c>const</c> modifier — its value is a compile-time constant (see <see cref="IrFieldDeclaration.IsCompileTimeConstant"/>). A backend that keeps a module-scope constant's source name (#390) reads this flag instead of re-reading Roslyn; distinct from locals' <c>IrVariableDeclaration.IsConst</c>, which only means "never reassigned".</param>
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
    IrTupleElementKind TupleElement = IrTupleElementKind.None,
    int TupleIndex = -1,
    bool IsBrandedValueMember = false,
    bool IsTranspiledDataMember = false,
    IReadOnlyList<string>? ParameterTypes = null,
    bool IsInitOnly = false,
    IrTypeReference? CompanionOwner = null,
    bool IsForeignMember = false,
    bool IsInitializerWrite = false,
    bool IsCompileTimeConstant = false,
    bool IsDeclaringTypeExtensionContainer = false
)
{
    /// <summary>The bound member's result type, including inferred generic arguments.</summary>
    public IrTypeRef? ResultType { get; init; }

    /// <summary>The access reads a positional element (<see cref="TupleElement"/> is set).</summary>
    public bool IsTupleElement => TupleElement is not IrTupleElementKind.None;

    /// <summary>
    /// The access reads a module variable the toolchain declares: a static
    /// field or property of a containerless static class. That read lowers to
    /// a bare identifier (or an import of one), so it names what the module
    /// declared (see #390); a member of a foreign or non-containerless
    /// declaring type is an ordinary member read.
    /// </summary>
    public bool IsModuleVariable =>
        IsStatic && IsDeclaringTypeNoContainer && IsTranspiledDataMember;

    /// <summary>
    /// The access reads a module variable that is a compile-time constant: a
    /// C# <c>const</c> field of a containerless static class whose declaration
    /// the toolchain emits. That is the one read the module-constant naming
    /// rule (kept C# name, see #390) applies to.
    /// </summary>
    public bool IsModuleConstant => IsModuleVariable && IsCompileTimeConstant;
}

/// <summary>
/// Which kind of positional element a member access reads
/// (<see cref="IrMemberOrigin.TupleElement"/>). Both read the element at
/// <see cref="IrMemberOrigin.TupleIndex"/>, but a backend may model the two
/// differently (Dart keeps a <c>[JsTuple]</c> record's member as a named field
/// and reads a C# tuple element as the record field <c>$N</c>).
/// </summary>
public enum IrTupleElementKind
{
    /// <summary>Not a positional element.</summary>
    None,

    /// <summary>A positional member of a <c>[JsTuple]</c> record.</summary>
    JsTuple,

    /// <summary>A C# value-tuple element, read by name or as <c>ItemN</c>.</summary>
    ValueTuple,
}

/// <summary>
/// Member access: <c>target.memberName</c>. <see cref="Origin"/> is null for
/// synthetic or unresolved nodes.
/// </summary>
public sealed record IrMemberAccess(
    IrExpression Target,
    string MemberName,
    IrMemberOrigin? Origin = null
) : IrExpression
{
    /// <summary>
    /// The static type of <see cref="Target"/> when resolved by the extractor,
    /// so targets that bind the receiver to a closure parameter can type it.
    /// </summary>
    public IrTypeRef? TargetType { get; init; }

    /// <summary>
    /// The dispatched overload a method-group reference resolved to (spec 013
    /// FR-018): a target renames the access to the overload's claimed fast
    /// path, keeping the receiver binding (<c>b.addX.bind(b)</c>). The
    /// access inside a call target carries it the same way, so the call and
    /// its method group spell one name. <c>null</c> when the access is not a
    /// dispatched group member.
    /// </summary>
    public IrSelectedOverload? Overload { get; init; }

    /// <summary>
    /// Reading the member runs no user code: it is a field or a
    /// non-overridable auto-property of this compilation, so reading it again
    /// gives the same value while nothing writes it. A hand-written,
    /// <c>field</c>-keyword or overridable getter, or a property of another
    /// assembly, may compute, so its read is not a storage read.
    /// </summary>
    public bool IsStorageRead { get; init; }
}

/// <summary>
/// Element/index access: <c>target[index]</c>. <see cref="IndexKind"/> is the
/// index's kind when an array is indexed with a 64-bit integer, which a
/// target that indexes with another type converts (spec 012 FR-036).
/// </summary>
public sealed record IrElementAccess(
    IrExpression Target,
    IrExpression Index,
    IrTypeRef? TargetType = null,
    IrNumericKind? IndexKind = null
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
) : IrExpression
{
    /// <summary>
    /// Whether <see cref="Start"/> is the <c>n</c> of a from-end bound
    /// (<c>^n..</c>) the target resolves against the receiver's length, for a
    /// target whose collections are runtime classes (spec 011 data-model § 2,
    /// B4); <c>false</c> once the extractor resolved it.
    /// </summary>
    public bool StartFromEnd { get; init; }

    /// <summary>Whether <see cref="End"/> is the <c>n</c> of a from-end bound (<c>..^n</c>).</summary>
    public bool EndFromEnd { get; init; }

    /// <summary>
    /// The source of the range access, for a diagnostic the target reports
    /// while it lowers it; set only for a target whose collections are runtime
    /// classes.
    /// </summary>
    public IrSourceLocation? Location { get; init; }
}

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

// -- Dynamic (spec 011 FR-030) --

/// <summary>
/// A member read off a <c>dynamic</c> receiver (<c>d.Foo</c>): the runtime
/// binder resolves it, so no symbol exists and <see cref="MemberName"/> is
/// the source spelling, emitted verbatim (no naming policy, no
/// <c>[Name]</c>; OD-11).
/// </summary>
public sealed record IrDynamicMemberAccess(IrExpression Receiver, string MemberName) : IrExpression;

/// <summary>
/// A call bound at run time whose target is <c>dynamic</c>: a method of a
/// <c>dynamic</c> receiver (<c>d.Foo(1)</c>, <see cref="Target"/> an
/// <see cref="IrDynamicMemberAccess"/>) or a <c>dynamic</c> value invoked as a
/// function (<c>d(1)</c>). The arguments are positional; the written type
/// arguments of a generic call are not carried.
/// </summary>
public sealed record IrDynamicInvocation(IrExpression Target, IReadOnlyList<IrExpression> Arguments)
    : IrExpression;

/// <summary>
/// An indexer read or write on a <c>dynamic</c> receiver (<c>d[i]</c>), with
/// its single positional index.
/// </summary>
public sealed record IrDynamicIndexerAccess(IrExpression Receiver, IrExpression Index)
    : IrExpression;

/// <summary>
/// An explicit numeric cast of a <c>dynamic</c> value (<c>(int)d</c>,
/// <c>(long?)d</c>): the runtime binder converts from the value's runtime
/// type, which the compiler does not know, so a target checks the value at
/// run time and converts it as the cast from that runtime type would.
/// </summary>
/// <param name="Operand">The <c>dynamic</c> value.</param>
/// <param name="Target">The numeric type cast to (<c>char</c> included).</param>
/// <param name="IsChecked">The cast sits in a checked context.</param>
/// <param name="IsLifted">The cast is to <c>T?</c>, which lets <c>null</c> through.</param>
public sealed record IrDynamicNumericConversion(
    IrExpression Operand,
    IrNumericKind Target,
    bool IsChecked,
    bool IsLifted
) : IrExpression;

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
) : IrExpression
{
    /// <summary>
    /// The overload a <c>base.M(...)</c> call into a dispatched group resolved
    /// to (spec 013 FR-018): <see cref="Arguments"/> are then Roslyn's bound
    /// arguments, and a target calls the base overload's fast path, since the
    /// base dispatcher would dispatch virtually again. <c>null</c> for every
    /// other call.
    /// </summary>
    public IrSelectedOverload? Overload { get; init; }

    /// <summary>
    /// The order the source evaluates <see cref="Arguments"/> in, as indices
    /// into them, on a call bound to no overload whose named arguments are
    /// out of parameter order with two or more order-sensitive ones (spec 013
    /// FR-017): a target must evaluate them in this order before it passes
    /// them. <c>null</c> when parameter order is evaluation order, and on a
    /// call bound to an <see cref="Overload"/>, which carries its own.
    /// </summary>
    public IReadOnlyList<int>? EvaluationOrder { get; init; }

    /// <summary>
    /// Whether the call invokes a delegate value: <see cref="Target"/> is
    /// then read whole before the arguments — a member access in it is a
    /// property or field read, not a method whose receiver the call keeps.
    /// Unset on the <c>.call(receiver, …)</c> rewrite of a <c>[This]</c>
    /// delegate, whose target is the delegate's <c>call</c> member.
    /// </summary>
    public bool InvokesDelegate { get; init; }

    /// <summary>
    /// Whether the source writes the type arguments (<c>Guard&lt;Shape&gt;(a, b)</c>)
    /// rather than leaving them to inference. A target whose own inference can
    /// differ from C#'s (TypeScript infers from the first operand where C#
    /// unifies them all) passes the written ones through.
    /// </summary>
    public bool HasExplicitTypeArguments { get; init; }

    /// <summary>
    /// Runtime type descriptors passed as trailing reified type arguments,
    /// parallel to the callee's <c>ReifiedTypeParameters</c> (spec 011 FR-024).
    /// For statics of generic classes, includes the containing type's arguments.
    /// </summary>
    public IReadOnlyList<IrTypeRef>? ReifiedTypeArguments { get; init; }

    /// <summary>
    /// The 0-based argument index where <see cref="ReifiedTypeArguments"/> are
    /// inserted (after all non-<c>params</c> declared parameters, before any
    /// <c>params</c> rest arguments), padding omitted optional arguments with
    /// <c>undefined</c>.
    /// </summary>
    public int ReifiedArgumentPosition { get; init; }

    /// <summary>
    /// The 0-based parameter index of the trailing <c>params</c> array when
    /// the call passes expanded elements (or omits them) rather than a single
    /// array expression; <c>null</c> for normal-form or non-<c>params</c> calls.
    /// </summary>
    public int? ExpandedParamsIndex { get; init; }
}

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
/// <para>
/// <see cref="IsOmittedDefault"/> tags a parameter's default the call does not
/// write, filled only to reach a later argument (a
/// <c>[CallerArgumentExpression]</c> text): a target whose callee applies its
/// own default for a missing argument (TypeScript's <c>undefined</c>) may pass
/// that instead of <see cref="Value"/>.
/// </para>
/// </summary>
public sealed record IrArgument(
    IrExpression Value,
    string? Name = null,
    bool IsSpread = false,
    bool IsOmittedDefault = false,
    IrTypeRef? Type = null
);

/// <summary>
/// Which <c>System.Object</c> member a <see cref="IrBaseObjectMemberCall"/>
/// lowers to the default semantics of.
/// </summary>
public enum IrObjectMember
{
    /// <summary><c>object.Equals(object?)</c>: reference equality.</summary>
    Equals,

    /// <summary><c>object.GetHashCode()</c>: the identity hash.</summary>
    GetHashCode,

    /// <summary><c>object.ToString()</c>: the runtime type name (<see cref="IrBaseObjectMemberCall.TypeName"/>).</summary>
    ToString,
}

/// <summary>
/// A <c>base.M(...)</c> call whose member no emitted base class declares
/// (#506): bound to <c>System.Object</c>'s member, or to a member of a base
/// the toolchain does not emit — so the emitted class has no <c>extends</c>
/// and the target's <c>super</c> has no such member. <see cref="Member"/>
/// carries <c>object</c>'s default semantics and every backend lowers it to
/// its own spelling: TypeScript compares references (<c>this === other</c>),
/// hashes the identity default (<c>0</c>, the constant the runtime's
/// <c>valueHashCode(null)</c> answers) and prints <see cref="TypeName"/>;
/// Dart reads its own <c>Object</c> contract (<c>identical(this, other)</c>,
/// <c>super.hashCode</c>) and prints <see cref="TypeName"/> for ToString.
/// For a non-emitted base the base's own override is unreachable — lowering
/// the <c>object</c> default is a documented divergence from .NET, the only
/// semantics every target can honor.
/// </summary>
/// <param name="TypeName">
/// The text .NET's <c>base.ToString()</c> prints: the enclosing type's
/// namespace-qualified metadata name (<see cref="Analysis.ClrTypeName"/>),
/// resolved at extraction. Deterministic spelling; a generic class prints
/// its open form (<c>Ns.Box`1[T]</c>) where the runtime would print the
/// closed construction.
/// </param>
public sealed record IrBaseObjectMemberCall(
    IrObjectMember Member,
    string TypeName,
    IReadOnlyList<IrArgument> Arguments
) : IrExpression;

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
/// <para>
/// <paramref name="Overload"/> is set when the constructor Roslyn resolved is
/// an overload of a dispatched set whose type constructs through factories,
/// whose IR is available, for a target that emits factories (spec 013
/// FR-017, DECIDED-11, DECIDED-24): the backend then builds the value through
/// that overload's factory (<c>Money.fromAmount(…)</c>) instead of its
/// runtime dispatcher. <b>It changes what <paramref name="Arguments"/>
/// holds</b>: Roslyn's bound arguments, unnamed and in parameter order —
/// omitted defaults explicit, an expanded <c>params</c> call as its elements,
/// a normal-form one as a spread — evaluated in
/// <see cref="IrSelectedOverload.EvaluationOrder"/> when that is set. Without
/// it, <paramref name="Arguments"/> are the source arguments. Null for every
/// other construction, and when an argument has no bound form the extractor
/// can lower.
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
    IReadOnlyList<IrTypeRef>? TypeArguments = null,
    IrSelectedOverload? Overload = null
) : IrExpression
{
    /// <summary>
    /// The order the source evaluates <see cref="Arguments"/> in, on a
    /// construction bound to no overload: see
    /// <see cref="IrCallExpression.EvaluationOrder"/>.
    /// </summary>
    public IReadOnlyList<int>? EvaluationOrder { get; init; }

    /// <summary>
    /// Runtime type descriptors passed to the constructor's trailing
    /// <c>$typeArgs</c> tuple (spec 011 FR-024).
    /// </summary>
    public IReadOnlyList<IrTypeRef>? ReifiedTypeArguments { get; init; }

    /// <summary>
    /// The 0-based argument index where the <c>$typeArgs</c> tuple is
    /// inserted (before any <c>params</c> rest parameter).
    /// </summary>
    public int ReifiedArgumentPosition { get; init; }

    /// <summary>
    /// True when this node represents a C# <c>default(S)</c> for a struct
    /// rather than an explicit <c>new S()</c> constructor invocation.
    /// </summary>
    public bool IsStructDefault { get; init; }

    /// <summary>
    /// The 0-based parameter index of the trailing <c>params</c> array when
    /// the construction passes expanded elements (or omits them); <c>null</c>
    /// otherwise.
    /// </summary>
    public int? ExpandedParamsIndex { get; init; }
}

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
/// <param name="NumericKind">
/// The operand kind of a built-in numeric operator (spec 012 FR-027,
/// FR-028), so a target picks the division helper or converts a shift count
/// without re-deriving types: the promoted left operand's kind (a shift's
/// count stays <c>int</c>). A compound shift the target's own operator
/// computes as C# does carries it too (<c>i &lt;&lt;= n</c> on an
/// <c>int</c>, <c>u &gt;&gt;= n</c> on a <c>uint</c>, spec 012 FR-076), so
/// the target picks the operator; that node is built for the TypeScript
/// target only. <c>null</c> for any other operator, including enum and
/// user-defined ones, and for every other compound assignment.
/// </param>
public sealed record IrBinaryExpression(
    IrExpression Left,
    IrBinaryOp Operator,
    IrExpression Right,
    IrNumericKind? NumericKind = null
) : IrExpression
{
    /// <summary>
    /// The enum type of the result of a built-in enum operator that computes a
    /// new value (<c>e + 1</c>, <c>a | b</c>, <c>e - 1</c>, the compound
    /// <c>e += 1</c> / <c>e |= f</c>, and the non-null case of their lifted
    /// <c>E?</c> forms): C# computes it over the underlying integer and types
    /// it as the enum. <c>null</c> for every other operator, including
    /// comparisons and simple assignments.
    /// </summary>
    public IrTypeRef? EnumResultType { get; init; }

    /// <summary>
    /// The source of an assignment that stores through an element access, for
    /// a diagnostic the target reports while it lowers it; set only on such a
    /// store, and only for a target whose collections are runtime classes.
    /// </summary>
    public IrSourceLocation? Location { get; init; }
}

/// <summary>
/// Unary expression: <c>op operand</c> or <c>operand op</c>.
/// </summary>
public sealed record IrUnaryExpression(
    IrUnaryOp Operator,
    IrExpression Operand,
    bool IsPrefix = true
) : IrExpression
{
    /// <summary>
    /// The enum type of the result of <c>~e</c>, <c>e++</c> or <c>--e</c> on an
    /// enum, or of the non-null case of their lifted forms (see
    /// <see cref="IrBinaryExpression.EnumResultType"/>); <c>null</c> for every
    /// other operator.
    /// </summary>
    public IrTypeRef? EnumResultType { get; init; }
}

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

    /// <summary>
    /// C#'s value equality <c>==</c> over a record (class or struct): true when
    /// both operands are null, or when the left operand's <c>Equals</c> accepts
    /// the right one. <see cref="Equal"/> keeps reference and primitive
    /// equality, and the user-defined operators of other types.
    /// </summary>
    ValueEqual,

    /// <summary>
    /// The negation of <see cref="ValueEqual"/>: C#'s <c>!=</c> over a record.
    /// </summary>
    ValueNotEqual,

    /// <summary>
    /// C#'s virtual <c>left.Equals(right)</c> (<c>object.Equals(object)</c>) on a
    /// receiver whose static type is <c>object</c>, <c>ValueType</c> or
    /// <c>Enum</c>, so the override that answers is the runtime value's: a user
    /// <c>Equals</c>, a boxed value's equality (spec 011 FR-051), or reference
    /// identity.
    /// </summary>
    DefaultEqual,

    /// <summary>
    /// C#'s non-short-circuit <c>&amp;</c> over <c>bool</c> operands: both
    /// operands are evaluated, left to right, then conjoined. A <c>bool?</c>
    /// operand never reaches it, since the extractor lowers the three-valued
    /// form first. <see cref="BitwiseAnd"/> keeps the integral and enum forms.
    /// </summary>
    BooleanAnd,

    /// <summary>
    /// C#'s non-short-circuit <c>|</c> over <c>bool</c> operands; see
    /// <see cref="BooleanAnd"/>.
    /// </summary>
    BooleanOr,

    /// <summary>
    /// C#'s <c>^</c> over <c>bool</c> operands: true when exactly one operand
    /// is; see <see cref="BooleanAnd"/>.
    /// </summary>
    BooleanXor,
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

    /// <summary>
    /// C#'s null-forgiving <c>x!</c> over a reference or type-parameter
    /// value, always postfix: C# attaches no runtime check, but a target that
    /// checks nullability statically needs it to read the value as non-null.
    /// A target whose own assertion checks at runtime (Dart's <c>x!</c>) may
    /// throw where C# lets the <c>null</c> through.
    /// </summary>
    NonNullAssertion,
}

// -- Conversions and checks --

/// <summary>
/// Type cast: <c>(T)expr</c>.
/// </summary>
/// <param name="IsAssertion">The compiler synthesized the cast to give the
/// value its static type (the <c>0</c> of an enum that declares no zero
/// member), so a target that erases casts still asserts it
/// (<c>0 as Level</c>).</param>
/// <param name="ReferenceConversion">The facts of an explicit reference or
/// unboxing conversion the source wrote (<c>(Derived)baseValue</c>,
/// <c>(IShape)obj</c>, <c>(int)obj</c>):
/// C# checks the value at runtime and throws <c>InvalidCastException</c> for
/// another type. <c>null</c> for every other cast. A target that erases static
/// types decides from them whether to type the result, and whether to check
/// it.</param>
public sealed record IrCastExpression(
    IrExpression Expression,
    IrTypeRef TargetType,
    bool IsAssertion = false,
    IrReferenceConversion? ReferenceConversion = null
) : IrExpression;

/// <summary>
/// Where an explicit reference conversion sits, as seen from the source.
/// Target-neutral facts: what a target makes of them (a typed result, a
/// runtime check, an assertion) is its own decision.
/// </summary>
/// <param name="IsNarrowedByTypeTest">An earlier type test of the same operand
/// for the same type guards the cast (<c>o is Money &amp;&amp; ((Money)o).Cents == 0</c>).</param>
/// <param name="TargetDerivesFromEnclosingType">The target type extends a type
/// that encloses the cast (a method of <c>Shape</c> casting to its subclass
/// <c>Circle</c>).</param>
/// <param name="InInlineExpansion">The cast belongs to an <c>[Inline]</c>
/// expansion, so it lands in a caller whose enclosing type and type
/// parameters are not the ones the extraction sees.</param>
/// <param name="TargetMentionsTypeParameter">The target type is, or is built
/// from, a type parameter (<c>(T)o</c>, <c>(List&lt;T&gt;)o</c>).</param>
public sealed record IrReferenceConversion(
    bool IsNarrowedByTypeTest,
    bool TargetDerivesFromEnclosingType,
    bool InInlineExpansion,
    bool TargetMentionsTypeParameter
)
{
    /// <summary>
    /// The conversion unboxes (<c>(int)obj</c>, <c>(Color)obj</c>,
    /// <c>(Point)obj</c> for a struct) instead of converting a reference: .NET
    /// checks it the same way, with the same <c>InvalidCastException</c>.
    /// </summary>
    public bool IsUnboxing { get; init; }
}

/// <summary>
/// Type check: <c>expr is T</c>.
/// </summary>
public sealed record IrTypeCheck(IrExpression Expression, IrTypeRef Type) : IrExpression;

/// <summary>
/// C# <c>receiver.GetType()</c> (<c>object.GetType</c>): the runtime type of
/// the value. <paramref name="ConstantType"/> is the type the receiver's static
/// type decides without evaluating anything (a non-nullable primitive, enum or
/// struct receiver that has no side effects); <c>null</c> when the answer is
/// read from the value at runtime. A sealed class is not folded.
/// </summary>
/// <param name="Receiver">The value whose type is read.</param>
/// <param name="ConstantType">The folded answer, or <c>null</c>.</param>
public sealed record IrGetTypeExpression(IrExpression Receiver, IrTypeRef? ConstantType)
    : IrExpression
{
    /// <summary>
    /// The receiver's static type, so a target that can answer more receivers
    /// without reading the value (a <c>[PlainObject]</c> with no derived plain
    /// object, spec 011 FR-020) folds them itself; <c>null</c> when unknown.
    /// </summary>
    public IrTypeRef? ReceiverType { get; init; }
}

/// <summary>C# <c>typeof(T)</c> for a closed, nameable <paramref name="Type"/>.</summary>
/// <param name="Type">The type whose runtime type the expression denotes.</param>
public sealed record IrTypeOfExpression(IrTypeRef Type) : IrExpression;

/// <summary>C# <c>default(T)</c> for a type parameter <paramref name="Type"/> (spec 011 FR-024).</summary>
/// <param name="Type">The type parameter, carrying its <see cref="IrTypeParameterScope"/>.</param>
public sealed record IrReifiedDefaultExpression(IrTypeParameterRef Type) : IrExpression;

// -- Conditional --

/// <summary>
/// Ternary conditional: <c>condition ? whenTrue : whenFalse</c>.
/// </summary>
public sealed record IrConditionalExpression(
    IrExpression Condition,
    IrExpression WhenTrue,
    IrExpression WhenFalse
) : IrExpression
{
    /// <summary>
    /// The target type of the C# <c>x as T</c> this conditional defines
    /// (<c>x is T ? (T)x : null</c>), so a target with a one-call form of
    /// <c>as</c> can render it from the condition's operand; <c>null</c> for
    /// every other conditional.
    /// </summary>
    public IrTypeRef? AsTarget { get; init; }
}

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
/// <para>
/// <see cref="ReturnType"/> is resolved only as <c>void</c>, for a lambda
/// bound to a non-async <c>void</c> delegate (its expression body's value is
/// discarded); <c>null</c> means not resolved, not "returns a value".
/// </para>
/// </summary>
public sealed record IrLambdaExpression(
    IReadOnlyList<IrParameter> Parameters,
    IrTypeRef? ReturnType,
    IReadOnlyList<IrStatement> Body,
    bool IsAsync = false,
    bool UsesThis = false,
    IrTypeRef? ThisType = null,
    string? Name = null
) : IrExpression
{
    /// <summary>
    /// Type parameters of this lambda that are reified at runtime via hidden
    /// <c>T$type</c> parameters (spec 011 FR-024).
    /// </summary>
    public IReadOnlyList<IrReifiedTypeParameter> ReifiedTypeParameters { get; init; } = [];
}

// -- Stage 011c collection accesses (spec 011 data-model § 2) --

/// <summary>What an <see cref="IrIndexerUpdateExpression"/> yields.</summary>
public enum IrIndexerUpdateResult
{
    /// <summary>The value written: <c>r[i] op= v</c>, <c>++r[i]</c>, <c>--r[i]</c>.</summary>
    NewValue,

    /// <summary>The element before the write: <c>r[i]++</c>, <c>r[i]--</c>.</summary>
    OldValue,

    /// <summary>
    /// <c>r[i] ??= v</c>: <see cref="IrIndexerUpdateExpression.NewValue"/> is
    /// the right-hand side, evaluated and written only when the current
    /// element is <c>null</c>; the expression yields the element after it.
    /// </summary>
    Coalesce,
}

/// <summary>
/// One element of a stage 011c collection addressed through the collection's
/// own indexer: <see cref="Receiver"/> and <see cref="Index"/> in source
/// order, the index counted from the end (<c>r[^Index]</c>) when
/// <see cref="FromEnd"/> is set, and <see cref="ReceiverType"/> the collection
/// type the element is read and written as.
/// </summary>
public sealed record IrCollectionElement(
    IrExpression Receiver,
    IrExpression Index,
    bool FromEnd,
    IrCollectionTypeRef ReceiverType
);

/// <summary>
/// The evaluate-once update of one element through the indexer of a stage
/// 011c collection (spec 011 FR-010): <c>r[i] op= v</c>, <c>r[i]++</c> /
/// <c>--</c> (prefix or postfix) and <c>r[i] ??= v</c>, list or dictionary,
/// class or interface, on <see cref="Element"/> (<c>r[^n] op= v</c> with its
/// <see cref="IrCollectionElement.FromEnd"/>). <see cref="NewValue"/> is the
/// value written, an expression over <c>IrIdentifier(CurrentName)</c> (the
/// current element) built with the element type's operator, increment and
/// overflow rules; <see cref="CurrentName"/> is unique per site and contains a
/// <c>$</c>. Only a target whose collections are runtime classes gets the node
/// (<c>IrExtractionContext.KeepsNativeCollections</c>); the target chooses
/// the shape that renders it.
/// </summary>
public sealed record IrIndexerUpdateExpression(
    IrCollectionElement Element,
    string CurrentName,
    IrExpression NewValue,
    IrIndexerUpdateResult Result
) : IrExpression
{
    /// <summary>
    /// The source of the update, for a diagnostic the target reports while it
    /// lowers it; set only for a target whose collections are runtime classes.
    /// </summary>
    public IrSourceLocation? Location { get; init; }
}

/// <summary>
/// <c>r[^n]</c> on a stage 011c collection, an array or a <c>JsArray&lt;T&gt;</c>
/// (spec 011 data-model § 2, B4): a read, or the target of <c>r[^n] = v</c>.
/// <see cref="Offset"/> is <c>n</c>; <see cref="ReceiverType"/> is the
/// receiver's static type (an <see cref="IrCollectionTypeRef"/> or an
/// <see cref="IrArrayTypeRef"/>, never nullable). Only a target whose
/// collections are runtime classes gets the node.
/// </summary>
public sealed record IrFromEndElementAccess(
    IrExpression Receiver,
    IrExpression Offset,
    IrTypeRef ReceiverType
) : IrExpression
{
    /// <summary>
    /// The source of the access, for a diagnostic the target reports while it
    /// lowers it; set only for a target whose collections are runtime classes.
    /// </summary>
    public IrSourceLocation? Location { get; init; }
}

/// <summary>
/// A C# conversion, implicit or explicit, from <c>T[]</c> / <c>JsArray&lt;T&gt;</c>
/// to <c>IList&lt;T&gt;</c>, <c>ICollection&lt;T&gt;</c>, <c>IReadOnlyList&lt;T&gt;</c>
/// or <c>IReadOnlyCollection&lt;T&gt;</c> (spec 011 FR-003): the array viewed
/// as a fixed-size list. Only a target whose collections are runtime classes
/// gets the node.
/// </summary>
public sealed record IrArrayToCollectionConversion(IrExpression Value, IrCollectionTypeRef Target)
    : IrExpression
{
    /// <summary>
    /// The element type of the converted array, which can differ from the
    /// target's argument by array covariance (<c>IReadOnlyList&lt;object&gt; x =
    /// new string[] { "a" }</c>): the run-time identity of the value, which a
    /// type test reads back. <c>null</c> when unknown.
    /// </summary>
    public IrTypeRef? SourceElementType { get; init; }
}

/// <summary>
/// An explicit cast <c>(IList&lt;T&gt;)v</c>, <c>(ICollection&lt;T&gt;)v</c>,
/// <c>(IReadOnlyList&lt;T&gt;)v</c> or <c>(IReadOnlyCollection&lt;T&gt;)v</c>
/// whose operand's static type is neither an array nor a stage 011c collection
/// (<c>object</c>, <c>IEnumerable&lt;T&gt;</c>), so the value may be a native
/// array at run time, which C# views as a fixed-size list (spec 011 FR-003,
/// data-model § 2 B3). <see cref="Target"/> is the cast's list interface and
/// <see cref="TargetType"/> the cast's full target type (nullable when the cast
/// names <c>T?</c>). Only a target whose collections are runtime classes gets
/// the node.
/// </summary>
public sealed record IrListInterfaceCast(
    IrExpression Value,
    IrCollectionTypeRef Target,
    IrTypeRef TargetType
) : IrExpression
{
    /// <summary>
    /// The name .NET gives <see cref="Target"/> (<c>Type.ToString()</c>,
    /// <see cref="Analysis.ClrTypeName"/>), for the <c>InvalidCastException</c>
    /// a target throws when the value is never a list (a string, a number).
    /// </summary>
    public required string TargetClrTypeName { get; init; }
}

/// <summary>
/// The C# collection initializer forms of a stage 011c collection (spec 011
/// FR-007): <c>{ a, b }</c> / <c>{ { k, v } }</c> call <c>Add</c>,
/// <c>{ [k] = v }</c> writes through the indexer.
/// </summary>
public enum IrCollectionInitializerForm
{
    Add,
    Assign,
}

/// <summary>
/// The constructor a collection initializer follows, as far as the factories
/// of the targets tell them apart: the parameterless one, the one taking only
/// an equality comparer, or any other (capacity, copy, capacity + comparer).
/// </summary>
public enum IrCollectionConstruction
{
    Parameterless,
    ComparerOnly,
    Other,
}

/// <summary>
/// <c>new List&lt;T&gt; { a, b }</c>, <c>new Dictionary&lt;K, V&gt;(cmp) { [k] = v }</c>,
/// <c>new HashSet&lt;T&gt;(capacity) { a }</c>: the construction of a stage 011c
/// <c>List</c>, <c>HashSet</c> or <c>Dictionary</c> followed by its
/// initializer (spec 011 FR-007). <see cref="Creation"/> is the lowered
/// construction; <see cref="Elements"/> holds per initializer element its
/// values in source order (one for a list or set, the key and the value for a
/// dictionary); <see cref="Comparer"/> is the comparer argument of a
/// <see cref="IrCollectionConstruction.ComparerOnly"/> construction. Only a
/// target whose collections are runtime classes gets the node; the target
/// chooses the factory.
/// </summary>
public sealed record IrCollectionInitialization(
    IrExpression Creation,
    IrCollectionTypeRef Type,
    IrCollectionInitializerForm Form,
    IrCollectionConstruction Construction,
    IrExpression? Comparer,
    IReadOnlyList<IReadOnlyList<IrExpression>> Elements
) : IrExpression;

// -- Collection literals --

/// <summary>
/// Array/list literal: <c>[a, b, c]</c>. <see cref="ElementType"/> is the
/// element type C# gives the literal, set only where a target that infers a
/// literal's type from its elements would get a narrower one and no
/// declaration around the literal says it (<c>List&lt;int?&gt; l = [1]</c>,
/// <c>List&lt;Animal&gt; l = [dog]</c>, <c>List&lt;int&gt; l = []</c>).
/// </summary>
public sealed record IrArrayLiteral(
    IReadOnlyList<IrExpression> Elements,
    IrTypeRef? ElementType = null
) : IrExpression
{
    /// <summary>
    /// The collection a C# collection expression constructs from the literal
    /// (spec 011 data-model § 2, B1): <c>List</c> for a <c>List&lt;T&gt;</c>,
    /// <c>IList&lt;T&gt;</c> or <c>ICollection&lt;T&gt;</c> target, the
    /// read-only interface for an <c>IReadOnlyList&lt;T&gt;</c> /
    /// <c>IReadOnlyCollection&lt;T&gt;</c> target; <c>null</c> for an array,
    /// a span, <c>IEnumerable&lt;T&gt;</c> and every literal that is not a
    /// collection expression. A target whose lists are native arrays ignores it.
    /// </summary>
    public IrCollectionTypeRef? TargetCollection { get; init; }
}

/// <summary>
/// <c>new T[length]</c> without an initializer: <see cref="Length"/>
/// elements, each <see cref="ElementDefault"/>, C#'s <c>default(T)</c> (a
/// literal or an immutable value, so every slot may share it).
/// <see cref="LengthKind"/> is the length's kind when it is a 64-bit
/// integer, which a target that indexes with another type converts (spec
/// 012 FR-004, FR-036). <see cref="LengthIsNonNegativeConstant"/> says the
/// length is a compile-time constant of at least zero, so no target needs to
/// check it.
/// </summary>
public sealed record IrArrayCreation(
    IrTypeRef ElementType,
    IrExpression Length,
    IrExpression ElementDefault,
    IrNumericKind? LengthKind = null,
    bool LengthIsNonNegativeConstant = false
) : IrExpression;

/// <summary>
/// A C# tuple literal, <c>(a, b)</c> or <c>(x: a, y: b)</c>: its elements in
/// source order (evaluated left to right) and the tuple type it produces.
/// Element names exist only at compile time; reads by name or as
/// <c>ItemN</c> carry the element position
/// (<see cref="IrMemberOrigin.IsTupleElement"/>). Each backend picks the
/// runtime shape — TypeScript a positional array typed as its tuple type,
/// Dart a record literal. <see cref="Type"/> is <c>null</c> when the literal
/// has no tuple type (an element without a type, and no tuple to convert to).
/// </summary>
public sealed record IrTupleLiteral(IReadOnlyList<IrExpression> Elements, IrTupleTypeRef? Type)
    : IrExpression;

/// <summary>
/// C# value equality against a <c>ValueTuple</c> instantiation (element
/// names aside): the lowering of <c>object.Equals(a, b)</c>, a tuple's own
/// <c>Equals</c> — through <c>Nullable&lt;T&gt;</c>, a conditional access
/// (<c>t?.Equals(u)</c>) or named arguments — where one operand unwraps to
/// that tuple and the other is converted to it or may hold it (an
/// <c>object</c>, a type parameter, …). Either operand may be <c>null</c>:
/// <c>null</c> equals <c>null</c> and nothing else, otherwise the comparison
/// is element-wise. <see cref="TupleType"/> carries the instantiation, so a
/// backend whose tuples are positional arrays — where a nested tuple and a C#
/// array are indistinguishable — can compare the tuple elements by value.
/// Each operand is evaluated exactly once, <see cref="Left"/> first.
/// </summary>
public sealed record IrTupleEqualityExpression(
    IrExpression Left,
    IrExpression Right,
    IrTupleTypeRef TupleType
) : IrExpression;

/// <summary>
/// Object literal / anonymous object: <c>{ prop1 = a, prop2 = b }</c>.
/// </summary>
public sealed record IrObjectLiteral(IReadOnlyList<(string Name, IrExpression Value)> Properties)
    : IrExpression;

/// <summary>
/// Spread expression: <c>...expr</c>. <see cref="Source"/> is what the spread
/// operand enumerates as in C#, for targets whose map or string is not itself
/// iterable (Dart spreads a map's <c>entries</c> and a string's characters).
/// <see cref="ElementConversion"/> is the conversion C# applies to each
/// spread element on its way into the collection (<c>List&lt;long&gt; l =
/// [.. ints]</c>), when it crosses a 64-bit integer (spec 012 FR-010);
/// <c>null</c> when the elements are spread as they are.
/// </summary>
public sealed record IrSpreadExpression(
    IrExpression Expression,
    IrSpreadSource Source = IrSpreadSource.Iterable,
    IrSpreadElementConversion? ElementConversion = null
) : IrExpression;

/// <summary>
/// The per-element conversion of a spread: <see cref="Converted"/> is the
/// value each element becomes, reading the element as the identifier
/// <see cref="ElementName"/>. TypeScript maps the source through it; Dart, whose
/// <c>int</c> already holds every 64-bit integer, spreads the source as it is.
/// </summary>
public sealed record IrSpreadElementConversion(string ElementName, IrExpression Converted);

/// <summary>What a spread operand enumerates as.</summary>
public enum IrSpreadSource
{
    /// <summary>A sequence of its elements (an array, a list, a set, an <c>IEnumerable&lt;T&gt;</c>).</summary>
    Iterable,

    /// <summary>A dictionary: its key-value pairs.</summary>
    Map,

    /// <summary>A <c>string</c>: its characters.</summary>
    String,
}

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
/// An expression segment within an interpolated string:
/// <c>{Expression,Alignment:FormatSpecifier}</c>.
/// <see cref="NumericKind"/> is the numeric kind of the hole's value, looking
/// through <c>Nullable&lt;T&gt;</c> (not an enum's underlying kind), so a
/// target that formats numbers itself can honor the format and the alignment
/// with the formatter of the value's type (baseline FR-051, spec 012 FR-037);
/// <c>null</c> for a non-numeric value. A hole with a clause carries its raw
/// value, <c>null</c> included, so a target's formatter MUST accept
/// <c>null</c> and print it as the empty string, as C# does (baseline
/// FR-051.1). A hole without a clause carries the null-safe operand of a
/// concatenation. A <see cref="FormatSpecifier"/> C# ignores (on a value
/// that is not <c>IFormattable</c>, or a <c>char</c>) is dropped, and so is
/// one the target prints as the clause-free text; a hole lowered through the
/// type's <c>ToString(string)</c> mapping carries that call as its value.
/// </summary>
public sealed record IrInterpolationExpression(
    IrExpression Expression,
    string? FormatSpecifier = null,
    int? Alignment = null,
    IrNumericKind? NumericKind = null
) : IrInterpolationPart
{
    /// <summary>Whether the hole has a format or an alignment clause.</summary>
    public bool HasClause => FormatSpecifier is not null || Alignment is not null;
}

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
) : IrExpression
{
    /// <summary>
    /// The record's synthesized copy when it joined a dispatched group (spec
    /// 013 FR-043): a target calls its fast path (<c>source.withByValue(…)</c>)
    /// instead of the shared dispatcher name. <c>null</c> otherwise.
    /// </summary>
    public IrSelectedOverload? Overload { get; init; }
}

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
/// <c>ExpressionVariableLowering</c>); anywhere else (a short-circuit operand, a
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
) : IrExpression
{
    /// <summary>
    /// The type the template's method is declared on, for a template that names it
    /// (<c>$Type.make($0)</c> on a user <c>[Emit]</c>): the emitted name, never a
    /// <c>using</c> alias of it. <c>null</c> for a template that has no declaring type.
    /// </summary>
    public IrTypeRef? ReceiverType { get; init; }

    /// <summary>
    /// The order the source evaluates <see cref="Arguments"/> in, on the
    /// template of a call: see <see cref="IrCallExpression.EvaluationOrder"/>.
    /// A template evaluates its arguments where its placeholders stand, so a
    /// target binds them first when this is set.
    /// </summary>
    public IReadOnlyList<int>? EvaluationOrder { get; init; }

    /// <summary>
    /// Runtime type descriptors passed to the constructor's trailing
    /// <c>$typeArgs</c> tuple (spec 011 FR-024).
    /// </summary>
    public IReadOnlyList<IrTypeRef>? ReifiedTypeArguments { get; init; }

    /// <summary>
    /// The 0-based argument index where the <c>$typeArgs</c> tuple is
    /// inserted (before any <c>params</c> rest parameter).
    /// </summary>
    public int ReifiedArgumentPosition { get; init; }
}
