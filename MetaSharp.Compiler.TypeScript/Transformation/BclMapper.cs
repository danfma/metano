using MetaSharp.TypeScript;
using MetaSharp.TypeScript.AST;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace MetaSharp.Transformation;

/// <summary>
/// Maps known BCL methods and properties to JavaScript equivalents.
///
/// Resolution order: declarative <c>[MapMethod]</c>/<c>[MapProperty]</c> entries from
/// referenced assemblies are consulted first via
/// <see cref="ExpressionTransformer.DeclarativeMappings"/>; the hardcoded fallbacks below
/// only fire when no declarative entry matches. As declarative coverage grows, the
/// hardcoded branches will be deleted one area at a time until <c>BclMapper</c> is a
/// pure dispatcher over the registry.
/// </summary>
public static class BclMapper
{
    /// <summary>
    /// Try to map a member access (property or method without invocation).
    /// </summary>
    public static TsExpression? TryMap(
        ISymbol symbol,
        MemberAccessExpressionSyntax member,
        ExpressionTransformer transformer
    )
    {
        if (symbol.ContainingType is null) return null;

        var obj = transformer.TransformExpression(member.Expression);

        // 1. Declarative property mapping wins over any hardcoded fallback below.
        if (symbol.Kind is SymbolKind.Property or SymbolKind.Field
            && transformer.DeclarativeMappings.TryGetProperty(symbol.ContainingType, symbol.Name, out var propMapping))
        {
            // For instance access (`x.Prop`), the receiver is `obj`. For static access
            // (`Type.Prop`), the receiver is dropped — `obj` would be the bare type
            // identifier which we don't want to leak into the JS output.
            var isStaticAccess = symbol.IsStatic;
            return ApplyPropertyMapping(propMapping, receiver: isStaticAccess ? null : obj);
        }

        var containing = symbol.ContainingType.ToDisplayString();

        // (string.Length is now handled declaratively via MetaSharp/Runtime/Strings.cs.)

        // List/Queue/Stack collection-family Count → length and Dictionary/HashSet
        // Count → size are now handled declaratively via MetaSharp/Runtime/Lists.cs,
        // Queues.cs, Stacks.cs, Dictionaries.cs and Sets.cs.

        // Task.CompletedTask and DateTimeOffset.UtcNow are now handled declaratively via
        // MetaSharp/Runtime/Tasks.cs and MetaSharp/Runtime/Temporal.cs.

        // DateOnly.DayNumber → dayNumber(date) helper from runtime.
        // Stays hardcoded because DateOnly is .NET 6+ and the MetaSharp annotations
        // assembly targets netstandard2.0 — typeof(DateOnly) doesn't resolve there.
        // Tracked as a follow-up alongside multi-targeting the project.
        if (containing == "System.DateOnly" && symbol.Name == "DayNumber")
            return new TsCallExpression(
                new TsIdentifier("dayNumber"),
                [obj]);

        return null;
    }

    /// <summary>
    /// Try to map a method invocation to a JS equivalent.
    /// </summary>
    public static TsExpression? TryMapMethod(
        IMethodSymbol method,
        InvocationExpressionSyntax invocation,
        ExpressionTransformer transformer
    )
    {
        if (method.ContainingType is null) return null;

        var name = method.Name;

        var args = invocation
            .ArgumentList.Arguments.Select(a => transformer.TransformExpression(a.Expression))
            .ToList();

        // 1. Declarative method mappings win over any hardcoded fallback below.
        // Multiple entries per (Type, Name) are allowed when WhenArg0StringEquals filters
        // are used to discriminate between literal arg shapes. Walk in declaration order
        // and pick the first whose filter matches the call site.
        if (transformer.DeclarativeMappings.TryGetMethods(method.ContainingType, name, out var methodMappings))
        {
            DeclarativeMappingEntry? match = null;
            foreach (var candidate in methodMappings)
            {
                if (MatchesArgFilter(candidate, args))
                {
                    match = candidate;
                    break;
                }
            }

            if (match is not null)
            {
                // Resolve the receiver from the syntax. For instance methods this is the
                // value expression to the left of the dot; for static methods called via
                // `TypeName.Method(args)` this is the type identifier itself, which the
                // IdentifierHandler renders verbatim in PascalCase. Both shapes feed the
                // same JsMethod / JsTemplate substitution. Free-standing calls (no member
                // access syntax) have no receiver to substitute.
                TsExpression? receiver = null;
                if (invocation.Expression is MemberAccessExpressionSyntax declarativeReceiverAccess)
                    receiver = transformer.TransformExpression(declarativeReceiverAccess.Expression);

                // Capture generic method type-argument names for $T0/$T1/... template
                // placeholders. For non-generic methods this is the empty list.
                var typeArgNames = method.TypeArguments
                    .Select(t => t.Name)
                    .ToList();

                return ApplyMethodMapping(match, receiver, args, typeArgNames);
            }
        }

        var containing = method.ContainingType.ToDisplayString();

        // System.Math static methods are now handled declaratively via
        // MetaSharp/Runtime/Math.cs.

        // string instance methods are now handled declaratively via
        // MetaSharp/Runtime/Strings.cs (ToUpper/ToLower, Contains, StartsWith, EndsWith,
        // Trim/TrimStart/TrimEnd, Replace, Substring, IndexOf).

        // List<T> / IList<T> / ICollection<T> / IReadOnlyList<T> instance methods are
        // now handled declaratively via MetaSharp/Runtime/Lists.cs (Count, Add, AddRange,
        // Contains, IndexOf, Insert, Clear, Reverse, Sort, ToArray). The Remove method is
        // intentionally not mapped — see the note in Lists.cs.

        // Queue<T> and Stack<T> instance methods are now handled declaratively via
        // MetaSharp/Runtime/Queues.cs and MetaSharp/Runtime/Stacks.cs.

        // LINQ extension methods → lazy Enumerable chain via @meta-sharp/runtime
        if (IsLinqExtensionMethod(containing) && invocation.Expression is MemberAccessExpressionSyntax linqAccess)
        {
            var source = transformer.TransformExpression(linqAccess.Expression);

            // Only wrap with Enumerable.from() if source is not already a LINQ chain
            var wrapped = IsAlreadyLinqChain(source)
                ? source
                : new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Enumerable"), "from"),
                    [source]);

            return name switch
            {
                // Composition (lazy — returns EnumerableBase)
                "Where"             => LinqCall(wrapped, "where", args),
                "Select"            => LinqCall(wrapped, "select", args),
                "SelectMany"        => LinqCall(wrapped, "selectMany", args),
                "OrderBy"           => LinqCall(wrapped, "orderBy", args),
                "OrderByDescending" => LinqCall(wrapped, "orderByDescending", args),
                "ThenBy"            => LinqCall(wrapped, "thenBy", args),
                "ThenByDescending"  => LinqCall(wrapped, "thenByDescending", args),
                "Take"              => LinqCall(wrapped, "take", args),
                "Skip"              => LinqCall(wrapped, "skip", args),
                "Distinct"          => LinqCall(wrapped, "distinct", []),
                "GroupBy"           => LinqCall(wrapped, "groupBy", args),
                "Concat"            => LinqCall(wrapped, "concat", args),
                "TakeWhile"         => LinqCall(wrapped, "takeWhile", args),
                "SkipWhile"         => LinqCall(wrapped, "skipWhile", args),
                "DistinctBy"        => LinqCall(wrapped, "distinctBy", args),
                "Reverse"           => LinqCall(wrapped, "reverse", []),
                "Zip"               => LinqCall(wrapped, "zip", args),
                "Append"            => LinqCall(wrapped, "append", args),
                "Prepend"           => LinqCall(wrapped, "prepend", args),
                "Union"             => LinqCall(wrapped, "union", args),
                "Intersect"         => LinqCall(wrapped, "intersect", args),
                "Except"            => LinqCall(wrapped, "except", args),

                // Terminal (materializes)
                "ToList" or "ToArray" => LinqCall(wrapped, "toArray", []),
                "ToDictionary"      => LinqCall(wrapped, "toMap", args),
                "ToHashSet"         => LinqCall(wrapped, "toSet", []),
                "First"             => LinqCall(wrapped, "first", args),
                "FirstOrDefault"    => LinqCall(wrapped, "firstOrDefault", args),
                "Last"              => LinqCall(wrapped, "last", args),
                "LastOrDefault"     => LinqCall(wrapped, "lastOrDefault", args),
                "Single"            => LinqCall(wrapped, "single", args),
                "SingleOrDefault"   => LinqCall(wrapped, "singleOrDefault", args),
                "Any"               => LinqCall(wrapped, "any", args),
                "All"               => LinqCall(wrapped, "all", args),
                "Count"             => LinqCall(wrapped, "count", args),
                "Sum"               => LinqCall(wrapped, "sum", args),
                "Average"           => LinqCall(wrapped, "average", args),
                "Min"               => LinqCall(wrapped, "min", args),
                "Max"               => LinqCall(wrapped, "max", args),
                "MinBy"             => LinqCall(wrapped, "minBy", args),
                "MaxBy"             => LinqCall(wrapped, "maxBy", args),
                "Contains"          => LinqCall(wrapped, "contains", args),
                "Aggregate"         => LinqCall(wrapped, "aggregate", args),

                _ => null,
            };
        }

        // Dictionary<K,V> and HashSet<T> family instance methods are now handled
        // declaratively via MetaSharp/Runtime/Dictionaries.cs and MetaSharp/Runtime/Sets.cs.
        // TryGetValue stays unmapped because the out-parameter idiom doesn't translate
        // cleanly to JS Map.get — see the note in Dictionaries.cs.

        // Enum.HasFlag(flag) and Enum.Parse<T>(text) are now handled declaratively via
        // MetaSharp/Runtime/Enums.cs (the latter uses the $T0 placeholder for the
        // generic method type-argument name).

        // Console.WriteLine, Guid.NewGuid, Guid.ToString(format), Task.FromResult are now
        // handled declaratively via MetaSharp/Runtime/Console.cs, Guid.cs and Tasks.cs.
        // Guid.ToString uses the WhenArg0StringEquals filter to discriminate "N" from the
        // unfiltered identity fallback.

        return null;
    }

    /// <summary>
    /// Applies a declarative property mapping. Properties never have arguments, so the
    /// result is either a bare identifier (static) or a property access (instance).
    /// Templates are still allowed: <c>JsTemplate = "Temporal.Now.plainDateTimeISO()"</c>
    /// is a perfectly valid mapping for <c>DateTime.Now</c>.
    /// </summary>
    private static TsExpression ApplyPropertyMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver)
    {
        if (mapping.HasTemplate)
            return JsTemplateExpander.Expand(mapping.JsTemplate!, receiver, args: []);

        var name = mapping.JsName!;
        return receiver is not null
            ? new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
    }

    /// <summary>
    /// Applies a declarative method mapping. For the simple-rename form
    /// (<see cref="DeclarativeMappingEntry.JsName"/>), the original receiver is preserved
    /// and the result is <c>receiver.jsName(args)</c> — that's true for both instance
    /// methods (where the receiver is the C# value expression) and for static methods
    /// called via <c>TypeName.Method(args)</c> (where the receiver is the type identifier
    /// rendered verbatim, e.g., <c>Math.round(x)</c>). Static methods whose JS counterpart
    /// has a different identifier (Console → console, Guid.NewGuid → crypto.randomUUID,
    /// etc.) should use a <see cref="DeclarativeMappingEntry.JsTemplate"/> instead of the
    /// rename shorthand.
    ///
    /// For free-standing calls (no member access on the LHS), the receiver is null and
    /// the rename produces a bare <c>jsName(args)</c> call.
    ///
    /// The template form uses <c>$this</c> for the receiver, <c>$0</c>, <c>$1</c>, … for
    /// the arguments (same convention as <see cref="EmitAttribute"/>), and
    /// <c>$T0</c>, <c>$T1</c>, … for the call site's generic method type-argument names.
    /// </summary>
    private static TsExpression ApplyMethodMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args,
        IReadOnlyList<string> typeArgumentNames)
    {
        if (mapping.HasTemplate)
            return JsTemplateExpander.Expand(mapping.JsTemplate!, receiver, args, typeArgumentNames);

        var name = mapping.JsName!;
        var callee = receiver is not null
            ? (TsExpression)new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
        return new TsCallExpression(callee, args);
    }

    /// <summary>
    /// Decides whether a declarative mapping's optional arg-literal filter matches the
    /// call site's transformed arguments. An entry without a filter matches anything;
    /// an entry with <see cref="DeclarativeMappingEntry.WhenArg0StringEquals"/> matches
    /// only when arg 0 is a TS string literal whose value equals the filter.
    /// </summary>
    private static bool MatchesArgFilter(DeclarativeMappingEntry entry, IReadOnlyList<TsExpression> args)
    {
        if (!entry.HasArgFilter) return true;
        if (args.Count < 1) return false;
        return args[0] is TsStringLiteral str && str.Value == entry.WhenArg0StringEquals;
    }

    // ─── Type classification helpers ────────────────────────
    // (IsCollectionType was deleted alongside the hardcoded List/IList/ICollection
    // branches — those are handled declaratively now via MetaSharp/Runtime/Lists.cs.
    // ImmutableList/ImmutableArray support has been temporarily lost in the migration
    // and is tracked as a follow-up.)

    private static bool IsLinqExtensionMethod(string? fullName)
    {
        if (fullName is null) return false;
        return fullName == "System.Linq.Enumerable"
            || fullName.StartsWith("System.Linq.IOrderedEnumerable");
    }

    private static TsCallExpression LinqCall(TsExpression source, string method, List<TsExpression> args) =>
        new(new TsPropertyAccess(source, method), args);

    /// <summary>
    /// Detects if an expression is already the result of a LINQ chain (Enumerable.from() or .where(), etc.)
    /// to avoid double-wrapping.
    /// </summary>
    private static bool IsAlreadyLinqChain(TsExpression expr) => expr switch
    {
        TsCallExpression { Callee: TsPropertyAccess { Object: TsIdentifier { Name: "Enumerable" } } } => true,
        TsCallExpression { Callee: TsPropertyAccess { Property: var p } } when IsLinqMethodName(p) => true,
        _ => false,
    };

    private static bool IsLinqMethodName(string name) => name is
        "where" or "select" or "selectMany" or "orderBy" or "orderByDescending" or "thenBy" or "thenByDescending"
        or "take" or "skip" or "distinct" or "groupBy" or "concat"
        or "takeWhile" or "skipWhile" or "distinctBy" or "reverse"
        or "zip" or "append" or "prepend" or "union" or "intersect" or "except"
        or "toArray" or "toMap" or "toSet"
        or "first" or "firstOrDefault" or "last" or "lastOrDefault"
        or "single" or "singleOrDefault" or "any" or "all"
        or "count" or "sum" or "average" or "min" or "max" or "minBy" or "maxBy"
        or "contains" or "aggregate";
}
