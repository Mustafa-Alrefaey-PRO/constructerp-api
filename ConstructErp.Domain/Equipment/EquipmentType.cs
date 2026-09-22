using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Equipment;

/// <summary>
/// Lifting, Concrete, Earthworks, and so on.
/// </summary>
/// <remarks>
/// A managed lookup rather than the prototype's free-text `type` field, so the
/// same category cannot arrive as "Lifting", "lifting" and "Lift" and split
/// every report three ways.
/// </remarks>
public sealed class EquipmentType : Entity
{
    public string Code { get; set; } = string.Empty;

    public LocalizedText Name { get; set; } = new();

    public ICollection<EquipmentAsset> Assets { get; set; } = [];
}
