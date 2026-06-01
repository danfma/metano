using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// A JSX element expression: <c>&lt;tag attr="..." attr2={expr}&gt;children&lt;/tag&gt;</c>
/// or the self-closing form <c>&lt;tag ... /&gt;</c>. A lowercase
/// <see cref="TagName"/> is an intrinsic element; a capitalized one is a
/// component (the JSX convention is preserved by the emitted casing).
/// </summary>
/// <param name="ExternalImports">External JS modules the tag itself resolves to
/// (e.g. a SolidJS helper element such as <c>&lt;For&gt;</c> synthesized from a
/// <c>Solid.For(...)</c> call → <c>import { For } from "solid-js"</c>). The
/// capitalized tag name is otherwise treated as a transpilable component and
/// would resolve to a wrong intra-project import; threading the external import
/// here lets the collector resolve it to the right module (mirrors
/// <see cref="TsTemplate.ExternalImports"/>). Null for plain components, whose
/// import is resolved from their declaring type.</param>
public sealed record TsJsxElement(
    string TagName,
    IReadOnlyList<TsJsxAttribute> Attributes,
    IReadOnlyList<TsJsxChild> Children,
    bool SelfClosing = false,
    IReadOnlyList<IrExternalImport>? ExternalImports = null
) : TsExpression;
