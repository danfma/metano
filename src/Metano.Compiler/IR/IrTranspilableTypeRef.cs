namespace Metano.Compiler.IR;

/// <summary>
/// Projection of a transpilable type the backend may look up by identifier.
/// Carries every emit-time fact the downstream import collector and guard
/// builder need — origin key, target-facing name, namespace, on-disk file
/// name (kebab-cased, honoring <c>[EmitInFile]</c>), and the string-enum
/// flag that forces a value import. Lets the backend resolve a bare TS
/// identifier (walked out of the generated AST) to emit metadata without
/// going back to the Roslyn symbol table.
/// </summary>
/// <param name="Key">Cross-assembly origin key
/// (<see cref="SymbolHelper.GetCrossAssemblyOriginKey"/>). Used to probe
/// <see cref="IrCompilation.GuardableTypeKeys"/> when resolving
/// <c>is{Name}</c> guard imports.</param>
/// <param name="TsName">Target-facing name — already resolves
/// <c>[Name(target, …)]</c> overrides. For top-level transpilable keys
/// this matches the value <see cref="IrCompilation.TypeNamesBySymbol"/>
/// would return for <see cref="Key"/>.</param>
/// <param name="Namespace">Declaring namespace, or the empty string when
/// the type lives in the global namespace. Equivalent to
/// <c>PathNaming.GetNamespace</c> on the source symbol.</param>
/// <param name="FileName">Kebab-cased file name (no extension) under which
/// the type is emitted. Honors <c>[EmitInFile("name")]</c> when present;
/// otherwise derived from <see cref="TsName"/>. The import collector pairs
/// this with <see cref="Namespace"/> to elide self-imports for types
/// co-located in the same file.</param>
/// <param name="IsStringEnum">Whether the type carries <c>[StringEnum]</c>
/// — string-enum targets emit as runtime const objects, so they must be
/// imported as values even when referenced only in type position.</param>
public sealed record IrTranspilableTypeRef(
    string Key,
    string TsName,
    string Namespace,
    string FileName,
    bool IsStringEnum
);
