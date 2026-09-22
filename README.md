# ConstructERP API

Backend for [ConstructERP](../constructerp-prototype) — a construction equipment ERP.
Separate repository; the Angular frontend lives on its own.

## Stack

| | |
| --- | --- |
| Runtime | .NET 10 |
| Data | EF Core 10 + SQL Server 2022 |
| Layout | `Api` → `Infrastructure` → `Application` → `Domain` |

## Running it

Requires a local SQL Server instance reachable with Windows auth. No Docker needed.

```bash
cd ConstructErp.Api
dotnet run
```

On start in Development the API applies pending migrations and seeds reference and
demo data, then listens on the URL printed in the console. `GET /health` returns
`{"status":"ok"}`.

Connection string lives in `ConstructErp.Api/appsettings.Development.json` and points
at `ConstructErp.Dev` on `localhost`.

## Migrations

```bash
dotnet ef migrations add <Name> --project ConstructErp.Infrastructure --startup-project ConstructErp.Api --output-dir Persistence/Migrations
dotnet ef database update --project ConstructErp.Infrastructure --startup-project ConstructErp.Api
```

## Three schema rules that are expensive to undo

These are enforced in `ErpDbContext` and verified against the live database. Read
before changing the model.

1. **Money is `decimal(18,3)`.** KWD has three decimal places — a fils is 1/1000 of a
   dinar. EF Core's default is `decimal(18,2)`, which silently rounds every amount and
   produces totals that will not reconcile. Applied by convention in
   `ConfigureConventions`, not per-property, so a new money column cannot miss it.
2. **All text is NVARCHAR.** Under SQL Server's default collation, `VARCHAR` cannot
   store Arabic — it writes `?????` and the original is unrecoverable. Roughly half
   this data is Arabic. Also applied by convention.
3. **Records link by ID, never by name.** The prototype joined equipment to projects
   on the project *name*, so renaming a project silently zeroed its linked counts.
   `Project.Code` (PRJ-1001) is a unique business identifier, but the key and every
   foreign key are GUIDs.

Bilingual names use the `LocalizedText` complex type (`Name_En` / `Name_Ar` columns).
The frontend's old approach — a lookup table of translations — only ever worked for
seeded records; anything a user created had no Arabic form.

## Seeding

`ErpDbSeeder` runs at startup in Development and is idempotent. Runtime seeding rather
than `HasData` because EF Core cannot seed entities with complex properties
([dotnet/efcore#31254](https://github.com/dotnet/efcore/issues/31254)).

Two seeded assets are deliberately left unassigned: the prototype's mock data
referenced projects that never existed as records, and inventing them to satisfy a
string match is exactly the bug the foreign key removes.
