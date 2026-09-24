using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Identity;

public enum OrganizationKind
{
    /// <summary>The company that owns this system. Exactly one of these.</summary>
    Internal = 0,

    /// <summary>An external haulage contractor that runs transport moves.</summary>
    Carrier = 1,
}

/// <summary>
/// A company whose users share a view of the data.
/// </summary>
/// <remarks>
/// This is the scoping boundary — the answer to "whose records are these".
/// A carrier's staff and drivers see that carrier's transport moves and
/// nothing else, enforced by a query filter in ErpDbContext rather than by
/// each endpoint remembering to add a WHERE clause.
///
/// Deliberately NOT merged with <c>Vendor</c>. A vendor rents equipment out; a
/// carrier moves it. They are different relationships with different records
/// hanging off them, and a company could one day be both — at which point one
/// shared table would have to grow a discriminator anyway. If they do need to
/// merge, that is its own migration, not something to guess at now.
/// </remarks>
public sealed class Organization : Entity
{
    /// <summary>Business identifier, e.g. ORG-001.</summary>
    public string Code { get; set; } = string.Empty;

    public LocalizedText Name { get; set; } = new();

    public OrganizationKind Kind { get; set; }

    public string ContactName { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public ICollection<AppUser> Users { get; set; } = [];
}
