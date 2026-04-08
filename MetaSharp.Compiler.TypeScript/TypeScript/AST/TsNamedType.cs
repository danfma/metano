namespace MetaSharp.TypeScript.AST;

/// <summary>
/// A named TypeScript type reference. <see cref="Origin"/> is non-null when the type
/// comes from a cross-package source — the type mapper attaches it at construction
/// time so the import collector can emit the right <c>import { Name } from "..."</c>
/// without re-resolving by name (avoiding ambiguity when two assemblies share simple
/// type names).
/// </summary>
public sealed record TsNamedType(
    string Name,
    IReadOnlyList<TsType>? TypeArguments = null,
    TsTypeOrigin? Origin = null) : TsType;
