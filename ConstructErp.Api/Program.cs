using ConstructErp.Api.Endpoints;
using ConstructErp.Infrastructure;

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
}

app.UseCors(DevCorsPolicy);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }))
    .WithName("Health");

app.MapProjectEndpoints();
app.MapEquipmentEndpoints();
app.MapRequestEndpoints();
app.MapCostEndpoints();
app.MapVendorEndpoints();
app.MapRentalEndpoints();

app.Run();

/// <summary>Exposed so the integration tests can reference the host.</summary>
public partial class Program;
