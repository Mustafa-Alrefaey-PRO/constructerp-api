namespace ConstructErp.Domain.Common;

/// <summary>
/// A user-entered name that exists in both languages.
/// </summary>
/// <remarks>
/// The prototype translated data values through a lookup table in the frontend
/// (`dataLabels`). That works only for seeded records: the moment a user
/// creates a project, it has no Arabic entry and falls back to English forever.
/// Storing both languages on the row is the only thing that survives real data.
///
/// Arabic is optional — a user may not supply it — and callers fall back to
/// <see cref="En"/>. Both are NVARCHAR; see ErpDbContext for why that matters.
/// </remarks>
public sealed class LocalizedText
{
    public LocalizedText() { }

    public LocalizedText(string en, string? ar = null)
    {
        En = en;
        Ar = ar;
    }

    public string En { get; set; } = string.Empty;

    public string? Ar { get; set; }

    public string For(string language) =>
        language.StartsWith("ar", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(Ar)
            ? Ar
            : En;

    public override string ToString() => En;
}
