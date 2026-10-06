namespace EbayGroup5.Application.Disputes;

// Contract: docs/architecture/DISPUTES.md. Enum names are persisted as strings; do not rename members.
public enum DisputeKind { ItemNotReceived, Return }
public enum DisputeReason
{
    ItemNotReceived,
    // Change of mind: seller may decline.
    OrderedWrongItem, ChangedMind, NoLongerNeeded, BetterPrice, DoesntFit,
    // Item problem (Money Back Guarantee): seller may not decline.
    Damaged, Defective, MissingParts, NotAsDescribed, WrongItem
}
/// <summary>INR choice: "I'd like a refund" / "I'd still like the item".</summary>
public enum DisputePreference { Refund, Item }
public enum DisputeStatus { Open, ReturnAccepted, ReturnShipped, Escalated, Closed }
public enum DisputeOutcome { Refunded, PartialRefund, BuyerClosed, ItemReceived, SellerDeclined, ReturnNotShipped, DecidedForBuyer, DecidedForSeller }
public enum DisputeActor { Buyer, Seller, Admin, System }
public enum DisputeAction { Message, AddTracking, Refund, AcceptReturn, OfferPartial, AcceptOffer, DeclineOffer, Decline, MarkReturnShipped, Escalate, Close, ExpireReturn, ItemReceived, Decide }
/// <summary>AutoNotice is reserved for E04 automatic responses.</summary>
public enum DisputeEventType { Opened, Message, TrackingAdded, Refunded, ReturnAccepted, OfferMade, OfferAccepted, OfferDeclined, Declined, ReturnShipped, Escalated, Closed, ReturnExpired, ItemReceived, Decided, AutoNotice }
public enum DisputeResultCode { Success, Invalid, NotFound, Forbidden, Conflict }

public static class DisputeLimits
{
    public const int MaxText = 2000;
    public const int MaxTracking = 100;
    public const int MaxEvents = 200;
    public const int MaxBatchLines = 50;
    public const int ListPageSize = 10;
    public const int TimelinePageSize = 20;
    public const int QueuePageSize = 20;
}

public sealed record OpenDisputeRequest(int OrderItemId, DisputeKind Kind, DisputeReason Reason, DisputePreference? Preference, string? Details);
public sealed record DisputeCommand(DisputeAction Action, string? Message = null, decimal? Amount = null, string? Tracking = null, bool? ForBuyer = null);
/// <summary>ExistingDisputeId is set on Conflict when the line already has an open request.</summary>
public sealed record DisputeResult(DisputeResultCode Code, int? DisputeId = null, string? Error = null, int? ExistingDisputeId = null);

public sealed record KindEligibility(bool CanOpen, string? Reason, DateTime? OpensAtUtc, DateTime? ClosesAtUtc, int? ExistingDisputeId);
/// <summary>Requests on a line (newest first) for status display; at most one is not Closed.</summary>
public sealed record DisputeSummary(int Id, DisputeKind Kind, DisputeStatus Status, DisputeOutcome? Outcome, DateTime? DueAtUtc);
public sealed record DisputeEligibility(int OrderId, int OrderItemId, DateTime? EstimatedDeliveryUtc, KindEligibility ItemNotReceived, KindEligibility Return)
{
    public string ItemTitle { get; init; } = "Item unavailable";
    public decimal? LineTotal { get; init; }
    public IReadOnlyList<DisputeSummary> Requests { get; init; } = [];
    public KindEligibility For(DisputeKind kind) => kind == DisputeKind.ItemNotReceived ? ItemNotReceived : Return;
}

public sealed record DisputeDraftInput(int OrderItemId, DisputeKind Kind, DisputeReason? Reason, DisputePreference? Preference, string? Details);
public sealed record DisputeDraftView(DisputeReason? Reason, DisputePreference? Preference, string? Details, DateTime UpdatedAtUtc);
public sealed record DraftSaveResult(DisputeResultCode Code, DateTime? SavedAtUtc = null, string? Error = null);

public sealed record DisputeEventView(int Id, DisputeActor ActorRole, DisputeEventType Type, string? Message, decimal? Amount, string? Tracking, DateTime CreatedAtUtc)
{
    /// <summary>Username of the acting user; filled only for Admin viewers (E05 audit trail), never shown to buyers or sellers.</summary>
    public string? ActorName { get; init; }
}
public sealed record DisputeDetail(int Id, int OrderId, int OrderItemId, string ItemTitle, DisputeKind Kind, DisputeReason Reason, DisputePreference? Preference,
    DisputeStatus Status, DisputeOutcome? Outcome, string? Details, DateTime CreatedAtUtc, DateTime? DueAtUtc, DateTime? EscalatedAtUtc, DateTime? ClosedAtUtc,
    decimal LineTotal, decimal? OfferAmount, decimal? RefundAmount, string? ReturnTracking, DisputeActor Viewer, IReadOnlyList<DisputeAction> Actions,
    IReadOnlyList<DisputeEventView> Events, bool DemoTiming)
{
    /// <summary>Username of the other party: the seller for the buyer, the buyer for the seller (E03).</summary>
    public string? Counterpart { get; init; }
    public string? BuyerName { get; init; }
    public string? SellerName { get; init; }
    /// <summary>Timeline page (newest first, TimelinePageSize events) and the request's total event count.</summary>
    public int EventPage { get; init; } = 1;
    public int EventTotal { get; init; }
    public int EventPages => Math.Max(1, (int)Math.Ceiling(EventTotal / (double)DisputeLimits.TimelinePageSize));
}

public enum DisputeListState { Open, Closed }
/// <summary>E05 admin queue: Waiting = escalated and undecided; Closed = escalated at some point and now closed.</summary>
public enum DisputeQueueState { Waiting, Closed }
public sealed record DisputeQueueQuery(DisputeQueueState State = DisputeQueueState.Waiting, int Page = 1);
public sealed record DisputeQueueItem(int Id, int OrderId, string ItemTitle, DisputeKind Kind, DisputeReason Reason, DisputeStatus Status, DisputeOutcome? Outcome,
    DateTime? EscalatedAtUtc, DateTime? DueAtUtc, DateTime? ClosedAtUtc, decimal LineTotal, decimal? RefundAmount, string? BuyerName, string? SellerName);
public sealed record DisputeQueue(DisputeQueueQuery Query, IReadOnlyList<DisputeQueueItem> Items, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)DisputeLimits.QueuePageSize));
}
public sealed record DisputeListQuery(DisputeKind? Kind = null, DisputeListState? State = null, int Page = 1);
public sealed record DisputeListItem(int Id, int OrderId, int OrderItemId, string ItemTitle, DisputeKind Kind, DisputeReason Reason, DisputeStatus Status,
    DisputeOutcome? Outcome, DateTime CreatedAtUtc, DateTime? DueAtUtc, decimal LineTotal, decimal? OfferAmount, string? Counterpart);
public sealed record DisputeList(DisputeActor Viewer, DisputeListQuery Query, IReadOnlyList<DisputeListItem> Items, int Total)
{
    public int TotalPages => Math.Max(1, (int)Math.Ceiling(Total / (double)DisputeLimits.ListPageSize));
}

public interface IDisputeService
{
    /// <summary>Per-kind eligibility of one line for the Buyer who owns the order; null when missing, foreign or the actor is not a Buyer.</summary>
    Task<DisputeEligibility?> EligibilityAsync(int buyerId, int orderItemId, CancellationToken cancellationToken = default);
    /// <summary>Same rules for up to MaxBatchLines lines (E02 Purchases page); lines the Buyer doesn't own are omitted.</summary>
    Task<IReadOnlyDictionary<int, DisputeEligibility>> EligibilityAsync(int buyerId, IReadOnlyCollection<int> orderItemIds, CancellationToken cancellationToken = default);
    Task<DisputeDraftView?> FindDraftAsync(int buyerId, int orderItemId, DisputeKind kind, CancellationToken cancellationToken = default);
    /// <summary>Upserts the draft only while the Buyer owns the line and the kind can be opened (Conflict otherwise).</summary>
    Task<DraftSaveResult> SaveDraftAsync(int buyerId, DisputeDraftInput input, CancellationToken cancellationToken = default);
    /// <summary>Opens the request and deletes the matching draft in the same transaction (G5-N03).</summary>
    Task<DisputeResult> OpenAsync(int buyerId, OpenDisputeRequest request, CancellationToken cancellationToken = default);
    /// <summary>Buyer/Seller/Admin commands; the actor's role is derived from the request and the database, never from input.</summary>
    Task<DisputeResult> ActAsync(int actorId, int disputeId, DisputeCommand command, CancellationToken cancellationToken = default);
    /// <summary>System commands for the E04 job (Escalate, ExpireReturn); idempotent: a no-longer-applicable command returns Conflict without writes.</summary>
    Task<DisputeResult> ActAsSystemAsync(int disputeId, DisputeAction action, CancellationToken cancellationToken = default);
    /// <summary>Visible to the request's buyer and seller, and to Admin users; null otherwise.</summary>
    Task<DisputeDetail?> FindAsync(int actorId, int disputeId, CancellationToken cancellationToken = default);
    /// <summary>Same as FindAsync with the timeline page (newest first) for the request details page (E03).</summary>
    Task<DisputeDetail?> FindAsync(int actorId, int disputeId, int timelinePage, CancellationToken cancellationToken = default);
    /// <summary>Requests the Buyer raised or the Seller received (E03, G5-07); null for any other role.</summary>
    Task<DisputeList?> ListAsync(int actorId, DisputeListQuery query, CancellationToken cancellationToken = default);
    /// <summary>E05: requests waiting for or closed after an eBay review; Admin users with a credential only, null otherwise.</summary>
    Task<DisputeQueue?> QueueAsync(int adminId, DisputeQueueQuery query, CancellationToken cancellationToken = default);
    /// <summary>Requests per order line (newest first) for the order's buyer or seller; empty for anyone else.</summary>
    Task<IReadOnlyDictionary<int, IReadOnlyList<DisputeSummary>>> RequestsForOrderAsync(int actorId, int orderId, CancellationToken cancellationToken = default);
}
