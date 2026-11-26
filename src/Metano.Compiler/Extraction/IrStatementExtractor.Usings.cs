using Metano.Compiler.IR;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Metano.Compiler.Extraction;

/// <summary>
/// <c>using</c> statements and declarations (FR-111), lowered on a target that declares
/// <c>System.IDisposable</c> to the <c>try</c>/<c>finally</c> the C# specification defines
/// them as, with the existing statement nodes:
/// <list type="bullet">
///   <item><c>using (T x = e) body</c> → <c>{ const x = e; try { body } finally { x.dispose(); } }</c>;</item>
///   <item><c>using (e) body</c> → the same over a temporary (<c>$using0</c>), bound even when
///   <c>e</c> is a local, since C# captures the value at entry;</item>
///   <item><c>using var x = e;</c> → <c>const x = e; try { rest of the scope } finally { x.dispose(); }</c>.</item>
/// </list>
/// Several declarators, and consecutive declarations, nest left to right, so the resources
/// are disposed in reverse order and a later initializer that throws still disposes the
/// earlier ones. A resource that may be <c>null</c> is disposed through <c>?.</c>, as C#
/// skips it. A <c>Dispose</c> that throws replaces the pending exception (plain
/// <c>finally</c>, as in .NET). Not lowered, reported as MS0001: <c>await using</c>, a
/// resource whose <c>Dispose</c> the target does not emit, and a <c>using</c> declaration
/// directly in a module entry point (its <c>try</c> would hide the exported variables).
/// </summary>
public sealed partial class IrStatementExtractor
{
    /// <summary>
    /// The statements of one scope in order, up to the first <c>using</c> declaration
    /// that lowers: it takes the rest of the scope as the body of its <c>try</c>.
    /// </summary>
    private List<IrStatement> ExtractUpToUsingDeclaration(IReadOnlyList<StatementSyntax> source)
    {
        var extracted = new List<IrStatement>();

        for (var index = 0; index < source.Count; index++)
        {
            if (source[index] is LocalDeclarationStatementSyntax local && IsLoweredUsing(local))
            {
                var resources = DeclaredResources(local.Declaration);
                var rest = ExtractStatementSequence([.. source.Skip(index + 1)]);

                extracted.AddRange(GuardedByResources(resources, rest));

                return extracted;
            }

            extracted.AddRange(ExtractStatementInScope(source[index]));
        }

        return extracted;
    }

    private bool IsLoweredUsing(LocalDeclarationStatementSyntax local) =>
        local.UsingKeyword.IsKind(SyntaxKind.UsingKeyword)
        && UnloweredUsingDeclarationReason(local) is null;

    /// <summary>
    /// <c>using (…) body</c>: a block that declares the resources and disposes them in a
    /// <c>finally</c> around the body. One that does not lower is dropped and reported.
    /// </summary>
    private IrStatement ExtractUsing(UsingStatementSyntax statement)
    {
        if (!_context.LowersUsingStatements)
            return DroppedCodeDiagnostics.UnsupportedStatement(statement);

        if (UnloweredUsingStatementReason(statement) is { } reason)
        {
            DroppedCodeDiagnostics.Report(reason, statement.GetLocation());

            return new IrUnsupportedStatement(statement.Kind().ToString());
        }

        var resources = statement.Declaration is { } declaration
            ? DeclaredResources(declaration)
            : [ExpressionResource(statement.Expression!)];

        var body = ExtractEmbedded(statement.Statement);

        return new IrBlockStatement(
            LowerStatementSequence(GuardedByResources(resources, UnwrappedBlock(body)))
        );
    }

    /// <summary>
    /// The statements of a body that is one nested block (<c>using (a) using (b) { … }</c>),
    /// whose scope the enclosing <c>try</c> block already gives.
    /// </summary>
    private static List<IrStatement> UnwrappedBlock(List<IrStatement> body) =>
        body is [IrBlockStatement block] ? [.. block.Statements] : body;

    /// <summary>
    /// <c>const r0 = e0; try { const r1 = e1; try { body } finally { r1.dispose(); } }
    /// finally { r0.dispose(); }</c>: each resource guards the ones after it and the body.
    /// The outermost declaration and <c>try</c> join the enclosing scope, which lowers
    /// their expression variables; each inner pair is lowered as its own scope.
    /// </summary>
    private List<IrStatement> GuardedByResources(
        IReadOnlyList<UsingResource> resources,
        List<IrStatement> body
    )
    {
        var guarded = body;

        for (var index = resources.Count - 1; index > 0; index--)
            guarded = LowerStatementSequence(Guard(resources[index], guarded));

        return Guard(resources[0], guarded);
    }

    private static List<IrStatement> Guard(UsingResource resource, List<IrStatement> body) =>
        [
            resource.Declaration,
            new IrTryStatement(body, Catches: null, Finally: [resource.Disposal]),
        ];

    /// <summary>The resources a declaration acquires, one per declarator, in source order.</summary>
    private List<UsingResource> DeclaredResources(VariableDeclarationSyntax declaration)
    {
        var declarations = ExtractDeclarators(declaration, isConst: true);

        return
        [
            .. declaration.Variables.Select(
                (variable, index) =>
                    new UsingResource(
                        declarations[index],
                        Disposal(
                            new IrIdentifier(((IrVariableDeclaration)declarations[index]).Name),
                            (_semantic.GetDeclaredSymbol(variable) as ILocalSymbol)?.Type,
                            variable.Initializer?.Value
                        )
                    )
            ),
        ];
    }

    /// <summary>The resource of <c>using (expression)</c>, held by a fresh temporary.</summary>
    private UsingResource ExpressionResource(ExpressionSyntax expression)
    {
        var name = _expressions.NextUsingResourceName();
        var type = _semantic.GetTypeInfo(expression).Type;

        var declaration = new IrVariableDeclaration(
            name,
            Type: null,
            _expressions.Extract(expression),
            IsConst: true,
            InferredType: type is null ? null : MapType(type)
        );

        return new UsingResource(
            IrStatementSpans.WithSpanOf(expression, declaration),
            Disposal(new IrIdentifier(name), type, expression)
        );
    }

    private IrStatement Disposal(IrExpression resource, ITypeSymbol? type, ExpressionSyntax? value)
    {
        var dispose = DisposableResources.DisposalOf(type, _semantic.Compilation, _context.Target);

        return new IrExpressionStatement(
            _expressions.DisposeCall(resource, dispose.Method, MayBeNull(type, value))
        );
    }

    /// <summary>
    /// Whether the resource may be <c>null</c> when disposed. Only a value that is non-null
    /// by construction skips the guard: a non-nullable value type, or an object or
    /// collection creation (<c>new …</c>, <c>[…]</c>). Roslyn's flow state is not used: it
    /// is unsound for array elements, captured locals and <c>!</c> (decision W10-9a), and
    /// a dereferenced <c>null</c> in the <c>finally</c> would replace the body's exception.
    /// A <c>Nullable&lt;T&gt;</c> resource always keeps the guard: <c>new S?()</c> has no value.
    /// A creation that reaches the resource through a user-defined conversion keeps it too:
    /// the conversion operator may return <c>null</c>.
    /// </summary>
    private bool MayBeNull(ITypeSymbol? type, ExpressionSyntax? value)
    {
        if (type is not null && type.IsNullableValueType())
            return true;

        if (type is { IsValueType: true })
            return false;

        return !IsCreatedResource(value);
    }

    private bool IsCreatedResource(ExpressionSyntax? value) =>
        value is not null
        && Unparenthesized(value)
            is BaseObjectCreationExpressionSyntax
                or CollectionExpressionSyntax
        && !_semantic.GetConversion(value).IsUserDefined;

    private static ExpressionSyntax? Unparenthesized(ExpressionSyntax? value) =>
        value is ParenthesizedExpressionSyntax parenthesized
            ? Unparenthesized(parenthesized.Expression)
            : value;

    /// <summary>
    /// Reports a <c>using</c> declaration that is extracted as a plain declaration: its
    /// value is never disposed.
    /// </summary>
    private void ReportUnloweredUsingDeclaration(LocalDeclarationStatementSyntax local) =>
        DroppedCodeDiagnostics.Report(
            UnloweredUsingDeclarationReason(local)
                ?? "The C# using declaration is not supported by the transpiler: its value would "
                    + "never be disposed at the end of the scope.",
            local.GetLocation()
        );

    /// <summary>
    /// Why a <c>using</c> declaration does not lower; <c>null</c> when it does. A target
    /// without the lowering keeps the generic report.
    /// </summary>
    private string? UnloweredUsingDeclarationReason(LocalDeclarationStatementSyntax local)
    {
        if (!_context.LowersUsingStatements)
            return null;

        if (local.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
            return AwaitUsingReason("declaration");

        if (IsDirectlyInModuleEntryPoint(local))
            return "A C# using declaration directly in a module entry point body is not "
                + "supported by the transpiler: the try/finally that disposes it would hide the "
                + "body's exported variables. Use a using statement or an inner block.";

        return UnemittedDisposeReason(DeclaredTypeOf(local.Declaration));
    }

    private string? UnloweredUsingStatementReason(UsingStatementSyntax statement)
    {
        if (statement.AwaitKeyword.IsKind(SyntaxKind.AwaitKeyword))
            return AwaitUsingReason("statement");

        var type = statement.Declaration is { } declaration
            ? DeclaredTypeOf(declaration)
            : _semantic.GetTypeInfo(statement.Expression!).Type;

        return UnemittedDisposeReason(type);
    }

    private ITypeSymbol? DeclaredTypeOf(VariableDeclarationSyntax declaration) =>
        declaration.Variables.FirstOrDefault() is { } variable
            ? (_semantic.GetDeclaredSymbol(variable) as ILocalSymbol)?.Type
            : null;

    private string? UnemittedDisposeReason(ITypeSymbol? type)
    {
        var disposal = DisposableResources.DisposalOf(type, _semantic.Compilation, _context.Target);

        if (!disposal.IsEmitted)
            return $"The C# using over '{type?.ToDisplayString()}' is not supported by the "
                + "transpiler: the target does not emit its Dispose method, so the resource "
                + "would never be disposed.";

        if (disposal.Shadowing is { } shadowing)
            return $"The C# using over '{type?.ToDisplayString()}' is not supported by the "
                + $"transpiler: '{shadowing.ToDisplayString()}' does not override the "
                + "IDisposable implementation but is emitted under its name, so the resource "
                + "would be disposed through the wrong method. Rename the overload.";

        return null;
    }

    private static string AwaitUsingReason(string form) =>
        $"The C# 'await using' {form} is not supported by the transpiler: IAsyncDisposable is "
        + "not mapped, so the resource would never be disposed.";

    /// <summary>
    /// A declaration that sits in a top-level statement list or directly in the body of a
    /// <c>[ModuleEntryPoint]</c> method, whose locals the module may export.
    /// </summary>
    private bool IsDirectlyInModuleEntryPoint(LocalDeclarationStatementSyntax local) =>
        local.Parent switch
        {
            GlobalStatementSyntax => true,
            BlockSyntax { Parent: MethodDeclarationSyntax method } => _semantic
                .GetDeclaredSymbol(method)
                ?.HasModuleEntryPoint() == true,
            _ => false,
        };

    /// <summary>A resource's declaration and the statement that disposes it.</summary>
    private sealed record UsingResource(IrStatement Declaration, IrStatement Disposal);
}
