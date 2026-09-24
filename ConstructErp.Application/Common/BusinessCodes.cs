namespace ConstructErp.Application.Common;

/// <summary>
/// Allocates the next business code in a series, e.g. REQ-0004.
/// </summary>
/// <remarks>
/// This exists because the client used to guess. It listed the codes it could
/// see, took the highest, and added one — which is wrong twice over:
///
///   1. Soft-deleted rows are hidden from it, but the unique index still
///      covers them. Delete REQ-0001 and the next guess is REQ-0001 again,
///      which passes every check the app can make and then fails at the
///      database with a 500.
///   2. Two people creating a record at the same moment guess the same code.
///
/// Allocation belongs to the system of record. Callers pass the codes ALREADY
/// IN USE — including soft-deleted ones, which is the part that was missing —
/// and get one that is free.
/// </remarks>
public static class BusinessCodes
{
    /// <param name="prefix">Including the separator, e.g. "REQ-".</param>
    /// <param name="taken">Every code in the series, live or soft-deleted.</param>
    public static string Next(string prefix, IEnumerable<string> taken)
    {
        var highest = taken
            .Where(code => code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            .Select(code => int.TryParse(code[prefix.Length..], out var number) ? number : 0)
            .DefaultIfEmpty(0)
            .Max();

        return $"{prefix}{highest + 1:0000}";
    }
}
