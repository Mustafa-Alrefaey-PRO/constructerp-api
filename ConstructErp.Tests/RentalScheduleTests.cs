using ConstructErp.Domain.Rentals;

namespace ConstructErp.Tests;

/// <summary>
/// The rules that replaced a typed-in status.
/// </summary>
/// <remarks>
/// These are pure and take <c>today</c> as an argument, so the boundary cases
/// can actually be tested — the day before a due date, the day itself, and the
/// day after. Those three are where a hand-maintained status was always wrong.
/// </remarks>
public sealed class RentalScheduleTests
{
    private static readonly DateOnly Today = new(2026, 9, 22);

    private static Rental Hire(
        int dueOffset, int? bookedOffset = null, int? returnedOffset = null) => new()
        {
            StartedOn = Today.AddDays(-30),
            ExpectedReturnOn = Today.AddDays(dueOffset),
            ReturnBookedOn = bookedOffset is { } b ? Today.AddDays(b) : null,
            ReturnedOn = returnedOffset is { } r ? Today.AddDays(r) : null,
        };

    [Fact]
    public void A_hire_running_with_no_return_booked_is_active() =>
        Assert.Equal(RentalStatus.Active, RentalSchedule.StatusOn(Hire(dueOffset: 5), Today));

    [Fact]
    public void A_hire_with_a_collection_arranged_is_return_scheduled() =>
        Assert.Equal(
            RentalStatus.ReturnScheduled,
            RentalSchedule.StatusOn(Hire(dueOffset: 5, bookedOffset: -1), Today));

    [Fact]
    public void A_returned_hire_is_closed_however_late_it_was() =>
        Assert.Equal(
            RentalStatus.Returned,
            RentalSchedule.StatusOn(Hire(dueOffset: -20, returnedOffset: -2), Today));

    [Theory]
    [InlineData(1, RentalStatus.Active)]
    // Due today is not yet late: the vendor has until the end of the day.
    [InlineData(0, RentalStatus.Active)]
    [InlineData(-1, RentalStatus.Overdue)]
    public void Overdue_turns_over_the_day_after_the_due_date(int dueOffset, RentalStatus expected)
    {
        // The case the stored status could never get right: nothing was edited
        // between these three, only the date moved.
        Assert.Equal(expected, RentalSchedule.StatusOn(Hire(dueOffset), Today));
    }

    [Fact]
    public void A_booked_return_that_did_not_happen_still_reads_overdue()
    {
        // Deliberate precedence. "Return Scheduled" over a passed due date is
        // how a late collection stops being chased.
        var rental = Hire(dueOffset: -3, bookedOffset: -5);

        Assert.Equal(RentalStatus.Overdue, RentalSchedule.StatusOn(rental, Today));
    }

    [Fact]
    public void Days_overdue_counts_from_the_due_date()
    {
        Assert.Equal(7, RentalSchedule.DaysOverdueOn(Hire(dueOffset: -7), Today));
        Assert.Equal(0, RentalSchedule.DaysOverdueOn(Hire(dueOffset: 7), Today));
        Assert.Equal(
            0, RentalSchedule.DaysOverdueOn(Hire(dueOffset: -7, returnedOffset: -1), Today));
    }

    [Fact]
    public void The_same_rental_answers_differently_as_time_passes()
    {
        // One record, never edited, read on three days. This is the whole
        // argument for deriving the status rather than storing it.
        var rental = Hire(dueOffset: 0);

        Assert.Equal(RentalStatus.Active, RentalSchedule.StatusOn(rental, Today.AddDays(-1)));
        Assert.Equal(RentalStatus.Active, RentalSchedule.StatusOn(rental, Today));
        Assert.Equal(RentalStatus.Overdue, RentalSchedule.StatusOn(rental, Today.AddDays(1)));
    }
}
