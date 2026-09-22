using ConstructErp.Application.Common;
using ConstructErp.Application.Requests;
using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Requests;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

public static class RequestEndpoints
{
    public static RouteGroupBuilder MapRequestEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/requests").WithTags("Requests");

        group.MapGet("/", GetAll).WithName("GetRequests");
        group.MapGet("/{id:guid}", GetById).WithName("GetRequest");
        group.MapPost("/", Create).WithName("CreateRequest");
        group.MapPut("/{id:guid}", Update).WithName("UpdateRequest");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteRequest");

        group.MapPut("/{id:guid}/checks/{checkId:guid}", SetCheck).WithName("SetRequestCheck");

        // Workflow transitions are their own endpoints rather than a writable
        // status field, so every status change passes through a guard.
        group.MapPost("/{id:guid}/submit", Submit).WithName("SubmitRequest");
        group.MapPost("/{id:guid}/approve", Approve).WithName("ApproveRequest");
        group.MapPost("/{id:guid}/reject", Reject).WithName("RejectRequest");
        group.MapPost("/{id:guid}/receive", Receive).WithName("ReceiveRequest");
        group.MapPost("/{id:guid}/inspect", Inspect).WithName("InspectRequest");

        return group;
    }

    private static async Task<IResult> GetAll(ErpDbContext db, CancellationToken ct) =>
        Results.Ok(Map(await Load(db).OrderBy(r => r.Code).ToListAsync(ct)));

    private static async Task<IResult> GetById(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var request = await Load(db).FirstOrDefaultAsync(r => r.Id == id, ct);

        return request is null ? Results.NotFound() : Results.Ok(Map(request));
    }

    private static async Task<IResult> Create(
        SaveRequestRequest body, ErpDbContext db, CancellationToken ct)
    {
        if (await db.Requests.AnyAsync(r => r.Code == body.Code, ct))
        {
            return Results.Conflict(new { error = $"Request code '{body.Code}' already exists." });
        }

        var request = new EquipmentRequest();
        var failure = await ApplyAsync(body, request, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        db.Requests.Add(request);

        // Every request starts as a Draft with its gates unticked. Status is
        // never taken from the caller.
        request.Status = RequestStatus.Draft;
        foreach (var check in RequestCheckCatalogue.CreateFor(request.Id))
        {
            request.Checks.Add(check);
        }

        await db.SaveChangesAsync(ct);

        return Results.Created($"/api/requests/{request.Id}", await Reload(db, request.Id, ct));
    }

    private static async Task<IResult> Update(
        Guid id, SaveRequestRequest body, ErpDbContext db, CancellationToken ct)
    {
        var request = await Load(db).FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return Results.NotFound();
        }

        // Details are editable only before approval: changing the asset or the
        // cost of an approved request would invalidate the approval.
        if (request.Status is not (RequestStatus.Draft or RequestStatus.Rejected))
        {
            return Results.Conflict(new
            {
                error = $"A {request.Status} request cannot be edited. "
                    + "Only a draft or rejected request may change.",
            });
        }

        if (await db.Requests.AnyAsync(r => r.Code == body.Code && r.Id != id, ct))
        {
            return Results.Conflict(new { error = $"Request code '{body.Code}' already exists." });
        }

        var failure = await ApplyAsync(body, request, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await Reload(db, id, ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var request = await db.Requests.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return Results.NotFound();
        }

        request.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult> SetCheck(
        Guid id, Guid checkId, SetCheckRequest body, ErpDbContext db, CancellationToken ct)
    {
        var request = await Load(db).FirstOrDefaultAsync(r => r.Id == id, ct);
        var check = request?.Checks.FirstOrDefault(c => c.Id == checkId);

        if (request is null || check is null)
        {
            return Results.NotFound();
        }

        // Ticking a gate after it has been passed through would rewrite the
        // record of why a decision was made.
        var locked = check.Kind == CheckKind.PreRequest
            ? request.Status is not (RequestStatus.Draft or RequestStatus.Rejected)
            : request.Status is not (RequestStatus.Draft or RequestStatus.Rejected
                or RequestStatus.Submitted or RequestStatus.Approved);

        if (locked)
        {
            return Results.Conflict(new
            {
                error = $"This checklist can no longer be changed; the request is {request.Status}.",
            });
        }

        check.Passed = body.Passed;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Reload(db, id, ct));
    }

    private static Task<IResult> Submit(Guid id, ErpDbContext db, CancellationToken ct) =>
        Transition(id, db, ct, RequestWorkflow.CanSubmit, RequestStatus.Submitted);

    private static Task<IResult> Approve(Guid id, ErpDbContext db, CancellationToken ct) =>
        Transition(id, db, ct, RequestWorkflow.CanApprove, RequestStatus.Approved);

    private static Task<IResult> Receive(Guid id, ErpDbContext db, CancellationToken ct) =>
        Transition(id, db, ct, RequestWorkflow.CanReceive, RequestStatus.Received);

    private static async Task<IResult> Reject(
        Guid id, RejectRequest body, ErpDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Reason.En))
        {
            // A rejection without a reason is not actionable by whoever has to
            // fix and resubmit it.
            return Results.BadRequest(new { error = "A rejection reason is required." });
        }

        return await Transition(id, db, ct, RequestWorkflow.CanReject, RequestStatus.Rejected,
            request => request.RejectionReason = body.Reason.ToDomain());
    }

    private static async Task<IResult> Inspect(
        Guid id, InspectRequest body, ErpDbContext db, CancellationToken ct)
    {
        var request = await Load(db).FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return Results.NotFound();
        }

        var guard = RequestWorkflow.CanInspect(request);

        if (!guard.Allowed)
        {
            return Results.Conflict(new { error = guard.Reason });
        }

        // A failed inspection does not send the asset back to the start; it
        // parks the request until it is re-inspected.
        var next = body.Passed ? RequestStatus.ReadyToUse : RequestStatus.InspectionPending;

        if (!body.Passed)
        {
            request.RejectionReason = body.Reason?.ToDomain();
        }

        RequestWorkflow.Apply(request, next, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Reload(db, id, ct));
    }

    private static async Task<IResult> Transition(
        Guid id,
        ErpDbContext db,
        CancellationToken ct,
        Func<EquipmentRequest, TransitionResult> guard,
        RequestStatus next,
        Action<EquipmentRequest>? mutate = null)
    {
        var request = await Load(db).FirstOrDefaultAsync(r => r.Id == id, ct);

        if (request is null)
        {
            return Results.NotFound();
        }

        var result = guard(request);

        if (!result.Allowed)
        {
            // 409, not 400: the payload is fine, the request is in the wrong
            // state for this action.
            return Results.Conflict(new { error = result.Reason });
        }

        mutate?.Invoke(request);
        RequestWorkflow.Apply(request, next, DateTimeOffset.UtcNow);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Reload(db, id, ct));
    }

    private static async Task<IResult?> ApplyAsync(
        SaveRequestRequest body, EquipmentRequest request, ErpDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
        {
            return Results.BadRequest(new { error = "Code is required." });
        }

        if (body.EstimatedCost < 0)
        {
            return Results.BadRequest(new { error = "Estimated cost cannot be negative." });
        }

        if (body.RequiredDate is { } required && body.ReturnDate is { } ret && ret < required)
        {
            // Only possible to check because these are real dates now.
            return Results.BadRequest(new { error = "Return date cannot be before the required date." });
        }

        if (!await db.Equipment.AnyAsync(e => e.Id == body.EquipmentId, ct))
        {
            return Results.BadRequest(new { error = "Unknown equipment." });
        }

        if (body.ProjectId is { } projectId
            && !await db.Projects.AnyAsync(p => p.Id == projectId, ct))
        {
            return Results.BadRequest(new { error = "Unknown project." });
        }

        Ownership ownership;

        try
        {
            ownership = EnumLabels.ToOwnership(body.Ownership);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        request.Code = body.Code.Trim();
        request.EquipmentId = body.EquipmentId;
        request.ProjectId = body.ProjectId;
        request.Ownership = ownership;
        request.RequestedBy = body.RequestedBy?.Trim() ?? string.Empty;
        request.RequiredDate = body.RequiredDate;
        request.ReturnDate = body.ReturnDate;
        request.Location = body.Location?.ToDomain() ?? new LocalizedText();
        request.Purpose = body.Purpose?.ToDomain() ?? new LocalizedText();
        request.EstimatedCost = body.EstimatedCost;

        return null;
    }

    private static IQueryable<EquipmentRequest> Load(ErpDbContext db) =>
        db.Requests
            .Include(r => r.Equipment)
            .Include(r => r.Project)
            .Include(r => r.Checks);

    private static async Task<RequestDto> Reload(ErpDbContext db, Guid id, CancellationToken ct)
    {
        db.ChangeTracker.Clear();

        return Map(await Load(db).FirstAsync(r => r.Id == id, ct));
    }

    private static IReadOnlyList<RequestDto> Map(IEnumerable<EquipmentRequest> requests) =>
        requests.Select(Map).ToList();

    private static RequestDto Map(EquipmentRequest request) => new(
        request.Id,
        request.Code,
        request.EquipmentId,
        request.Equipment?.Code ?? string.Empty,
        LocalizedTextDto.From(request.Equipment?.Name ?? new LocalizedText()),
        request.ProjectId,
        request.Project?.Code,
        request.Project is null ? null : LocalizedTextDto.From(request.Project.Name),
        request.Ownership.ToLabel(),
        request.RequestedBy,
        request.RequiredDate,
        request.ReturnDate,
        LocalizedTextDto.From(request.Location),
        LocalizedTextDto.From(request.Purpose),
        request.EstimatedCost,
        request.Status.ToLabel(),
        request.Stage.ToLabel(),
        request.RejectionReason is null ? null : LocalizedTextDto.From(request.RejectionReason),
        request.Checks
            .OrderBy(check => check.Kind)
            .ThenBy(check => check.Sequence)
            .Select(check => new RequestCheckDto(
                check.Id,
                check.Kind == CheckKind.PreRequest ? "PreRequest" : "PreReceiving",
                check.Code,
                LocalizedTextDto.From(check.Label),
                check.Passed,
                check.Sequence))
            .ToList(),
        AvailableActions(request));

    /// <summary>What the workflow would currently permit, for the client's UI.</summary>
    private static IReadOnlyList<string> AvailableActions(EquipmentRequest request)
    {
        var actions = new List<string>();

        if (RequestWorkflow.CanSubmit(request).Allowed) actions.Add("submit");
        if (RequestWorkflow.CanApprove(request).Allowed) actions.Add("approve");
        if (RequestWorkflow.CanReject(request).Allowed) actions.Add("reject");
        if (RequestWorkflow.CanReceive(request).Allowed) actions.Add("receive");
        if (RequestWorkflow.CanInspect(request).Allowed) actions.Add("inspect");

        return actions;
    }
}
