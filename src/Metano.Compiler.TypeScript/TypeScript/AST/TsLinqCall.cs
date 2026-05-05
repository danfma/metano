namespace Metano.TypeScript.AST;

/// <summary>
/// Specialized call to the LINQ pipe runtime entry point. Lowers to
/// <c>linq(source, op1(args), op2(args), opN(args))</c> and instructs
/// the import collector to bundle <see cref="Helpers"/> as a named
/// import from the <c>metano-runtime/system/linq</c> subpath.
///
/// <para>
/// Helpers always include the entry point itself (<c>linq</c>) plus
/// every operator factory name the chain uses (<c>where</c>,
/// <c>select</c>, <c>toArray</c>, etc). The printer emits the call
/// verbatim; import resolution is the collector's job.
/// </para>
/// </summary>
public sealed record TsLinqCall(IReadOnlyList<TsExpression> Args, IReadOnlySet<string> Helpers)
    : TsExpression;
