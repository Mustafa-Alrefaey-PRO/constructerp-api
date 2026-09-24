using System.Text;
using ConstructErp.Application.Common;
using ConstructErp.Domain.Identity;
using ConstructErp.Infrastructure.Identity;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

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
        services.AddAuthenticationAndScoping(configuration);

        return services;
    }

    /// <summary>Policy names, so endpoints never hard-code a role string.</summary>
    public static class Policies
    {
        public const string AdminOnly = "admin-only";

        /// <summary>Internal staff: everything that is not carrier-facing.</summary>
        public const string InternalOnly = "internal-only";

        /// <summary>Anyone who legitimately works with transport moves.</summary>
        public const string TransportAccess = "transport-access";
    }

    private static IServiceCollection AddAuthenticationAndScoping(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()
            ?? new JwtOptions();

        // No fallback key. A hard-coded default works everywhere, which is
        // exactly why it survives to production unnoticed - and anyone holding
        // the source could then mint an admin token.
        if (string.IsNullOrWhiteSpace(jwt.SigningKey) || jwt.SigningKey.Length < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey is missing or shorter than 32 characters. Set it via "
                + "configuration or an environment variable; it must never be committed.");
        }

        services.Configure<JwtOptions>(configuration.GetSection(JwtOptions.SectionName));
        services.AddSingleton<TokenService>();

        // ICurrentUser is what the DbContext query filter reads, so it must be
        // scoped to the request - a singleton would leak one caller's scope to
        // every other caller.
        services.AddHttpContextAccessor();
        services.AddScoped<ICurrentUser, HttpCurrentUser>();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey =
                        new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
                    // Default is five minutes of leeway on expiry, which
                    // quietly extends every token's life. Tokens here are
                    // short-lived by design, so that slack matters.
                    ClockSkew = TimeSpan.Zero,
                };
            });

        services.AddAuthorization(options =>
        {
            options.AddPolicy(Policies.AdminOnly, policy =>
                policy.RequireRole(nameof(UserRole.Admin)));

            options.AddPolicy(Policies.InternalOnly, policy =>
                policy.RequireRole(nameof(UserRole.Admin)));

            options.AddPolicy(Policies.TransportAccess, policy =>
                policy.RequireRole(
                    nameof(UserRole.Admin),
                    nameof(UserRole.TruckingCompany),
                    nameof(UserRole.Driver)));

            // Every endpoint requires a signed-in user unless it opts out with
            // AllowAnonymous. A default-deny fallback means forgetting to add
            // an attribute fails closed rather than publishing the route.
            options.FallbackPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
        });

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
