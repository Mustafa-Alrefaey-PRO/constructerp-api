using ConstructErp.Application.Common;
using ConstructErp.Application.Projects;
using ConstructErp.Domain.Common;
using ConstructErp.Domain.Projects;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Cost entries — the detail behind every project spend figure.
/// </summary>
/// <remarks>
/// Project totals are summed from these rows, never stored, so a total cannot
/// disagree with the records behind it. The prototype kept equipmentSpend and
/// friends as manually typed numbers on the project, which could say anything.
/// </remarks>
public static class CostEndpoints
{
    public static RouteGroupBuilder MapCostEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/costs").WithTags("Costs");

        group.MapGet("/", GetAll).WithName("GetCostEntries");
        group.MapPost("/", Create).WithName("CreateCostEntry");
        group.MapPut("/{id:guid}", Update).WithName("UpdateCostEntry");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteCostEntry");

        return group;
    }

    private static async Task<IResult> GetAll(
        Guid? projectId, ErpDbContext db, CancellationToken ct)
    {
        var query = db.CostEntries.AsQueryable();

        if (projectId is { } id)
        {
            query = query.Where(entry => entry.ProjectId == id);
        }

        return Results.Ok(await Project(query.OrderByDescending(e => e.IncurredOn)).ToListAsync(ct));
    }

    private static async Task<IResult> Create(
        SaveCostEntryRequest body, ErpDbContext db, CancellationToken ct)
    {
        var entry = new CostEntry();
        var failure = await ApplyAsync(body, entry, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        db.CostEntries.Add(entry);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/costs/{entry.Id}",
            await Project(db.CostEntries.Where(e => e.Id == entry.Id)).FirstAsync(ct));
    }

    private static async Task<IResult> Update(
        Guid id, SaveCostEntryRequest body, ErpDbContext db, CancellationToken ct)
    {
        var entry = await db.CostEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null)
        {
            return Results.NotFound();
        }

        var failure = await ApplyAsync(body, entry, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await Project(db.CostEntries.Where(e => e.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var entry = await db.CostEntries.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (entry is null)
        {
            return Results.NotFound();
        }

        entry.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult?> ApplyAsync(
        SaveCostEntryRequest body, CostEntry entry, ErpDbContext db, CancellationToken ct)
    {
        if (body.Amount < 0)
        {
            // Negative spend is a credit note, which is a different record with
            // its own rules — not something to sneak through this endpoint.
            return Results.BadRequest(new { error = "Amount cannot be negative." });
        }

        if (!await db.Projects.AnyAsync(p => p.Id == body.ProjectId, ct))
        {
            return Results.BadRequest(new { error = "Unknown project." });
        }

        CostCategory category;

        try
        {
            category = EnumLabels.ToCostCategory(body.Category);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        entry.ProjectId = body.ProjectId;
        entry.Category = category;
        entry.Amount = body.Amount;
        entry.IncurredOn = body.IncurredOn;
        entry.Description = body.Description?.ToDomain() ?? new LocalizedText();

        return null;
    }

    private static IQueryable<CostEntryDto> Project(IQueryable<CostEntry> source) =>
        source.Select(entry => new CostEntryDto(
            entry.Id,
            entry.ProjectId,
            entry.Project!.Code,
            entry.Category == CostCategory.Equipment ? "Equipment"
                : entry.Category == CostCategory.Transport ? "Transport"
                : "Extras",
            entry.Amount,
            entry.IncurredOn,
            new LocalizedTextDto(entry.Description.En, entry.Description.Ar)));
}
