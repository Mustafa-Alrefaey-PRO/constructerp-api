using ConstructErp.Application.Common;

namespace ConstructErp.Application.Equipment;

public sealed record EquipmentDto(
    Guid Id,
    string Code,
    LocalizedTextDto Name,
    Guid EquipmentTypeId,
    LocalizedTextDto EquipmentType,
    string Ownership,
    // Both the id and the display name: the id is the real link, the name saves
    // the client a second request just to render a row.
    Guid? ProjectId,
    string? ProjectCode,
    LocalizedTextDto? ProjectName,
    string Status,
    int Utilization,
    decimal DailyCost,
    LocalizedTextDto NextAction);

public sealed record SaveEquipmentRequest(
    string Code,
    LocalizedTextDto Name,
    Guid EquipmentTypeId,
    string Ownership,
    Guid? ProjectId,
    string Status,
    int Utilization,
    decimal DailyCost,
    LocalizedTextDto? NextAction);

public sealed record EquipmentTypeDto(Guid Id, string Code, LocalizedTextDto Name);
