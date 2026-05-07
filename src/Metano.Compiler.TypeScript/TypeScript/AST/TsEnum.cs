namespace Metano.Compiler.TypeScript.AST;

public sealed record TsEnum(string Name, IReadOnlyList<TsEnumMember> Members, bool Exported = true)
    : TsTopLevel;
