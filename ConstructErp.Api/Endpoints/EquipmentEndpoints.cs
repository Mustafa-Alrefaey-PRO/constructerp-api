using ConstructErp.Application.Common;
using ConstructErp.Application.Equipment;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Requests;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

public static class EquipmentEndpoints
{
    public static RouteGroupBuilder MapEquipmentEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/equipment").WithTags("Equipment");

        group.MapGet("/", GetAll).WithName("GetEquipment");
        group.MapGet("/types", GetTypes).WithName("GetEquipmentTypes");
        group.MapGet("/{id:guid}", GetById).WithName("GetEquipmentAsset");
        group.MapPost("/", Create).WithName("CreateEquipment");
        group.MapPut("/{id:guid}", Update).WithName("UpdateEquipment");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteEquipment");

        return group;
    }

    private static async Task<IResult> GetAll(ErpDbContext db, CancellationToken ct) =>
        // Ordered BEFORE projecting: EF inlines a Select into a following
        // OrderBy and then cannot translate "order by this whole record".
        Results.Ok(await Project(db.Equipment.OrderBy(e => e.Code)).ToListAsync(ct));

    private static async Task<IResult> GetById(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var asset = await Project(db.Equipment.Where(e => e.Id == id)).FirstOrDefaultAsync(ct);

        return asset is null ? Results.NotFound() : Results.Ok(asset);
    }

    private static async Task<IResult> GetTypes(ErpDbContext db, CancellationToken ct) =>
        Results.Ok(await db.EquipmentTypes
            .OrderBy(t => t.Code)
            .Select(t => new EquipmentTypeDto(t.Id, t.Code, new LocalizedTextDto(t.Name.En, t.Name.Ar)))
            .ToListAsync(ct));

    private static async Task<IResult> Create(
        SaveEquipmentRequest request, ErpDbContext db, CancellationToken ct)
    {
        if (await db.Equipment.AnyAsync(e => e.Code == request.Code, ct))
        {
            return Results.Conflict(new { error = $"Equipment code '{request.Code}' already exists." });
        }

        var asset = new EquipmentAsset();
        var result = await TryApplyAsync(request, asset, db, ct);

        if (result is not null)
        {
            return result;
        }

        db.Equipment.Add(asset);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/equipment/{asset.Id}",
            await Project(db.Equipment.Where(e => e.Id == asset.Id)).FirstAsync(ct));
    }

    private static async Task<IResult> Update(
        Guid id, SaveEquipmentRequest request, ErpDbContext db, CancellationToken ct)
    {
        var asset = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (asset is null)
        {
            return Results.NotFound();
        }

        if (await db.Equipment.AnyAsync(e => e.Code == request.Code && e.Id != id, ct))
        {
            return Results.Conflict(new { error = $"Equipment code '{request.Code}' already exists." });
        }

        var wasWorking = asset.Status == EquipmentStatus.Working;
        var result = await TryApplyAsync(request, asset, db, ct);

        if (result is not null)
        {
            return result;
        }

        // THE rule this product exists to enforce: an asset may not be put to
        // work until a request for it has been approved, received and has
        // passed pre-use inspection. Checked only on the TRANSITION into
        // Working, so existing records are still editable.
        if (!wasWorking && asset.Status == EquipmentStatus.Working)
        {
            var requests = await db.Requests
                .Where(r => r.EquipmentId == id)
                .ToListAsync(ct);

            var guard = RequestWorkflow.CanPutToWork(requests);

            if (!guard.Allowed)
            {
                return Results.Conflict(new { error = guard.Reason });
            }
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await Project(db.Equipment.Where(e => e.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var asset = await db.Equipment.FirstOrDefaultAsync(e => e.Id == id, ct);

        if (asset is null)
        {
            return Results.NotFound();
        }

        asset.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    /// <summary>Returns null when the request is valid and has been applied.</summary>
    private static async Task<IResult?> TryApplyAsync(
        SaveEquipmentRequest request, EquipmentAsset asset, ErpDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Code))
        {
            return Results.BadRequest(new { error = "Code is required." });
        }

        if (string.IsNullOrWhiteSpace(request.Name.En))
        {
            return Results.BadRequest(new { error = "An English name is required." });
        }

        if (request.DailyCost < 0)
        {
            return Results.BadRequest(new { error = "Daily cost cannot be negative." });
        }

        if (!await db.EquipmentTypes.AnyAsync(t => t.Id == request.EquipmentTypeId, ct))
        {
            return Results.BadRequest(new { error = "Unknown equipment type." });
        }

        // Checked explicitly rather than letting the FK fail: a 400 naming the
        // problem beats a 500 carrying a constraint-violation message.
        if (request.ProjectId is { } projectId
            && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            return Results.BadRequest(new { error = "Unknown project." });
        }

        Ownership ownership;
        EquipmentStatus status;

        try
        {
            ownership = EnumLabels.ToOwnership(request.Ownership);
            status = EnumLabels.ToEquipmentStatus(request.Status);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        asset.Code = request.Code.Trim();
        asset.Name = request.Name.ToDomain();
        asset.EquipmentTypeId = request.EquipmentTypeId;
        asset.Ownership = ownership;
        asset.ProjectId = request.ProjectId;
        asset.Status = status;
        asset.Utilization = Math.Clamp(request.Utilization, 0, 100);
        asset.DailyCost = request.DailyCost;
        asset.NextAction = request.NextAction?.ToDomain() ?? new();

        return null;
    }

    private static IQueryable<EquipmentDto> Project(IQueryable<EquipmentAsset> source) =>
        source.Select(e => new EquipmentDto(
            e.Id,
            e.Code,
            new LocalizedTextDto(e.Name.En, e.Name.Ar),
            e.EquipmentTypeId,
            new LocalizedTextDto(e.EquipmentType!.Name.En, e.EquipmentType.Name.Ar),
            e.Ownership == Ownership.Owned ? "Owned" : "External Rental",
            e.ProjectId,
            e.Project != null ? e.Project.Code : null,
            e.Project != null ? new LocalizedTextDto(e.Project.Name.En, e.Project.Name.Ar) : null,
            e.Status == EquipmentStatus.Working ? "Working"
                : e.Status == EquipmentStatus.Idle ? "Idle"
                : e.Status == EquipmentStatus.InTransit ? "In Transit"
                : e.Status == EquipmentStatus.InspectionDue ? "Inspection Due"
                : "Return Scheduled",
            e.Utilization,
            e.DailyCost,
            new LocalizedTextDto(e.NextAction.En, e.NextAction.Ar)));
}
