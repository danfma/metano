using System.Globalization;
using Metano.Compiler.Analysis;
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
    /// Lowers <c>expr is pattern</c> to a boolean TS expression. Binding patterns
    /// (type-with-designator, var) emit a TODO comment because expression-scoped
    /// variable introductions need a higher-level rewrite than a single
    /// expression can carry in TS. The common non-binding shapes — constant
    /// comparisons and bare type tests — lower cleanly. A side-effecting
    /// subject is evaluated once, whatever the pattern reads.
    /// </summary>
    private static TsExpression MapIsPattern(
        IrIsPatternExpression node,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var subject = Map(node.Expression, bclRegistry);

        if (IrEvaluationTraits.IsSideEffectFree(node.Expression) || ReadsSubjectOnce(node.Pattern))
            return MapIsPatternTest(subject, node, bclRegistry);

        var boundSubject = new TsIdentifier(PatternSubjectParameterName);

        return TsImmediateInvocation.Of(
            [new TsImmediateBinding(PatternSubjectParameterName, subject)],
            [new TsReturnStatement(MapIsPatternTest(boundSubject, node, bclRegistry))],
            isAsync: false
        );
    }

    /// <summary>
    /// Whether the test a pattern lowers to reads the subject at most once:
    /// a constant or relational comparison, <c>_</c>, a type test other than
    /// the two-read <see cref="IrRuntimeTypeTest.ArrayTuple"/> shape, or the
    /// negation of one of these.
    /// Any other pattern reads the subject (or its members) repeatedly, so a
    /// side-effecting subject is bound once first:
    /// <c>(($v) =&gt; test)(subject)</c>.
    /// </summary>
    private static bool ReadsSubjectOnce(IrPattern pattern) =>
        pattern switch
        {
            IrConstantPattern or IrRelationalPattern or IrDiscardPattern => true,
            IrLogicalPattern { Operator: IrLogicalOp.Not } notPattern => ReadsSubjectOnce(
                notPattern.Left
            ),
            IrTypePattern { DesignatorName: null } typePattern => IrRuntimeTypeTest.Of(
                typePattern.Type
            )
                is not IrRuntimeTypeTest.ArrayTuple,
            _ => false,
        };

    private static TsExpression MapIsPatternTest(
        TsExpression lowered,
        IrIsPatternExpression node,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        switch (node.Pattern)
        {
            case IrConstantPattern constant:
                return BuildConstantPatternTest(
                    lowered,
                    BinaryOperatorToken(IrBinaryOp.Equal, node.Expression, constant.Value),
                    constant.Value,
                    bclRegistry
                );
            case IrTypePattern typePat when typePat.DesignatorName is null:
                return BuildTypeTest(lowered, typePat.Type);
            case IrDiscardPattern:
                return new TsLiteral("true");
            case IrPropertyPattern prop when prop.DesignatorName is null:
                return BuildPropertyPatternTest(lowered, prop, bclRegistry);
            case IrRelationalPattern:
            case IrLogicalPattern:
            case IrListPattern:
                return BuildPatternTest(lowered, node.Pattern, bclRegistry);
            case IrPositionalPattern pos when pos.DesignatorName is null:
                return BuildPositionalPatternTest(lowered, pos, bclRegistry);
            default:
                return new TsIdentifier(
                    $"/* TODO: unsupported is-pattern {node.Pattern.GetType().Name} */"
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
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        IsNaNConstant(constantValue)
            ? new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Number"), "isNaN"),
                [value]
            )
            : new TsBinaryExpression(value, equalityOperator, Map(constantValue, bclRegistry));

    /// <summary>
    /// Lowers one <c>case</c> label of a switch statement that was rewritten to
    /// <c>switch (true)</c>: the label becomes a boolean test against the subject,
    /// with the same NaN rule as constant patterns.
    /// </summary>
    internal static TsExpression BuildCaseLabelTest(
        TsExpression subject,
        IrExpression label,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        BuildConstantPatternTest(
            subject,
            BinaryOperatorToken(IrBinaryOp.Equal, right: label),
            label,
            bclRegistry
        );

    internal static bool IsNaNConstant(IrExpression constant) =>
        constant switch
        {
            IrLiteral { Value: double literal } => double.IsNaN(literal),
            IrLiteral { Value: float literal } => float.IsNaN(literal),
            IrMemberAccess { Origin: { } origin } => IsFloatingPointNaNMember(origin),
            _ => false,
        };

    // Stable full names use the C# keyword for special types ("double", "float").
    private static bool IsFloatingPointNaNMember(IrMemberOrigin origin) =>
        origin.MemberName == nameof(double.NaN)
        && (origin.DeclaringTypeFullName is "double" or "float");

    /// <summary>
    /// Lowers a pattern to a boolean test against <paramref name="value"/>.
    /// The test never binds; <see cref="BuildPatternBindings"/> declares the
    /// designators separately inside the matched branch.
    /// </summary>
    private static TsExpression BuildPatternTest(
        TsExpression value,
        IrPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        pattern switch
        {
            IrConstantPattern constant => BuildConstantPatternTest(
                value,
                BinaryOperatorToken(IrBinaryOp.Equal, right: constant.Value),
                constant.Value,
                bclRegistry
            ),
            IrTypePattern typePat => BuildTypeTest(value, typePat.Type),
            IrDiscardPattern => new TsLiteral("true"),
            IrVarPattern => new TsLiteral("true"),
            IrPropertyPattern prop => BuildPropertyPatternTest(value, prop, bclRegistry),
            IrRelationalPattern rel => new TsBinaryExpression(
                value,
                RelationalOpToken(rel.Operator),
                Map(rel.Value, bclRegistry)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.Not } notPat => new TsUnaryExpression(
                "!",
                BuildPatternTest(value, notPat.Left, bclRegistry)
            ),
            IrLogicalPattern log => new TsBinaryExpression(
                BuildPatternTest(value, log.Left, bclRegistry),
                log.Operator is IrLogicalOp.And ? "&&" : "||",
                BuildPatternTest(value, log.Right!, bclRegistry)
            ),
            IrListPattern list => BuildListPatternTest(value, list, bclRegistry),
            IrPositionalPattern pos => BuildPositionalPatternTest(value, pos, bclRegistry),
            _ => new TsIdentifier($"/* TODO: unsupported pattern {pattern.GetType().Name} */"),
        };

    /// <summary>
    /// <see cref="BuildPatternTest"/>, or <c>null</c> for a pattern that
    /// always matches (<c>_</c>, <c>var x</c>) and so needs no test.
    /// </summary>
    private static TsExpression? BuildPatternTestOrNull(
        TsExpression value,
        IrPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    ) => IsAlwaysMatchingPattern(pattern) ? null : BuildPatternTest(value, pattern, bclRegistry);

    private static bool IsAlwaysMatchingPattern(IrPattern pattern) =>
        pattern is IrDiscardPattern or IrVarPattern;

    /// <summary>
    /// Joins a pattern test with a <c>when</c> guard as <c>test &amp;&amp; guard</c>.
    /// Without a test the guard alone is the condition. The printer keeps
    /// each side's grouping (<c>(n === 1 || n === 2) &amp;&amp; flag</c>).
    /// </summary>
    private static TsExpression JoinWithGuard(TsExpression? test, TsExpression guard) =>
        test is null ? guard : new TsBinaryExpression(test, "&&", guard);

    /// <summary>
    /// The runtime test a type pattern performs, as
    /// <see cref="IrRuntimeTypeTest"/> classifies the type: <c>typeof</c> of the
    /// JS primitive it lowers to (primitives, enums, branded wrappers), its
    /// generated <c>[GenerateGuard]</c> guard (erased interfaces and
    /// <c>[PlainObject]</c>s), the array shape and length of a
    /// <c>[JsTuple]</c> (a type alias with no runtime value to reference), or
    /// <c>instanceof</c> its class. Extraction reports MS0001 for a type with
    /// no test, so none reaches this point.
    /// </summary>
    private static TsExpression BuildTypeTest(TsExpression value, IrTypeRef targetType) =>
        IrRuntimeTypeTest.Of(targetType) switch
        {
            IrRuntimeTypeTest.Typeof { Primitive: var primitive } => IrTypeCheckBuilder.TypeofTest(
                value,
                IrTypeCheckBuilder.TypeofKind(primitive)
            ),
            IrRuntimeTypeTest.Guard => new TsCallExpression(
                new TsIdentifier($"is{RuntimeNamedType(targetType).Name}"),
                [value]
            ),
            IrRuntimeTypeTest.ArrayTuple { Arity: var arity } => ArrayTupleTest(value, arity),
            IrRuntimeTypeTest.Instanceof => InstanceofTest(value, RuntimeNamedType(targetType)),
            _ => throw NoRuntimeTypeTest(targetType),
        };

    /// <summary>
    /// The structural <see cref="IrRuntimeTypeTest.ArrayTuple"/> test: an
    /// array of the tuple's length.
    /// </summary>
    private static TsExpression ArrayTupleTest(TsExpression value, int arity) =>
        new TsBinaryExpression(
            new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Array"), "isArray"),
                [value]
            ),
            "&&",
            new TsBinaryExpression(
                new TsPropertyAccess(value, "length"),
                "===",
                new TsLiteral(arity.ToString(CultureInfo.InvariantCulture))
            )
        );

    /// <summary>
    /// Lowers a cast. JS has no runtime cast, so the value passes through —
    /// except for a cast to a <c>[JsTuple]</c>: <c>Array.isArray</c> narrows a
    /// value to <c>any[]</c>, never to the tuple, so unlike a class (narrowed
    /// by <c>instanceof</c>) or a guarded shape (narrowed by its type
    /// predicate) the cast stays a TS assertion for the result to type-check.
    /// </summary>
    private static TsExpression MapCast(
        IrCastExpression cast,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var value = Map(cast.Expression, bclRegistry);
        var testedType = cast.TargetType is IrNullableTypeRef nullable
            ? nullable.Inner
            : cast.TargetType;

        return IrRuntimeTypeTest.Of(testedType) is IrRuntimeTypeTest.ArrayTuple
            ? new TsCastExpression(value, IrToTsTypeMapper.Map(cast.TargetType))
            : value;
    }

    private static TsExpression InstanceofTest(TsExpression value, TsNamedType type) =>
        new TsBinaryExpression(value, "instanceof", TypeValueReference(type.Name, type.Origin));

    private static TsNamedType RuntimeNamedType(IrTypeRef type) =>
        IrToTsTypeMapper.Map(type) as TsNamedType ?? throw NoRuntimeTypeTest(type);

    private static InvalidOperationException NoRuntimeTypeTest(IrTypeRef type) =>
        new(
            $"Type '{type}' has no TypeScript runtime type test; extraction must report "
                + "MS0001 before a type pattern on it reaches the bridge."
        );

    /// <summary>
    /// Lowers <c>[p0, p1, …]</c> to a length-gated conjunction of per-index
    /// tests. When the pattern carries a <c>..</c> slice we drop the exact
    /// length check (<c>&gt;=</c> instead of <c>===</c>) and reverse-index
    /// the tail sub-patterns with <c>arr.length - k</c>. Slice sub-pattern
    /// bindings (<c>.. var tail</c>) aren't modeled yet — the IR extractor
    /// drops them, so the tail is treated as `always match`. Always-matching
    /// elements add no test.
    /// </summary>
    private static TsExpression BuildListPatternTest(
        TsExpression value,
        IrListPattern pattern,
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var lengthCheck = new TsBinaryExpression(
            new TsPropertyAccess(value, "length"),
            pattern.SliceIndex is null ? "===" : ">=",
            new TsLiteral(pattern.Elements.Count.ToString())
        );

        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildPatternTestOrNull(ListElementAccess(value, pattern, i), element, bclRegistry)
        );

        return JoinWithAnd([lengthCheck, .. elementTests.OfType<TsExpression>()]);
    }

    /// <summary>
    /// The element a list sub-pattern inspects: <c>arr[i]</c> before the
    /// slice, and — for the tail element <c>k</c> positions past a
    /// <c>..</c> slice — <c>arr[arr.length - k]</c>, so <c>[a, .., b]</c>
    /// tests <c>arr[arr.length - 1]</c>.
    /// </summary>
    private static TsExpression ListElementAccess(
        TsExpression value,
        IrListPattern pattern,
        int index
    )
    {
        if (pattern.SliceIndex is null || index < pattern.SliceIndex)
            return new TsElementAccess(value, new TsLiteral(index.ToString()));

        var tailOffset = pattern.Elements.Count - index;
        var lengthAccess = new TsPropertyAccess(value, "length");

        return new TsElementAccess(
            value,
            new TsBinaryExpression(lengthAccess, "-", new TsLiteral(tailOffset.ToString()))
        );
    }

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
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var typeTest = PositionalTypeTest(value, pattern);

        var elementTests = pattern.Elements.Select(
            (element, i) =>
                BuildPatternTestOrNull(
                    PositionalElementAccess(value, pattern, i),
                    element,
                    bclRegistry
                )
        );

        var tests = new[] { typeTest }.Concat(elementTests).OfType<TsExpression>().ToList();

        return tests.Count == 0 ? new TsLiteral("true") : JoinWithAnd(tests);
    }

    private static TsExpression? PositionalTypeTest(TsExpression value, IrPositionalPattern pattern)
    {
        if (pattern.Type is not null)
            return BuildTypeTest(value, pattern.Type);

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
        DeclarativeMappingRegistry? bclRegistry
    )
    {
        var typeTest = pattern.Type is null
            ? NonNullTest(value)
            : BuildTypeTest(value, pattern.Type);

        var memberTests = pattern.Subpatterns.Select(sub =>
            BuildPatternTestOrNull(
                PropertyPatternMemberAccess(value, sub),
                sub.Pattern,
                bclRegistry
            )
        );

        return JoinWithAnd([typeTest, .. memberTests.OfType<TsExpression>()]);
    }

    private static TsExpression NonNullTest(TsExpression value) =>
        new TsBinaryExpression(value, "!=", new TsLiteral("null"));

    private static TsExpression JoinWithAnd(IReadOnlyList<TsExpression> tests) =>
        tests
            .Skip(1)
            .Aggregate(
                tests[0],
                (accumulated, test) => new TsBinaryExpression(accumulated, "&&", test)
            );

    private static TsExpression PropertyPatternMemberAccess(
        TsExpression value,
        IrPropertySubpattern sub
    ) =>
        new TsPropertyAccess(
            value,
            ReadDataMemberName(sub.MemberName, sub.EmittedName, sub.IsTranspiledDataMember)
        );

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
    /// (<paramref name="reassigned"/>) is a <c>let</c>, every other one a
    /// <c>const</c>.
    /// </summary>
    private static IReadOnlyList<TsStatement> BuildPatternBindings(
        TsExpression value,
        IrPattern pattern,
        IReadOnlySet<string> reassigned
    ) =>
        EnumeratePatternBindings(value, pattern)
            .Select(binding =>
                (TsStatement)
                    new TsVariableDeclaration(
                        binding.Name,
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
    private static bool HasDesignators(IrPattern pattern) =>
        EnumeratePatternBindings(new TsIdentifier(ScrutineeParameterName), pattern).Any();

    /// <summary>
    /// Every designator the pattern introduces with the value it reads, in
    /// source order. Designators under <c>or</c> / <c>not</c> are never
    /// definitely assigned in C#, so they are not bound.
    /// </summary>
    private static IEnumerable<PatternBinding> EnumeratePatternBindings(
        TsExpression value,
        IrPattern pattern
    ) =>
        pattern switch
        {
            IrTypePattern { DesignatorName: { } name } => [new PatternBinding(name, value)],
            IrVarPattern varPattern => [new PatternBinding(varPattern.Name, value)],
            IrPropertyPattern property => DesignatorBinding(property.DesignatorName, value)
                .Concat(
                    property.Subpatterns.SelectMany(sub =>
                        EnumeratePatternBindings(
                            PropertyPatternMemberAccess(value, sub),
                            sub.Pattern
                        )
                    )
                ),
            IrPositionalPattern positional => DesignatorBinding(positional.DesignatorName, value)
                .Concat(
                    positional.Elements.SelectMany(
                        (element, i) =>
                            EnumeratePatternBindings(
                                PositionalElementAccess(value, positional, i),
                                element
                            )
                    )
                ),
            IrListPattern list => list.Elements.SelectMany(
                (element, i) => EnumeratePatternBindings(ListElementAccess(value, list, i), element)
            ),
            IrLogicalPattern { Operator: IrLogicalOp.And, Right: { } right } and =>
                EnumeratePatternBindings(value, and.Left)
                    .Concat(EnumeratePatternBindings(value, right)),
            _ => [],
        };

    private static IEnumerable<PatternBinding> DesignatorBinding(
        string? designatorName,
        TsExpression value
    ) => designatorName is null ? [] : [new PatternBinding(designatorName, value)];

    /// <summary>A designator a pattern binds and the value it reads.</summary>
    private sealed record PatternBinding(string Name, TsExpression Value);
}
