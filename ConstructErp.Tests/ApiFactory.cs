using ConstructErp.Infrastructure;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.Data.SqlClient;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace ConstructErp.Tests;

/// <summary>
/// Hosts the real API against a throwaway SQL Server database.
/// </summary>
/// <remarks>
/// Deliberately a REAL database, not the in-memory provider. The things most
/// worth protecting here are provider-specific — decimal(18,3) keeping three
/// places, NVARCHAR holding Arabic, foreign keys actually constraining — and
/// the in-memory provider verifies none of them. It would happily pass tests
/// for a schema SQL Server would reject.
///
/// The database is created once per test class collection and dropped at the
/// end, so a failed run leaves nothing behind.
/// </remarks>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private readonly string _databaseName = $"ConstructErp.Tests.{Guid.NewGuid():N}";

    /// <summary>
    /// Where the test database lives.
    /// </summary>
    /// <remarks>
    /// Locally this is the developer's SQL Server with Windows auth. CI has no
    /// Windows auth, so the workflow supplies a SQL-auth connection string for
    /// its service container through ConstructErp_TestSqlServer. The database
    /// name is always ours, whatever the caller put in theirs.
    /// </remarks>
    private string ConnectionString
    {
        get
        {
            var configured = Environment.GetEnvironmentVariable("ConstructErp_TestSqlServer");

            if (string.IsNullOrWhiteSpace(configured))
            {
                return $"Server=localhost;Database={_databaseName};"
                    + "Integrated Security=True;TrustServerCertificate=True";
            }

            var builder = new SqlConnectionStringBuilder(configured)
            {
                InitialCatalog = _databaseName,
            };

            return builder.ConnectionString;
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        // Development, so the app's own startup path (migrate + seed) runs —
        // that is part of what these tests are checking.
        builder.UseEnvironment(Environments.Development);

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(
                d => d.ServiceType == typeof(DbContextOptions<ErpDbContext>));

            if (descriptor is not null)
            {
                services.Remove(descriptor);
            }

            services.AddDbContext<ErpDbContext>(options => options.UseSqlServer(ConnectionString));
        });
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public new async Task DisposeAsync()
    {
        using (var scope = Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
            await db.Database.EnsureDeletedAsync();
        }

        await base.DisposeAsync();
    }

    /// <summary>Runs an assertion directly against the database.</summary>
    public async Task WithDbAsync(Func<ErpDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
