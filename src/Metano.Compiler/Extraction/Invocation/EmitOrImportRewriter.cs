using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction.Invocation;

/// <summary>
/// Lowers a call site whose target method carries one of the two
/// template-bearing attributes:
/// <list type="bullet">
///   <item><c>[Emit("…$0…")]</c> — the template author owns every
///   placeholder; we substitute argument positions and thread any
///   coexisting <c>[Import]</c> metadata into
///   <see cref="IrTemplateExpression.ExternalImports"/>.</item>
///   <item><c>[Import("name", "from")]</c> only — synthesise a
///   facade template <c>name($0, $1, …)</c> with the import baked
///   in. Static methods drop the receiver; instance methods promote
///   it to the leading positional slot (the only shape
///   <c>name(receiver, …)</c> has a sound mapping for).</item>
/// </list>
/// Precedence is <c>[Emit]</c> then <c>[Import]</c>: a method that
/// carries both lowers through the emit branch and threads the
/// import as an external dependency. <c>[Emit]</c> wins because the
/// template already encodes the call shape; <c>[Import]</c> alone
/// only declares provenance (where the helper lives), not how to
/// call it — so the two never compete for the same lowering
/// decision. The canary tests in
/// <c>InvocationRewriterCanaryTests</c> pin the precedence.
/// </summary>
public sealed class EmitOrImportRewriter(InvocationLoweringHelpers helpers) : IInvocationRewriter
{
    private readonly InvocationLoweringHelpers _helpers = helpers;

    public IrExpression? Rewrite(InvocationRewriteContext context)
    {
        if (context.Symbol is not { } symbol)
            return null;

        if (GetEmitTemplate(symbol) is { } emitTemplate)
            return BuildEmitTemplate(context, symbol, emitTemplate);

        if (SymbolHelper.GetImport(symbol) is { } directImport)
            return BuildImportFacade(context, symbol, directImport);

        return null;
    }

    /// <summary>
    /// Reads the raw template string from a symbol's <c>[Emit("…")]</c>
    /// attribute. Returns <see langword="null"/> when the attribute
    /// is absent so the caller falls back to the next rewriter.
    /// </summary>
    private static string? GetEmitTemplate(ISymbol symbol) =>
        symbol
            .GetAttributes()
            .FirstOrDefault(a => a.AttributeClass?.Name is "EmitAttribute" or "Emit")
            ?.ConstructorArguments.FirstOrDefault()
            .Value as string;

    private IrTemplateExpression BuildEmitTemplate(
        InvocationRewriteContext context,
        IMethodSymbol symbol,
        string emitTemplate
    )
    {
        // Run the same normalization the regular invocation path uses so
        // named args reorder to parameter declaration order, params arrays
        // spread, and skipped optional parameters surface their explicit
        // default. The [Emit] template author addresses arguments by index
        // ($0, $1, …) and expects those indices to align with the C#
        // parameter slots — bypassing normalization would land named args
        // positionally and silently corrupt the emitted call.
        var args = BuildNormalizedArguments(context, symbol);

        var templateArgs = new List<IrExpression>();
        PrependReceiverIfInstance(templateArgs, context, symbol);
        templateArgs.AddRange(args.Select(a => a.Value));

        IReadOnlyList<IrTypeRef>? typeArgs = null;
        if (symbol is { TypeArguments.Length: > 0 })
            typeArgs = symbol.TypeArguments.Select(_helpers.MapTypeRef).ToList();

        IReadOnlyList<IrExternalImport>? externalImports = null;
        if (SymbolHelper.GetImport(symbol) is { } emitImport)
            externalImports = [ToIrExternalImport(emitImport)];

        return new IrTemplateExpression(
            emitTemplate,
            Receiver: null,
            templateArgs,
            TypeArguments: typeArgs,
            ExternalImports: externalImports
        );
    }

    private IrTemplateExpression BuildImportFacade(
        InvocationRewriteContext context,
        IMethodSymbol symbol,
        SymbolHelper.ImportInfo import
    )
    {
        var args = BuildNormalizedArguments(context, symbol);

        var templateArgs = new List<IrExpression>();
        PrependReceiverIfInstance(templateArgs, context, symbol);
        templateArgs.AddRange(args.Select(a => a.Value));

        var emittedName =
            SymbolHelper.GetNameOverride(symbol, _helpers.Target)
            ?? SymbolHelper.ToCamelCase(symbol.Name);
        var template = $"{emittedName}({BuildPositionalPlaceholders(templateArgs.Count)})";

        return new IrTemplateExpression(
            template,
            Receiver: null,
            templateArgs,
            ExternalImports: [ToIrExternalImport(import, emittedName)]
        );
    }

    /// <summary>
    /// For instance methods called with an explicit receiver
    /// (<c>receiver.Method(args)</c>), promotes the receiver
    /// expression into the leading template-argument slot so
    /// <c>$0</c> resolves to the receiver. Calls written without an
    /// explicit qualifier (<c>Method(args)</c> from inside the same
    /// type) parse as <see cref="IdentifierNameSyntax"/>, not
    /// <see cref="MemberAccessExpressionSyntax"/>, and inherit the
    /// legacy behaviour of silently omitting the implicit
    /// <c>this</c> receiver — callers should write the explicit
    /// receiver if they need it threaded.
    /// </summary>
    private static void PrependReceiverIfInstance(
        List<IrExpression> templateArgs,
        InvocationRewriteContext context,
        IMethodSymbol symbol
    )
    {
        if (
            !symbol.IsStatic
            && context.Invocation.Expression is MemberAccessExpressionSyntax memberAccess
        )
            templateArgs.Add(context.Recurse(memberAccess.Expression));
    }

    private IReadOnlyList<IrArgument> BuildNormalizedArguments(
        InvocationRewriteContext context,
        IMethodSymbol symbol
    )
    {
        var args = context
            .Invocation.ArgumentList.Arguments.Select(context.ExtractArgument)
            .ToList();
        _helpers.ApplyParamsSpread(args, symbol, context.Invocation.ArgumentList.Arguments);
        if (args.Any(a => a.Name is not null))
            return _helpers.NormalizeArguments(args, symbol);
        return args;
    }

    private static IrExternalImport ToIrExternalImport(
        SymbolHelper.ImportInfo import,
        string? emittedName = null
    ) =>
        new(
            import.Name,
            import.From,
            import.AsDefault,
            import.Version,
            EmittedName: SymbolHelper.NormalizeDivergentName(emittedName, import.Name)
        );

    private static string BuildPositionalPlaceholders(int count) =>
        string.Join(", ", Enumerable.Range(0, count).Select(i => $"${i}"));
}
