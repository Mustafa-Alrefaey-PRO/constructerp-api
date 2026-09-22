using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ConstructErp.Infrastructure;

public static class DependencyInjection
{
    public const string ConnectionStringName = "ErpDatabase";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString(ConnectionStringName)
            ?? throw new InvalidOperationException(
                $"Connection string '{ConnectionStringName}' is not configured.");

        services.AddDbContext<ErpDbContext>(options =>
            options.UseSqlServer(connectionString, sql =>
            {
                sql.MigrationsAssembly(typeof(ErpDbContext).Assembly.FullName);
                // Transient SQL Server faults (failovers, throttling) are worth
                // one automatic retry rather than surfacing as a 500.
                sql.EnableRetryOnFailure(maxRetryCount: 3, maxRetryDelay: TimeSpan.FromSeconds(5), errorNumbersToAdd: null);
            }));

        services.AddScoped<ErpDbSeeder>();

        return services;
    }

    /// <summary>
    /// Applies pending migrations and seeds. Development only — production
    /// migrations belong in the deploy pipeline, where a failure can stop the
    /// release rather than leaving a half-migrated database serving traffic.
    /// </summary>
    public static async Task InitialiseDatabaseAsync(this IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();

        var db = scope.ServiceProvider.GetRequiredService<ErpDbContext>();
        await db.Database.MigrateAsync();

        var seeder = scope.ServiceProvider.GetRequiredService<ErpDbSeeder>();
        await seeder.SeedAsync();
    }
}
