using Metano.Compiler.IR;
using Metano.Transformation;
using Metano.TypeScript.AST;

namespace Metano.TypeScript.Bridge;

/// <summary>
/// Small helpers shared by the function-shaped bridges
/// (<see cref="IrToTsModuleBridge"/>, <see cref="IrToTsInlineWrapperBridge"/>) that
/// turn an <see cref="IrMethodDeclaration"/> body into the TS statement list a
/// <see cref="TsFunction"/> can consume.
/// </summary>
internal static class IrToTsBodyHelpers
{
    /// <summary>
    /// Lowers <paramref name="body"/> via <see cref="IrToTsStatementBridge"/>, or
    /// emits a single <c>throw new Error("Not implemented: …")</c> statement when
    /// the IR carries no body. The fallback exists so the emitted file still
    /// compiles when the extractor couldn't reach a method's source (referenced
    /// assemblies, partial syntax, etc.) — the runtime error makes the gap
    /// visible the first time the unimplemented function is called.
    /// </summary>
    public static List<TsStatement> LowerOrNotImplemented(
        IReadOnlyList<IrStatement>? body,
        string nameForError,
        DeclarativeMappingRegistry? bclRegistry
    ) =>
        body is null
            ? [NotImplementedThrow(nameForError)]
            : IrToTsStatementBridge.MapBody(body, bclRegistry).ToList();

    private static TsStatement NotImplementedThrow(string name) =>
        new TsThrowStatement(
            new TsNewExpression(
                new TsIdentifier("Error"),
                [new TsStringLiteral($"Not implemented: {name}")]
            )
        );
}
