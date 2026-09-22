using ConstructErp.Api.Endpoints;
using ConstructErp.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddProblemDetails();

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

app.Run();

/// <summary>Exposed so the integration tests can reference the host.</summary>
public partial class Program;
