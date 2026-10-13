namespace ConstructErp.Domain.Common;

/// <summary>
/// Every reason the API can refuse a request, as a stable code.
/// </summary>
/// <remarks>
/// Clients translate these codes; the English text sent with them is only a
/// fallback. A code is permanent once released: never rename or reuse one —
/// add a new code and stop sending the old one instead. Each code needs an
/// English and Arabic entry in the frontend's error table.
/// </remarks>
public static class ErrorCodes
{
    // Validation
    public const string CodeRequired = "codeRequired";
    public const string EnglishNameRequired = "englishNameRequired";
    public const string NegativeAmount = "negativeAmount";
    public const string InvalidValue = "invalidValue";
    public const string UnknownProject = "unknownProject";
    public const string UnknownEquipment = "unknownEquipment";
    public const string UnknownEquipmentType = "unknownEquipmentType";
    public const string UnknownVendor = "unknownVendor";
    public const string CodeInUse = "codeInUse";
    public const string ReturnBeforeRequired = "returnBeforeRequired";
    public const string RejectionReasonRequired = "rejectionReasonRequired";

    // Requests
    public const string SubmitNotAllowed = "submitNotAllowed";
    public const string PreRequestChecksIncomplete = "preRequestChecksIncomplete";
    public const string ApproveNotAllowed = "approveNotAllowed";
    public const string RejectNotAllowed = "rejectNotAllowed";
    public const string ReceiveNotAllowed = "receiveNotAllowed";
    public const string PreReceivingChecksIncomplete = "preReceivingChecksIncomplete";
    public const string InspectNotAllowed = "inspectNotAllowed";
    public const string RequestLocked = "requestLocked";
    public const string ChecklistLocked = "checklistLocked";
    public const string EquipmentNotReady = "equipmentNotReady";

    // Rentals and vendors
    public const string RentalAlreadyReturned = "rentalAlreadyReturned";
    public const string HireNotStarted = "hireNotStarted";
    public const string ReturnBeforeHireStart = "returnBeforeHireStart";
    public const string VendorHasRentals = "vendorHasRentals";

    // Transport
    public const string MoveCancelled = "moveCancelled";
    public const string MoveAlreadyApproved = "moveAlreadyApproved";
    public const string MoveNotApproved = "moveNotApproved";
    public const string MoveAlreadyDeparted = "moveAlreadyDeparted";
    public const string MoveNotDeparted = "moveNotDeparted";
    public const string MoveAlreadyArrived = "moveAlreadyArrived";
    public const string MoveAlreadyCancelled = "moveAlreadyCancelled";
    public const string MoveCompleted = "moveCompleted";
}
