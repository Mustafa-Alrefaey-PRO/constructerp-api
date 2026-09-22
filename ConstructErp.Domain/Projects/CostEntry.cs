using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Projects;

public enum CostCategory
{
    Equipment = 1,
    Transport = 2,
    Extras = 3,
}

/// <summary>
/// One line of spend against a project. Project totals are summed from these
/// rather than stored, so a total can never disagree with its detail.
/// </summary>
public sealed class CostEntry : Entity
{
    public Guid ProjectId { get; set; }

    public Project? Project { get; set; }

    public CostCategory Category { get; set; }

    /// <summary>KWD, three decimal places.</summary>
    public decimal Amount { get; set; }

    public DateOnly IncurredOn { get; set; }

    public LocalizedText Description { get; set; } = new();
}
