using ConstructErp.Domain.Common;
using ConstructErp.Domain.Requests;

namespace ConstructErp.Tests;

/// <summary>
/// The lifecycle rules, tested as pure functions.
/// </summary>
/// <remarks>
/// No database and no HTTP: these are the rules themselves, so they should
/// fail for a reason about the rule, never about infrastructure.
/// </remarks>
public sealed class RequestWorkflowTests
{
    [Fact]
    public void Draft_cannot_be_submitted_until_every_pre_request_check_passes()
    {
        var request = NewRequest();

        var refused = RequestWorkflow.CanSubmit(request);
        Assert.False(refused.Allowed);
        Assert.Contains("pre-request check", refused.Reason);

        PassAll(request, CheckKind.PreRequest);

        Assert.True(RequestWorkflow.CanSubmit(request).Allowed);
    }

    [Fact]
    public void Approval_requires_a_submitted_request()
    {
        var request = NewRequest();

        Assert.False(RequestWorkflow.CanApprove(request).Allowed);

        request.Status = RequestStatus.Submitted;

        Assert.True(RequestWorkflow.CanApprove(request).Allowed);
    }

    [Fact]
    public void Delivery_cannot_be_received_before_approval()
    {
        var request = NewRequest();
        PassAll(request, CheckKind.PreReceiving);
        request.Status = RequestStatus.Submitted;

        var refused = RequestWorkflow.CanReceive(request);

        Assert.False(refused.Allowed);
        Assert.Contains("approved request", refused.Reason);
    }

    [Fact]
    public void Delivery_cannot_be_received_until_every_pre_receiving_check_passes()
    {
        var request = NewRequest();
        request.Status = RequestStatus.Approved;

        var refused = RequestWorkflow.CanReceive(request);
        Assert.False(refused.Allowed);
        Assert.Contains("pre-receiving check", refused.Reason);

        PassAll(request, CheckKind.PreReceiving);

        Assert.True(RequestWorkflow.CanReceive(request).Allowed);
    }

    [Fact]
    public void Stage_is_derived_from_status_and_cannot_contradict_it()
    {
        var request = NewRequest();

        Assert.Equal(RequestStage.Request, request.Stage);

        request.Status = RequestStatus.Submitted;
        Assert.Equal(RequestStage.Approval, request.Stage);

        request.Status = RequestStatus.Approved;
        Assert.Equal(RequestStage.Receiving, request.Stage);

        request.Status = RequestStatus.ReadyToUse;
        Assert.Equal(RequestStage.Inspection, request.Stage);
    }

    [Theory]
    [InlineData(RequestStatus.Draft)]
    [InlineData(RequestStatus.Submitted)]
    [InlineData(RequestStatus.Approved)]
    [InlineData(RequestStatus.Received)]
    [InlineData(RequestStatus.InspectionPending)]
    [InlineData(RequestStatus.Rejected)]
    public void Equipment_cannot_be_put_to_work_on_an_incomplete_request(RequestStatus status)
    {
        var request = NewRequest();
        request.Status = status;

        var refused = RequestWorkflow.CanPutToWork([request]);

        Assert.False(refused.Allowed);
        Assert.Contains("pre-use inspection", refused.Reason);
    }

    [Fact]
    public void Equipment_can_be_put_to_work_once_a_request_is_ready()
    {
        var request = NewRequest();
        request.Status = RequestStatus.ReadyToUse;

        Assert.True(RequestWorkflow.CanPutToWork([request]).Allowed);
    }

    [Fact]
    public void Equipment_with_no_request_at_all_cannot_be_put_to_work()
    {
        Assert.False(RequestWorkflow.CanPutToWork([]).Allowed);
    }

    [Fact]
    public void Applying_a_transition_stamps_its_moment()
    {
        var request = NewRequest();
        var now = DateTimeOffset.UtcNow;

        RequestWorkflow.Apply(request, RequestStatus.Submitted, now);

        Assert.Equal(RequestStatus.Submitted, request.Status);
        Assert.Equal(now, request.SubmittedAt);
    }

    [Fact]
    public void Resubmitting_clears_a_previous_rejection_reason()
    {
        var request = NewRequest();
        request.Status = RequestStatus.Rejected;
        request.RejectionReason = new LocalizedText("Budget exceeded");

        RequestWorkflow.Apply(request, RequestStatus.Submitted, DateTimeOffset.UtcNow);

        // A stale reason on a live request would misexplain its current state.
        Assert.Null(request.RejectionReason);
    }

    private static EquipmentRequest NewRequest()
    {
        var request = new EquipmentRequest { Code = "REQ-0001" };

        foreach (var check in RequestCheckCatalogue.CreateFor(request.Id))
        {
            request.Checks.Add(check);
        }

        return request;
    }

    private static void PassAll(EquipmentRequest request, CheckKind kind)
    {
        foreach (var check in request.Checks.Where(c => c.Kind == kind))
        {
            check.Passed = true;
        }
    }
}
