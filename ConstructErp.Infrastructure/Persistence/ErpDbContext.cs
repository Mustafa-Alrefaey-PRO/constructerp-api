using ConstructErp.Application.Common;
using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Identity;
using ConstructErp.Domain.Projects;
using ConstructErp.Domain.Rentals;
using ConstructErp.Domain.Requests;
using ConstructErp.Domain.Transport;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Infrastructure.Persistence;

public sealed class ErpDbContext(DbContextOptions<ErpDbContext> options, ICurrentUser currentUser)
    : DbContext(options)
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

    public DbSet<EquipmentRequest> Requests => Set<EquipmentRequest>();

    public DbSet<RequestCheck> RequestChecks => Set<RequestCheck>();

    public DbSet<Vendor> Vendors => Set<Vendor>();

    public DbSet<Rental> Rentals => Set<Rental>();

    public DbSet<TransportMove> TransportMoves => Set<TransportMove>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<AppUser> Users => Set<AppUser>();

    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

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
        ConfigureRequests(builder);
        ConfigureRentals(builder);
        ConfigureTransport(builder);
        ConfigureIdentity(builder);
        ConfigureAuditing(builder);

        // AFTER auditing: it sets a soft-delete filter on every entity, and EF
        // allows only one filter per entity type, so the scoped entities have
        // to restate soft-delete inside their own combined expression.
        ConfigureScoping(builder);

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

    private static void ConfigureRequests(ModelBuilder builder)
    {
        builder.Entity<EquipmentRequest>(request =>
        {
            request.HasIndex(r => r.Code).IsUnique();
            request.Property(r => r.Code).HasMaxLength(32);
            request.Property(r => r.RequestedBy).HasMaxLength(128);
            request.Property(r => r.Status).HasConversion<int>();
            request.Property(r => r.Ownership).HasConversion<int>();

            // Stage is derived from Status, so it is not a column. Persisting
            // both is what let the prototype hold contradictory values.
            request.Ignore(r => r.Stage);

            request.ComplexProperty(r => r.Location).IsRequired();
            request.ComplexProperty(r => r.Purpose).IsRequired();

            // Optional, so it cannot be a complex property — those are always
            // present. Two plain nullable columns instead.
            request.OwnsOne(r => r.RejectionReason, reason =>
            {
                reason.Property(text => text.En).HasColumnName("RejectionReason_En");
                reason.Property(text => text.Ar).HasColumnName("RejectionReason_Ar");
            });

            // Restrict: an asset with request history cannot be quietly
            // removed from under it.
            request.HasOne(r => r.Equipment)
                .WithMany()
                .HasForeignKey(r => r.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            request.HasOne(r => r.Project)
                .WithMany()
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            request.HasMany(r => r.Checks)
                .WithOne(c => c.Request!)
                .HasForeignKey(c => c.RequestId)
                .OnDelete(DeleteBehavior.Cascade);

            request.HasIndex(r => r.Status);
            request.HasIndex(r => r.EquipmentId);
        });

        builder.Entity<RequestCheck>(check =>
        {
            check.Property(c => c.Kind).HasConversion<int>();
            check.Property(c => c.Code).HasMaxLength(64);
            check.ComplexProperty(c => c.Label).IsRequired();
            check.HasIndex(c => new { c.RequestId, c.Kind, c.Sequence });
        });
    }

    private static void ConfigureRentals(ModelBuilder builder)
    {
        builder.Entity<Vendor>(vendor =>
        {
            vendor.HasIndex(v => v.Code).IsUnique();
            vendor.Property(v => v.Code).HasMaxLength(32);
            vendor.Property(v => v.ContactName).HasMaxLength(128);
            vendor.Property(v => v.Phone).HasMaxLength(32);
            vendor.Property(v => v.Email).HasMaxLength(256);

            vendor.ComplexProperty(v => v.Name).IsRequired();
        });

        builder.Entity<Rental>(rental =>
        {
            rental.HasIndex(r => r.Code).IsUnique();
            rental.Property(r => r.Code).HasMaxLength(32);

            rental.ComplexProperty(r => r.Notes).IsRequired();

            // No Status column. A rental's status is derived from its dates by
            // RentalSchedule, so there is nothing here to fall out of date.

            // Restrict: a vendor with hire history cannot be deleted out from
            // under the spend that is attributed to it.
            rental.HasOne(r => r.Vendor)
                .WithMany(v => v.Rentals)
                .HasForeignKey(r => r.VendorId)
                .OnDelete(DeleteBehavior.Restrict);

            rental.HasOne(r => r.Equipment)
                .WithMany()
                .HasForeignKey(r => r.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            rental.HasOne(r => r.Project)
                .WithMany()
                .HasForeignKey(r => r.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            // The index behind "what is overdue" and "what is due this week":
            // both filter on still-on-hire rows ordered by due date.
            rental.HasIndex(r => new { r.ReturnedOn, r.ExpectedReturnOn });
            rental.HasIndex(r => r.VendorId);
            rental.HasIndex(r => r.ProjectId);
        });
    }

    private static void ConfigureTransport(ModelBuilder builder)
    {
        builder.Entity<TransportMove>(move =>
        {
            move.HasIndex(m => m.Code).IsUnique();
            move.Property(m => m.Code).HasMaxLength(32);
            move.Property(m => m.Kind).HasConversion<int>();

            move.ComplexProperty(m => m.Origin).IsRequired();
            move.ComplexProperty(m => m.Destination).IsRequired();
            move.ComplexProperty(m => m.Notes).IsRequired();

            // No Status column. A move's status is derived from its four event
            // timestamps by TransportSchedule; there is nothing to fall stale.

            move.HasOne(m => m.Equipment)
                .WithMany()
                .HasForeignKey(m => m.EquipmentId)
                .OnDelete(DeleteBehavior.Restrict);

            move.HasOne(m => m.Project)
                .WithMany()
                .HasForeignKey(m => m.ProjectId)
                .OnDelete(DeleteBehavior.SetNull);

            // Backs "what is still to come" and "what is running late": both
            // filter undeparted moves ordered by their slot.
            // Restrict: a carrier with jobs on record cannot be removed from
            // under them, and neither can a driver with a delivery history.
            move.HasOne(m => m.Carrier)
                .WithMany()
                .HasForeignKey(m => m.CarrierId)
                .OnDelete(DeleteBehavior.Restrict);

            move.HasOne(m => m.Driver)
                .WithMany()
                .HasForeignKey(m => m.DriverId)
                .OnDelete(DeleteBehavior.Restrict);

            move.HasIndex(m => new { m.DepartedAt, m.ScheduledFor });
            move.HasIndex(m => m.ProjectId);
            move.HasIndex(m => m.EquipmentId);
            // The scoping columns are on the hot path of every transport read,
            // because the query filter adds them to every single query.
            move.HasIndex(m => m.CarrierId);
            move.HasIndex(m => m.DriverId);
        });
    }

    private static void ConfigureIdentity(ModelBuilder builder)
    {
        builder.Entity<Organization>(org =>
        {
            org.HasIndex(o => o.Code).IsUnique();
            org.Property(o => o.Code).HasMaxLength(32);
            org.Property(o => o.ContactName).HasMaxLength(128);
            org.Property(o => o.Phone).HasMaxLength(32);
            org.Property(o => o.Kind).HasConversion<int>();
            org.ComplexProperty(o => o.Name).IsRequired();
        });

        builder.Entity<AppUser>(user =>
        {
            // Unique on email: it is the login identifier. Stored lowercased,
            // so the uniqueness check cannot be sidestepped by capitalisation.
            user.HasIndex(u => u.Email).IsUnique();
            user.Property(u => u.Email).HasMaxLength(256);
            user.Property(u => u.DisplayName).HasMaxLength(128);
            user.Property(u => u.Role).HasConversion<int>();

            // PBKDF2 output from PasswordHasher, base64-encoded.
            user.Property(u => u.PasswordHash).HasMaxLength(512);

            user.HasOne(u => u.Organization)
                .WithMany(o => o.Users)
                .HasForeignKey(u => u.OrganizationId)
                .OnDelete(DeleteBehavior.Restrict);

            user.HasMany(u => u.RefreshTokens)
                .WithOne(t => t.User!)
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            user.HasIndex(u => u.OrganizationId);
        });

        builder.Entity<RefreshToken>(token =>
        {
            // Looked up by hash on every refresh, so it needs its own index.
            token.HasIndex(t => t.TokenHash).IsUnique();
            token.Property(t => t.TokenHash).HasMaxLength(128);
            token.HasIndex(t => t.UserId);
        });
    }

    /// <summary>
    /// Row-level scoping: whose records a signed-in user may read.
    /// </summary>
    /// <remarks>
    /// Enforced here rather than in each endpoint on purpose. An endpoint that
    /// forgets its WHERE clause leaks data silently and looks perfectly correct
    /// in review; a filter on the model applies to every query that will ever
    /// be written, including ones added years from now by someone who has never
    /// read this file.
    ///
    /// The comparisons read from <c>currentUser</c>, which is scoped per
    /// request. EF parameterises instance members referenced in a filter, so
    /// this does not bake one user's id into a cached query plan.
    ///
    /// Both scope values are null for an Admin and for system contexts, which
    /// makes the filter a no-op. That is why scoped endpoints must ALSO require
    /// authorization: a filter cannot protect a route that never established
    /// who was calling.
    ///
    /// <c>IgnoreQueryFilters()</c> bypasses this as well as soft-delete, so
    /// treat any new use of it as a security review.
    /// </remarks>
    private void ConfigureScoping(ModelBuilder builder)
    {
        builder.Entity<TransportMove>().HasQueryFilter(move =>
            move.DeletedAt == null
            // A carrier's staff see their carrier's moves.
            && (currentUser.ScopeOrganizationId == null
                || move.CarrierId == currentUser.ScopeOrganizationId)
            // A driver is scoped tighter still: their own assignments only.
            && (currentUser.ScopeDriverId == null
                || move.DriverId == currentUser.ScopeDriverId));
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
            // CreatedBy/UpdatedBy have existed since the first migration and
            // were always NULL, because there was no user to attribute
            // anything to. This is where the audit trail starts working.
            var actor = currentUser.UserId?.ToString();

            if (entry.State == EntityState.Added)
            {
                entry.Entity.CreatedAt = now;
                entry.Entity.CreatedBy ??= actor;
            }
            else if (entry.State == EntityState.Modified)
            {
                entry.Entity.UpdatedAt = now;
                entry.Entity.UpdatedBy = actor ?? entry.Entity.UpdatedBy;
            }
        }
    }
}
