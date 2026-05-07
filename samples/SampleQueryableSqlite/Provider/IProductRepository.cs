using System.Linq;
using Metano.Annotations;

namespace SampleQueryableSqlite.Provider;

/// <summary>
/// Adapter contract C# user code talks to. The interface is
/// transpiled (so the generated TypeScript surface declares the
/// shape) but no implementation lives on the C# side — the consumer
/// project provides a concrete adapter that owns the real
/// <c>bun:sqlite</c> handle and translates each call's
/// <see cref="IQueryable{Product}"/> into a SQL statement via the
/// runtime's <c>getStages</c> introspection helper.
///
/// <para>
/// The methods mirror the LINQ terminals the sample exercises
/// (<c>ToArray</c>, <c>Count</c>, <c>FirstOrDefault</c>) but accept
/// the chain as a regular argument instead of being chained on the
/// queryable itself. That shape lets the consumer pass the chain to
/// the adapter while keeping the call site readable in C#:
/// <c>repo.ToArray(repo.Products.Where(p => p.IsActive))</c>.
/// </para>
/// </summary>
[Transpile]
public interface IProductRepository
{
    IQueryable<Product> Products { get; }

    Product[] ToArray(IQueryable<Product> query);

    int Count(IQueryable<Product> query);

    Product? FirstOrDefault(IQueryable<Product> query);
}
