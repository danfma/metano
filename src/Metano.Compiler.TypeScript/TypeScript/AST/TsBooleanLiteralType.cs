namespace Metano.Compiler.TypeScript.AST;

/// <summary>The literal type <c>true</c> or <c>false</c>.</summary>
public sealed record TsBooleanLiteralType(bool Value) : TsType;
