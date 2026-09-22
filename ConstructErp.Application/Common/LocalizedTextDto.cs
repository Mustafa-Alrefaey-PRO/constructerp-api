using ConstructErp.Domain.Common;

namespace ConstructErp.Application.Common;

/// <summary>
/// A bilingual name on the wire.
/// </summary>
/// <remarks>
/// Both languages are returned on every response rather than the caller's
/// Accept-Language. The frontend's language toggle is instant — switching to
/// Arabic must not require refetching every list — so the client holds both and
/// picks. It also keeps responses cacheable without varying on a header.
/// </remarks>
public sealed record LocalizedTextDto(string En, string? Ar)
{
    public static LocalizedTextDto From(LocalizedText value) => new(value.En, value.Ar);

    public LocalizedText ToDomain() => new(En, Ar);
}
