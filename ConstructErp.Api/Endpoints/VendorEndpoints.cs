using ConstructErp.Application.Common;
using ConstructErp.Application.Rentals;
using ConstructErp.Domain.Rentals;
using ConstructErp.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace ConstructErp.Api.Endpoints;

/// <summary>
/// Rental vendors.
/// </summary>
/// <remarks>
/// The prototype had no such thing — "Delta Heavy Rentals" was a string typed
/// onto each rental row. That makes the vendor uncontactable, unrateable, and
/// unsummable: one typo and half a vendor's spend lands under a second name
/// nobody notices. This is that string promoted to a record.
/// </remarks>
public static class VendorEndpoints
{
    public static RouteGroupBuilder MapVendorEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/vendors").WithTags("Vendors");

        group.MapGet("/", GetAll).WithName("GetVendors");
        group.MapGet("/{id:guid}", GetById).WithName("GetVendor");
        group.MapPost("/", Create).WithName("CreateVendor");
        group.MapPut("/{id:guid}", Update).WithName("UpdateVendor");
        group.MapDelete("/{id:guid}", Delete).WithName("DeleteVendor");

        return group;
    }

    private static async Task<IResult> GetAll(ErpDbContext db, CancellationToken ct) =>
        Results.Ok(await Project(db.Vendors.OrderBy(v => v.Code)).ToListAsync(ct));

    private static async Task<IResult> GetById(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var vendor = await Project(db.Vendors.Where(v => v.Id == id)).FirstOrDefaultAsync(ct);

        return vendor is null ? Results.NotFound() : Results.Ok(vendor);
    }

    private static async Task<IResult> Create(
        SaveVendorRequest body, ErpDbContext db, CancellationToken ct)
    {
        var failure = await ValidateAsync(body, null, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        var vendor = new Vendor();
        Apply(body, vendor);

        db.Vendors.Add(vendor);
        await db.SaveChangesAsync(ct);

        return Results.Created(
            $"/api/vendors/{vendor.Id}",
            await Project(db.Vendors.Where(v => v.Id == vendor.Id)).FirstAsync(ct));
    }

    private static async Task<IResult> Update(
        Guid id, SaveVendorRequest body, ErpDbContext db, CancellationToken ct)
    {
        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vendor is null)
        {
            return Results.NotFound();
        }

        var failure = await ValidateAsync(body, id, db, ct);

        if (failure is not null)
        {
            return failure;
        }

        Apply(body, vendor);
        await db.SaveChangesAsync(ct);

        return Results.Ok(await Project(db.Vendors.Where(v => v.Id == id)).FirstAsync(ct));
    }

    private static async Task<IResult> Delete(Guid id, ErpDbContext db, CancellationToken ct)
    {
        var vendor = await db.Vendors.FirstOrDefaultAsync(v => v.Id == id, ct);

        if (vendor is null)
        {
            return Results.NotFound();
        }

        // Hire history is the vendor's spend record. Removing the vendor would
        // orphan it, so the caller has to deal with the rentals first.
        if (await db.Rentals.AnyAsync(r => r.VendorId == id, ct))
        {
            return Results.Conflict(new
            {
                error = "This vendor has rentals recorded against it and cannot be removed.",
            });
        }

        vendor.DeletedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);

        return Results.NoContent();
    }

    private static async Task<IResult?> ValidateAsync(
        SaveVendorRequest body, Guid? id, ErpDbContext db, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(body.Code))
        {
            return Results.BadRequest(new { error = "Code is required." });
        }

        if (string.IsNullOrWhiteSpace(body.Name.En))
        {
            return Results.BadRequest(new { error = "An English name is required." });
        }

        var clash = await db.Vendors.AnyAsync(
            v => v.Code == body.Code && (id == null || v.Id != id), ct);

        return clash
            ? Results.Conflict(new { error = $"Vendor code {body.Code} is already in use." })
            : null;
    }

    private static void Apply(SaveVendorRequest body, Vendor vendor)
    {
        vendor.Code = body.Code.Trim();
        vendor.Name = body.Name.ToDomain();
        vendor.ContactName = body.ContactName?.Trim() ?? string.Empty;
        vendor.Phone = body.Phone?.Trim() ?? string.Empty;
        vendor.Email = body.Email?.Trim() ?? string.Empty;
    }

    private static IQueryable<VendorDto> Project(IQueryable<Vendor> source) =>
        source.Select(vendor => new VendorDto(
            vendor.Id,
            vendor.Code,
            new LocalizedTextDto(vendor.Name.En, vendor.Name.Ar),
            vendor.ContactName,
            vendor.Phone,
            vendor.Email,
            vendor.Rentals.Count,
            vendor.Rentals.Count(r => r.ReturnedOn == null),
            vendor.Rentals.Sum(r => (decimal?)r.Amount) ?? 0m));
}
