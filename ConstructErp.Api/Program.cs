using ConstructErp.Api.Endpoints;
using ConstructErp.Infrastructure;
using Policies = ConstructErp.Infrastructure.DependencyInjection.Policies;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

// Registered rather than calling DateTime.Now anywhere: date-derived fields
// like a rental's overdue status are only testable if the clock can be
// substituted, and only consistent if every caller reads the same one.
builder.Services.AddSingleton(TimeProvider.System);

// The Angular dev server is a different origin, so it needs an explicit grant.
// Origins come from configuration rather than a wildcard: AllowAnyOrigin cannot
// be combined with credentials, and we will need cookies once auth lands.
const string DevCorsPolicy = "erp-dev-clients";
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

builder.Services.AddCors(options => options.AddPolicy(DevCorsPolicy, policy =>
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials()));

var app = builder.Build();

app.UseExceptionHandler();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    await app.Services.InitialiseDatabaseAsync();
}
else
{
    // Only in non-development: the Angular dev server talks plain HTTP, and
    // redirecting its API calls to HTTPS breaks CORS preflight locally.
    app.UseHttpsRedirection();

    // No migration here on purpose — the deploy pipeline applies those, so a
    // schema failure stops the release instead of leaving a half-migrated
    // database serving requests. This only makes sure an administrator
    // exists, because a freshly migrated database has nobody who can sign in
    // and every route requires authentication.
    await app.Services.EnsureAccountsAsync();
}

app.UseCors(DevCorsPolicy);

// Order matters: authentication establishes WHO the caller is, authorization
// then decides what they may reach. Both must sit after CORS so a rejected
// request still carries the headers the browser needs to read the response.
app.UseAuthentication();
app.UseAuthorization();

// Anonymous: a health probe has no credentials and must not need any.
app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health")
    .AllowAnonymous();

app.MapAuthEndpoints();

// Internal-only for now. The request and inspection workflows have no
// dedicated roles yet (see UserRole), so Admin operates them; adding
// ProjectManager and friends means changing this policy, not every endpoint.
app.MapProjectEndpoints().RequireAuthorization(Policies.InternalOnly);
app.MapEquipmentEndpoints().RequireAuthorization(Policies.InternalOnly);
app.MapRequestEndpoints().RequireAuthorization(Policies.InternalOnly);
app.MapCostEndpoints().RequireAuthorization(Policies.InternalOnly);
app.MapVendorEndpoints().RequireAuthorization(Policies.InternalOnly);
app.MapRentalEndpoints().RequireAuthorization(Policies.InternalOnly);

// Carriers and drivers reach this one. WHICH moves they see is not decided
// here — the query filter in ErpDbContext scopes the rows.
app.MapTransportEndpoints().RequireAuthorization(Policies.TransportAccess);

app.Run();

/// <summary>Exposed so the integration tests can reference the host.</summary>
public partial class Program;
