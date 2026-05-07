namespace Metano.Compiler.TypeScript.AST;

/// <summary>
/// TypeScript inline object type: <c>{ p1: T1; p2?: T2 }</c>. Carries field
/// shape as structured nodes so the import collector and the printer share
/// the same source of truth — fields render through the regular type-printing
/// path and nested type references stay visible to the collector.
/// </summary>
public sealed record TsObjectType(IReadOnlyList<TsObjectTypeField> Fields) : TsType;

public sealed record TsObjectTypeField(string Name, TsType Type, bool Optional = false);
