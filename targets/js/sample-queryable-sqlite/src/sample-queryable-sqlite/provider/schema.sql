-- Schema for the SampleQueryableSqlite product fixture.
-- Shipped as a <MetanoAsset> so the same DDL is the single source of
-- truth for both the C# side (if a query analyser ever wants to read
-- it) and the generated TS bootstrap (provider/db.ts loads this file
-- at module init).
CREATE TABLE products (
  id INTEGER PRIMARY KEY,
  name TEXT NOT NULL,
  display_name TEXT NOT NULL,
  unit_price REAL NOT NULL,
  stock_count INTEGER NOT NULL,
  is_active INTEGER NOT NULL
);
