namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// The <c>unique symbol</c> type of a <c>static readonly</c> field initialized
/// with <c>Symbol(...)</c>: a constructor overload's selector (spec 013 FR-008).
/// Two <c>unique symbol</c> types are never assignable to each other.
/// </summary>
public sealed record TsUniqueSymbolType : TsType;
