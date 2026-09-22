using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;

namespace ConstructErp.Domain.Requests;

public enum RequestStatus
{
    Draft = 1,
    Submitted = 2,
    Approved = 3,
    Received = 4,
    InspectionPending = 5,
    ReadyToUse = 6,
    Rejected = 7,
}

public enum RequestStage
{
    Request = 1,
    Approval = 2,
    Receiving = 3,
    Inspection = 4,
}

public sealed class EquipmentRequest : Entity
{
    /// <summary>Business identifier, e.g. REQ-2407.</summary>
    public string Code { get; set; } = string.Empty;

    public Guid EquipmentId { get; set; }

    public EquipmentAsset? Equipment { get; set; }

    public Guid? ProjectId { get; set; }

    public Project? Project { get; set; }

    public Ownership Ownership { get; set; } = Ownership.Owned;

    public string RequestedBy { get; set; } = string.Empty;

    /// <summary>
    /// Real dates, not the prototype's display strings ("Jul 20"), which could
    /// not be compared, sorted or filtered.
    /// </summary>
    public DateOnly? RequiredDate { get; set; }

    public DateOnly? ReturnDate { get; set; }

    public LocalizedText Location { get; set; } = new();

    public LocalizedText Purpose { get; set; } = new();

    /// <summary>KWD, three decimal places.</summary>
    public decimal EstimatedCost { get; set; }

    public RequestStatus Status { get; set; } = RequestStatus.Draft;

    /// <summary>
    /// Derived, never stored.
    /// </summary>
    /// <remarks>
    /// The prototype stored stage and status as two independently editable
    /// fields, so a record could claim stage "Request" while its status said
    /// "Ready to Use". Deriving one from the other makes that state
    /// unrepresentable.
    /// </remarks>
    public RequestStage Stage => Status switch
    {
        RequestStatus.Draft or RequestStatus.Rejected => RequestStage.Request,
        RequestStatus.Submitted => RequestStage.Approval,
        RequestStatus.Approved or RequestStatus.Received => RequestStage.Receiving,
        _ => RequestStage.Inspection,
    };

    public ICollection<RequestCheck> Checks { get; set; } = [];

    /// <summary>Why the request was rejected, if it was.</summary>
    public LocalizedText? RejectionReason { get; set; }

    public DateTimeOffset? SubmittedAt { get; set; }

    public DateTimeOffset? ApprovedAt { get; set; }

    public DateTimeOffset? ReceivedAt { get; set; }

    public DateTimeOffset? ReadyAt { get; set; }

    public bool AllPassed(CheckKind kind) =>
        Checks.Where(check => check.Kind == kind).All(check => check.Passed);
}
