using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ConstructErp.Application.Identity;
using ConstructErp.Infrastructure;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
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

    /// <summary>
    /// A client already signed in as the seeded administrator.
    /// </summary>
    /// <remarks>
    /// Every endpoint now requires authentication, so this is what most tests
    /// want. It logs in through the real endpoint rather than forging a token,
    /// which means the login path itself is exercised by every test that runs.
    /// </remarks>
    public Task<HttpClient> AdminAsync() => SignInAsync("AdminEmail", "AdminPassword");

    /// <summary>Signed in as a carrier's office staff: scoped to that carrier.</summary>
    public Task<HttpClient> CarrierAsync() => SignInAsync("CarrierEmail", "CarrierPassword");

    /// <summary>Signed in as a driver: scoped to their own assignments.</summary>
    public Task<HttpClient> DriverAsync() => SignInAsync("DriverEmail", "DriverPassword");

    /// <summary>No credentials at all, for asserting that routes are closed.</summary>
    public HttpClient AnonymousClient() => CreateClient();

    public (string Email, string Password) Credentials(string emailKey, string passwordKey)
    {
        var configuration = Services.GetRequiredService<IConfiguration>().GetSection("Seed");

        return (configuration[emailKey]!, configuration[passwordKey]!);
    }

    private async Task<HttpClient> SignInAsync(string emailKey, string passwordKey)
    {
        var (email, password) = Credentials(emailKey, passwordKey);
        var client = CreateClient();

        var response = await client.PostAsJsonAsync(
            "/api/auth/login", new LoginRequest(email, password), JsonOptions);

        response.EnsureSuccessStatusCode();

        var auth = await response.Content.ReadFromJsonAsync<AuthResultDto>(JsonOptions);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", auth!.AccessToken);

        return client;
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>Runs an assertion directly against the database.</summary>
    public async Task WithDbAsync(Func<ErpDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<ErpDbContext>());
    }
}

[CollectionDefinition(nameof(ApiCollection))]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>;
