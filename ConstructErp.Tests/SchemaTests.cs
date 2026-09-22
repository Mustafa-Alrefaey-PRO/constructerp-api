using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Tests;

/// <summary>
/// Guards the three schema decisions that are expensive to reverse.
/// </summary>
/// <remarks>
/// These assert against SQL Server's own metadata, not against the EF model.
/// The model is what we asked for; INFORMATION_SCHEMA is what we actually got.
/// </remarks>
[Collection(nameof(ApiCollection))]
public sealed class SchemaTests(ApiFactory factory)
{
    [Fact]
    public async Task Every_money_column_keeps_three_decimal_places()
    {
        await factory.WithDbAsync(async db =>
        {
            var offenders = await db.Database
                .SqlQuery<string>($@"
                    SELECT TABLE_NAME + '.' + COLUMN_NAME + ' (' + DATA_TYPE + ','
                         + CAST(NUMERIC_SCALE AS VARCHAR(10)) + ')' AS Value
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE DATA_TYPE = 'decimal' AND NUMERIC_SCALE <> 3")
                .ToListAsync();

            // KWD has three decimal places. EF Core's default is decimal(18,2),
            // which silently rounds every fils.
            Assert.Empty(offenders);
        });
    }

    [Fact]
    public async Task No_column_uses_a_non_unicode_text_type()
    {
        await factory.WithDbAsync(async db =>
        {
            var offenders = await db.Database
                .SqlQuery<string>($@"
                    SELECT TABLE_NAME + '.' + COLUMN_NAME + ' (' + DATA_TYPE + ')' AS Value
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE DATA_TYPE IN ('varchar', 'char', 'text')")
                .ToListAsync();

            // VARCHAR under SQL Server's default collation cannot store Arabic;
            // it writes '?????' and the original is unrecoverable.
            Assert.Empty(offenders);
        });
    }

    [Fact]
    public async Task Equipment_is_linked_to_projects_by_foreign_key()
    {
        await factory.WithDbAsync(async db =>
        {
            var keys = await db.Database
                .SqlQuery<string>($@"
                    SELECT name AS Value FROM sys.foreign_keys
                    WHERE parent_object_id = OBJECT_ID('Equipment')
                      AND referenced_object_id = OBJECT_ID('Projects')")
                .ToListAsync();

            // The prototype joined equipment to projects on the project NAME,
            // so a rename silently zeroed the linked counts.
            Assert.Single(keys);
        });
    }

    [Fact]
    public async Task Money_survives_a_three_decimal_round_trip()
    {
        await factory.WithDbAsync(async db =>
        {
            var asset = await db.Equipment.FirstAsync();
            asset.DailyCost = 1250.375m;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var reloaded = await db.Equipment.FirstAsync(e => e.Id == asset.Id);

            Assert.Equal(1250.375m, reloaded.DailyCost);
        });
    }

    [Fact]
    public async Task Arabic_survives_a_round_trip()
    {
        const string arabic = "ونش زاحف ٨٠ طن";

        await factory.WithDbAsync(async db =>
        {
            var asset = await db.Equipment.FirstAsync();
            asset.Name.Ar = arabic;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            var reloaded = await db.Equipment.FirstAsync(e => e.Id == asset.Id);

            Assert.Equal(arabic, reloaded.Name.Ar);
            Assert.DoesNotContain('?', reloaded.Name.Ar!);
        });
    }

    [Fact]
    public async Task Soft_deleted_rows_are_hidden_but_not_destroyed()
    {
        await factory.WithDbAsync(async db =>
        {
            var asset = await db.Equipment.FirstAsync();
            asset.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
            db.ChangeTracker.Clear();

            Assert.Null(await db.Equipment.FirstOrDefaultAsync(e => e.Id == asset.Id));

            // Still there, recoverable by clearing DeletedAt.
            var raw = await db.Equipment.IgnoreQueryFilters()
                .FirstOrDefaultAsync(e => e.Id == asset.Id);
            Assert.NotNull(raw);

            raw!.DeletedAt = null;
            await db.SaveChangesAsync();
        });
    }
}
