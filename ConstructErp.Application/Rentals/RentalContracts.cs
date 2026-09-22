using ConstructErp.Application.Common;

namespace ConstructErp.Application.Rentals;

public sealed record VendorDto(
    Guid Id,
    string Code,
    LocalizedTextDto Name,
    string ContactName,
    string Phone,
    string Email,
    // Summed from the vendor's rentals, never stored — the same rule the
    // project spend figures follow.
    int RentalCount,
    int OpenRentalCount,
    decimal TotalSpend);

public sealed record SaveVendorRequest(
    string Code,
    LocalizedTextDto Name,
    string? ContactName,
    string? Phone,
    string? Email);

public sealed record RentalDto(
    Guid Id,
    string Code,
    Guid VendorId,
    LocalizedTextDto VendorName,
    Guid EquipmentId,
    string EquipmentCode,
    LocalizedTextDto EquipmentName,
    Guid? ProjectId,
    string? ProjectCode,
    LocalizedTextDto? ProjectName,
    DateOnly StartedOn,
    DateOnly ExpectedReturnOn,
    DateOnly? ReturnBookedOn,
    DateOnly? ReturnedOn,
    decimal Amount,
    LocalizedTextDto Notes,
    // Derived from the dates above against today's date on the server. Not a
    // stored column — see RentalSchedule.
    string Status,
    int DaysOverdue);

public sealed record SaveRentalRequest(
    string Code,
    Guid VendorId,
    Guid EquipmentId,
    Guid? ProjectId,
    DateOnly StartedOn,
    DateOnly ExpectedReturnOn,
    decimal Amount,
    LocalizedTextDto? Notes);

/// <summary>Books a collection with the vendor. Date is optional — defaults to today.</summary>
public sealed record BookReturnRequest(DateOnly? BookedOn);

/// <summary>Records the asset actually coming back.</summary>
public sealed record RecordReturnRequest(DateOnly? ReturnedOn);
