using ConstructErp.Application.Common;
using ConstructErp.Application.Projects;
using ConstructErp.Domain.Projects;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

public static class ProjectEndpoints
{
    public static RouteGroupBuilder MapProjectEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/projects").WithTags("Projects");

        group.MapGet("/", GetAll).WithName("GetProjects");
        group.MapGet("/{id:guid}", GetById).WithName("GetProject");
        group.MapPost("/", Create).WithName("CreateProject");
        group.MapPut("/{id:guid}", Update).WithName("UpdateProject");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteProject");

        return group;
    }

    private static async Task<IResult> GetAll(ErpDbContext db, CancellationToken ct)
    {
        // Ordered BEFORE projecting: EF inlines a Select into a following
        // OrderBy and then cannot translate "order by this whole record".
        var projects = await Project(db.Projects.OrderBy(p => p.Code)).ToListAsync(ct);

        return Results.Ok(projects);
    }

    private static async Task<IResult> GetById(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var project = await Project(db.Projects.Where(p => p.Id == id)).FirstOrDefaultAsync(ct);

        return project is null ? Results.NotFound() : Results.Ok(project);
    }

    private static async Task<IResult> Create(
        SaveProjectRequest request, ErpDbContext db, CancellationToken ct)
    {
        if (await db.Projects.AnyAsync(p => p.Code == request.Code, ct))
        {
            return Results.Conflict(new { error = $"Project code '{request.Code}' already exists." });
        }

        var project = new Domain.Projects.Project();

        if (!TryApply(request, project, out var error))
        {
            return Results.BadRequest(new { error });
        }

        db.Projects.Add(project);
        await db.SaveChangesAsync(ct);

        var created = await Project(db.Projects.Where(p => p.Id == project.Id)).FirstAsync(ct);

        return Results.Created($"/api/projects/{project.Id}", created);
    }

    private static async Task<IResult> Update(
        Guid id, SaveProjectRequest request, ErpDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (project is null)
        {
            return Results.NotFound();
        }

        // Codes are editable but must stay unique — excluding this row, or an
        // unchanged code would collide with itself.
        if (await db.Projects.AnyAsync(p => p.Code == request.Code && p.Id != id, ct))
        {
            return Results.Conflict(new { error = $"Project code '{request.Code}' already exists." });
        }

        if (!TryApply(request, project, out var error))
        {
            return Results.BadRequest(new { error });
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await Project(db.Projects.Where(p => p.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var project = await db.Projects.FirstOrDefaultAsync(p => p.Id == id, ct);

        if (project is null)
        {
            return Results.NotFound();
        }

        // Soft delete. Equipment pointing at it keeps its own row: the FK is
        // ON DELETE SET NULL, but since the row survives, assignments are
        // preserved and can be restored by clearing DeletedAt.
        project.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static bool TryApply(
        SaveProjectRequest request, Domain.Projects.Project project, out string? error)
    {
        error = null;

        if (string.IsNullOrWhiteSpace(request.Code))
        {
            error = "Code is required.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(request.Name.En))
        {
            error = "An English name is required.";
            return false;
        }

        if (request.Budget < 0)
        {
            error = "Budget cannot be negative.";
            return false;
        }

        ProjectStatus status;

        try
        {
            status = EnumLabels.ToProjectStatus(request.Status);
        }
        catch (ArgumentOutOfRangeException)
        {
            error = $"Unknown status '{request.Status}'.";
            return false;
        }

        project.Code = request.Code.Trim();
        project.Name = request.Name.ToDomain();
        project.Client = request.Client.ToDomain();
        project.Manager = request.Manager?.Trim() ?? string.Empty;
        project.Location = request.Location?.ToDomain() ?? new();
        project.Status = status;
        project.Budget = request.Budget;
        project.Progress = Math.Clamp(request.Progress, 0, 100);
        project.StartDate = request.StartDate;
        project.EndDate = request.EndDate;

        return true;
    }

    /// <summary>
    /// Projects a Project with its spend summed from cost entries, so totals
    /// can never disagree with the rows behind them.
    /// </summary>
    private static IQueryable<ProjectDto> Project(IQueryable<Domain.Projects.Project> source) =>
        source.Select(p => new ProjectDto(
            p.Id,
            p.Code,
            new LocalizedTextDto(p.Name.En, p.Name.Ar),
            new LocalizedTextDto(p.Client.En, p.Client.Ar),
            p.Manager,
            new LocalizedTextDto(p.Location.En, p.Location.Ar),
            p.Status == ProjectStatus.Active ? "Active"
                : p.Status == ProjectStatus.AtRisk ? "At Risk"
                : "Closing",
            p.Budget,
            p.Progress,
            p.StartDate,
            p.EndDate,
            p.CostEntries.Where(c => c.Category == CostCategory.Equipment).Sum(c => (decimal?)c.Amount) ?? 0m,
            p.CostEntries.Where(c => c.Category == CostCategory.Transport).Sum(c => (decimal?)c.Amount) ?? 0m,
            p.CostEntries.Where(c => c.Category == CostCategory.Extras).Sum(c => (decimal?)c.Amount) ?? 0m,
            p.Equipment.Count));
}
