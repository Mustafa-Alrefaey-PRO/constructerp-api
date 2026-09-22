using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;

namespace ConstructErp.Domain.Rentals;

/// <summary>
/// A hire agreement: one asset, from one vendor, onto one project.
/// </summary>
/// <remarks>
/// The dates here are facts — when the hire started, when it is due back, when
/// a return was booked, when it actually came back. What the rental IS at this
/// moment is not stored; see <see cref="RentalSchedule"/>.
/// </remarks>
public sealed class Rental : Entity
{
    /// <summary>Business identifier, e.g. RNT-2007.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid VendorId { get; set; }

    public Vendor? Vendor { get; set; }

    public Guid EquipmentId { get; set; }

    public EquipmentAsset? Equipment { get; set; }

    /// <summary>Null while the asset is off-hire in the yard.</summary>
    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public DateOnly StartedOn { get; set; }

    /// <summary>Contractual return date. The one date overdue is measured against.</summary>
    public DateOnly ExpectedReturnOn { get; set; }

    /// <summary>Set when a collection has been arranged with the vendor.</summary>
    public DateOnly? ReturnBookedOn { get; set; }

    /// <summary>Set when the asset is actually back. Null means still on hire.</summary>
    public DateOnly? ReturnedOn { get; set; }

    /// <summary>Contract value in KWD.</summary>
    public decimal Amount { get; set; }

    public LocalizedText Notes { get; set; } = new();
}
