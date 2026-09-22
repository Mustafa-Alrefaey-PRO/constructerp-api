using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace ConstructErp.Infrastructure.Persistence;

/// <summary>
/// Seeds reference data and the demo dataset.
/// </summary>
/// <remarks>
/// Runtime seeding rather than <c>HasData</c>: EF Core cannot seed entities
/// with complex properties (dotnet/efcore#31254), and every name here is a
/// <see cref="LocalizedText"/>.
///
/// Idempotent — it checks before inserting, so it is safe to run on every
/// start. The demo records mirror the frontend's mock data so the Angular app
/// shows the same fleet it always has once it is pointed at the API.
/// </remarks>
public sealed class ErpDbSeeder(ErpDbContext db, ILogger<ErpDbSeeder> logger)
{
    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        var types = await SeedEquipmentTypesAsync(cancellationToken);
        var projects = await SeedProjectsAsync(cancellationToken);
        await SeedEquipmentAsync(types, projects, cancellationToken);
    }

    private async Task<Dictionary<string, EquipmentType>> SeedEquipmentTypesAsync(
        CancellationToken cancellationToken)
    {
        var existing = await db.EquipmentTypes.ToDictionaryAsync(t => t.Code, cancellationToken);

        var wanted = new (string Code, string En, string Ar)[]
        {
            ("LIFT", "Lifting", "رفع"),
            ("CONC", "Concrete", "خرسانة"),
            ("TRAN", "Transportation", "نقل"),
            ("SITE", "Site Support", "دعم الموقع"),
            ("EART", "Earthworks", "أعمال ترابية"),
        };

        foreach (var (code, en, ar) in wanted)
        {
            if (existing.ContainsKey(code))
            {
                continue;
            }

            var type = new EquipmentType { Code = code, Name = new LocalizedText(en, ar) };
            db.EquipmentTypes.Add(type);
            existing[code] = type;
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private async Task<Dictionary<string, Project>> SeedProjectsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await db.Projects.ToDictionaryAsync(p => p.Code, cancellationToken);

        var wanted = new[]
        {
            new Project
            {
                Code = "PRJ-1001",
                Name = new LocalizedText("Downtown Tower", "برج وسط المدينة"),
                Client = new LocalizedText("Finesco Development", "فينسكو للتطوير"),
                Manager = "M. Hassan",
                Location = new LocalizedText("East Gate, Zone 4", "البوابة الشرقية، المنطقة 4"),
                Status = ProjectStatus.Active,
                Budget = 260000m,
                Progress = 76,
            },
            new Project
            {
                Code = "PRJ-1018",
                Name = new LocalizedText("Airport Expansion", "توسعة المطار"),
                Client = new LocalizedText("National Airports Authority", "هيئة المطارات الوطنية"),
                Manager = "A. Farouk",
                Location = new LocalizedText("Airport Expansion", "توسعة المطار"),
                Status = ProjectStatus.Active,
                Budget = 230000m,
                Progress = 64,
            },
            new Project
            {
                Code = "PRJ-1032",
                Name = new LocalizedText("Metro Station Works", "أعمال محطة المترو"),
                Client = new LocalizedText("Metro Projects JV", "تحالف مشاريع المترو"),
                Manager = "L. Ibrahim",
                Location = new LocalizedText("Metro Station Works", "أعمال محطة المترو"),
                Status = ProjectStatus.AtRisk,
                Budget = 168000m,
                Progress = 42,
            },
        };

        foreach (var project in wanted)
        {
            if (existing.ContainsKey(project.Code))
            {
                continue;
            }

            db.Projects.Add(project);
            existing[project.Code] = project;
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    private async Task SeedEquipmentAsync(
        Dictionary<string, EquipmentType> types,
        Dictionary<string, Project> projects,
        CancellationToken cancellationToken)
    {
        if (await db.Equipment.AnyAsync(cancellationToken))
        {
            logger.LogInformation("Equipment already seeded; skipping.");
            return;
        }

        // Project is matched by CODE here, not by name — the whole point of the
        // foreign key. "Ring Road Package B" and "Harbor Yard" have no project
        // record in the prototype data, so those assets are seeded unassigned
        // rather than inventing projects to satisfy a string match.
        var assets = new[]
        {
            Asset("EQ-104", "Crawler Crane 80T", "ونش زاحف 80 طن", "LIFT", Ownership.Owned,
                "PRJ-1001", EquipmentStatus.Working, 86, 1250m,
                "Routine inspection tomorrow", "تفتيش دوري غدا"),
            Asset("EQ-219", "Concrete Pump 42m", "مضخة خرسانة 42 م", "CONC", Ownership.ExternalRental,
                "PRJ-1018", EquipmentStatus.ReturnScheduled, 72, 980m,
                "Return booking confirmed", "تم تأكيد حجز الرجوع"),
            Asset("EQ-331", "Lowbed Trailer", "مقطورة لوبد", "TRAN", Ownership.Owned,
                null, EquipmentStatus.InTransit, 64, 410m,
                "Arrives at site 16:30", "الوصول للموقع 16:30"),
            Asset("EQ-448", "Tower Light Set", "وحدة إضاءة برجية", "SITE", Ownership.ExternalRental,
                "PRJ-1032", EquipmentStatus.Idle, 18, 160m,
                "Review rental continuation", "مراجعة استمرار الإيجار"),
            Asset("EQ-512", "Excavator 36T", "حفار 36 طن", "EART", Ownership.Owned,
                null, EquipmentStatus.InspectionDue, 57, 690m,
                "Operator checklist missing", "قائمة فحص المشغل غير مكتملة"),
        };

        db.Equipment.AddRange(assets);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} equipment assets.", assets.Length);

        EquipmentAsset Asset(
            string code, string nameEn, string nameAr, string typeCode, Ownership ownership,
            string? projectCode, EquipmentStatus status, int utilization, decimal dailyCost,
            string actionEn, string actionAr) => new()
            {
                Code = code,
                Name = new LocalizedText(nameEn, nameAr),
                EquipmentTypeId = types[typeCode].Id,
                Ownership = ownership,
                ProjectId = projectCode is null ? null : projects[projectCode].Id,
                Status = status,
                Utilization = utilization,
                DailyCost = dailyCost,
                NextAction = new LocalizedText(actionEn, actionAr),
            };
    }
}
