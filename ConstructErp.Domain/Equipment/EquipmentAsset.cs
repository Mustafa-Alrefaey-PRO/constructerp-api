using ConstructErp.Domain.Common;
using ConstructErp.Domain.Projects;

namespace ConstructErp.Domain.Equipment;

public enum Ownership
{
    Owned = 1,
    ExternalRental = 2,
}

public enum EquipmentStatus
{
    Working = 1,
    Idle = 2,
    InTransit = 3,
    InspectionDue = 4,
    ReturnScheduled = 5,
}

/// <summary>
/// Named EquipmentAsset rather than Equipment so the type does not collide with
/// its own namespace.
/// </summary>
public sealed class EquipmentAsset : Entity
{
    /// <summary>Human-readable business identifier, e.g. EQ-104. Unique, but not the key.</summary>
    public string Code { get; set; } = string.Empty;

    public LocalizedText Name { get; set; } = new();

    public Guid EquipmentTypeId { get; set; }

    public EquipmentType? EquipmentType { get; set; }

    public Ownership Ownership { get; set; } = Ownership.Owned;

    /// <summary>
    /// Nullable: an asset can sit in the yard unassigned. A real foreign key,
    /// which is the whole point of this slice — the prototype linked by name.
    /// </summary>
    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public EquipmentStatus Status { get; set; } = EquipmentStatus.Idle;

    /// <summary>Percentage, 0-100. Kept manual for now; derive from usage later.</summary>
    public int Utilization { get; set; }

    /// <summary>KWD per day, three decimal places.</summary>
    public decimal DailyCost { get; set; }

    public LocalizedText NextAction { get; set; } = new();
}
