using ConstructErp.Application.Common;
using ConstructErp.Application.Rentals;
using ConstructErp.Domain.Rentals;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Hire agreements.
/// </summary>
/// <remarks>
/// There is no way to set a rental's status through this API, and that is the
/// point of the module. Status is derived from the dates by RentalSchedule, so
/// the only way to make a rental Overdue is to let its return date pass, and
/// the only way to clear it is to record the return. The prototype let a client
/// write "Overdue" — or fail to — which is why the dashboard's overdue count
/// was never trustworthy.
/// </remarks>
public static class RentalEndpoints
{
    public static RouteGroupBuilder MapRentalEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/rentals").WithTags("Rentals");

        group.MapGet("/", GetAll).WithName("GetRentals");
        group.MapGet("/{id:guid}", GetById).WithName("GetRental");
        group.MapPost("/", Create).WithName("CreateRental");
        group.MapPut("/{id:guid}", Update).WithName("UpdateRental");
        group.MapPost("/{id:guid}/book-return", BookReturn).WithName("BookRentalReturn");
        group.MapPost("/{id:guid}/return", RecordReturn).WithName("RecordRentalReturn");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteRental");

        return group;
    }

    /// <param name="onHire">true for rentals not yet returned, false for closed ones.</param>
    /// <param name="overdue">true for rentals past their return date and still out.</param>
    private static async Task<IResult> GetAll(
        Guid? projectId,
        Guid? vendorId,
        bool? onHire,
        bool? overdue,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var today = BusinessCalendar.Today(clock);
        var query = db.Rentals.AsQueryable();

        if (projectId is { } project)
        {
            query = query.Where(rental => rental.ProjectId == project);
        }

        if (vendorId is { } vendor)
        {
            query = query.Where(rental => rental.VendorId == vendor);
        }

        if (onHire is { } stillOut)
        {
            query = stillOut
                ? query.Where(rental => rental.ReturnedOn == null)
                : query.Where(rental => rental.ReturnedOn != null);
        }

        if (overdue == true)
        {
            // The same rule RentalSchedule applies, expressed so SQL Server can
            // run it against the (ReturnedOn, ExpectedReturnOn) index rather
            // than the API pulling every rental back to filter in memory.
            query = query.Where(rental =>
                rental.ReturnedOn == null && rental.ExpectedReturnOn < today);
        }

        // Ordered before projecting: applying OrderBy afterwards makes EF inline
        // the Select into the ordering and the query stops translating.
        // Still-out rentals first, soonest due at the top — the order somebody
        // chasing returns actually wants to read.
        var rows = await Project(query
            .OrderBy(rental => rental.ReturnedOn != null)
            .ThenBy(rental => rental.ExpectedReturnOn))
            .ToListAsync(ct);

        return Results.Ok(rows.Select(row => ToDto(row, today)).ToList());
    }

    private static async Task<IResult> GetById(
        Guid id, ErpDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var row = await Project(db.Rentals.Where(r => r.Id == id)).FirstOrDefaultAsync(ct);

        return row is null
            ? Results.NotFound()
            : Results.Ok(ToDto(row, BusinessCalendar.Today(clock)));
    }

    private static async Task<IResult> Create(
        SaveRentalRequest body, ErpDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var failure = await ValidateAsync(body, null, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        var rental = new Rental();
        Apply(body, rental);

        db.Rentals.Add(rental);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/rentals/{rental.Id}",
            await Single(rental.Id, db, clock, ct));
    }

    private static async Task<IResult> Update(
        Guid id, SaveRentalRequest body, ErpDbContext db, TimeProvider clock, CancellationToken ct)
    {
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rental is null)
        {
            return Results.NotFound();
        }

        var failure = await ValidateAsync(body, id, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        Apply(body, rental);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Single(id, db, clock, ct));
    }

    private static async Task<IResult> BookReturn(
        Guid id,
        BookReturnRequest? body,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rental is null)
        {
            return Results.NotFound();
        }

        if (rental.ReturnedOn is not null)
        {
            return Results.Conflict(new
            {
                error = "This rental has already been returned.",
            });
        }

        var bookedOn = body?.BookedOn ?? BusinessCalendar.Today(clock);

        if (bookedOn < rental.StartedOn)
        {
            return Results.BadRequest(new
            {
                error = "A return cannot be booked before the hire started.",
            });
        }

        rental.ReturnBookedOn = bookedOn;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Single(id, db, clock, ct));
    }

    private static async Task<IResult> RecordReturn(
        Guid id,
        RecordReturnRequest? body,
        ErpDbContext db,
        TimeProvider clock,
        CancellationToken ct)
    {
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rental is null)
        {
            return Results.NotFound();
        }

        if (rental.ReturnedOn is not null)
        {
            return Results.Conflict(new
            {
                error = "This rental has already been returned.",
            });
        }

        var returnedOn = body?.ReturnedOn ?? BusinessCalendar.Today(clock);

        if (returnedOn < rental.StartedOn)
        {
            return Results.BadRequest(new
            {
                error = "A return cannot be recorded before the hire started.",
            });
        }

        rental.ReturnedOn = returnedOn;
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Single(id, db, clock, ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var rental = await db.Rentals.FirstOrDefaultAsync(r => r.Id == id, ct);

        if (rental is null)
        {
            return Results.NotFound();
        }

        rental.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateAsync(
        SaveRentalRequest body, Guid? id, ErpDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
        {
            return Results.BadRequest(new { error = "Code is required." });
        }

        if (body.Amount < 0)
        {
            return Results.BadRequest(new { error = "Amount cannot be negative." });
        }

        if (body.ExpectedReturnOn < body.StartedOn)
        {
            return Results.BadRequest(new
            {
                error = "The return date cannot fall before the hire starts.",
            });
        }

        if (!await db.Vendors.AnyAsync(v => v.Id == body.VendorId, ct))
        {
            return Results.BadRequest(new { error = "Unknown vendor." });
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

        var clash = await db.Rentals.AnyAsync(
            r => r.Code == body.Code && (id == null || r.Id != id), ct);

        return clash
            ? Results.Conflict(new { error = $"Rental code {body.Code} is already in use." })
            : null;
    }

    private static void Apply(SaveRentalRequest body, Rental rental)
    {
        // Note what is absent: no status. There is nowhere for a caller to put
        // one, which is what stops it drifting from the dates.
        rental.Code = body.Code.Trim();
        rental.VendorId = body.VendorId;
        rental.EquipmentId = body.EquipmentId;
        rental.ProjectId = body.ProjectId;
        rental.StartedOn = body.StartedOn;
        rental.ExpectedReturnOn = body.ExpectedReturnOn;
        rental.Amount = body.Amount;
        rental.Notes = body.Notes?.ToDomain() ?? new();
    }

    private static async Task<RentalDto> Single(
        Guid id, ErpDbContext db, TimeProvider clock, CancellationToken ct) =>
        ToDto(
            await Project(db.Rentals.Where(r => r.Id == id)).FirstAsync(ct),
            BusinessCalendar.Today(clock));

    /// <summary>The flat shape SQL Server returns, before status is derived.</summary>
    private sealed record RentalRow(
        Guid Id,
        string Code,
        Guid VendorId,
        LocalizedTextDto VendorName,
        Guid EquipmentId,
        string EquipmentCode,
        LocalizedTextDto EquipmentName,
        Guid? ProjectId,
        string? ProjectCode,
        LocalizedTextDto? ProjectName,
        DateOnly StartedOn,
        DateOnly ExpectedReturnOn,
        DateOnly? ReturnBookedOn,
        DateOnly? ReturnedOn,
        decimal Amount,
        LocalizedTextDto Notes);

    private static IQueryable<RentalRow> Project(IQueryable<Rental> source) =>
        source.Select(rental => new RentalRow(
            rental.Id,
            rental.Code,
            rental.VendorId,
            new LocalizedTextDto(rental.Vendor!.Name.En, rental.Vendor.Name.Ar),
            rental.EquipmentId,
            rental.Equipment!.Code,
            new LocalizedTextDto(rental.Equipment.Name.En, rental.Equipment.Name.Ar),
            rental.ProjectId,
            rental.Project != null ? rental.Project.Code : null,
            rental.Project != null
                ? new LocalizedTextDto(rental.Project.Name.En, rental.Project.Name.Ar)
                : null,
            rental.StartedOn,
            rental.ExpectedReturnOn,
            rental.ReturnBookedOn,
            rental.ReturnedOn,
            rental.Amount,
            new LocalizedTextDto(rental.Notes.En, rental.Notes.Ar)));

    /// <remarks>
    /// Status is derived here, after the row lands, rather than as part of the
    /// SQL projection. <see cref="RentalSchedule"/> is the one implementation of
    /// these rules; restating them as an expression tree would be a second copy
    /// to keep in step. Filtering still happens in SQL — see the overdue filter
    /// in <c>GetAll</c>, which is the case where it matters for performance.
    /// </remarks>
    private static RentalDto ToDto(RentalRow row, DateOnly today) => new(
        row.Id,
        row.Code,
        row.VendorId,
        row.VendorName,
        row.EquipmentId,
        row.EquipmentCode,
        row.EquipmentName,
        row.ProjectId,
        row.ProjectCode,
        row.ProjectName,
        row.StartedOn,
        row.ExpectedReturnOn,
        row.ReturnBookedOn,
        row.ReturnedOn,
        row.Amount,
        row.Notes,
        RentalSchedule
            .StatusOn(row.ExpectedReturnOn, row.ReturnBookedOn, row.ReturnedOn, today)
            .ToLabel(),
        RentalSchedule.DaysOverdueOn(
            row.ExpectedReturnOn, row.ReturnBookedOn, row.ReturnedOn, today));
}
