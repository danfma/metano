using Metano.Annotations;
using Metano.Annotations.TypeScript;

namespace SampleRuntimeTypes.Unions;

/// <summary>
/// A strict union (FR-029): <c>isToken</c> lives in <c>token.guard.ts</c>,
/// which imports the variants' guards; <c>token.ts</c> imports no variant, so
/// importing it first runs no variant module.
/// </summary>
[StringEnum, EmitInFile("token")]
public enum TokenKind
{
    NumberToken,
    TextToken,
}

[GenerateGuard, StrictUnionGuard]
[Discriminator("Kind")]
public abstract record Token(TokenKind Kind);

[GenerateGuard]
public sealed record NumberToken(TokenKind Kind, double Value) : Token(Kind);

[GenerateGuard]
public sealed record TextToken(TokenKind Kind, string Text) : Token(Kind);
