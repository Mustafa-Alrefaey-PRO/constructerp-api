using ConstructErp.Application.Common;
using ConstructErp.Application.Transport;
using ConstructErp.Domain.Requests;
using ConstructErp.Domain.Transport;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Transport moves.
/// </summary>
/// <remarks>
/// Same shape as the request workflow: no endpoint sets a status. Each
/// transition records one event and states its own precondition, and the
/// status is derived from the events by TransportSchedule. The rule that
/// actually matters — a move cannot depart before it is approved — was
/// decoration in the prototype, where a client could simply write "In Transit".
/// </remarks>
public static class TransportEndpoints
{
    public static RouteGroupBuilder MapTransportEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/transport").WithTags("Transport");

        group.MapGet("/", GetAll).WithName("GetTransportMoves");
        group.MapGet("/{id:guid}", GetById).WithName("GetTransportMove");
        group.MapPost("/", Create).WithName("CreateTransportMove");
        group.MapPut("/{id:guid}", Update).WithName("UpdateTransportMove");
        group.MapPost("/{id:guid}/approve", Approve).WithName("ApproveTransportMove");
        group.MapPost("/{id:guid}/depart", Depart).WithName("DepartTransportMove");
        group.MapPost("/{id:guid}/arrive", Arrive).WithName("ArriveTransportMove");
        group.MapPost("/{id:guid}/cancel", Cancel).WithName("CancelTransportMove");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteTransportMove");

        return group;
    }

    /// <param name="late">true for moves that missed their slot and have not left.</param>
    /// <param name="open">true for moves not yet arrived or cancelled.</param>
    private static async Task<IResult> GetAll(
        Guid? projectId,
        Guid? equipmentId,
        bool? open,
        bool? late,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var query = db.TransportMoves.AsQueryable();

        if (projectId is { } project)
        {
            query = query.Where(move => move.ProjectId == project);
        }

        if (equipmentId is { } asset)
        {
            query = query.Where(move => move.EquipmentId == asset);
        }

        if (open is { } stillOpen)
        {
            query = stillOpen
                ? query.Where(move => move.ArrivedAt == null && move.CancelledAt == null)
                : query.Where(move => move.ArrivedAt != null || move.CancelledAt != null);
        }

        if (late == true)
        {
            // The same rule TransportSchedule.IsLate applies, expressed so SQL
            // Server runs it against the (DepartedAt, ScheduledFor) index.
            query = query.Where(move =>
                move.DepartedAt == null
                && move.ArrivedAt == null
                && move.CancelledAt == null
                && move.ScheduledFor < now);
        }

        // Ordered before projecting: OrderBy applied afterwards makes EF inline
        // the Select into the ordering and the query stops translating.
        var rows = await Project(query
            .OrderBy(move => move.ArrivedAt != null || move.CancelledAt != null)
            .ThenBy(move => move.ScheduledFor))
            .ToListAsync(ct);

        return Results.Ok(rows.Select(row => ToDto(row, now)).ToList());
    }

    private static async Task<IResult> GetById(
        Guid id, ErpDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var row = await Project(db.TransportMoves.Where(m => m.Id == id)).FirstOrDefaultAsync(ct);

        return row is null ? Results.NotFound() : Results.Ok(ToDto(row, clock.GetUtcNow()));
    }

    private static async Task<IResult> Create(
        SaveTransportMoveRequest body, ErpDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var move = new TransportMove();
        var failure = await ApplyAsync(body, move, null, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        db.TransportMoves.Add(move);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/transport/{move.Id}", await Single(move.Id, db, clock, ct));
    }

    private static async Task<IResult> Update(
        Guid id,
        SaveTransportMoveRequest body,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var move = await db.TransportMoves.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (move is null)
        {
            return Results.NotFound();
        }

        var failure = await ApplyAsync(body, move, id, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        await db.SaveChangesAsync(ct);

        return Results.Ok(await Single(id, db, clock, ct));
    }

    private static Task<IResult> Approve(
        Guid id, TransportEventRequest? body, ErpDbContext db, TimeProvider clock,
        CancellationToken ct) =>
        Transition(id, body, db, clock, ct, TransportSchedule.CanApprove,
            (move, at) => move.ApprovedAt = at);

    private static Task<IResult> Depart(
        Guid id, TransportEventRequest? body, ErpDbContext db, TimeProvider clock,
        CancellationToken ct) =>
        Transition(id, body, db, clock, ct, TransportSchedule.CanDepart,
            (move, at) => move.DepartedAt = at);

    private static Task<IResult> Arrive(
        Guid id, TransportEventRequest? body, ErpDbContext db, TimeProvider clock,
        CancellationToken ct) =>
        Transition(id, body, db, clock, ct, TransportSchedule.CanArrive,
            (move, at) => move.ArrivedAt = at);

    private static Task<IResult> Cancel(
        Guid id, TransportEventRequest? body, ErpDbContext db, TimeProvider clock,
        CancellationToken ct) =>
        Transition(id, body, db, clock, ct, TransportSchedule.CanCancel,
            (move, at) => move.CancelledAt = at);

    /// <summary>
    /// One shape for every transition: load, ask the guard, record the event.
    /// </summary>
    /// <remarks>
    /// A refused transition is a 409 carrying the domain's own sentence, not a
    /// generic failure — the caller can show it to the user as-is.
    /// </remarks>
    private static async Task<IResult> Transition(
        Guid id,
        TransportEventRequest? body,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct,
        Func<TransportMove, TransitionResult> guard,
        Action<TransportMove, DateTimeOffset> record)
    {
        var move = await db.TransportMoves.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (move is null)
        {
            return Results.NotFound();
        }

        var decision = guard(move);

        if (!decision.Allowed)
        {
            return Results.Conflict(new { error = decision.Reason });
        }

        record(move, body?.OccurredAt ?? clock.GetUtcNow());
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Single(id, db, clock, ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var move = await db.TransportMoves.FirstOrDefaultAsync(m => m.Id == id, ct);

        if (move is null)
        {
            return Results.NotFound();
        }

        move.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult?> ApplyAsync(
        SaveTransportMoveRequest body,
        TransportMove move,
        Guid? id,
        ErpDbContext db,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
        {
            return Results.BadRequest(new { error = "Code is required." });
        }

        if (body.Cost < 0)
        {
            return Results.BadRequest(new { error = "Cost cannot be negative." });
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

        TransportKind kind;

        try
        {
            kind = EnumLabels.ToTransportKind(body.Kind);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Results.BadRequest(new { error = ex.Message });
        }

        if (await db.TransportMoves.AnyAsync(
                m => m.Code == body.Code && (id == null || m.Id != id), ct))
        {
            return Results.Conflict(new { error = $"Move code {body.Code} is already in use." });
        }

        // No status assignment here, deliberately: there is nowhere for a
        // caller to put one, which is what keeps it honest.
        move.Code = body.Code.Trim();
        move.EquipmentId = body.EquipmentId;
        move.ProjectId = body.ProjectId;
        move.Origin = body.Origin.ToDomain();
        move.Destination = body.Destination.ToDomain();
        move.Kind = kind;
        move.ScheduledFor = body.ScheduledFor;
        move.Cost = body.Cost;
        move.Notes = body.Notes?.ToDomain() ?? new();

        return null;
    }

    private static async Task<TransportMoveDto> Single(
        Guid id, ErpDbContext db, TimeProvider clock, CancellationToken ct) =>
        ToDto(
            await Project(db.TransportMoves.Where(m => m.Id == id)).FirstAsync(ct),
            clock.GetUtcNow());

    /// <summary>The flat shape SQL Server returns, before status is derived.</summary>
    private sealed record MoveRow(
        Guid Id,
        string Code,
        Guid EquipmentId,
        string EquipmentCode,
        LocalizedTextDto EquipmentName,
        Guid? ProjectId,
        string? ProjectCode,
        LocalizedTextDto? ProjectName,
        LocalizedTextDto Origin,
        LocalizedTextDto Destination,
        TransportKind Kind,
        Guid? CarrierId,
        LocalizedTextDto? CarrierName,
        Guid? DriverId,
        string? DriverName,
        DateTimeOffset ScheduledFor,
        DateTimeOffset? ApprovedAt,
        DateTimeOffset? DepartedAt,
        DateTimeOffset? ArrivedAt,
        DateTimeOffset? CancelledAt,
        decimal Cost,
        LocalizedTextDto Notes);

    private static IQueryable<MoveRow> Project(IQueryable<TransportMove> source) =>
        source.Select(move => new MoveRow(
            move.Id,
            move.Code,
            move.EquipmentId,
            move.Equipment!.Code,
            new LocalizedTextDto(move.Equipment.Name.En, move.Equipment.Name.Ar),
            move.ProjectId,
            move.Project != null ? move.Project.Code : null,
            move.Project != null
                ? new LocalizedTextDto(move.Project.Name.En, move.Project.Name.Ar)
                : null,
            new LocalizedTextDto(move.Origin.En, move.Origin.Ar),
            new LocalizedTextDto(move.Destination.En, move.Destination.Ar),
            move.Kind,
            move.CarrierId,
            move.Carrier != null
                ? new LocalizedTextDto(move.Carrier.Name.En, move.Carrier.Name.Ar)
                : null,
            move.DriverId,
            move.Driver != null ? move.Driver.DisplayName : null,
            move.ScheduledFor,
            move.ApprovedAt,
            move.DepartedAt,
            move.ArrivedAt,
            move.CancelledAt,
            move.Cost,
            new LocalizedTextDto(move.Notes.En, move.Notes.Ar)));

    /// <remarks>
    /// Status is derived here rather than in the SQL projection, so
    /// <see cref="TransportSchedule"/> stays the single implementation of these
    /// rules. Filtering still happens in SQL — see the late filter in GetAll.
    /// </remarks>
    private static TransportMoveDto ToDto(MoveRow row, DateTimeOffset now)
    {
        var move = new TransportMove
        {
            ScheduledFor = row.ScheduledFor,
            ApprovedAt = row.ApprovedAt,
            DepartedAt = row.DepartedAt,
            ArrivedAt = row.ArrivedAt,
            CancelledAt = row.CancelledAt,
        };

        var actions = new List<string>();

        if (TransportSchedule.CanApprove(move).Allowed)
        {
            actions.Add("approve");
        }

        if (TransportSchedule.CanDepart(move).Allowed)
        {
            actions.Add("depart");
        }

        if (TransportSchedule.CanArrive(move).Allowed)
        {
            actions.Add("arrive");
        }

        if (TransportSchedule.CanCancel(move).Allowed)
        {
            actions.Add("cancel");
        }

        return new TransportMoveDto(
            row.Id,
            row.Code,
            row.EquipmentId,
            row.EquipmentCode,
            row.EquipmentName,
            row.ProjectId,
            row.ProjectCode,
            row.ProjectName,
            row.Origin,
            row.Destination,
            row.Kind.ToLabel(),
            row.CarrierId,
            row.CarrierName,
            row.DriverId,
            row.DriverName,
            row.ScheduledFor,
            row.ApprovedAt,
            row.DepartedAt,
            row.ArrivedAt,
            row.CancelledAt,
            row.Cost,
            row.Notes,
            TransportSchedule.StatusOf(move).ToLabel(),
            TransportSchedule.IsLate(move, now),
            actions);
    }
}
