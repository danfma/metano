using System.Linq;
using Metano.Annotations;

namespace SampleQueryableSqlite.Provider;

/// <summary>
/// User-facing entry points for the sample. Each method composes a
/// LINQ chain over <see cref="IProductRepository.Products"/> and
/// hands the resulting <see cref="IQueryable{Product}"/> to the
/// repository's terminal method.
///
/// <para>
/// The chain itself is captured by Phase B (#196) — every lambda
/// body is encoded as an <c>ExprTree</c> alongside the closure. The
/// runtime's <c>getStages</c> helper (#200) makes that descriptor
/// list visible on the materialized result, so the TS-side adapter
/// can translate the trees into SQL and execute them via
/// <c>bun:sqlite</c> instead of evaluating the closures.
/// </para>
/// </summary>
[Transpile]
public static class ProductDemo
{
    public static Product[] ActiveProducts(IProductRepository repo) =>
        repo.ToArray(repo.Products.Where(p => p.IsActive));

    public static Product[] InStock(IProductRepository repo) =>
        repo.ToArray(repo.Products.Where(p => p.IsActive && p.StockCount > 0));

    public static Product[] AtLeastPrice(IProductRepository repo, decimal minPrice) =>
        repo.ToArray(repo.Products.Where(p => p.UnitPrice >= minPrice));

    public static Product[] TopByPrice(IProductRepository repo, int topN) =>
        repo.ToArray(repo.Products.OrderByDescending(p => p.UnitPrice).Take(topN));

    public static Product[] Page(IProductRepository repo, int offset, int pageSize) =>
        repo.ToArray(repo.Products.OrderBy(p => p.Id).Skip(offset).Take(pageSize));

    public static int CountActive(IProductRepository repo) =>
        repo.Count(repo.Products.Where(p => p.IsActive));

    public static Product? FirstById(IProductRepository repo) =>
        repo.FirstOrDefault(repo.Products.OrderBy(p => p.Id));
}
