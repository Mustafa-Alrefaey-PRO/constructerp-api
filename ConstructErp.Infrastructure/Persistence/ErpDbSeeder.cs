using ConstructErp.Domain.Common;
using ConstructErp.Domain.Equipment;
using ConstructErp.Domain.Projects;
using ConstructErp.Domain.Rentals;
using ConstructErp.Domain.Transport;
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
        await SeedCostEntriesAsync(projects, cancellationToken);

        var vendors = await SeedVendorsAsync(cancellationToken);
        await SeedRentalsAsync(vendors, projects, cancellationToken);
        await SeedTransportAsync(projects, cancellationToken);
    }

    /// <summary>
    /// Demo moves, one at each stage of the workflow.
    /// </summary>
    /// <remarks>
    /// Like the rentals, these are seeded as EVENTS rather than statuses, and
    /// dated relative to now. The move that is In Transit is in transit because
    /// a departure was recorded for it, not because the word was typed into a
    /// column — which is the whole point of TransportSchedule.
    ///
    /// The prototype's third move belonged to "Harbor Yard", which was never a
    /// project. It is seeded with no project rather than inventing one, the
    /// same choice the equipment seeder makes.
    /// </remarks>
    private async Task SeedTransportAsync(
        Dictionary<string, Project> projects, CancellationToken cancellationToken)
    {
        if (await db.TransportMoves.AnyAsync(cancellationToken))
        {
            return;
        }

        var equipment = await db.Equipment.ToDictionaryAsync(e => e.Code, cancellationToken);
        var now = DateTimeOffset.UtcNow;

        var wanted = new (string Code, string Equipment, string? Project, string OriginEn,
            string OriginAr, string DestEn, string DestAr, TransportKind Kind, double HoursOut,
            bool Approved, bool Departed, decimal Cost)[]
        {
            // Approved and departed -> In Transit, derived.
            ("TRP-5001", "EQ-331", "PRJ-1001", "Yard A", "الساحة أ",
                "Downtown Tower", "برج وسط المدينة", TransportKind.Delivery, 3, true, true, 2400m),
            // Approved, not yet departed -> Scheduled.
            ("TRP-5002", "EQ-219", "PRJ-1018", "Airport Expansion", "توسعة المطار",
                "Vendor Yard", "ساحة المورد", TransportKind.ReturnMove, 48, true, false, 3150m),
            // Not approved -> Awaiting Approval, and it cannot depart.
            ("TRP-5003", "EQ-512", null, "Harbor Yard", "ساحة الميناء",
                "Service Center", "مركز الخدمة", TransportKind.InspectionTransfer, 96,
                false, false, 1100m),
        };

        foreach (var (code, equipmentCode, projectCode, originEn, originAr, destEn, destAr,
                     kind, hoursOut, approved, departed, cost) in wanted)
        {
            if (!equipment.TryGetValue(equipmentCode, out var asset))
            {
                continue;
            }

            db.TransportMoves.Add(new TransportMove
            {
                Code = code,
                EquipmentId = asset.Id,
                ProjectId = projectCode is not null && projects.TryGetValue(projectCode, out var p)
                    ? p.Id
                    : null,
                Origin = new LocalizedText(originEn, originAr),
                Destination = new LocalizedText(destEn, destAr),
                Kind = kind,
                ScheduledFor = now.AddHours(hoursOut),
                ApprovedAt = approved ? now.AddHours(-6) : null,
                DepartedAt = departed ? now.AddHours(-1) : null,
                Cost = cost,
                Notes = new LocalizedText(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} transport moves.", wanted.Length);
    }

    private async Task<Dictionary<string, Vendor>> SeedVendorsAsync(
        CancellationToken cancellationToken)
    {
        var existing = await db.Vendors.ToDictionaryAsync(v => v.Code, cancellationToken);

        // The three names the prototype carried as bare strings on rental rows.
        var wanted = new[]
        {
            new Vendor
            {
                Code = "VEN-001",
                Name = new LocalizedText("Delta Heavy Rentals", "دلتا لتأجير المعدات الثقيلة"),
                ContactName = "K. Mansour",
                Phone = "+965 2222 1180",
                Email = "hire@deltaheavy.com.kw",
            },
            new Vendor
            {
                Code = "VEN-002",
                Name = new LocalizedText("Prime Lift Services", "برايم لخدمات الرفع"),
                ContactName = "R. Aziz",
                Phone = "+965 2222 4471",
                Email = "bookings@primelift.com.kw",
            },
            new Vendor
            {
                Code = "VEN-003",
                Name = new LocalizedText("SitePower Rental", "سايت باور للتأجير"),
                ContactName = "H. Darwish",
                Phone = "+965 2222 9034",
                Email = "support@sitepower.com.kw",
            },
        };

        foreach (var vendor in wanted)
        {
            if (existing.ContainsKey(vendor.Code))
            {
                continue;
            }

            db.Vendors.Add(vendor);
            existing[vendor.Code] = vendor;
        }

        await db.SaveChangesAsync(cancellationToken);
        return existing;
    }

    /// <summary>
    /// Demo hires, dated relative to today.
    /// </summary>
    /// <remarks>
    /// Deliberately relative. The prototype's rentals carried fixed dates
    /// ("Jul 24") beside a hand-typed status, so within weeks every row read
    /// "Active" next to a return date months in the past — the exact
    /// contradiction RentalSchedule removes. Anchoring to today keeps one
    /// rental of each kind on screen no matter when the demo is opened, and the
    /// statuses shown are genuinely derived rather than arranged.
    /// </remarks>
    private async Task SeedRentalsAsync(
        Dictionary<string, Vendor> vendors,
        Dictionary<string, Project> projects,
        CancellationToken cancellationToken)
    {
        if (await db.Rentals.AnyAsync(cancellationToken))
        {
            return;
        }

        var equipment = await db.Equipment.ToDictionaryAsync(e => e.Code, cancellationToken);
        var today = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3).Date);

        var wanted = new (string Code, string Vendor, string Equipment, string? Project,
            int StartOffset, int DueOffset, int? BookedOffset, decimal Amount)[]
        {
            // Return booked, still in the future -> Return Scheduled.
            ("RNT-2007", "VEN-001", "EQ-219", "PRJ-1018", -34, 12, -2, 9800m),
            // Running, nothing booked yet -> Active.
            ("RNT-2011", "VEN-002", "EQ-577", "PRJ-1001", -21, 26, null, 14600m),
            // Due date has passed and it is still out -> Overdue, derived.
            ("RNT-2014", "VEN-003", "EQ-448", "PRJ-1032", -48, -7, null, 1920m),
        };

        foreach (var (code, vendorCode, equipmentCode, projectCode,
                     startOffset, dueOffset, bookedOffset, amount) in wanted)
        {
            if (!vendors.TryGetValue(vendorCode, out var vendor)
                || !equipment.TryGetValue(equipmentCode, out var asset))
            {
                continue;
            }

            db.Rentals.Add(new Rental
            {
                Code = code,
                VendorId = vendor.Id,
                EquipmentId = asset.Id,
                ProjectId = projectCode is not null && projects.TryGetValue(projectCode, out var p)
                    ? p.Id
                    : null,
                StartedOn = today.AddDays(startOffset),
                ExpectedReturnOn = today.AddDays(dueOffset),
                ReturnBookedOn = bookedOffset is { } booked ? today.AddDays(booked) : null,
                Amount = amount,
                Notes = new LocalizedText(),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} rentals.", wanted.Length);
    }

    /// <summary>
    /// Demo spend, so the cost screens show something real.
    /// </summary>
    /// <remarks>
    /// The prototype displayed these totals as numbers typed onto the project.
    /// They are now cost ENTRIES, and the project's totals are summed from
    /// them — so the figures on screen are the same, but they can no longer
    /// disagree with the records behind them.
    /// </remarks>
    private async Task SeedCostEntriesAsync(
        Dictionary<string, Project> projects, CancellationToken cancellationToken)
    {
        if (await db.CostEntries.AnyAsync(cancellationToken))
        {
            return;
        }

        var wanted = new (string ProjectCode, CostCategory Category, decimal Amount, string En, string Ar)[]
        {
            ("PRJ-1001", CostCategory.Equipment, 184000m, "Crane and plant hire", "إيجار الأوناش والمعدات"),
            ("PRJ-1001", CostCategory.Transport, 24500m, "Site deliveries", "توصيلات الموقع"),
            ("PRJ-1001", CostCategory.Extras, 11200m, "Operator overtime", "ساعات إضافية للمشغلين"),
            ("PRJ-1018", CostCategory.Equipment, 139000m, "Concrete pumping", "ضخ الخرسانة"),
            ("PRJ-1018", CostCategory.Transport, 31800m, "Lowbed movements", "حركات المقطورات"),
            ("PRJ-1018", CostCategory.Extras, 8400m, "Permits and escorts", "التصاريح والمرافقة"),
            ("PRJ-1032", CostCategory.Equipment, 98000m, "Lighting and site support", "الإضاءة ودعم الموقع"),
            ("PRJ-1032", CostCategory.Transport, 14900m, "Return haulage", "نقل الرجوع"),
            ("PRJ-1032", CostCategory.Extras, 6200m, "Standby charges", "رسوم الانتظار"),
        };

        var incurred = new DateOnly(2026, 7, 1);

        foreach (var (code, category, amount, en, ar) in wanted)
        {
            if (!projects.TryGetValue(code, out var project))
            {
                continue;
            }

            db.CostEntries.Add(new CostEntry
            {
                ProjectId = project.Id,
                Category = category,
                Amount = amount,
                IncurredOn = incurred,
                Description = new LocalizedText(en, ar),
            });
        }

        await db.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded {Count} cost entries.", wanted.Length);
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
        // Checked per code rather than "any equipment exists". The all-or-nothing
        // version meant an asset added to this list later never reached a
        // database that had already been seeded once.
        var existing = await db.Equipment
            .Select(asset => asset.Code)
            .ToListAsync(cancellationToken);

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
            // Hired from Prime Lift. The prototype had a rental for this crane
            // but no fleet record, so the hire referred to a machine that did
            // not exist — a foreign key makes that impossible to repeat.
            Asset("EQ-577", "Mobile Crane 120T", "ونش متحرك 120 طن", "LIFT",
                Ownership.ExternalRental, "PRJ-1001", EquipmentStatus.Working, 81, 1480m,
                "Hire runs to month end", "الإيجار حتى نهاية الشهر"),
        };

        var added = assets.Where(asset => !existing.Contains(asset.Code)).ToArray();

        if (added.Length == 0)
        {
            logger.LogInformation("Equipment already seeded; skipping.");
            return;
        }

        db.Equipment.AddRange(added);
        await db.SaveChangesAsync(cancellationToken);

        logger.LogInformation("Seeded {Count} equipment assets.", added.Length);

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
