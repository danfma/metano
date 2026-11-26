using System.Globalization;
using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.AST;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Pattern lowering: the boolean test a pattern performs against a value
/// (<c>is</c> expressions and switch-expression arms), the access path each
/// sub-pattern inspects, and the designators a matched pattern binds.
/// </summary>
public static partial class IrToTsExpressionBridge
{
    private const string PatternSubjectParameterName = "$v";

    /// <summary>
    /// Lowers <c>expr is pattern</c> to a boolean TS expression. A pattern that
    /// declares locals lowers through <see cref="BuildBindingPatternTest"/>:
    /// extraction declares them ahead of the statement, in the innermost
    /// function, and the test assigns them. A subject the pattern would read
    /// more than once is captured in such a local by extraction too, so the
    /// remaining patterns read their subject at most once or read a repeatable
    /// one. Only an <c>is</c> node that did not come from C# source (a
    /// synthesized one) with a subject that is neither falls back to binding
    /// it as the argument of an arrow: <c>(($v) =&gt; test)(subject)</c>.
    /// </summary>
    private static TsExpression MapIsPattern(
        IrIsPatternExpression node,
        IrToTsBridgeContext context
    )
    {
        var subject = Map(node.Expression, context);

        if (IrPatternDesignators.Declares(node.Pattern))
            return BuildBindingPatternTest(subject, node.Pattern, context);

        if (
            IrEvaluationTraits.IsRepeatableRead(node.Expression)
            || IrEvaluationTraits.ReadsSubjectOnce(node.Pattern)
        )
            return MapIsPatternTest(subject, node, context);

        var boundSubject = new TsIdentifier(PatternSubjectParameterName);

        return TsImmediateInvocation.Of(
            [new TsImmediateBinding(PatternSubjectParameterName, subject)],
            [new TsReturnStatement(MapIsPatternTest(boundSubject, node, context))],
            isAsync: false
        );
    }

    private static TsExpression MapIsPatternTest(
        TsExpression lowered,
        IrIsPatternExpression node,
        IrToTsBridgeContext context
    )
    {
        switch (node.Pattern)
        {
            case IrConstantPattern constant:
                return BuildBoxedConstantTest(lowered, constant, context)
                    ?? BuildConstantPatternTest(
                        lowered,
                        BinaryOperatorToken(IrBinaryOp.Equal, node.Expression, constant.Value),
                        constant.Value,
                        constant.IsNaN,
                        context
                    );
            case IrTypePattern typePat:
                return BuildTypeTest(lowered, typePat.Type, typePat.InputType, context);
            case IrDiscardPattern:
                return new TsLiteral("true");
            case IrPropertyPattern prop:
                return BuildPropertyPatternTest(lowered, prop, context);
            case IrRelationalPattern:
            case IrLogicalPattern:
            case IrListPattern:
                return BuildPatternTest(lowered, node.Pattern, context);
            case IrPositionalPattern pos:
                return BuildPositionalPatternTest(lowered, pos, context);
            default:
                return MapUnsupported(
                    new IrUnsupportedExpression($"Pattern:{node.Pattern.GetType().Name}")
                );
        }
    }

    /// <summary>
    /// Lowers a constant pattern to an equality test. A NaN constant is the one
    /// exception: C# <c>x is double.NaN</c> matches NaN, but <c>x === NaN</c> is
    /// always false in JavaScript, so it lowers to <c>Number.isNaN(x)</c>.
    /// </summary>
    private static TsExpression BuildConstantPatternTest(
        TsExpression value,
        string equalityOperator,
        IrExpression constantValue,
        bool isNaN,
        IrToTsBridgeContext context
    ) =>
        isNaN
            ? new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Number"), "isNaN"),
                [value]
            )
            : new TsBinaryExpression(value, equalityOperator, Map(constantValue, context));

    /// <summary>
    /// Lowers one <c>case</c> label of a switch statement that was rewritten to
    /// <c>switch (true)</c>: the label becomes a boolean test against the subject,
    /// with the same NaN rule as constant patterns.
    /// </summary>
    internal static TsExpression BuildCaseLabelTest(
        TsExpression subject,
        IrConstantCaseLabel label,
        IrToTsBridgeContext context
    ) =>
        BuildBoxedConstantTest(subject, label.Value, label.InputType, label.ConstantType, context)
        ?? BuildConstantPatternTest(
            subject,
            BinaryOperatorToken(IrBinaryOp.Equal, right: label.Value),
            label.Value,
            label.IsNaN,
            context
        );

    private static TsExpression? BuildBoxedConstantTest(
        TsExpression value,
        IrConstantPattern constant,
        IrToTsBridgeContext context
    ) =>
        BuildBoxedConstantTest(
            value,
            constant.Value,
            constant.InputType,
            constant.ConstantType,
            context
        );

    /// <summary>
    /// An enum constant compared with a value that may hold it as a box
    /// (spec 011 FR-050, <c>o is Color.Red</c> on an <c>object</c>):
    /// <c>unboxFor(o, Color$runtimeType) === Color.Red</c>, which unwraps a box
    /// of the enum, keeps a box of another type (never equal to the constant),
    /// and compares a raw value as it is (the accepted divergence of
    /// <see cref="IrToTsRuntimeTestBridge.MeetsBoxes"/>). <c>null</c> for any
    /// other constant, and with boxing off.
    /// </summary>
    private static TsExpression? BuildBoxedConstantTest(
        TsExpression value,
        IrExpression constant,
        IrTypeRef? inputType,
        IrTypeRef? constantType,
        IrToTsBridgeContext context
    )
    {
        if (
            constantType is null
            || !IrToTsRuntimeTestBridge.MeetsBoxes(constantType, inputType, context.RuntimeTypes)
        )
            return null;

        var raw = constant is IrBoxExpression box ? box.Value : constant;

        var descriptor = IrToTsRuntimeTestBridge.BoxedEnumDescriptor(
            constantType,
            context.RuntimeTypes
        );

        return new TsBinaryExpression(
            new TsCallExpression(new TsRuntimeHelperReference("unboxFor"), [value, descriptor]),
            "===",
            Map(raw, context)
        );
    }

    /// <summary>
    /// Lowers a pattern to a boolean test against <paramref name="value"/>.
    /// The test never binds; <see cref="BuildPatternBindings"/> declares the
    /// designators separately inside the matched branch.
    /// </summary>
    private static TsExpression BuildPatternTest(
        TsExpression value,
        IrPattern pattern,
        IrToTsBridgeContext context
    ) =>
        pattern switch
        {
            IrConstantPattern constant => BuildBoxedConstantTest(value, constant, context)
                ?? BuildConstantPatternTest(
                    value,
                    BinaryOperatorToken(IrBinaryOp.Equal, right: constant.Value),
                    constant.Value,
                    constant.IsNaN,
                    context
                ),
            IrTypePattern typePat => BuildTypeTest(value, typePat.Type, typePat.InputType, context),
            IrDiscardPattern => new TsLiteral("true"),
            IrVarPattern => new TsLiteral("true"),
            IrPropertyPattern prop => BuildPropertyPatternTest(value, prop, context),
            IrRelationalPattern rel => new TsBinaryExpression(
                value,
                RelationalOpToken(rel.Operator),
                Map(rel.Value, context)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.Not } notPat => NegatedTest(
                BuildPatternTest(value, notPat.Left, context)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.And } and => new TsBinaryExpression(
                BuildPatternTest(value, and.Left, context),
                "&&",
                BuildPatternTest(
                    ConjunctionRightValue(value, and.Left, context.RuntimeTypes.BoxingEnabled),
                    and.Right!,
                    context
                )
            ),
            IrLogicalPattern or => new TsBinaryExpression(
                BuildPatternTest(value, or.Left, context),
                "||",
                BuildPatternTest(value, or.Right!, context)
            ),
            IrListPattern list => BuildListPatternTest(value, list, context),
            IrPositionalPattern pos => BuildPositionalPatternTest(value, pos, context),
            _ => MapUnsupported(new IrUnsupportedExpression($"Pattern:{pattern.GetType().Name}")),
        };

    /// <summary>
    /// The negation of a pattern test: an equality test flips its operator
    /// (<c>x == null</c> becomes <c>x != null</c>, <c>is not null</c>), any
    /// other test is wrapped in <c>!</c>.
    /// </summary>
    private static TsExpression NegatedTest(TsExpression test) =>
        test is TsBinaryExpression { Operator: "==" or "===" or "!=" or "!==" } equality
            ? equality with
            {
                Operator = equality.Operator switch
                {
                    "==" => "!=",
                    "===" => "!==",
                    "!=" => "==",
                    _ => "===",
                },
            }
            : new TsUnaryExpression("!", test);

    /// <summary>
    /// <see cref="BuildPatternTest"/>, or <c>null</c> for a pattern that
    /// always matches (<c>_</c>, <c>var x</c>) and so needs no test.
    /// </summary>
    private static TsExpression? BuildPatternTestOrNull(
        TsExpression value,
        IrPattern pattern,
        IrToTsBridgeContext context
    ) => pattern.AlwaysMatches ? null : BuildPatternTest(value, pattern, context);

    /// <summary>
    /// Joins a pattern test with a <c>when</c> guard as <c>test &amp;&amp; guard</c>.
    /// Without a test the guard alone is the condition. The printer keeps
    /// each side's grouping (<c>(n === 1 || n === 2) &amp;&amp; flag</c>).
    /// </summary>
    private static TsExpression JoinWithGuard(TsExpression? test, TsExpression guard) =>
        test is null ? guard : new TsBinaryExpression(test, "&&", guard);

    /// <summary>
    /// The value the right operand of <c>left and right</c> reads: once
    /// <paramref name="left"/> proved a value that may hold a box to be an enum
    /// (<c>o is Color and not Color.Red</c>, <c>o is Color.Red and Color c</c>,
    /// spec 011 FR-050), the raw enum, <c>unbox(o) as Color</c>, so the nested
    /// constant and relational patterns compare the value and a designator binds
    /// it, rather than the box; <paramref name="value"/> otherwise.
    /// </summary>
    private static TsExpression ConjunctionRightValue(
        TsExpression value,
        IrPattern left,
        bool unboxes
    ) => unboxes && ProvedEnumOf(left) is { } enumType ? UnboxedEnum(value, enumType) : value;

    /// <summary>
    /// The enum a matched <paramref name="pattern"/> proves a value that may
    /// hold a box of it to hold: a type test of it, <c>Color { }</c>, a constant
    /// of it, an <c>or</c> whose sides prove the same enum, or a conjunction
    /// with any of these; <c>null</c> when it proves none. Unboxing a raw value
    /// is the identity, so the proof needs no descriptor.
    /// </summary>
    private static IrTypeRef? ProvedEnumOf(IrPattern pattern) =>
        pattern switch
        {
            IrTypePattern typePattern => EnumOnBoxCapableInput(
                typePattern.Type,
                typePattern.InputType
            ),
            IrPropertyPattern property => EnumOnBoxCapableInput(property.Type, property.InputType),
            IrConstantPattern constant => EnumOnBoxCapableInput(
                constant.ConstantType,
                constant.InputType
            ),
            IrLogicalPattern { Operator: IrLogicalOp.And, Right: { } right } and => ProvedEnumOf(
                and.Left
            ) ?? ProvedEnumOf(right),
            IrLogicalPattern { Operator: IrLogicalOp.Or, Right: { } right } or => SameEnum(
                ProvedEnumOf(or.Left),
                ProvedEnumOf(right)
            ),
            _ => null,
        };

    private static IrTypeRef? EnumOnBoxCapableInput(IrTypeRef? type, IrTypeRef? inputType) =>
        type is not null
        && IrToTsRuntimeTestBridge.IsEnum(type)
        && inputType is not null
        && IrToTsRuntimeTestBridge.MayHoldBox(inputType)
            ? type
            : null;

    private static IrTypeRef? SameEnum(IrTypeRef? left, IrTypeRef? right) =>
        left is not null && IrTypeRefEquivalence.AreEquivalent(left, right) ? left : null;

    /// <summary><c>unbox(value) as Color</c>: the raw enum of a value that may be its box.</summary>
    private static TsExpression UnboxedEnum(TsExpression value, IrTypeRef enumType) =>
        new TsCastExpression(
            new TsCallExpression(new TsRuntimeHelperReference("unbox"), [value]),
            IrToTsTypeMapper.Map(enumType)
        );

    /// <summary>
    /// The runtime test a type pattern performs, as
    /// <see cref="IrToTsRuntimeTestBridge"/> renders the type's category, with
    /// the static type of the tested value when extraction knew it
    /// (<paramref name="staticType"/>); a descriptor read is checked against the
    /// file group's reached set (<paramref name="context"/>). Extraction reports MS0001 for a type
    /// with no test, so none reaches this point but a delegate, whose test the
    /// reachability phase rejected (MS0045): it lowers to a placeholder that
    /// throws.
    /// </summary>
    internal static TsExpression BuildTypeTest(
        TsExpression value,
        IrTypeRef targetType,
        IrTypeRef? staticType,
        IrToTsBridgeContext context
    )
    {
        if (targetType is IrCollectionTypeRef collection)
            return CollectionTypeTest(value, collection, staticType, context);

        if (
            IrToTsRuntimeTestBridge.Render(
                value,
                targetType,
                staticType,
                context.RuntimeTypes,
                context.TypeParameters
            ) is
            { } test
        )
            return test;

        if (targetType is IrTypeParameterRef)
            return MapUnsupported(new IrUnsupportedExpression("UnreifiableTypeTest"));

        return IrRuntimeTypeTest.IsDelegate(targetType)
            ? MapUnsupported(new IrUnsupportedExpression("DelegateTypeTest"))
            : throw NoRuntimeTypeTest(targetType);
    }

    /// <summary>
    /// A test against a collection type (spec 011 D31): <c>isType(value, listType(Int32Type))</c>,
    /// or the throwing placeholder where the reachability phase reported
    /// <c>MS0001</c> (a type that is not a runtime collection may be the value).
    /// A kind with no descriptor factory keeps its <c>instanceof</c>.
    /// </summary>
    private static TsExpression CollectionTypeTest(
        TsExpression value,
        IrCollectionTypeRef collection,
        IrTypeRef? staticType,
        IrToTsBridgeContext context
    )
    {
        if (context.RuntimeTypes.IsUnanswerable(collection.Kind, staticType))
            return MapUnsupported(new IrUnsupportedExpression("CollectionInterfaceTest"));

        if (
            IrToTsRuntimeTypeReferences.Of(
                collection,
                "collection test",
                context.RuntimeTypes,
                context.TypeParameters
            ) is
            { } descriptor
        )
            return IrToTsRuntimeTestBridge.IsTypeCall(value, descriptor);

        return IrToTsRuntimeTestBridge.Render(value, collection, staticType, context.RuntimeTypes)
            ?? throw NoRuntimeTypeTest(collection);
    }

    /// <summary>
    /// Lowers a cast. JS has no runtime cast, so the value passes through —
    /// except for a cast to a <c>[JsTuple]</c>: <c>Array.isArray</c> narrows a
    /// value to <c>any[]</c>, never to the tuple, so unlike a class (narrowed
    /// by <c>instanceof</c>) or a guarded shape (narrowed by its type
    /// predicate) the cast stays a TS assertion for the result to type-check.
    /// So does a cast the compiler synthesized as an assertion. An explicit
    /// reference conversion is typed, and checked where it can be
    /// (<see cref="ReferenceCastLowering"/>). A cast to a numeric enum is
    /// asserted (<see cref="EnumValueAssertion"/>).
    /// </summary>
    private static TsExpression MapCast(IrCastExpression cast, IrToTsBridgeContext context)
    {
        var value = Map(EnumConstantOperand(cast), context);
        var testedType = cast.TargetType is IrNullableTypeRef nullable
            ? nullable.Inner
            : cast.TargetType;

        if (cast.IsAssertion || IrRuntimeTypeTest.Of(testedType) is IrRuntimeTypeTest.FixedTuple)
            return new TsCastExpression(value, IrToTsTypeMapper.Map(cast.TargetType));

        if (cast.ReferenceConversion is not { } conversion)
            return EnumValueAssertion.Wrap(value, cast.TargetType);

        if (ReferenceCastLowering.TypesResult(conversion))
            return MapReferenceDowncast(cast, conversion, value, testedType, context);

        return UnboxesNarrowedEnum(conversion, testedType, context)
            ? UnboxedEnum(value, testedType)
            : EnumValueAssertion.Wrap(value, cast.TargetType);
    }

    /// <summary>
    /// The cast's operand, an integer constant cast to an enum (the zero a
    /// member-less <c>default(E)</c> asserts) in the enum's representation
    /// (<see cref="TsEnumRepresentation.Constant"/>).
    /// </summary>
    private static IrExpression EnumConstantOperand(IrCastExpression cast) =>
        cast.Expression is IrLiteral literal
            ? TsEnumRepresentation.Constant(literal, cast.TargetType)
            : cast.Expression;

    /// <summary>
    /// Whether an unboxing cast to an enum that a type test already narrowed
    /// (<c>o is Color &amp;&amp; (Color)o == Color.Red</c>) still unwraps the box
    /// the test accepted (spec 011 FR-050): it is not checked again, but its
    /// value is the raw enum.
    /// </summary>
    private static bool UnboxesNarrowedEnum(
        IrReferenceConversion conversion,
        IrTypeRef testedType,
        IrToTsBridgeContext context
    ) =>
        conversion.IsUnboxing
        && context.RuntimeTypes.BoxingEnabled
        && IrToTsRuntimeTestBridge.IsEnum(testedType);

    /// <summary>
    /// An explicit reference or unboxing conversion: the result takes the
    /// target type (TypeScript keeps the static type of the operand otherwise,
    /// so every member access through it fails to type-check), and is checked
    /// as .NET checks it where the target has a runtime identity. The
    /// <c>castTo</c> helper lets <c>null</c> through and throws
    /// <c>InvalidCastException</c> for any other type:
    /// <list type="bullet">
    ///   <item>a class (a transpiled class or record, a mapped BCL exception)
    ///   passes its constructor, <c>castTo(x, Ctor)</c>, which reads no
    ///   descriptor (#669, D29), when <see cref="ReferenceCastLowering"/> lets
    ///   this module import the class;</item>
    ///   <item>an interface, an enum, a primitive or a <c>[PlainObject]</c>
    ///   passes its descriptor, <c>castTo(x, IFoo$runtimeType)</c> (D29),
    ///   <c>castTo(x, DecimalType)</c> for a <c>decimal</c>,
    ///   <c>castTo(x, Dto$runtimeType)</c> for a shape (FR-020);</item>
    ///   <item>any other target (a type parameter, an array, a delegate, a
    ///   class this module cannot import) is only asserted.</item>
    /// </list>
    /// </summary>
    private static TsExpression MapReferenceDowncast(
        IrCastExpression cast,
        IrReferenceConversion conversion,
        TsExpression value,
        IrTypeRef testedType,
        IrToTsBridgeContext context
    )
    {
        var targetType = IrToTsTypeMapper.Map(cast.TargetType);

        return CheckedCastTarget(conversion, testedType, context) is { } target
            ? new TsCallExpression(
                new TsRuntimeHelperReference(ReferenceCastLowering.CastHelper),
                [value, target],
                TypeArguments: [CheckedResultType(cast.TargetType, testedType, targetType)]
            )
            : new TsCastExpression(value, targetType);
    }

    /// <summary>
    /// The type a checked cast's result takes: the target's, except that a
    /// <c>decimal</c> names its decimal.js class, the type <c>DecimalType</c>
    /// describes, whatever <c>[ExportFromBcl]</c> override the position
    /// carries.
    /// </summary>
    private static TsType CheckedResultType(IrTypeRef target, IrTypeRef testedType, TsType mapped)
    {
        if (testedType is not IrPrimitiveTypeRef { Primitive: IrPrimitive.Decimal })
            return mapped;

        var decimalType = new TsNamedType("Decimal", Origin: DecimalOrigin());

        return target is IrNullableTypeRef
            ? new TsUnionType([decimalType, new TsNamedType("null")])
            : decimalType;
    }

    private static TsTypeOrigin? DecimalOrigin() =>
        BclDecimalValueReference() is TsTypeReference { Origin: var origin } ? origin : null;

    /// <summary>
    /// What <c>castTo</c> checks the value against: the class of a nominal
    /// class, the descriptor of an interface, an enum or a primitive;
    /// <c>null</c> when the cast is only asserted.
    /// </summary>
    private static TsExpression? CheckedCastTarget(
        IrReferenceConversion conversion,
        IrTypeRef testedType,
        IrToTsBridgeContext context
    ) =>
        IrRuntimeTypeTest.Of(testedType) switch
        {
            IrRuntimeTypeTest.NominalClass => CheckedClassTarget(conversion, testedType, context),
            // A plain object casts through its shape descriptor (FR-020).
            IrRuntimeTypeTest.NominalInterface
            or IrRuntimeTypeTest.Primitive
            or IrRuntimeTypeTest.ErasedShape => IrToTsRuntimeTypeReferences.Of(
                testedType,
                "checked cast",
                context.RuntimeTypes
            ),
            IrRuntimeTypeTest.Reified
            {
                TypeParameter: { Scope: not IrTypeParameterScope.None } tp,
            } => TypeParameterTsResolver.Resolve(tp, context.TypeParameters),
            _ => null,
        };

    private static TsExpression? CheckedClassTarget(
        IrReferenceConversion conversion,
        IrTypeRef testedType,
        IrToTsBridgeContext context
    )
    {
        var runtimeClass = ReferenceCastLowering.MayImportTargetClass(conversion)
            ? RuntimeClassOf(testedType)
            : null;

        return runtimeClass is not null && ReferenceCastLowering.Checks(runtimeClass, context.Casts)
            ? runtimeClass
            : null;
    }

    /// <summary>
    /// The class value a <see cref="IrRuntimeTypeTest.NominalClass"/> test of
    /// <paramref name="type"/> checks; <c>null</c> when the type has none to
    /// pass (a temporal type, or a BCL exception without a mapped class).
    /// </summary>
    private static TsExpression? RuntimeClassOf(IrTypeRef type)
    {
        if (
            type is not IrNamedTypeRef named
            || IrToTsTypeMapper.Map(type) is not TsNamedType runtime
        )
            return null;

        if (!BclExceptionClasses.IsBclException(named))
            return TypeValueReference(runtime.Name, runtime.Origin);

        return IrToTsTypeMapper.ExceptionClasses.IsMapped(named)
            ? IrToTsTypeMapper.ExceptionClasses.ClassOf(named).InstanceofTarget()
            : null;
    }

    private static InvalidOperationException NoRuntimeTypeTest(IrTypeRef type) =>
        new(
            $"Type '{type}' has no TypeScript runtime type test; extraction must report "
                + "MS0001 before a type pattern on it reaches the bridge."
        );

    /// <summary>
    /// Lowers <c>[p0, p1, …]</c> to a length-gated conjunction of per-index
    /// tests. When the pattern carries a <c>..</c> slice we drop the exact
    /// length check (<c>&gt;=</c> instead of <c>===</c>) and reverse-index
    /// the tail sub-patterns with <c>arr.length - k</c>. This test binds
    /// nothing, so the slice always matches; a <c>.. var rest</c> slice binds
    /// through <see cref="SliceBinding"/> and
    /// <see cref="BuildBindingListPatternTest"/>. Always-matching elements add
    /// no test.
    /// </summary>
    private static TsExpression BuildListPatternTest(
        TsExpression value,
        IrListPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildPatternTestOrNull(ListElementAccess(value, pattern, i), element, context)
        );

        return JoinWithAnd([
            ListLengthCheck(value, pattern),
            .. elementTests.OfType<TsExpression>(),
        ]);
    }

    /// <summary>
    /// <c>value.length === n</c> (<c>value.count</c> on a runtime collection),
    /// or <c>&gt;= n</c> when a <c>..</c> slice absorbs any further elements;
    /// preceded by a non-null test when the value may be <c>null</c>.
    /// </summary>
    private static TsExpression ListLengthCheck(TsExpression value, IrListPattern pattern)
    {
        var lengthCheck = new TsBinaryExpression(
            ListLength(value, pattern),
            pattern.SliceIndex is null ? "===" : ">=",
            new TsLiteral(pattern.Elements.Count.ToString())
        );

        return pattern.InputMayBeNull
            ? new TsBinaryExpression(NonNullTest(value), "&&", lengthCheck)
            : lengthCheck;
    }

    /// <summary>
    /// The element a list sub-pattern inspects: <c>arr[i]</c> before the
    /// slice, and — for the tail element <c>k</c> positions past a
    /// <c>..</c> slice — <c>arr[arr.length - k]</c>, so <c>[a, .., b]</c>
    /// tests <c>arr[arr.length - 1]</c>. A runtime collection reads
    /// <c>list.get(i)</c> / <c>list.get(list.count - k)</c>.
    /// </summary>
    private static TsExpression ListElementAccess(
        TsExpression value,
        IrListPattern pattern,
        int index
    )
    {
        var position =
            pattern.SliceIndex is null || index < pattern.SliceIndex
                ? new TsLiteral(index.ToString())
                : ListTailPosition(value, pattern, index);

        return pattern.Collection is null
            ? new TsElementAccess(value, position)
            : Get(value, position);
    }

    private static TsExpression ListTailPosition(
        TsExpression value,
        IrListPattern pattern,
        int index
    )
    {
        var tailOffset = pattern.Elements.Count - index;

        return new TsBinaryExpression(
            ListLength(value, pattern),
            "-",
            new TsLiteral(tailOffset.ToString())
        );
    }

    /// <summary><c>value.length</c>, or <c>value.count</c> on a runtime collection.</summary>
    private static TsPropertyAccess ListLength(TsExpression value, IrListPattern pattern) =>
        new(value, pattern.Collection is null ? "length" : "count");

    /// <summary>
    /// Lowers <c>(p0, p1, …)</c> / <c>Point(p0, p1)</c>: an optional
    /// <c>instanceof</c> type filter followed by one test per element, read
    /// through <see cref="PositionalElementAccess"/>. Always-matching
    /// elements add no test. Without a type filter, an input whose type
    /// admits <c>null</c> (a record, a nullable tuple or <c>KeyValuePair</c>)
    /// is tested for <c>null</c> first, as C# does.
    /// </summary>
    private static TsExpression BuildPositionalPatternTest(
        TsExpression value,
        IrPositionalPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var typeTest = PositionalTypeTest(value, pattern, context);

        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildPatternTestOrNull(PositionalElementAccess(value, pattern, i), element, context)
        );

        var tests = new[] { typeTest }.Concat(elementTests).OfType<TsExpression>().ToList();

        return tests.Count == 0 ? new TsLiteral("true") : JoinWithAnd(tests);
    }

    private static TsExpression? PositionalTypeTest(
        TsExpression value,
        IrPositionalPattern pattern,
        IrToTsBridgeContext context
    )
    {
        if (pattern.Type is not null)
            return BuildTypeTest(value, pattern.Type, staticType: null, context);

        return pattern.InputMayBeNull ? NonNullTest(value) : null;
    }

    /// <summary>
    /// The value a positional element reads. A value that lowers to an array
    /// tuple (tuples, <c>KeyValuePair</c>, <c>[JsTuple]</c> records) is read by
    /// position (<c>value[i]</c>); a record's synthesized <c>Deconstruct</c>
    /// element reads the positional property (<c>value.radius</c>). An element
    /// the extractor could not resolve (already reported as MS0001) reads <c>undefined</c> with a marker
    /// comment, so the output still parses.
    /// </summary>
    private static TsExpression PositionalElementAccess(
        TsExpression value,
        IrPositionalPattern pattern,
        int index
    ) =>
        pattern.ElementMembers switch
        {
            null => new TsElementAccess(value, new TsLiteral(index.ToString())),
            var members when members[index] is { } member => new TsPropertyAccess(
                value,
                ReadDataMemberName(member.Name, member.EmittedName, member.IsTranspiledDataMember)
            ),
            _ => new TsIdentifier(
                $"undefined /* TODO: positional element {index} has no member to read */"
            ),
        };

    /// <summary>
    /// Lowers a property pattern to a conjunction of sub-tests:
    /// <list type="bullet">
    ///   <item>When the pattern has a <see cref="IrPropertyPattern.Type"/> filter,
    ///   start with <c>value instanceof T</c>.</item>
    ///   <item>For each <c>{ MemberName: pattern }</c> entry, emit the inner
    ///   pattern test against <c>value.memberName</c> (camelCased to match the
    ///   TS backend's naming policy); always-matching entries add no test.</item>
    /// </list>
    /// Without a type filter the pattern still requires a non-null value, as
    /// C# does (<c>is { }</c> is a null check), so the member reads never
    /// dereference <c>null</c>.
    /// </summary>
    private static TsExpression BuildPropertyPatternTest(
        TsExpression value,
        IrPropertyPattern pattern,
        IrToTsBridgeContext context
    )
    {
        var typeTest = PropertyPatternTypeTest(value, pattern, context);

        var memberTests = pattern.Subpatterns.Select(sub =>
            BuildPatternTestOrNull(
                PropertyPatternMemberAccess(value, sub, context),
                sub.Pattern,
                context
            )
        );

        return JoinWithAnd([typeTest, .. memberTests.OfType<TsExpression>()]);
    }

    /// <summary>
    /// The test a property pattern starts with: its type filter, classified
    /// with the static type of the tested value (<c>o is Color { }</c> on an
    /// <c>object</c> tests a possibly boxed enum), or a non-null test.
    /// </summary>
    private static TsExpression PropertyPatternTypeTest(
        TsExpression value,
        IrPropertyPattern pattern,
        IrToTsBridgeContext context
    ) =>
        pattern.Type is null
            ? NonNullTest(value)
            : BuildTypeTest(value, pattern.Type, pattern.InputType, context);

    private static TsExpression NonNullTest(TsExpression value) =>
        new TsBinaryExpression(value, "!=", new TsLiteral("null"));

    private static TsExpression JoinWithAnd(IReadOnlyList<TsExpression> tests) =>
        tests
            .Skip(1)
            .Aggregate(tests[0], (accumulated, test) =>
                new TsBinaryExpression(accumulated, "&&", test)
            );

    /// <summary>
    /// The member read of a property pattern entry, through the declarative
    /// BCL mappings like an ordinary member read (<c>{ LongLength: 2 }</c> on
    /// an array reads <c>BigInt(value.length)</c>).
    /// </summary>
    private static TsExpression PropertyPatternMemberAccess(
        TsExpression value,
        IrPropertySubpattern sub,
        IrToTsBridgeContext context
    ) =>
        MappedPropertyPatternRead(value, sub, context)
        ?? new TsPropertyAccess(
            value,
            ReadDataMemberName(sub.MemberName, sub.EmittedName, sub.IsTranspiledDataMember)
        );

    private static TsExpression? MappedPropertyPatternRead(
        TsExpression value,
        IrPropertySubpattern sub,
        IrToTsBridgeContext context
    ) =>
        (sub.Origin, context.Bcl) is ({ } origin, { } bcl)
            ? IrToTsBclMapper.TryMapInstanceRead(origin, value, bcl)
            : null;

    private static string RelationalOpToken(IrRelationalOp op) =>
        op switch
        {
            IrRelationalOp.LessThan => "<",
            IrRelationalOp.LessThanOrEqual => "<=",
            IrRelationalOp.GreaterThan => ">",
            IrRelationalOp.GreaterThanOrEqual => ">=",
            _ => "<",
        };

    /// <summary>
    /// One declaration per designator the pattern introduces, reading the
    /// access path the pattern test inspected (<c>$s</c>, <c>$s.member</c>,
    /// <c>$s[i]</c>). Inside the matched <c>if</c> TypeScript narrows those
    /// paths by the <c>instanceof</c> test, so a type-pattern designator gets
    /// the tested type. A designator the arm reassigns
    /// (<paramref name="reassigned"/> — IR names, like the
    /// <see cref="PatternBinding"/> names it checks) is a <c>let</c>, every
    /// other one a <c>const</c>; the declaration carries the emitted local
    /// name, so it matches the reads (#390).
    /// </summary>
    private static IReadOnlyList<TsStatement> BuildPatternBindings(
        TsExpression value,
        IrPattern pattern,
        IReadOnlySet<string> reassigned,
        IrToTsBridgeContext context
    ) =>
        EnumeratePatternBindings(value, pattern, context)
            .Select(binding =>
                (TsStatement)
                    new TsVariableDeclaration(
                        IrToTsNamingPolicy.ToLocalName(binding.Name),
                        binding.Value,
                        Const: !reassigned.Contains(binding.Name)
                    )
            )
            .ToList();

    /// <summary>
    /// Whether the pattern binds any designator. Enumerates
    /// <see cref="EnumeratePatternBindings"/> lazily, so it stops at the
    /// first one.
    /// </summary>
    private static bool HasDesignators(IrPattern pattern, IrToTsBridgeContext context) =>
        EnumeratePatternBindings(new TsIdentifier(ScrutineeParameterName), pattern, context).Any();

    /// <summary>
    /// Every designator the pattern introduces with the value it reads, in
    /// source order. Designators under <c>or</c> / <c>not</c> are never
    /// definitely assigned in C#, so they are not bound.
    /// </summary>
    private static IEnumerable<PatternBinding> EnumeratePatternBindings(
        TsExpression value,
        IrPattern pattern,
        IrToTsBridgeContext context
    ) =>
        pattern switch
        {
            IrUnsupportedPattern
            {
                TypePattern.Designator: { } designator,
                TypePattern: var rejected
            } =>
            [
                new PatternBinding(
                    designator.Name,
                    new TsCastExpression(
                        new TsCastExpression(value, new TsNamedType("unknown")),
                        IrToTsTypeMapper.Map(rejected.Type)
                    )
                ),
            ],
            IrTypePattern { Designator: { } designator } typePattern =>
            [
                new PatternBinding(
                    designator.Name,
                    UnboxedBindingValue(value, typePattern, context)
                ),
            ],
            IrVarPattern varPattern => [new PatternBinding(varPattern.Designator.Name, value)],
            IrPropertyPattern property => DesignatorBinding(
                    property.Designator,
                    UnboxedBindingValue(value, property.Type, property.InputType, context)
                )
                .Concat(
                    property.Subpatterns.SelectMany(sub =>
                        EnumeratePatternBindings(
                            PropertyPatternMemberAccess(value, sub, context),
                            sub.Pattern,
                            context
                        )
                    )
                ),
            IrPositionalPattern positional => DesignatorBinding(positional.Designator, value)
                .Concat(
                    positional.Elements.SelectMany(
                        (element, i) =>
                            EnumeratePatternBindings(
                                PositionalElementAccess(value, positional, i),
                                element,
                                context
                            )
                    )
                ),
            IrListPattern list => list
                .Elements.SelectMany(
                    (element, i) =>
                        EnumeratePatternBindings(
                            ListElementAccess(value, list, i),
                            element,
                            context
                        )
                )
                .Concat(SliceBinding(value, list)),
            IrLogicalPattern { Operator: IrLogicalOp.And, Right: { } right } and =>
                EnumeratePatternBindings(value, and.Left, context)
                    .Concat(
                        EnumeratePatternBindings(
                            ConjunctionRightValue(
                                value,
                                and.Left,
                                context.RuntimeTypes.BoxingEnabled
                            ),
                            right,
                            context
                        )
                    ),
            _ => [],
        };

    /// <summary>
    /// The value a type pattern's designator reads: an enum matched on a value
    /// that may hold a box of it (spec 011 FR-050) reads the raw value,
    /// <c>unbox(value) as Color</c>; a raw value passes through <c>unbox</c>
    /// unchanged.
    /// </summary>
    private static TsExpression UnboxedBindingValue(
        TsExpression value,
        IrTypePattern pattern,
        IrToTsBridgeContext context
    ) => UnboxedBindingValue(value, pattern.Type, pattern.InputType, context);

    private static TsExpression UnboxedBindingValue(
        TsExpression value,
        IrTypeRef? type,
        IrTypeRef? inputType,
        IrToTsBridgeContext context
    ) =>
        context.RuntimeTypes.BoxingEnabled && EnumOnBoxCapableInput(type, inputType) is { } enumType
            ? UnboxedEnum(value, enumType)
            : value;

    private static IEnumerable<PatternBinding> DesignatorBinding(
        IrPatternDesignator? designator,
        TsExpression value
    ) => designator is null ? [] : [new PatternBinding(designator.Name, value)];

    /// <summary>
    /// <c>.. var rest</c> binds the elements the slice spans (see
    /// <see cref="ListSliceAccess"/>); a slice with a nested pattern binds
    /// nothing, as its test is not modeled.
    /// </summary>
    private static IEnumerable<PatternBinding> SliceBinding(
        TsExpression value,
        IrListPattern list
    ) =>
        list.SlicePattern is IrVarPattern rest
            ? [new PatternBinding(rest.Designator.Name, ListSliceAccess(value, list))]
            : [];

    /// <summary>
    /// The elements a <c>..</c> slice spans: <c>value.slice(i)</c>, or
    /// <c>value.slice(i, -k)</c> when <c>k</c> elements follow it. A runtime
    /// <c>List</c> (the one collection C# slices, through <c>Slice</c>) takes
    /// absolute bounds: <c>value.slice(i, value.count - k)</c>.
    /// </summary>
    private static TsExpression ListSliceAccess(TsExpression value, IrListPattern list)
    {
        var start = list.SliceIndex ?? list.Elements.Count;
        var tailCount = list.Elements.Count - start;
        var startLiteral = new TsLiteral(start.ToString());

        TsExpression[] arguments =
            tailCount == 0 ? [startLiteral] : [startLiteral, SliceEnd(value, list, tailCount)];

        return new TsCallExpression(new TsPropertyAccess(value, "slice"), arguments);
    }

    private static TsExpression SliceEnd(TsExpression value, IrListPattern list, int tailCount) =>
        list.Collection is null
            ? new TsLiteral($"-{tailCount}")
            : CountMinus(value, new TsLiteral(tailCount.ToString()));

    /// <summary>A designator a pattern binds and the value it reads.</summary>
    private sealed record PatternBinding(string Name, TsExpression Value);
}
