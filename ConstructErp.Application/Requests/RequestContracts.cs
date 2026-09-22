using ConstructErp.Application.Common;

namespace ConstructErp.Application.Requests;

public sealed record RequestCheckDto(
    Guid Id,
    string Kind,
    string Code,
    LocalizedTextDto Label,
    bool Passed,
    int Sequence);

public sealed record RequestDto(
    Guid Id,
    string Code,
    Guid EquipmentId,
    string EquipmentCode,
    LocalizedTextDto EquipmentName,
    Guid? ProjectId,
    string? ProjectCode,
    LocalizedTextDto? ProjectName,
    string Ownership,
    string RequestedBy,
    DateOnly? RequiredDate,
    DateOnly? ReturnDate,
    LocalizedTextDto Location,
    LocalizedTextDto Purpose,
    decimal EstimatedCost,
    string Status,
    /// Derived from Status by the domain, never stored or accepted.
    string Stage,
    LocalizedTextDto? RejectionReason,
    IReadOnlyList<RequestCheckDto> Checks,
    /// What the workflow will currently permit — lets a client disable
    /// impossible actions instead of discovering the refusal on click.
    IReadOnlyList<string> AvailableActions);

/// <summary>
/// Note the absence of Status and Stage.
/// </summary>
/// <remarks>
/// A client cannot set them. Status changes only through the workflow
/// endpoints, each of which enforces its own precondition; Stage is derived.
/// The prototype let any caller write "Ready to Use" directly and skip every
/// gate, which made the approval flow decorative.
/// </remarks>
public sealed record SaveRequestRequest(
    string Code,
    Guid EquipmentId,
    Guid? ProjectId,
    string Ownership,
    string? RequestedBy,
    DateOnly? RequiredDate,
    DateOnly? ReturnDate,
    LocalizedTextDto? Location,
    LocalizedTextDto? Purpose,
    decimal EstimatedCost);

public sealed record SetCheckRequest(bool Passed);

public sealed record RejectRequest(LocalizedTextDto Reason);

/// <summary>Records the pre-use inspection outcome.</summary>
public sealed record InspectRequest(bool Passed, LocalizedTextDto? Reason);
