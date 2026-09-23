using ConstructErp.Domain.Transport;

namespace ConstructErp.Tests;

/// <summary>
/// The rules that replaced a second typed-in status.
/// </summary>
/// <remarks>
/// Pure, so the whole lifecycle is testable without a database or a clock.
/// The status here needs no "now" at all — every input is an event that either
/// happened or did not.
/// </remarks>
public sealed class TransportScheduleTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 23, 9, 0, 0, TimeSpan.Zero);

    private static TransportMove Move(
        double scheduledInHours = 4,
        bool approved = false,
        bool departed = false,
        bool arrived = false,
        bool cancelled = false) => new()
        {
            ScheduledFor = Now.AddHours(scheduledInHours),
            ApprovedAt = approved ? Now.AddHours(-5) : null,
            DepartedAt = departed ? Now.AddHours(-2) : null,
            ArrivedAt = arrived ? Now.AddHours(-1) : null,
            CancelledAt = cancelled ? Now.AddHours(-3) : null,
        };

    [Fact]
    public void A_new_move_waits_for_approval() =>
        Assert.Equal(TransportStatus.AwaitingApproval, TransportSchedule.StatusOf(Move()));

    [Fact]
    public void Approving_it_makes_it_scheduled() =>
        Assert.Equal(
            TransportStatus.Scheduled, TransportSchedule.StatusOf(Move(approved: true)));

    [Fact]
    public void Recording_departure_is_what_makes_it_in_transit() =>
        Assert.Equal(
            TransportStatus.InTransit,
            TransportSchedule.StatusOf(Move(approved: true, departed: true)));

    [Fact]
    public void Recording_arrival_completes_it() =>
        Assert.Equal(
            TransportStatus.Completed,
            TransportSchedule.StatusOf(Move(approved: true, departed: true, arrived: true)));

    [Fact]
    public void Cancelling_outranks_every_other_event() =>
        Assert.Equal(
            TransportStatus.Cancelled,
            TransportSchedule.StatusOf(Move(approved: true, departed: true, cancelled: true)));

    [Fact]
    public void A_move_cannot_depart_before_it_is_approved()
    {
        // The rule the prototype only described. Writing "In Transit" into a
        // status column skipped it entirely.
        var refusal = TransportSchedule.CanDepart(Move(approved: false));

        Assert.False(refusal.Allowed);
        Assert.Contains("approved", refusal.Reason);
    }

    [Fact]
    public void Arrival_cannot_be_recorded_before_departure()
    {
        var refusal = TransportSchedule.CanArrive(Move(approved: true));

        Assert.False(refusal.Allowed);
        Assert.Contains("departed", refusal.Reason);
    }

    [Fact]
    public void A_completed_move_cannot_be_cancelled()
    {
        // It is history. Cancelling it would erase the record that an asset
        // actually moved.
        var refusal = TransportSchedule.CanCancel(
            Move(approved: true, departed: true, arrived: true));

        Assert.False(refusal.Allowed);
    }

    [Fact]
    public void Nothing_can_be_done_to_a_cancelled_move()
    {
        var cancelled = Move(cancelled: true);

        Assert.False(TransportSchedule.CanApprove(cancelled).Allowed);
        Assert.False(TransportSchedule.CanDepart(cancelled).Allowed);
        Assert.False(TransportSchedule.CanArrive(cancelled).Allowed);
        Assert.False(TransportSchedule.CanCancel(cancelled).Allowed);
    }

    [Theory]
    // Slot is in the future, so not late whatever its approval state.
    [InlineData(4, false, false)]
    // Slot has passed and it has not left.
    [InlineData(-4, false, true)]
    // Slot has passed but it did leave, so it is not sitting in the yard.
    [InlineData(-4, true, false)]
    public void Late_means_the_slot_passed_and_it_never_left(
        double scheduledInHours, bool departed, bool expected)
    {
        // Only answerable because ScheduledFor is a timestamp. The prototype's
        // "ETA 16:30" string could not be compared to anything.
        var move = Move(scheduledInHours, approved: true, departed: departed);

        Assert.Equal(expected, TransportSchedule.IsLate(move, Now));
    }

    [Fact]
    public void A_cancelled_move_is_never_late() =>
        Assert.False(TransportSchedule.IsLate(Move(-10, cancelled: true), Now));
}
