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

        // string instance properties
        if (containing == "string" && symbol.Name == "Length")
            return new TsPropertyAccess(obj, "length");

        // List<T>.Count, Queue<T>.Count, Stack<T>.Count → .length
        if (symbol.Name == "Count" && (IsCollectionType(containing) || IsQueueType(containing) || IsStackType(containing)))
            return new TsPropertyAccess(obj, "length");

        // Dictionary<K,V>.Count, HashSet<T>.Count → .size
        if (symbol.Name == "Count" && IsMapOrSetType(containing))
            return new TsPropertyAccess(obj, "size");

        // Task.CompletedTask → Promise.resolve()
        if (containing == "System.Threading.Tasks.Task" && symbol.Name == "CompletedTask")
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Promise"), "resolve"),
                []);

        // DateOnly.DayNumber → dayNumber(date) helper from runtime
        if (containing == "System.DateOnly" && symbol.Name == "DayNumber")
            return new TsCallExpression(
                new TsIdentifier("dayNumber"),
                [obj]);

        // DateTimeOffset.UtcNow → Temporal.Now.zonedDateTimeISO()
        if (containing == "System.DateTimeOffset" && symbol.Name == "UtcNow")
            return new TsCallExpression(
                new TsPropertyAccess(
                    new TsPropertyAccess(new TsIdentifier("Temporal"), "Now"),
                    "zonedDateTimeISO"),
                []);

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

        // 1. Declarative method mapping wins over any hardcoded fallback below.
        if (transformer.DeclarativeMappings.TryGetMethod(method.ContainingType, name, out var methodMapping))
        {
            // Resolve the receiver: for instance methods, the syntax is
            // `<expr>.Method(args)`, so the receiver expression is the left side of the
            // member access. For static methods (and the rarer free-standing form), there
            // is no receiver to substitute.
            TsExpression? receiver = null;
            if (!method.IsStatic && invocation.Expression is MemberAccessExpressionSyntax declarativeReceiverAccess)
                receiver = transformer.TransformExpression(declarativeReceiverAccess.Expression);

            return ApplyMethodMapping(methodMapping, receiver, args);
        }

        var containing = method.ContainingType.ToDisplayString();

        // System.Math static methods
        if (containing == "System.Math")
        {
            var jsMethod = name switch
            {
                "Round" => "round",
                "Floor" => "floor",
                "Ceiling" => "ceil",
                "Ceil" => "ceil",
                "Abs" => "abs",
                "Min" => "min",
                "Max" => "max",
                "Sqrt" => "sqrt",
                "Pow" => "pow",
                _ => null,
            };

            if (jsMethod is not null)
                return new TsCallExpression(
                    new TsPropertyAccess(new TsIdentifier("Math"), jsMethod),
                    args
                );
        }

        // string instance methods
        if (
            containing == "string"
            && invocation.Expression is MemberAccessExpressionSyntax memberAccess
        )
        {
            var obj = transformer.TransformExpression(memberAccess.Expression);
            var jsMethod = name switch
            {
                "ToUpper" or "ToUpperInvariant" => "toUpperCase",
                "ToLower" or "ToLowerInvariant" => "toLowerCase",
                "Contains" => "includes",
                "StartsWith" => "startsWith",
                "EndsWith" => "endsWith",
                "Trim" => "trim",
                "TrimStart" => "trimStart",
                "TrimEnd" => "trimEnd",
                "Replace" => "replace",
                "Substring" => "substring",
                "IndexOf" => "indexOf",
                _ => null,
            };

            if (jsMethod is not null)
                return new TsCallExpression(new TsPropertyAccess(obj, jsMethod), args);
        }

        // List<T> / ICollection<T> instance methods
        if (IsCollectionType(containing) && invocation.Expression is MemberAccessExpressionSyntax listAccess)
        {
            var obj = transformer.TransformExpression(listAccess.Expression);
            var jsMethod = name switch
            {
                "Add" => "push",
                "Contains" => "includes",
                "IndexOf" => "indexOf",
                "Remove" => null, // complex — needs splice pattern, skip for now
                "Clear" => null,
                "Insert" => "splice", // approximate
                "Reverse" => "reverse",
                "Sort" => "sort",
                "ToArray" => "slice", // creates a copy
                _ => null,
            };

            if (jsMethod is not null)
                return new TsCallExpression(new TsPropertyAccess(obj, jsMethod), args);

            // Clear() → .length = 0
            if (name == "Clear")
                return new TsBinaryExpression(
                    new TsPropertyAccess(obj, "length"),
                    "=",
                    new TsLiteral("0"));
        }

        // Queue<T> instance methods
        if (IsQueueType(containing) && invocation.Expression is MemberAccessExpressionSyntax queueAccess)
        {
            var obj = transformer.TransformExpression(queueAccess.Expression);
            return name switch
            {
                "Enqueue" => new TsCallExpression(new TsPropertyAccess(obj, "push"), args),
                "Dequeue" => new TsCallExpression(new TsPropertyAccess(obj, "shift"), []),
                "Peek" => new TsElementAccess(obj, new TsLiteral("0")),
                "Contains" => new TsCallExpression(new TsPropertyAccess(obj, "includes"), args),
                "Clear" => new TsBinaryExpression(new TsPropertyAccess(obj, "length"), "=", new TsLiteral("0")),
                _ => null,
            };
        }

        // Stack<T> instance methods
        if (IsStackType(containing) && invocation.Expression is MemberAccessExpressionSyntax stackAccess)
        {
            var obj = transformer.TransformExpression(stackAccess.Expression);
            return name switch
            {
                "Push" => new TsCallExpression(new TsPropertyAccess(obj, "push"), args),
                "Pop" => new TsCallExpression(new TsPropertyAccess(obj, "pop"), []),
                "Peek" => new TsElementAccess(obj,
                    new TsBinaryExpression(new TsPropertyAccess(obj, "length"), "-", new TsLiteral("1"))),
                "Contains" => new TsCallExpression(new TsPropertyAccess(obj, "includes"), args),
                "Clear" => new TsBinaryExpression(new TsPropertyAccess(obj, "length"), "=", new TsLiteral("0")),
                _ => null,
            };
        }

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

        // Dictionary<K,V> instance methods
        if (IsMapOrSetType(containing) && invocation.Expression is MemberAccessExpressionSyntax dictAccess)
        {
            var obj = transformer.TransformExpression(dictAccess.Expression);
            return name switch
            {
                "ContainsKey" => new TsCallExpression(new TsPropertyAccess(obj, "has"), args),
                "TryGetValue" => null, // complex pattern, skip for now
                "Add" when args.Count == 2 => new TsCallExpression(new TsPropertyAccess(obj, "set"), args),
                "Add" when args.Count == 1 => new TsCallExpression(new TsPropertyAccess(obj, "add"), args),
                "Remove" => new TsCallExpression(new TsPropertyAccess(obj, "delete"), args),
                "Clear" => new TsCallExpression(new TsPropertyAccess(obj, "clear"), []),
                "Contains" => new TsCallExpression(new TsPropertyAccess(obj, "has"), args),
                _ => null,
            };
        }

        // Enum.HasFlag(flag) → (value & flag) === flag
        if (name == "HasFlag"
            && (containing == "System.Enum" || method.ContainingType is { TypeKind: TypeKind.Enum })
            && invocation.Expression is MemberAccessExpressionSyntax hasFlagAccess
            && args.Count == 1)
        {
            var enumValue = transformer.TransformExpression(hasFlagAccess.Expression);
            var flag = args[0];
            return new TsBinaryExpression(
                new TsParenthesized(new TsBinaryExpression(enumValue, "&", flag)),
                "===",
                flag);
        }

        // Enum.Parse<T>(text) → T[text as keyof typeof T]  (for numeric enums)
        if (containing == "System.Enum" && name == "Parse"
            && method.TypeArguments.Length == 1
            && args.Count >= 1)
        {
            var enumType = method.TypeArguments[0];
            var enumName = enumType.Name;
            var textArg = args[0];
            // For numeric enums: EnumName[text as keyof typeof EnumName]
            return new TsElementAccess(
                new TsIdentifier(enumName),
                new TsCastExpression(textArg, new TsNamedType($"keyof typeof {enumName}")));
        }

        // Console.WriteLine → console.log
        if (containing == "System.Console" && name == "WriteLine")
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("console"), "log"),
                args
            );

        // Guid.ToString("N") → .replace(/-/g, ""), Guid.ToString() → (identity, already string)
        if (containing == "System.Guid" && name == "ToString"
            && invocation.Expression is MemberAccessExpressionSyntax guidToStringAccess)
        {
            var obj = transformer.TransformExpression(guidToStringAccess.Expression);
            // Check if format arg is "N" (no hyphens)
            if (args.Count == 1 && args[0] is TsStringLiteral { Value: "N" })
                return new TsCallExpression(
                    new TsPropertyAccess(obj, "replace"),
                    [new TsLiteral("/-/g"), new TsStringLiteral("")]);
            // Default: Guid is already a string in JS, .toString() is identity
            return obj;
        }

        // Guid.NewGuid() → crypto.randomUUID()
        if (containing == "System.Guid" && name == "NewGuid")
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("crypto"), "randomUUID"),
                []);

        // Task.FromResult(x) → Promise.resolve(x), Task.CompletedTask → Promise.resolve()
        if (containing is "System.Threading.Tasks.Task" && name == "FromResult")
            return new TsCallExpression(
                new TsPropertyAccess(new TsIdentifier("Promise"), "resolve"),
                args
            );

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
    /// Applies a declarative method mapping. The simple-rename form always emits a call
    /// expression — instance methods become <c>receiver.jsName(args)</c>, static methods
    /// become <c>jsName(args)</c>. Templates take the receiver as <c>$this</c> and the
    /// arguments as <c>$0</c>, <c>$1</c>, …
    /// </summary>
    private static TsExpression ApplyMethodMapping(
        DeclarativeMappingEntry mapping,
        TsExpression? receiver,
        IReadOnlyList<TsExpression> args)
    {
        if (mapping.HasTemplate)
            return JsTemplateExpander.Expand(mapping.JsTemplate!, receiver, args);

        var name = mapping.JsName!;
        var callee = receiver is not null
            ? (TsExpression)new TsPropertyAccess(receiver, name)
            : new TsIdentifier(name);
        return new TsCallExpression(callee, args);
    }

    // ─── Type classification helpers ────────────────────────

    private static bool IsCollectionType(string? fullName)
    {
        if (fullName is null) return false;
        return fullName.StartsWith("System.Collections.Generic.List")
            || fullName.StartsWith("System.Collections.Generic.IList")
            || fullName.StartsWith("System.Collections.Generic.ICollection")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyList")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyCollection")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableList")
            || fullName.StartsWith("System.Collections.Immutable.ImmutableArray");
    }

    private static bool IsMapOrSetType(string? fullName)
    {
        if (fullName is null) return false;
        return fullName.StartsWith("System.Collections.Generic.Dictionary")
            || fullName.StartsWith("System.Collections.Generic.IDictionary")
            || fullName.StartsWith("System.Collections.Generic.IReadOnlyDictionary")
            || fullName.StartsWith("System.Collections.Generic.HashSet")
            || fullName.StartsWith("System.Collections.Generic.ISet")
            || fullName.StartsWith("System.Collections.Generic.SortedSet");
    }

    private static bool IsQueueType(string? fullName)
    {
        if (fullName is null) return false;
        return fullName.StartsWith("System.Collections.Generic.Queue");
    }

    private static bool IsStackType(string? fullName)
    {
        if (fullName is null) return false;
        return fullName.StartsWith("System.Collections.Generic.Stack");
    }

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
