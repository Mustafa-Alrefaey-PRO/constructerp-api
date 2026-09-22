using ConstructErp.Domain.Common;

namespace ConstructErp.Domain.Requests;

public enum CheckKind
{
    /// <summary>Gate before the request may be submitted for approval.</summary>
    PreRequest = 1,

    /// <summary>Gate before delivery may be accepted on site.</summary>
    PreReceiving = 2,
}

/// <summary>
/// One condition on a request's gate.
/// </summary>
/// <remarks>
/// A row per check rather than a JSON blob, so a check can be queried,
/// reported on and eventually given its own evidence and signer. The prototype
/// stored these as an array of booleans on the request, which meant "how many
/// requests are blocked on the receiver signature?" was unanswerable.
/// </remarks>
public sealed class RequestCheck : Entity
{
    public Guid RequestId { get; set; }

    public EquipmentRequest? Request { get; set; }

    public CheckKind Kind { get; set; }

    /// <summary>Stable key, e.g. EQUIPMENT_AVAILABLE. Survives label changes.</summary>
    public string Code { get; set; } = string.Empty;

    public LocalizedText Label { get; set; } = new();

    public bool Passed { get; set; }

    /// <summary>Display order within its gate.</summary>
    public int Sequence { get; set; }
}
