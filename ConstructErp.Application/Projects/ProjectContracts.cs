using ConstructErp.Application.Common;

namespace ConstructErp.Application.Projects;

public sealed record ProjectDto(
    Guid Id,
    string Code,
    LocalizedTextDto Name,
    LocalizedTextDto Client,
    string Manager,
    LocalizedTextDto Location,
    string Status,
    decimal Budget,
    int Progress,
    DateOnly? StartDate,
    DateOnly? EndDate,
    // Spend is summed from cost entries, never stored on the project. The
    // prototype kept these as manual numbers that could disagree with reality.
    decimal EquipmentSpend,
    decimal TransportSpend,
    decimal ExtraSpend,
    int EquipmentCount);

public sealed record SaveProjectRequest(
    string Code,
    LocalizedTextDto Name,
    LocalizedTextDto Client,
    string? Manager,
    LocalizedTextDto? Location,
    string Status,
    decimal Budget,
    int Progress,
    DateOnly? StartDate,
    DateOnly? EndDate);
