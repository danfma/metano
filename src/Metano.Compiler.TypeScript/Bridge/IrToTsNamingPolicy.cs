using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Transformation;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// TypeScript-specific naming decisions applied when lowering IR to TS AST.
/// Keeps the IR target-agnostic (PascalCase, no reserved-word escaping) while
/// centralizing JS/TS casing rules in one place.
/// </summary>
public static class IrToTsNamingPolicy
{
    /// <summary>
    /// The name of the hash-code contract on the TypeScript side: records
    /// synthesize it, and the runtime (<c>valueHashCode</c>, <c>HashSet</c>)
    /// calls it. A C# <c>GetHashCode</c> override is emitted under it, so the
    /// runtime picks the user's hash up.
    /// </summary>
    public const string HashCodeMethodName = "hashCode";

    /// <summary>
    /// Resolves the emitted name of a declared data member (an interface or
    /// class property or field): honors <c>[Name("x")]</c> overrides when
    /// present, otherwise applies camelCase with reserved-word escaping (see
    /// <see cref="ToDataMemberName"/>). Methods use <see cref="ToMethodName"/>.
    /// </summary>
    public static string ToDeclaredDataMemberName(
        string irName,
        IReadOnlyList<IrAttribute>? attributes
    ) => ToDataMemberName(irName, FindNameOverride(attributes));

    /// <summary>
    /// The emitted name of a data member (field or property): its
    /// target-resolved <c>[Name]</c> override, otherwise the camelCased C#
    /// name with JS reserved words escaped — the policy the class emitter
    /// declares the member with (a promoted constructor parameter cannot be
    /// named <c>default</c>, so the property it declares is
    /// <c>default_</c>), applied to every data member so the rule does not
    /// depend on how the member is declared. Every read of the member —
    /// ordinary and optional-chain member access, record equality,
    /// <c>with</c> keys, pattern member reads, JSON specs — goes through here
    /// so it names what was declared. Methods and events are never escaped
    /// (see <see cref="ToMethodName"/>).
    /// </summary>
    public static string ToDataMemberName(string irName, string? emittedName) =>
        emittedName ?? TypeScriptNaming.ToCamelCase(irName);

    /// <summary>
    /// Resolves the parameter name: camelCase with reserved-word escaping
    /// (<c>delete</c> → <c>delete_</c>) since parameters are used as bare identifiers.
    /// </summary>
    public static string ToParameterName(string irName) => TypeScriptNaming.ToCamelCase(irName);

    /// <summary>
    /// Resolves the emitted type name from the IR's <see cref="IrAttribute"/>
    /// entries (which already carry <c>[Name]</c> overrides). Mirrors the
    /// per-target resolution the frontend now ships via
    /// <see cref="IrCompilation.TypeNamesBySymbol"/> for Roslyn-keyed paths.
    /// </summary>
    public static string ToTypeName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? irName;
    }

    /// <summary>
    /// For enum members in string enums, the emitted string value honors <c>[Name]</c>
    /// when present and otherwise uses the original member name in its C# casing.
    /// </summary>
    public static string ToEnumMemberName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? irName;
    }

    /// <summary>
    /// Top-level function name: honors <c>[Name]</c> verbatim when present
    /// (so users can opt into a reserved word or a wire shape unchanged) and
    /// otherwise camelCases the IR name.
    /// </summary>
    public static string ToFunctionName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? TypeScriptNaming.ToCamelCase(irName);
    }

    /// <summary>
    /// Class method name: honors <c>[Name]</c> verbatim when present,
    /// otherwise camelCases the IR name <em>without</em> reserved-word
    /// escaping (a method called <c>Delete</c> becomes <c>delete()</c>, not
    /// <c>delete_()</c>) — JS lets methods use any identifier as a member
    /// access. Both this declaration helper and the call-site bridge route
    /// through the same lowering so the two halves stay in agreement.
    /// </summary>
    public static string ToMethodName(string irName, IReadOnlyList<IrAttribute>? attributes)
    {
        var overrideName = FindNameOverride(attributes);
        return overrideName ?? TypeScriptNaming.ToCamelCaseMember(irName);
    }

    /// <summary>
    /// The name a class method is declared with: <see cref="HashCodeMethodName"/>
    /// for an override of <c>object.GetHashCode</c>,
    /// <see cref="ToMethodName(string, IReadOnlyList{IrAttribute})"/> otherwise.
    /// </summary>
    public static string ToMethodName(IrMethodDeclaration method) =>
        IrObjectContract.IsHashCodeOverride(method)
            ? HashCodeMethodName
            : ToMethodName(method.Name, method.Attributes);

    /// <summary>
    /// Reads the <c>[Name]</c> override, preferring the TypeScript-specific
    /// <c>TypeScript = "…"</c> named argument when present and falling back to
    /// the positional <c>Value</c>. Mirrors the Dart naming policy's shape so
    /// the same attribute can carry per-target renames at once.
    /// </summary>
    public static string? FindNameOverride(IReadOnlyList<IrAttribute>? attributes)
    {
        if (attributes is null)
            return null;

        var targetKey = nameof(TargetLanguage.TypeScript);
        foreach (var attr in attributes)
        {
            if (attr.Name != "Name" || attr.Arguments is null)
                continue;
            if (
                attr.Arguments.TryGetValue(targetKey, out var perTarget)
                && perTarget is string perTargetS
            )
                return perTargetS;
            if (attr.Arguments.TryGetValue("Value", out var value) && value is string s)
                return s;
        }

        return null;
    }
}
