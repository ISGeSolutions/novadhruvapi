# Nova Naming Registry — Query Reference

SQLite reference DB for canonical table/column names and legacy MSSQL alias mappings.

**Open the DB:**
```bash
sqlite3 dev-tools/naming-registry.db
```
Or run a one-liner directly:
```bash
sqlite3 dev-tools/naming-registry.db "SELECT ..."
```

---

## Before naming a new column

Search the vocabulary before choosing a column name. If it exists, use it as-is.

```sql
-- Search by keyword
SELECT id, canonical_name, description
FROM nova_db_column
WHERE canonical_name LIKE '%bkg%'
   OR description     LIKE '%booking%';
```

If the name you need does not exist, add it to `naming-registry.sql` and regenerate the DB before using it in any migration.

---

## Before naming a new table

Check the table short alias is available, and that the canonical name does not already exist.

```sql
-- Check both canonical name and alias availability
SELECT id, canonical_name, short_alias
FROM nova_db_table
WHERE canonical_name LIKE '%booking%'
   OR short_alias    = 'bkt';
```

---

## All columns for a table

```sql
-- By canonical table name
SELECT c.canonical_name, c.description
FROM nova_db_column c
-- (columns are vocabulary-wide; use this to see all columns a table uses in migrations)

-- All tables with their schema context
SELECT id, canonical_name, short_alias, legacy_name, description
FROM nova_db_table
ORDER BY canonical_name;
```

---

## Legacy MSSQL alias lookup

Use this when writing a query against a legacy MSSQL table.
The result is the alias to apply: `legacy_column_name AS canonical_name`

```sql
-- Full alias sheet for a legacy table (replace 'BookedTours' with your table)
SELECT l.legacy_column_name,
       c.canonical_name,
       l.description
FROM   nova_db_column_legacy l
JOIN   nova_db_column        c ON c.id = l.column_id
WHERE  l.table_id = (SELECT id FROM nova_db_table WHERE legacy_name = 'BookedTours')
ORDER  BY l.legacy_column_name;
```

```sql
-- Look up one specific legacy column
SELECT c.canonical_name
FROM   nova_db_column_legacy l
JOIN   nova_db_column        c ON c.id = l.column_id
WHERE  l.table_id = (SELECT id FROM nova_db_table WHERE legacy_name = 'BookedTours')
AND    l.legacy_column_name = 'BookingNo';
```

```sql
-- Find all legacy aliases that map to a given canonical column
SELECT t.legacy_name  AS legacy_table,
       l.legacy_column_name,
       c.canonical_name
FROM   nova_db_column_legacy l
JOIN   nova_db_table         t ON t.id = l.table_id
JOIN   nova_db_column        c ON c.id = l.column_id
WHERE  c.canonical_name = 'bkg_no';
```

---

## All legacy tables and their canonical names

```sql
SELECT canonical_name, short_alias, legacy_name, description
FROM   nova_db_table
WHERE  legacy_name IS NOT NULL
ORDER  BY legacy_name;
```

---

## All unmapped legacy tables

Useful during legacy migration work to find tables with no alias mappings yet.

```sql
SELECT t.id, t.legacy_name, t.canonical_name
FROM   nova_db_table t
WHERE  t.legacy_name IS NOT NULL
AND    NOT EXISTS (
    SELECT 1 FROM nova_db_column_legacy l WHERE l.table_id = t.id
);
```

---

## Adding entries

All changes go in `naming-registry.sql`. After editing, regenerate:

```bash
sqlite3 dev-tools/naming-registry.db < dev-tools/naming-registry.sql
```

Then commit both files together.
