namespace ConstructErp.Domain.Rentals;

public enum RentalStatus
{
    Active = 0,
    ReturnScheduled = 1,
    Overdue = 2,
    Returned = 3,
}

/// <summary>
/// Derives what a rental is, from the dates on it.
/// </summary>
/// <remarks>
/// The prototype STORED this as a status somebody typed. That is the bug this
/// class exists to kill: a rental was "Overdue" because a person had noticed
/// and written it down, so the overdue count on the dashboard was reporting
/// somebody's attention span rather than a fact. A hire that ran past its
/// return date over the weekend stayed "Active" until Monday, and one returned
/// on time stayed "Overdue" until somebody remembered to change it back.
///
/// Status is now a function of (dates, today) and cannot be written to. There
/// is no setter to get wrong and no row to go stale — the same rental read on
/// two different days correctly answers differently.
///
/// Pure, and takes <c>today</c> as an argument rather than reading the clock,
/// so the boundaries are testable: see RentalScheduleTests, which checks the
/// day before, the day of, and the day after a due date.
/// </remarks>
public static class RentalSchedule
{
    public static RentalStatus StatusOn(Rental rental, DateOnly today) =>
        StatusOn(rental.ExpectedReturnOn, rental.ReturnBookedOn, rental.ReturnedOn, today);

    /// <summary>
    /// The dates-only overload, so read paths that project straight out of SQL
    /// share this implementation instead of restating the rules.
    /// </summary>
    public static RentalStatus StatusOn(
        DateOnly expectedReturnOn, DateOnly? returnBookedOn, DateOnly? returnedOn, DateOnly today)
    {
        if (returnedOn is not null)
        {
            return RentalStatus.Returned;
        }

        // Overdue outranks a booked return on purpose: a collection that was
        // arranged but has not happened is exactly the case somebody needs to
        // chase, and hiding it behind "Return Scheduled" is how it gets missed.
        if (expectedReturnOn < today)
        {
            return RentalStatus.Overdue;
        }

        return returnBookedOn is not null
            ? RentalStatus.ReturnScheduled
            : RentalStatus.Active;
    }

    /// <summary>Days past the return date; 0 when not overdue.</summary>
    public static int DaysOverdueOn(Rental rental, DateOnly today) =>
        DaysOverdueOn(rental.ExpectedReturnOn, rental.ReturnBookedOn, rental.ReturnedOn, today);

    public static int DaysOverdueOn(
        DateOnly expectedReturnOn, DateOnly? returnBookedOn, DateOnly? returnedOn, DateOnly today) =>
        StatusOn(expectedReturnOn, returnBookedOn, returnedOn, today) == RentalStatus.Overdue
            ? today.DayNumber - expectedReturnOn.DayNumber
            : 0;

    /// <summary>Whether the hire is still running, ignoring how late it is.</summary>
    public static bool IsOnHire(Rental rental) => rental.ReturnedOn is null;
}
