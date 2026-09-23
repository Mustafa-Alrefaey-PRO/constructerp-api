using ConstructErp.Domain.Requests;

namespace ConstructErp.Domain.Transport;

public enum TransportStatus
{
    AwaitingApproval = 0,
    Scheduled = 1,
    InTransit = 2,
    Completed = 3,
    Cancelled = 4,
}

/// <summary>
/// Derives what a move is, from the events recorded against it.
/// </summary>
/// <remarks>
/// The prototype stored this status too, with the same consequence as rentals:
/// a lorry that left the yard stayed "Scheduled" until somebody updated the
/// row, and a move sat at "Awaiting Approval" after it had been approved.
///
/// Here the status is a function of four nullable timestamps. Each one is set
/// by its own transition below, each of which states its precondition — so
/// "In Transit" cannot be reached without a departure being recorded, and a
/// departure cannot be recorded before approval.
///
/// Unlike <c>RentalSchedule</c> this needs no clock for the status itself:
/// every input is an event that either happened or did not. The clock is only
/// needed for <see cref="IsLate"/>, which is a separate question.
/// </remarks>
public static class TransportSchedule
{
    public static TransportStatus StatusOf(TransportMove move) =>
        StatusOf(move.ApprovedAt, move.DepartedAt, move.ArrivedAt, move.CancelledAt);

    /// <summary>
    /// The timestamps-only overload, so read paths projecting straight out of
    /// SQL share this implementation rather than restating the rules.
    /// </summary>
    public static TransportStatus StatusOf(
        DateTimeOffset? approvedAt,
        DateTimeOffset? departedAt,
        DateTimeOffset? arrivedAt,
        DateTimeOffset? cancelledAt)
    {
        if (cancelledAt is not null)
        {
            return TransportStatus.Cancelled;
        }

        if (arrivedAt is not null)
        {
            return TransportStatus.Completed;
        }

        if (departedAt is not null)
        {
            return TransportStatus.InTransit;
        }

        return approvedAt is null ? TransportStatus.AwaitingApproval : TransportStatus.Scheduled;
    }

    /// <summary>
    /// Booked to leave in the past and still has not departed.
    /// </summary>
    /// <remarks>
    /// Only answerable because ScheduledFor is a timestamp. With the
    /// prototype's "ETA 16:30" string there was no way to ask this at all, so
    /// a move that quietly missed its slot looked identical to one on time.
    /// </remarks>
    public static bool IsLate(TransportMove move, DateTimeOffset now) =>
        IsLate(move.ScheduledFor, move.DepartedAt, move.ArrivedAt, move.CancelledAt, now);

    public static bool IsLate(
        DateTimeOffset scheduledFor,
        DateTimeOffset? departedAt,
        DateTimeOffset? arrivedAt,
        DateTimeOffset? cancelledAt,
        DateTimeOffset now) =>
        departedAt is null && arrivedAt is null && cancelledAt is null && scheduledFor < now;

    public static TransitionResult CanApprove(TransportMove move)
    {
        if (move.CancelledAt is not null)
        {
            return TransitionResult.Refuse("A cancelled move cannot be approved.");
        }

        return move.ApprovedAt is null
            ? TransitionResult.Ok()
            : TransitionResult.Refuse("This move has already been approved.");
    }

    public static TransitionResult CanDepart(TransportMove move)
    {
        if (move.CancelledAt is not null)
        {
            return TransitionResult.Refuse("A cancelled move cannot depart.");
        }

        if (move.ApprovedAt is null)
        {
            // The gate the status field used to merely describe.
            return TransitionResult.Refuse(
                "A move cannot depart before it has been approved.");
        }

        return move.DepartedAt is null
            ? TransitionResult.Ok()
            : TransitionResult.Refuse("This move has already departed.");
    }

    public static TransitionResult CanArrive(TransportMove move)
    {
        if (move.CancelledAt is not null)
        {
            return TransitionResult.Refuse("A cancelled move cannot arrive.");
        }

        if (move.DepartedAt is null)
        {
            return TransitionResult.Refuse(
                "Arrival can only be recorded against a move that has departed.");
        }

        return move.ArrivedAt is null
            ? TransitionResult.Ok()
            : TransitionResult.Refuse("This move has already arrived.");
    }

    public static TransitionResult CanCancel(TransportMove move)
    {
        if (move.CancelledAt is not null)
        {
            return TransitionResult.Refuse("This move is already cancelled.");
        }

        // A completed move is history. Cancelling it would erase the record
        // that an asset actually moved, which other records depend on.
        return move.ArrivedAt is null
            ? TransitionResult.Ok()
            : TransitionResult.Refuse("A completed move cannot be cancelled.");
    }
}
