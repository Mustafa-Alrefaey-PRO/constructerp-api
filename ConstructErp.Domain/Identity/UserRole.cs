namespace ConstructErp.Domain.Identity;

/// <summary>
/// What a user may do.
/// </summary>
/// <remarks>
/// A role answers "what may this person do". It deliberately does NOT answer
/// "whose data may they see" — that is <see cref="AppUser.OrganizationId"/>.
/// Collapsing the two is the mistake that makes row-level scoping impossible
/// to add later: with one carrier it looks identical, and with two every
/// carrier can read the other's jobs.
///
/// Stored as an int, like every other enum here, so renaming a member is not a
/// data migration.
///
/// This is the first slice. ProjectManager, SiteEngineer, Inspector and
/// Procurement belong here when the request and inspection workflows get their
/// own operators; until then those screens are Admin-only. Add new members
/// with new numbers — never renumber an existing one.
/// </remarks>
public enum UserRole
{
    /// <summary>Full access, no organization scoping.</summary>
    Admin = 0,

    /// <summary>A carrier's office staff: sees and runs that carrier's moves.</summary>
    TruckingCompany = 1,

    /// <summary>Sees only the moves assigned to them, and records departure and arrival.</summary>
    Driver = 2,
}
