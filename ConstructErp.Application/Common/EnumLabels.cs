using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;
using ConstructErp.Domain.Requests;

namespace ConstructErp.Application.Common;

/// <summary>
/// Maps enums to the exact strings the existing frontend already uses.
/// </summary>
/// <remarks>
/// Enums are stored as ints, but the wire format is the canonical English
/// label ("External Rental", "Inspection Due"). That is deliberate for this
/// slice: the Angular app already keys its status colours, Arabic labels and
/// tone map off these exact strings, so matching them means the frontend swaps
/// its data source without rewriting any of that.
///
/// It is a transitional choice. The eventual shape is a coded value plus
/// per-language labels served by the API — see PROJECT_GUIDE.md §6.4. Renaming
/// any string here silently breaks the frontend, so treat them as a contract.
/// </remarks>
public static class EnumLabels
{
    public static string ToLabel(this Ownership value) => value switch
    {
        Ownership.Owned => "Owned",
        Ownership.ExternalRental => "External Rental",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static Ownership ToOwnership(string label) => label switch
    {
        "Owned" => Ownership.Owned,
        "External Rental" => Ownership.ExternalRental,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown ownership."),
    };

    public static string ToLabel(this EquipmentStatus value) => value switch
    {
        EquipmentStatus.Working => "Working",
        EquipmentStatus.Idle => "Idle",
        EquipmentStatus.InTransit => "In Transit",
        EquipmentStatus.InspectionDue => "Inspection Due",
        EquipmentStatus.ReturnScheduled => "Return Scheduled",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static EquipmentStatus ToEquipmentStatus(string label) => label switch
    {
        "Working" => EquipmentStatus.Working,
        "Idle" => EquipmentStatus.Idle,
        "In Transit" => EquipmentStatus.InTransit,
        "Inspection Due" => EquipmentStatus.InspectionDue,
        "Return Scheduled" => EquipmentStatus.ReturnScheduled,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown equipment status."),
    };

    public static string ToLabel(this ProjectStatus value) => value switch
    {
        ProjectStatus.Active => "Active",
        ProjectStatus.AtRisk => "At Risk",
        ProjectStatus.Closing => "Closing",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static ProjectStatus ToProjectStatus(string label) => label switch
    {
        "Active" => ProjectStatus.Active,
        "At Risk" => ProjectStatus.AtRisk,
        "Closing" => ProjectStatus.Closing,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown project status."),
    };

    public static string ToLabel(this RequestStatus value) => value switch
    {
        RequestStatus.Draft => "Draft",
        RequestStatus.Submitted => "Submitted",
        RequestStatus.Approved => "Approved",
        RequestStatus.Received => "Received",
        RequestStatus.InspectionPending => "Inspection Pending",
        RequestStatus.ReadyToUse => "Ready to Use",
        RequestStatus.Rejected => "Rejected",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static string ToLabel(this RequestStage value) => value switch
    {
        RequestStage.Request => "Request",
        RequestStage.Approval => "Approval",
        RequestStage.Receiving => "Receiving",
        RequestStage.Inspection => "Inspection",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };

    public static CostCategory ToCostCategory(string label) => label switch
    {
        "Equipment" => CostCategory.Equipment,
        "Transport" => CostCategory.Transport,
        "Extras" => CostCategory.Extras,
        _ => throw new ArgumentOutOfRangeException(nameof(label), label, "Unknown cost category."),
    };

    public static string ToLabel(this CostCategory value) => value switch
    {
        CostCategory.Equipment => "Equipment",
        CostCategory.Transport => "Transport",
        CostCategory.Extras => "Extras",
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
