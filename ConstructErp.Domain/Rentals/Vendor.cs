using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Rentals;

/// <summary>
/// A company equipment is rented from.
/// </summary>
/// <remarks>
/// A record rather than the prototype's bare string. "Delta Heavy Rentals"
/// typed onto each rental cannot be contacted, rated, or totalled — and the
/// same vendor spelled two ways splits every spend report in half.
/// </remarks>
public sealed class Vendor : Entity
{
    /// <summary>Business identifier, e.g. VEN-001.</summary>
    public string Code { get; set; } = string.Empty;

    public LocalizedText Name { get; set; } = new();

    public string ContactName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public ICollection<Rental> Rentals { get; set; } = [];
}
