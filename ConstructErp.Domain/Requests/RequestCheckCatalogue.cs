using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Requests;

/// <summary>
/// The standard gate conditions applied to every new request.
/// </summary>
/// <remarks>
/// Codes are stable and are what rules and reports key off; labels are display
/// text and may be reworded without a data migration.
///
/// These are still manually ticked, exactly as in the prototype. Several are
/// answerable from data the system already holds — "Equipment available" and
/// "No idle similar equipment" are queries, not opinions — and should become
/// automatic. See PROJECT_GUIDE.md §6.6.
/// </remarks>
public static class RequestCheckCatalogue
{
    public static IReadOnlyList<RequestCheck> CreateFor(Guid requestId) =>
    [
        .. PreRequest.Select((item, index) => Build(requestId, CheckKind.PreRequest, item, index)),
        .. PreReceiving.Select((item, index) => Build(requestId, CheckKind.PreReceiving, item, index)),
    ];

    private static readonly (string Code, string En, string Ar)[] PreRequest =
    [
        ("EQUIPMENT_AVAILABLE", "Equipment available", "المعدة متاحة"),
        ("PROJECT_ACTIVE", "Project is active", "المشروع نشط"),
        ("NO_IDLE_SIMILAR", "No idle similar equipment", "لا توجد معدة مشابهة خاملة"),
        ("RENTAL_PERIOD_VALID", "Rental period is valid", "فترة الإيجار صحيحة"),
        ("COST_WITHIN_BUDGET", "Cost within budget", "التكلفة داخل الميزانية"),
        ("DELIVERY_COST_ENTERED", "Delivery cost entered", "تم إدخال تكلفة التوصيل"),
    ];

    private static readonly (string Code, string En, string Ar)[] PreReceiving =
    [
        ("APPROVED_REQUEST_EXISTS", "Approved request exists", "يوجد طلب معتمد"),
        ("CORRECT_EQUIPMENT", "Correct equipment and project", "المعدة والمشروع صحيحان"),
        ("TRANSPORT_ENTERED", "Transport details entered", "تم إدخال بيانات النقل"),
        ("ARRIVAL_DOCUMENTED", "Arrival condition documented", "تم توثيق حالة الوصول"),
        ("MEDIA_ATTACHED", "Photos/videos attached", "تم إرفاق الصور والفيديوهات"),
        ("RECEIVER_SIGNED", "Receiver signature captured", "تم تسجيل توقيع المستلم"),
    ];

    private static RequestCheck Build(
        Guid requestId, CheckKind kind, (string Code, string En, string Ar) item, int index) => new()
        {
            RequestId = requestId,
            Kind = kind,
            Code = item.Code,
            Label = new LocalizedText(item.En, item.Ar),
            Passed = false,
            Sequence = index,
        };
}
