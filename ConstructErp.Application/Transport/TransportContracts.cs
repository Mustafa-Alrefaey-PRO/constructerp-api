using ConstructErp.Application.Common;

namespace ConstructErp.Application.Transport;

public sealed record TransportMoveDto(
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
    string Kind,
    DateTimeOffset ScheduledFor,
    DateTimeOffset? ApprovedAt,
    DateTimeOffset? DepartedAt,
    DateTimeOffset? ArrivedAt,
    DateTimeOffset? CancelledAt,
    decimal Cost,
    LocalizedTextDto Notes,
    // Derived from the four timestamps above; never sent back.
    string Status,
    bool IsLate,
    // What the workflow will currently permit, so the client shows only the
    // buttons that would actually succeed.
    IReadOnlyList<string> AvailableActions);

/// <summary>
/// Note the absence of status: the API refuses to take one. A move becomes
/// In Transit by its departure being recorded, not by anyone saying so.
/// </summary>
public sealed record SaveTransportMoveRequest(
    string Code,
    Guid EquipmentId,
    Guid? ProjectId,
    LocalizedTextDto Origin,
    LocalizedTextDto Destination,
    string Kind,
    DateTimeOffset ScheduledFor,
    decimal Cost,
    LocalizedTextDto? Notes);

/// <summary>Timestamps are optional on every transition — they default to now.</summary>
public sealed record TransportEventRequest(DateTimeOffset? OccurredAt);
