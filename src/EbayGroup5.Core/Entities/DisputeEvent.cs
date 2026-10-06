namespace EbayGroup5.Core.Entities;

/// <summary>Append-only request timeline (DbUp 0011, trigger TR_DisputeEvent_AppendOnly).</summary>
public sealed class DisputeEvent
{
    public int Id { get; set; }
    public int DisputeId { get; set; }
    public int? ActorId { get; set; }
    public string ActorRole { get; set; } = "";
    public string Type { get; set; } = "";
    public string? Message { get; set; }
    public decimal? Amount { get; set; }
    public string? Tracking { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}
