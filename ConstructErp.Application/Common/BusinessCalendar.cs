namespace ConstructErp.Application.Common;

/// <summary>
/// What "today" means to this business.
/// </summary>
/// <remarks>
/// Anything derived from a date — an overdue rental, a due inspection — needs
/// one answer to this, or the same record reads differently depending on which
/// server answered.
///
/// Kuwait is UTC+3 year-round with no daylight saving, so a fixed offset is
/// exact rather than an approximation. It is written as an offset instead of a
/// time zone id on purpose: zone databases differ between Windows and the Linux
/// containers CI runs on, and a lookup that fails at runtime would take out
/// every date-derived field at once.
///
/// If the product ever operates outside Kuwait this becomes per-tenant and
/// every caller below has to pass one in.
/// </remarks>
public static class BusinessCalendar
{
    public static readonly TimeSpan Offset = TimeSpan.FromHours(3);

    public static DateOnly Today(TimeProvider clock) =>
        DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(Offset).DateTime);
}
