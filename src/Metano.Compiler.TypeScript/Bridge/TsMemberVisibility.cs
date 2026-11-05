using Metano.Compiler.TypeScript.AST;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// How a TypeScript declaration renders a C# visibility: its access
/// modifier, and the JSDoc printed ahead of it (the <c>/** @internal *&#47;</c>
/// tag, or <c>null</c>). <see cref="IrToTsVisibilityMapper.Map"/> builds it.
/// </summary>
public readonly record struct TsMemberVisibility(TsAccessibility Accessibility, TsJSDoc? JSDoc);
