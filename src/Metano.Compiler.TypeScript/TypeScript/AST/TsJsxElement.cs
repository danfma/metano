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
/// <param name="Origin">The cross-package origin of a component tag sourced from a
/// referenced <c>[EmitPackage]</c> assembly. Non-null only for a component whose
/// declaring type lives in another package; the collector routes such a tag through
/// the cross-package import channel (and records the package dependency) instead of
/// synthesizing a wrong intra-project import. Null for intra-project components
/// (resolved from the local barrel) and for native/external-import elements.</param>
public sealed record TsJsxElement(
    string TagName,
    IReadOnlyList<TsJsxAttribute> Attributes,
    IReadOnlyList<TsJsxChild> Children,
    bool SelfClosing = false,
    IReadOnlyList<IrExternalImport>? ExternalImports = null,
    TsTypeOrigin? Origin = null
) : TsExpression;
