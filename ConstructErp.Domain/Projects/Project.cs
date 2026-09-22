using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;

namespace ConstructErp.Domain.Projects;

public enum ProjectStatus
{
    Active = 1,
    AtRisk = 2,
    Closing = 3,
}

public sealed class Project : Entity
{
    /// <summary>Human-readable business identifier, e.g. PRJ-1001. Unique, but not the key.</summary>
    public string Code { get; set; } = string.Empty;

    public LocalizedText Name { get; set; } = new();

    public LocalizedText Client { get; set; } = new();

    public string Manager { get; set; } = string.Empty;

    public LocalizedText Location { get; set; } = new();

    public ProjectStatus Status { get; set; } = ProjectStatus.Active;

    /// <summary>
    /// KWD. Three decimal places — see ErpDbContext.MoneyPrecision. A fils is
    /// 1/1000 of a dinar, so decimal(18,2) would silently round every amount.
    /// </summary>
    public decimal Budget { get; set; }

    public DateOnly? StartDate { get; set; }

    public DateOnly? EndDate { get; set; }

    /// <summary>Percentage, 0-100.</summary>
    public int Progress { get; set; }

    public ICollection<EquipmentAsset> Equipment { get; set; } = [];

    /// <summary>
    /// Spend is DERIVED from cost entries, not typed in.
    /// </summary>
    /// <remarks>
    /// The prototype stored equipmentSpend/transportSpend/extraSpend as manual
    /// numbers on the project, which meant the totals could disagree with the
    /// records behind them. Cost entries are the source of truth; these
    /// properties are not mapped and are projected by queries instead.
    /// </remarks>
    public ICollection<CostEntry> CostEntries { get; set; } = [];
}
