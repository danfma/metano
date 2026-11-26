using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Whether lowered TypeScript reads a name inside a function it creates: an
/// arrow or function expression, or a template or inline code whose text
/// spells one (<c>=&gt;</c>, <c>function</c>), whose holes are then read
/// inside it. A binding such a function reads must keep a scope of its own per
/// evaluation, so it cannot be a temporary the enclosing statement list
/// reassigns. Types are not read: a type query names a value only statically.
/// The walk visits every AST record through its properties, so a node kind
/// added later is covered without a change here.
/// </summary>
internal static class TsClosureReads
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildPropertiesByType =
        new();

    private static readonly string AstNamespace = typeof(TsExpression).Namespace!;

    /// <summary>Whether a function created in <paramref name="root"/> reads one of <paramref name="names"/>.</summary>
    public static bool AnyIn(TsExpression root, IReadOnlySet<string> names) =>
        names.Count > 0 && Reads(root, insideFunction: false, names);

    private static bool Reads(object node, bool insideFunction, IReadOnlySet<string> names)
    {
        if (node is TsIdentifier identifier)
            return insideFunction && names.Contains(identifier.Name);

        if (node is TsType)
            return false;

        var inside = insideFunction || CreatesFunction(node);

        return ChildrenOf(node).Any(child => Reads(child, inside, names));
    }

    private static bool CreatesFunction(object node) =>
        node switch
        {
            TsArrowFunction or TsNamedFunctionExpression => true,
            TsTemplate template => SpellsFunction(template.Template),
            TsInlineCode code => code
                .Parts.OfType<TsInlineCodeText>()
                .Any(text => SpellsFunction(text.Text)),
            _ => false,
        };

    private static bool SpellsFunction(string text) =>
        text.Contains("=>", StringComparison.Ordinal)
        || text.Contains("function", StringComparison.Ordinal);

    private static IEnumerable<object> ChildrenOf(object node) =>
        ChildPropertiesByType
            .GetOrAdd(node.GetType(), ChildPropertiesOf)
            .Select(property => property.GetValue(node))
            .SelectMany(Nodes);

    private static IEnumerable<object> Nodes(object? value) =>
        value switch
        {
            null or string => [],
            IEnumerable items => items.Cast<object?>().SelectMany(Nodes),
            _ when value.GetType().Namespace == AstNamespace => [value],
            _ => [],
        };

    private static PropertyInfo[] ChildPropertiesOf(Type type) =>
        [
            .. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property => property.GetIndexParameters().Length == 0),
        ];
}
