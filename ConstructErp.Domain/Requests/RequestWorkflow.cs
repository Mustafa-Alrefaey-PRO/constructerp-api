namespace ConstructErp.Domain.Requests;

/// <summary>The outcome of attempting a transition.</summary>
public readonly record struct TransitionResult(bool Allowed, string? Reason)
{
    public static TransitionResult Ok() => new(true, null);

    public static TransitionResult Refuse(string reason) => new(false, reason);
}

/// <summary>
/// The request lifecycle, enforced.
/// </summary>
/// <remarks>
/// This is the rule the whole product exists to enforce:
///
///   Equipment cannot be put to work until its request has been approved,
///   the delivery has been received, and the pre-use inspection has passed.
///
/// In the prototype this was decoration — stage and status were free-form
/// fields, so any client could write "Ready to Use" directly and skip every
/// gate. Here the status is only ever changed by one of the named transitions
/// below, each of which states its own precondition.
///
/// Deliberately a pure function of (status, request): no database, no clock,
/// no user. That makes every rule testable in isolation and keeps the decision
/// in one readable place.
/// </remarks>
public static class RequestWorkflow
{
    public static TransitionResult CanSubmit(EquipmentRequest request)
    {
        if (request.Status is not (RequestStatus.Draft or RequestStatus.Rejected))
        {
            return TransitionResult.Refuse(
                $"Only a draft or rejected request can be submitted; this one is {request.Status}.");
        }

        if (!request.AllPassed(CheckKind.PreRequest))
        {
            return TransitionResult.Refuse(
                "Every pre-request check must pass before the request can be submitted.");
        }

        return TransitionResult.Ok();
    }

    public static TransitionResult CanApprove(EquipmentRequest request) =>
        request.Status is RequestStatus.Submitted
            ? TransitionResult.Ok()
            : TransitionResult.Refuse(
                $"Only a submitted request can be approved; this one is {request.Status}.");

    public static TransitionResult CanReject(EquipmentRequest request) =>
        request.Status is RequestStatus.Submitted
            ? TransitionResult.Ok()
            : TransitionResult.Refuse(
                $"Only a submitted request can be rejected; this one is {request.Status}.");

    public static TransitionResult CanReceive(EquipmentRequest request)
    {
        if (request.Status is not RequestStatus.Approved)
        {
            return TransitionResult.Refuse(
                $"Delivery can only be received against an approved request; this one is {request.Status}.");
        }

        if (!request.AllPassed(CheckKind.PreReceiving))
        {
            return TransitionResult.Refuse(
                "Every pre-receiving check must pass before delivery can be accepted.");
        }

        return TransitionResult.Ok();
    }

    /// <summary>Records the pre-use inspection result.</summary>
    public static TransitionResult CanInspect(EquipmentRequest request) =>
        request.Status is RequestStatus.Received or RequestStatus.InspectionPending
            ? TransitionResult.Ok()
            : TransitionResult.Refuse(
                $"Inspection applies after delivery is received; this one is {request.Status}.");

    /// <summary>
    /// Whether an asset may be put to work.
    /// </summary>
    /// <remarks>
    /// Called with every request that exists for the asset. This is the
    /// cross-entity half of the rule — the request lifecycle above governs one
    /// record, but "may this machine start work" is a question about the asset.
    /// </remarks>
    public static TransitionResult CanPutToWork(IEnumerable<EquipmentRequest> requestsForAsset)
    {
        var ready = requestsForAsset.Any(request => request.Status == RequestStatus.ReadyToUse);

        return ready
            ? TransitionResult.Ok()
            : TransitionResult.Refuse(
                "Equipment cannot be set to Working without a request that has been approved, "
                + "received and passed pre-use inspection.");
    }

    /// <summary>Applies a transition. Call only after the matching guard passes.</summary>
    public static void Apply(EquipmentRequest request, RequestStatus next, DateTimeOffset now)
    {
        request.Status = next;

        switch (next)
        {
            case RequestStatus.Submitted:
                request.SubmittedAt = now;
                request.RejectionReason = null;
                break;
            case RequestStatus.Approved:
                request.ApprovedAt = now;
                break;
            case RequestStatus.Received:
                request.ReceivedAt = now;
                break;
            case RequestStatus.ReadyToUse:
                request.ReadyAt = now;
                break;
        }
    }
}
