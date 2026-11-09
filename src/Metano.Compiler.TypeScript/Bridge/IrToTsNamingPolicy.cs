using Metano.Annotations;
using Metano.Compiler.Analysis;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;
using Metano.Compiler.Mappings;
using Metano.Compiler.TypeScript.Transformation;
using Microsoft.CodeAnalysis;

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
    /// (<c>delete</c> → <c>delete_</c>) since parameters are used as bare
    /// identifiers. A parameter is a local binding, so this is
    /// <see cref="ToLocalName"/> — one decision for declarations and reads.
    /// </summary>
    public static string ToParameterName(string irName) => ToLocalName(irName);

    /// <summary>
    /// The TypeScript local-naming decision as the core extractors receive
    /// it (#398): <see cref="ToLocalName"/> itself, defined once here so the
    /// transform context, the transformer and every bridge hand the same
    /// function to the extractors.
    /// </summary>
    public static LocalNaming LocalNaming { get; } = new(ToLocalName);

    /// <summary>
    /// The emitted name of a local binding — a local (a <c>const</c> included), a
    /// parameter, a foreach or catch variable, a pattern designator, an out-var
    /// or a deconstruction element: camelCase with reserved-word escaping
    /// (<c>@default</c> → <c>default_</c>). The statement bridge's declaration
    /// sites, the expression bridge's <c>IrIdentifier</c> reads and the pattern
    /// binding emissions all route through here so every half names the same
    /// symbol whatever the C# casing was (#390).
    /// </summary>
    public static string ToLocalName(string irName) =>
        TypeScriptNaming.EscapeIfReserved(TypeScriptNaming.ToCamelCase(irName));

    /// <summary>
    /// The emitted name of a module-scope compile-time constant — a C#
    /// <c>const</c> field of a containerless static class: the <c>[Name]</c>
    /// override verbatim, otherwise the C# name (module scope makes it the
    /// module's public shape, and C# spells constants in PascalCase) with JS
    /// reserved words escaped, since a top-level binding cannot carry one (#390).
    /// </summary>
    public static string ToModuleConstantName(string irName, string? emittedName) =>
        emittedName ?? TypeScriptNaming.EscapeIfReserved(irName);

    /// <summary>
    /// The emitted name of a module data variable — a non-<c>const</c> static
    /// field or property of a containerless static class declared as a
    /// top-level variable: the <c>[Name]</c> override verbatim, otherwise the
    /// data-member rule (camelCase, reserved words escaped — a top-level
    /// binding is a bare identifier). Compile-time constants take
    /// <see cref="ToModuleConstantName"/> instead (#390).
    /// </summary>
    public static string ToModuleDataVariableName(string irName, string? emittedName) =>
        emittedName ?? TypeScriptNaming.EscapeIfReserved(TypeScriptNaming.ToCamelCase(irName));

    /// <summary>
    /// The emitted name of <paramref name="variable"/> as a module variable,
    /// from the facts the IR node carries: a C# compile-time constant keeps
    /// its C# name (<see cref="ToModuleConstantName"/>), anything else takes
    /// the member rule (<see cref="ToModuleDataVariableName"/>) (#390). The
    /// module bridge's declaration and the collision report both resolve
    /// through here so they name what is declared.
    /// </summary>
    public static string ToDeclaredModuleVariableName(IrMemberDeclaration variable) =>
        ToDeclaredModuleVariableName(
            variable.Name,
            FindNameOverride(variable.Attributes),
            variable is IrFieldDeclaration { IsCompileTimeConstant: true }
        );

    /// <summary>
    /// One decision for a module variable's emitted name over the facts each
    /// caller computes from its own source (an IR node here, a Roslyn symbol
    /// in the shadow validator): the <c>[Name]</c> override wins verbatim, a
    /// C# compile-time constant keeps its C# name, anything else takes the
    /// member rule (#390).
    /// </summary>
    public static string ToDeclaredModuleVariableName(
        string name,
        string? nameOverride,
        bool isCompileTimeConstant
    ) =>
        isCompileTimeConstant
            ? ToModuleConstantName(name, nameOverride)
            : ToModuleDataVariableName(name, nameOverride);

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
    /// The name a module function declares: a dispatched group's dispatcher
    /// keeps the camelCased C# name, which its fast paths extend — a
    /// <c>[Name]</c> on one of its overloads pins that overload's suffix
    /// (spec 013 FR-005) —; a single function takes
    /// <see cref="ToFunctionName(string, IReadOnlyList{IrAttribute})"/>.
    /// </summary>
    public static string ToFunctionName(IrModuleFunction function) =>
        ToGroupFunctionName(function.Name, function.Attributes, function.OverloadName);

    /// <summary>
    /// <see cref="ToFunctionName(IrModuleFunction)"/> for a static method a
    /// companion namespace declares as a function.
    /// </summary>
    public static string ToFunctionName(IrMethodDeclaration method) =>
        ToGroupFunctionName(method.Name, method.Attributes, method.OverloadName);

    /// <summary>
    /// The name a method is declared and called by as a function — a module
    /// function, an extension helper, a <c>[NoContainer]</c> export, a
    /// branded companion function — read from its symbol: its
    /// <see cref="OverloadEmittedName"/>, else its camelCased C# name.
    /// </summary>
    public static string ToFunctionName(IMethodSymbol method) =>
        OverloadEmittedName.NameOverrideOf(method, TargetLanguage.TypeScript)
        ?? TypeScriptNaming.ToCamelCase(method.Name);

    /// <summary>
    /// The <c>[Name]</c> that renames a declaration: none for an overload of
    /// a dispatched group (<paramref name="overloadName"/> set), whose
    /// <c>[Name]</c> pins its suffix while the dispatcher keeps the C# name
    /// (spec 013 FR-005) — the IR side of <see cref="OverloadEmittedName"/>.
    /// </summary>
    public static string? FindMemberNameOverride(
        IReadOnlyList<IrAttribute>? attributes,
        IrOverloadName? overloadName
    ) => overloadName is null ? FindNameOverride(attributes) : null;

    private static string ToGroupFunctionName(
        string name,
        IReadOnlyList<IrAttribute>? attributes,
        IrOverloadName? overloadName
    ) => FindMemberNameOverride(attributes, overloadName) ?? TypeScriptNaming.ToCamelCase(name);

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
            if (attr.Name != IrAttribute.NameOverrideAttributeName || attr.Arguments is null)
                continue;

            if (
                attr.Arguments.TryGetValue(targetKey, out var perTarget)
                && perTarget is string perTargetS
            )
                return perTargetS;

            if (
                attr.Arguments.TryGetValue(IrAttribute.NameOverrideValueArgument, out var value)
                && value is string s
            )
                return s;
        }

        return null;
    }
}
