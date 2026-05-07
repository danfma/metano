namespace SampleQueryableSqlite;

/// <summary>
/// Row shape for the <c>products</c> SQLite table. C# / TS uses
/// camelCase property names (<c>displayName</c>, <c>unitPrice</c>);
/// the SQLite schema uses snake_case columns
/// (<c>display_name</c>, <c>unit_price</c>). The hand-written provider
/// on the JS side carries an explicit member → column map so the
/// captured expression trees translate to the right column references.
/// </summary>
public sealed record Product(
    int Id,
    string Name,
    string DisplayName,
    decimal UnitPrice,
    int StockCount,
    bool IsActive
);
