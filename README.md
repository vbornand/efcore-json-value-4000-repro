# JSON collection elements lose strings longer than 4000 characters

Minimal reproduction for EF Core 11.0.0 and 10.0.15 on SQL Server.

`Blog.Notes` is a complex collection mapped to JSON, and a note's `Text` is 4500 characters long.
Reading the whole collection returns the text. Projecting the elements one by one returns `null`,
because EF reads each property with `JSON_VALUE` over the `[value]` column of `OPENJSON`, which
returns at most 4000 characters and null beyond. The same happens with a native `json` column and
with an `nvarchar(max)` one.

## Run

```shell
dotnet run -f net11.0   # EF Core 11.0.0
dotnet run -f net10.0   # EF Core 10.0.15
```

Requires Docker: the program starts a SQL Server 2025 container with Testcontainers.

## Output (both versions)

```text
== UseAzureSql, compatibility level 170 (json column)
Select(b => b.Notes):                    4500 characters
Select(b => b.Notes.Select(n => new { n.Language, n.Text })): null
SELECT [b0].[Id], JSON_VALUE([n].[value], '$.Language') AS [Language], JSON_VALUE([n].[value], '$.Text') AS [Text], [n].[key]
    FROM (
        SELECT TOP(2) [b].[Id], [b].[Notes]
        FROM [Blogs] AS [b]
    ) AS [b0]
    OUTER APPLY OPENJSON([b0].[Notes], '$') AS [n]
    ORDER BY [b0].[Id], CAST([n].[key] AS int)
== UseSqlServer, compatibility level 160 (nvarchar(max) column)
Select(b => b.Notes):                    4500 characters
Select(b => b.Notes.Select(n => new { n.Language, n.Text })): null
SELECT [b0].[Id], JSON_VALUE([n].[value], '$.Language') AS [Language], JSON_VALUE([n].[value], '$.Text') AS [Text], [n].[key]
    FROM (
        SELECT TOP(2) [b].[Id], [b].[Notes]
        FROM [Blogs] AS [b]
    ) AS [b0]
    OUTER APPLY OPENJSON([b0].[Notes], '$') AS [n]
    ORDER BY [b0].[Id], CAST([n].[key] AS int)
```

Expected: `4500 characters` in both cases.

## Why

- The elements must keep their order, so EF drops the `WITH` clause of `OPENJSON` to read its
  `[key]` column, then reads each property with `JSON_VALUE([n].[value], '$.Text')`.
- `RETURNING` is only emitted over the `json` type, and `[value]` is `nvarchar(max)`.
- Two forms read the whole text on SQL Server 2025 and Azure SQL:
  - `JSON_VALUE(CAST([n].[value] AS json), '$.Text' RETURNING nvarchar(max))`;
  - `OUTER APPLY OPENJSON([n].[value]) WITH ([Text] nvarchar(max) '$.Text')`, which also works on
    earlier versions.
