using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options) : DbContext(options)
{
    /// <summary>
    /// KWD has THREE decimal places (1 dinar = 1000 fils).
    /// </summary>
    /// <remarks>
    /// EF Core's default for decimal on SQL Server is decimal(18,2), which
    /// silently truncates every fils and produces totals that do not reconcile.
    /// It is applied to every money column below, and it is the single most
    /// expensive thing to get wrong here — changing scale after data exists
    /// means a migration that cannot recover the lost digits.
    /// </remarks>
    public const string MoneyPrecision = "decimal(18,3)";

    public DbSet<Project> Projects => Set<Project>();

    public DbSet<CostEntry> CostEntries => Set<CostEntry>();

    public DbSet<EquipmentAsset> Equipment => Set<EquipmentAsset>();

    public DbSet<EquipmentType> EquipmentTypes => Set<EquipmentType>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Every string column is NVARCHAR. Under SQL Server's default
        // collation, VARCHAR cannot store Arabic — it writes "?????" and the
        // data is gone. Half this data set is Arabic, so this is not optional.
        builder.Properties<string>().AreUnicode().HaveMaxLength(256);

        builder.Properties<decimal>().HaveColumnType(MoneyPrecision);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        ConfigureProjects(builder);
        ConfigureEquipment(builder);
        ConfigureAuditing(builder);

        // No HasData here: EF Core cannot seed entities that use complex
        // properties (dotnet/efcore#31254), and every name on these entities is
        // a LocalizedText. Reference and demo data are seeded at runtime by
        // ErpDbSeeder instead.
    }

    private static void ConfigureProjects(ModelBuilder builder)
    {
        builder.Entity<Project>(project =>
        {
            project.HasIndex(p => p.Code).IsUnique();
            project.Property(p => p.Code).HasMaxLength(32);
            project.Property(p => p.Manager).HasMaxLength(128);

            project.ComplexProperty(p => p.Name).IsRequired();
            project.ComplexProperty(p => p.Client).IsRequired();
            project.ComplexProperty(p => p.Location).IsRequired();

            // Stored as int. Storing enums as strings makes renaming a member a
            // data migration; ints keep the wire format stable, and the API
            // serves per-language labels anyway.
            project.Property(p => p.Status).HasConversion<int>();

            project.HasMany(p => p.CostEntries)
                .WithOne(c => c.Project!)
                .HasForeignKey(c => c.ProjectId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<CostEntry>(entry =>
        {
            entry.Property(c => c.Category).HasConversion<int>();
            entry.ComplexProperty(c => c.Description).IsRequired();
            entry.HasIndex(c => new { c.ProjectId, c.IncurredOn });
        });
    }

    private static void ConfigureEquipment(ModelBuilder builder)
    {
        builder.Entity<EquipmentType>(type =>
        {
            type.HasIndex(t => t.Code).IsUnique();
            type.Property(t => t.Code).HasMaxLength(32);
            type.ComplexProperty(t => t.Name).IsRequired();
        });

        builder.Entity<EquipmentAsset>(asset =>
        {
            asset.HasIndex(a => a.Code).IsUnique();
            asset.Property(a => a.Code).HasMaxLength(32);

            asset.ComplexProperty(a => a.Name).IsRequired();
            asset.ComplexProperty(a => a.NextAction).IsRequired();

            asset.Property(a => a.Ownership).HasConversion<int>();
            asset.Property(a => a.Status).HasConversion<int>();

            // The point of this slice: a real foreign key. The prototype joined
            // equipment to projects on the project NAME, so renaming a project
            // silently zeroed its linked counts.
            asset.HasOne(a => a.Project)
                .WithMany(p => p.Equipment)
                .HasForeignKey(a => a.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            asset.HasOne(a => a.EquipmentType)
                .WithMany(t => t.Assets)
                .HasForeignKey(a => a.EquipmentTypeId)
                .OnDelete(DeleteBehavior.Restrict);

            asset.HasIndex(a => a.Status);
            asset.HasIndex(a => a.ProjectId);
        });
    }

    private static void ConfigureAuditing(ModelBuilder builder)
    {
        foreach (var entityType in builder.Model.GetEntityTypes()
                     .Where(e => typeof(Entity).IsAssignableFrom(e.ClrType)))
        {
            builder.Entity(entityType.ClrType)
                .Property(nameof(Entity.CreatedBy)).HasMaxLength(128);

            builder.Entity(entityType.ClrType)
                .Property(nameof(Entity.UpdatedBy)).HasMaxLength(128);

            // Soft-deleted rows are invisible to every query unless a caller
            // explicitly opts out with IgnoreQueryFilters().
            builder.Entity(entityType.ClrType).HasQueryFilter(
                BuildNotDeletedFilter(entityType.ClrType));
        }
    }

    private static System.Linq.Expressions.LambdaExpression BuildNotDeletedFilter(Type clrType)
    {
        var parameter = System.Linq.Expressions.Expression.Parameter(clrType, "e");
        var property = System.Linq.Expressions.Expression.Property(parameter, nameof(Entity.DeletedAt));
        var nullConstant = System.Linq.Expressions.Expression.Constant(null, typeof(DateTimeOffset?));
        var body = System.Linq.Expressions.Expression.Equal(property, nullConstant);

        return System.Linq.Expressions.Expression.Lambda(body, parameter);
    }

    public override int SaveChanges()
    {
        StampAudit();
        return base.SaveChanges();
    }

    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        StampAudit();
        return base.SaveChangesAsync(cancellationToken);
    }

    private void StampAudit()
    {
        var now = DateTimeOffset.UtcNow;

        foreach (var entry in ChangeTracker.Entries<Entity>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
            }
        }
    }
}
