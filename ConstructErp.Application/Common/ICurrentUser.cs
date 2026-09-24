using ConstructErp.Domain.Identity;

namespace ConstructErp.Application.Common;

/// <summary>
/// Who is making this request, and what they are allowed to see.
/// </summary>
/// <remarks>
/// The two scope properties are what the database query filter reads. Both
/// null means "no scoping" — which is correct for an Admin and for background
/// work like seeding, and is why every endpoint that touches scoped data must
/// also carry an authorization requirement. A filter cannot protect a route
/// that never asked who was calling.
/// </remarks>
public interface ICurrentUser
{
    Guid? UserId { get; }

    UserRole? Role { get; }

    Guid? OrganizationId { get; }

    bool IsAuthenticated { get; }

    /// <summary>
    /// The organization to restrict rows to, or null for no restriction.
    /// </summary>
    /// <remarks>
    /// Null for an Admin and for system contexts. A carrier's staff and its
    /// drivers both carry their carrier's id.
    /// </remarks>
    Guid? ScopeOrganizationId { get; }

    /// <summary>
    /// The user to restrict rows to, or null for no restriction.
    /// </summary>
    /// <remarks>
    /// Set for a Driver only. A driver is scoped more tightly than their
    /// carrier: to the jobs assigned to them personally.
    /// </remarks>
    Guid? ScopeDriverId { get; }
}

/// <summary>
/// The no-user context: seeding, migrations, and tests that talk to the
/// DbContext directly. Unscoped by design, and never reachable over HTTP.
/// </summary>
public sealed class SystemUser : ICurrentUser
{
    public Guid? UserId => null;

    public UserRole? Role => null;

    public Guid? OrganizationId => null;

    public bool IsAuthenticated => false;

    public Guid? ScopeOrganizationId => null;

    public Guid? ScopeDriverId => null;
}
