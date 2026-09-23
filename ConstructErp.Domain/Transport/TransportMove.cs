using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;

namespace ConstructErp.Domain.Transport;

public enum TransportKind
{
    Delivery = 0,
    ReturnMove = 1,
    InspectionTransfer = 2,
}

/// <summary>
/// Moving one asset from one place to another.
/// </summary>
/// <remarks>
/// Like a rental, this records the events that happened — approved, departed,
/// arrived — and derives what the move IS from them. See
/// <see cref="TransportSchedule"/>.
///
/// The prototype's <c>schedule</c> was a display string ("ETA 16:30", "Jul 24")
/// that nothing could sort, filter or compare. It is a real timestamp here, so
/// "what is running late" becomes a query rather than a reading exercise.
/// </remarks>
public sealed class TransportMove : Entity
{
    /// <summary>Business identifier, e.g. TRP-5001.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid EquipmentId { get; set; }

    public EquipmentAsset? Equipment { get; set; }

    /// <summary>The project the move is billed to, if any.</summary>
    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    /// <summary>
    /// Free text, not a foreign key.
    /// </summary>
    /// <remarks>
    /// Deliberate: "Yard A", "Vendor Yard" and "Service Center" are not
    /// projects and have no records to point at. Inventing a Location entity to
    /// hold three strings would be scaffolding without a purpose — unlike the
    /// vendor on a rental, nothing needs to be summed or contacted per yard
    /// yet. When routing or depot capacity arrives, this becomes a real link.
    /// </remarks>
    public LocalizedText Origin { get; set; } = new();

    public LocalizedText Destination { get; set; } = new();

    public TransportKind Kind { get; set; }

    /// <summary>When the move is booked for. A timestamp, not a label.</summary>
    public DateTimeOffset ScheduledFor { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? DepartedAt { get; set; }

    public DateTimeOffset? ArrivedAt { get; set; }

    public DateTimeOffset? CancelledAt { get; set; }

    /// <summary>Move cost in KWD.</summary>
    public decimal Cost { get; set; }

    public LocalizedText Notes { get; set; } = new();
}
