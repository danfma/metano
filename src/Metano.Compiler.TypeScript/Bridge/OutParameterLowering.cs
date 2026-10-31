using Metano.Compiler.Analysis;
using Metano.Compiler.Diagnostics;
using Metano.Compiler.Extraction;
using Metano.Compiler.IR;

namespace Metano.Compiler.TypeScript.Bridge;

/// <summary>
/// Lowers methods and functions with C# <c>out</c> parameters to the shape
/// their call sites (<see cref="IrOutArgumentCall"/>) expect: the <c>out</c>
/// parameters leave the parameter list and the function returns
/// <c>[result, out1, out2, …]</c> — every <c>out</c> value after the return
/// value, in parameter order; a <c>void</c> method returns only the
/// <c>out</c> values, as a tuple even when there is one. Runs on the IR before
/// any bridge sees it, so class, interface, abstract, override and module
/// function emission all agree with each other and with call sites in other
/// files and packages.
/// <para>
/// In the body each <c>out</c> parameter becomes an uninitialized local
/// (<c>let value: V | null;</c>) — C#'s definite assignment guarantees it is
/// assigned before every <c>return</c> — and each <c>return x</c> becomes
/// <c>return [x, value]</c>. A <c>throw</c> needs no change. When the result
/// type is a discriminated union (see <see cref="OutResultTypeRef"/>), the
/// returned tuple is asserted to it: the callee's <c>[NotNullWhen]</c> /
/// <c>[MaybeNullWhen]</c> contract is what C# checked, and TypeScript cannot
/// relate a <c>boolean</c> result to the per-branch value types by itself.
/// </para>
/// </summary>
internal static class OutParameterLowering
{
    public static IrClassDeclaration Lower(IrClassDeclaration declaration) =>
        (IrClassDeclaration)Lower((IrTypeDeclaration)declaration);

    public static IrTypeDeclaration Lower(IrTypeDeclaration declaration) =>
        declaration switch
        {
            IrClassDeclaration cls => cls with
            {
                Members = cls.Members?.Select(Lower).ToList(),
                NestedTypes = cls.NestedTypes?.Select(Lower).ToList(),
            },
            IrInterfaceDeclaration iface => iface with
            {
                Members = iface.Members?.Select(Lower).ToList(),
            },
            _ => declaration,
        };

    public static IrModuleFunction Lower(IrModuleFunction function)
    {
        if (!function.Parameters.Any(p => p.IsOut))
            return function;

        var shape = OutResultShape.Of(function.Parameters, function.ReturnType);

        return function with
        {
            Parameters = shape.InParameters,
            ReturnType = shape.ResultType,
            Body = function.Body is null ? null : shape.LowerBody(function.Body, function.Name),
        };
    }

    private static IrMemberDeclaration Lower(IrMemberDeclaration member) =>
        member is IrMethodDeclaration method ? Lower(method) : member;

    private static IrMethodDeclaration Lower(IrMethodDeclaration method)
    {
        var overloads = method.Overloads?.Select(Lower).ToList();

        if (!method.Parameters.Any(p => p.IsOut))
            return overloads is null ? method : method with { Overloads = overloads };

        var shape = OutResultShape.Of(method.Parameters, method.ReturnType);

        return method with
        {
            Parameters = shape.InParameters,
            ReturnType = shape.ResultType,
            Body = method.Body is null ? null : shape.LowerBody(method.Body, method.Name),
            Overloads = overloads,
        };
    }

    /// <summary>The lowered signature of one method and how its body changes.</summary>
    private sealed record OutResultShape(
        IReadOnlyList<IrParameter> InParameters,
        IReadOnlyList<IrParameter> OutParameters,
        OutResultTypeRef ResultType
    )
    {
        public static OutResultShape Of(IReadOnlyList<IrParameter> parameters, IrTypeRef returnType)
        {
            var outParameters = parameters.Where(p => p.IsOut).ToList();

            var result = returnType is IrPrimitiveTypeRef { Primitive: IrPrimitive.Void }
                ? null
                : returnType;

            return new OutResultShape(
                parameters.Where(p => !p.IsOut).ToList(),
                outParameters,
                OutResultTypeRef.For(result, outParameters)
            );
        }

        public IReadOnlyList<IrStatement> LowerBody(IReadOnlyList<IrStatement> body, string name)
        {
            var declarations = OutParameters.Select(p =>
                (IrStatement)
                    new IrVariableDeclaration(
                        p.Name,
                        OutResultTypeRef.LocalTypeOf(p),
                        Initializer: null
                    )
            );

            ReportReturnsBeforeFinallyWrites(body, name);

            var lowered = body.Select(LowerReturns).ToList();

            if (ResultType.Result is null && !EndsInJump(lowered))
                lowered.Add(new IrReturnStatement(ResultTuple(null)));

            return [.. declarations, .. lowered];
        }

        /// <summary>
        /// A <c>return</c> inside a <c>try</c> whose <c>finally</c> assigns an
        /// <c>out</c> parameter: C# callers see the <c>finally</c>'s write
        /// (the argument is by reference), the returned tuple snapshots the
        /// value before it. Reported as MS0001.
        /// </summary>
        private void ReportReturnsBeforeFinallyWrites(IReadOnlyList<IrStatement> body, string name)
        {
            var outNames = OutParameters.Select(p => p.Name).ToHashSet(StringComparer.Ordinal);

            var affected = Descendants(body)
                .OfType<IrTryStatement>()
                .Any(t =>
                    t.Finally is { } final
                    && IrLocalReassignmentCollector.CollectFromStatements(final).Overlaps(outNames)
                    && Descendants([.. t.Body, .. (t.Catches ?? []).SelectMany(c => c.Body)])
                        .OfType<IrReturnStatement>()
                        .Any()
                );

            if (!affected)
                return;

            ExtractionDiagnostics.Report(
                new MetanoDiagnostic(
                    MetanoDiagnosticSeverity.Warning,
                    DiagnosticCodes.UnsupportedFeature,
                    $"'{name}': a 'return' inside 'try' returns the 'out' values ({string.Join(", ", outNames)}) "
                        + "before the 'finally' block assigns them; callers do not see that write."
                )
            );
        }

        private static IEnumerable<IrStatement> Descendants(IEnumerable<IrStatement> statements) =>
            statements.SelectMany(s => Descendants(IrNodeChildren.StatementsOf(s)).Prepend(s));

        private IrExpression ResultTuple(IrExpression? value)
        {
            var outs = OutParameters.Select(p => (IrExpression)new IrIdentifier(p.Name));

            var tuple = new IrArrayLiteral(
                value is null ? outs.ToList() : outs.Prepend(value).ToList()
            );

            return ResultType.IsDiscriminated ? new IrCastExpression(tuple, ResultType) : tuple;
        }

        private static bool EndsInJump(IReadOnlyList<IrStatement> body) =>
            body.Count > 0 && body[^1] is IrReturnStatement or IrThrowStatement;

        private IReadOnlyList<IrStatement> LowerReturns(IReadOnlyList<IrStatement> statements) =>
            statements.Select(LowerReturns).ToList();

        /// <summary>
        /// Rewrites the <c>return</c>s of the function itself; lambda bodies
        /// are expressions here and are left alone.
        /// </summary>
        private IrStatement LowerReturns(IrStatement statement) =>
            statement switch
            {
                IrReturnStatement ret => new IrReturnStatement(ResultTuple(ret.Value)),
                IrIfStatement ifs => ifs with
                {
                    Then = LowerReturns(ifs.Then),
                    Else = ifs.Else is null ? null : LowerReturns(ifs.Else),
                },
                IrBlockStatement block => block with
                {
                    Statements = LowerReturns(block.Statements),
                },
                IrForEachStatement fe => fe with { Body = LowerReturns(fe.Body) },
                IrForStatement fs => fs with { Body = LowerReturns(fs.Body) },
                IrWhileStatement ws => ws with { Body = LowerReturns(ws.Body) },
                IrDoWhileStatement dw => dw with { Body = LowerReturns(dw.Body) },
                IrTryStatement ts => ts with
                {
                    Body = LowerReturns(ts.Body),
                    Catches = ts
                        .Catches?.Select(c => c with { Body = LowerReturns(c.Body) })
                        .ToList(),
                    Finally = ts.Finally is null ? null : LowerReturns(ts.Finally),
                },
                IrSwitchStatement sw => sw with
                {
                    Cases = sw.Cases.Select(c => c with { Body = LowerReturns(c.Body) }).ToList(),
                },
                _ => statement,
            };
    }
}
