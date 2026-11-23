using System.Collections;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.RegularExpressions;
using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// The identifiers a lowered TypeScript fragment may read: every
/// <see cref="TsIdentifier"/> anywhere in its tree, plus every whole word of
/// the raw code it carries as text (<see cref="TsLiteral"/>,
/// <see cref="TsRawStatement"/>, <see cref="TsTemplate"/>,
/// <see cref="TsInlineCodeText"/>). A member name after a dot or a string
/// literal's value is not an identifier. The answer errs toward "read" (a
/// shadowing declaration counts too), so a caller that drops what is not
/// mentioned never drops a binding the fragment needs.
/// </summary>
internal sealed class TsIdentifierReads
{
    private static readonly ConcurrentDictionary<Type, PropertyInfo[]> ChildrenByType = new();

    private readonly HashSet<string> _identifiers = new(StringComparer.Ordinal);
    private readonly List<string> _rawCode = [];

    public TsIdentifierReads(IEnumerable<TsStatement> fragment)
    {
        foreach (var statement in fragment)
            Visit(statement);
    }

    /// <summary>Whether the fragment mentions <paramref name="name"/> as an identifier.</summary>
    public bool Mentions(string name) =>
        _identifiers.Contains(name)
        || _rawCode.Exists(code =>
            Regex.IsMatch(code, $@"(?<![\w$]){Regex.Escape(name)}(?![\w$])")
        );

    private void Visit(object? node)
    {
        switch (node)
        {
            case null or TsStringLiteral or TsType:
                return;
            case TsIdentifier identifier:
                _identifiers.Add(identifier.Name);

                return;
            case string:
                return;
            case IEnumerable children:
                foreach (var child in children)
                    Visit(child);

                return;
        }

        if (RawCodeOf(node) is { } code)
            _rawCode.Add(code);

        if (node.GetType().Namespace != typeof(TsIdentifier).Namespace)
            return;

        foreach (var property in ChildrenOf(node.GetType()))
            Visit(property.GetValue(node));
    }

    private static string? RawCodeOf(object node) =>
        node switch
        {
            TsLiteral literal => literal.Raw,
            TsRawStatement raw => raw.Text,
            TsTemplate template => template.Template,
            TsInlineCodeText text => text.Text,
            _ => null,
        };

    /// <summary>The properties of an AST node that can hold further nodes.</summary>
    private static PropertyInfo[] ChildrenOf(Type type) =>
        ChildrenByType.GetOrAdd(type, nodeType =>
            nodeType
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(property =>
                    property.GetIndexParameters().Length == 0
                    && property.Name != "EqualityContract"
                    && !property.PropertyType.IsValueType
                    && property.PropertyType != typeof(string)
                )
                .ToArray()
        );
}
