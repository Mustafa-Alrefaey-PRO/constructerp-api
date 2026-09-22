namespace ConstructErp.Domain.Common;

/// <summary>
/// Base for every persisted entity.
/// </summary>
/// <remarks>
/// Identity is a GUID, not the human-readable code (PRJ-1001, EQ-104). Those
/// codes are business identifiers that users edit and occasionally re-issue;
/// making one a primary key means a rename cascades through every foreign key.
/// The prototype linked equipment to projects by NAME, which silently broke a
/// project's linked counts whenever it was renamed — this is the fix.
/// </remarks>
public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    /// <summary>Set once auth exists; null until then.</summary>
    public string? CreatedBy { get; set; }

    public string? UpdatedBy { get; set; }

    /// <summary>
    /// Soft delete. The prototype's delete was unrecoverable; construction
    /// disputes are settled with records, so rows are retired, not destroyed.
    /// </summary>
    public DateTimeOffset? DeletedAt { get; set; }
}
