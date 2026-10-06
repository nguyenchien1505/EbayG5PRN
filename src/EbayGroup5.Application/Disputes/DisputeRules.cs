namespace EbayGroup5.Application.Disputes;

/// <summary>State of one request as the rules need it (DISPUTES.md, Chuyển trạng thái).</summary>
public sealed record DisputeSnapshot(DisputeKind Kind, DisputeReason Reason, DisputeStatus Status, DateTime CreatedAtUtc, DateTime? DueAtUtc,
    bool SellerResponded, decimal LineTotal, decimal? OfferAmount);

/// <summary>New persisted state plus the single timeline event a command produces.</summary>
public sealed record DisputeChange(DisputeStatus Status, DateTime? DueAtUtc, DisputeOutcome? Outcome, decimal? OfferAmount, decimal? RefundAmount,
    string? ReturnTracking, bool Escalates, DisputeEventType Event, string? Message, decimal? Amount, string? Tracking);

public sealed record DisputeDecision(DisputeResultCode Code, DisputeChange? Change = null, string? Error = null);

/// <summary>Pure transition table; the service loads a snapshot, calls Decide and persists the change.</summary>
public static class DisputeRules
{
    public static bool IsChangeOfMind(DisputeReason reason) =>
        reason is DisputeReason.OrderedWrongItem or DisputeReason.ChangedMind or DisputeReason.NoLongerNeeded or DisputeReason.BetterPrice or DisputeReason.DoesntFit;

    public static bool ReasonMatches(DisputeKind kind, DisputeReason reason) =>
        Enum.IsDefined(reason) && (kind == DisputeKind.ItemNotReceived) == (reason == DisputeReason.ItemNotReceived);

    /// <summary>Roles that may ever issue a command; a role outside this set is Forbidden rather than Conflict.</summary>
    public static bool RoleMayUse(DisputeActor actor, DisputeAction action) => action switch
    {
        DisputeAction.Message => actor is DisputeActor.Buyer or DisputeActor.Seller or DisputeActor.Admin,
        DisputeAction.AddTracking or DisputeAction.Refund or DisputeAction.AcceptReturn or DisputeAction.OfferPartial or DisputeAction.Decline => actor == DisputeActor.Seller,
        DisputeAction.AcceptOffer or DisputeAction.DeclineOffer or DisputeAction.MarkReturnShipped or DisputeAction.Close or DisputeAction.ItemReceived => actor == DisputeActor.Buyer,
        DisputeAction.Escalate => actor is DisputeActor.Buyer or DisputeActor.System,
        DisputeAction.ExpireReturn => actor == DisputeActor.System,
        DisputeAction.Decide => actor == DisputeActor.Admin,
        _ => false
    };

    public static bool StateAllows(DisputeSnapshot s, DisputeActor actor, DisputeAction action, DateTime nowUtc, DisputeTiming timing)
    {
        if (s.Status == DisputeStatus.Closed || !RoleMayUse(actor, action)) return false;
        var inr = s.Kind == DisputeKind.ItemNotReceived;
        var due = s.DueAtUtc is { } d && nowUtc >= d;
        return action switch
        {
            DisputeAction.Message => actor != DisputeActor.Admin || s.Status == DisputeStatus.Escalated,
            DisputeAction.AddTracking => inr && s.Status == DisputeStatus.Open,
            DisputeAction.Refund => s.Status is DisputeStatus.Open or DisputeStatus.Escalated || (!inr && s.Status == DisputeStatus.ReturnShipped),
            DisputeAction.AcceptReturn => !inr && s.Status == DisputeStatus.Open,
            DisputeAction.OfferPartial => !inr && s.Status == DisputeStatus.Open && s.OfferAmount == null,
            DisputeAction.AcceptOffer or DisputeAction.DeclineOffer => !inr && s.Status == DisputeStatus.Open && s.OfferAmount != null,
            DisputeAction.Decline => !inr && s.Status == DisputeStatus.Open && IsChangeOfMind(s.Reason),
            DisputeAction.MarkReturnShipped => s.Status == DisputeStatus.ReturnAccepted,
            DisputeAction.Escalate when actor == DisputeActor.System => s.Status == DisputeStatus.Open && due && !s.SellerResponded,
            DisputeAction.Escalate => s.Status is DisputeStatus.Open or DisputeStatus.ReturnShipped && due &&
                (!inr || nowUtc <= timing.AddBusinessDays(s.CreatedAtUtc, DisputeTiming.ItemNotReceivedEscalationBusinessDays)),
            DisputeAction.Close => true,
            DisputeAction.ExpireReturn => s.Status == DisputeStatus.ReturnAccepted && due,
            DisputeAction.ItemReceived => inr,
            DisputeAction.Decide => s.Status == DisputeStatus.Escalated,
            _ => false
        };
    }

    public static IReadOnlyList<DisputeAction> AvailableActions(DisputeSnapshot s, DisputeActor actor, DateTime nowUtc, DisputeTiming timing) =>
        Enum.GetValues<DisputeAction>().Where(a => a != DisputeAction.ItemReceived && StateAllows(s, actor, a, nowUtc, timing)).ToList();

    public static DisputeDecision Decide(DisputeSnapshot s, DisputeActor actor, DisputeCommand c, DateTime nowUtc, DisputeTiming timing)
    {
        if (!Enum.IsDefined(c.Action) || !Enum.IsDefined(actor)) return new(DisputeResultCode.Invalid, Error: "Unsupported action.");
        if (!RoleMayUse(actor, c.Action)) return new(DisputeResultCode.Forbidden);
        var needsMessage = c.Action is DisputeAction.Message or DisputeAction.Decide;
        if (!TryText(c.Message, DisputeLimits.MaxText, needsMessage, out var message))
            return new(DisputeResultCode.Invalid, Error: needsMessage && string.IsNullOrWhiteSpace(c.Message) ? "Enter a message." : $"Messages must be at most {DisputeLimits.MaxText} characters without control characters.");
        var needsTracking = c.Action is DisputeAction.AddTracking or DisputeAction.MarkReturnShipped;
        if (!TryText(c.Tracking, DisputeLimits.MaxTracking, needsTracking, out var tracking, multiline: false) || (!needsTracking && tracking != null))
            return new(DisputeResultCode.Invalid, Error: $"Enter a tracking number of at most {DisputeLimits.MaxTracking} characters.");
        if (c.Action == DisputeAction.OfferPartial)
        {
            if (c.Amount is not decimal amount || amount <= 0 || amount >= s.LineTotal || decimal.Round(amount, 2) != amount)
                return new(DisputeResultCode.Invalid, Error: $"Offer an amount greater than 0 and less than US ${s.LineTotal:0.00}.");
        }
        else if (c.Amount != null) return new(DisputeResultCode.Invalid, Error: "An amount is only used for partial refund offers.");
        if (c.Action == DisputeAction.Decide ? c.ForBuyer == null : c.ForBuyer != null) return new(DisputeResultCode.Invalid, Error: "Choose who the decision favors.");
        if (!StateAllows(s, actor, c.Action, nowUtc, timing)) return new(DisputeResultCode.Conflict, Error: "This action isn't available for this request right now.");

        DisputeChange Same(DisputeEventType type, decimal? offer = null) =>
            new(s.Status, s.DueAtUtc, null, offer ?? s.OfferAmount, null, null, false, type, message, c.Amount, tracking);
        DisputeChange Close(DisputeOutcome outcome, DisputeEventType type, decimal? refund = null) =>
            new(DisputeStatus.Closed, null, outcome, null, refund, null, false, type, message, refund, tracking);
        DisputeChange To(DisputeStatus status, DateTime due, DisputeEventType type, string? returnTracking = null, bool escalates = false) =>
            new(status, due, null, null, null, returnTracking, escalates, type, message, null, tracking);

        var change = c.Action switch
        {
            DisputeAction.Message => Same(DisputeEventType.Message),
            DisputeAction.AddTracking => Same(DisputeEventType.TrackingAdded),
            DisputeAction.Refund => Close(DisputeOutcome.Refunded, DisputeEventType.Refunded, s.LineTotal),
            DisputeAction.AcceptReturn => To(DisputeStatus.ReturnAccepted, timing.AddBusinessDays(nowUtc, DisputeTiming.ReturnShipBusinessDays), DisputeEventType.ReturnAccepted),
            DisputeAction.OfferPartial => Same(DisputeEventType.OfferMade, c.Amount),
            DisputeAction.AcceptOffer => Close(DisputeOutcome.PartialRefund, DisputeEventType.OfferAccepted, s.OfferAmount),
            DisputeAction.DeclineOffer => Same(DisputeEventType.OfferDeclined) with { OfferAmount = null, Amount = s.OfferAmount },
            DisputeAction.Decline => Close(DisputeOutcome.SellerDeclined, DisputeEventType.Declined),
            DisputeAction.MarkReturnShipped => To(DisputeStatus.ReturnShipped, timing.AddBusinessDays(nowUtc, DisputeTiming.RefundAfterReturnBusinessDays), DisputeEventType.ReturnShipped, tracking),
            DisputeAction.Escalate => To(DisputeStatus.Escalated, nowUtc + DisputeTiming.EscalationReview, DisputeEventType.Escalated, escalates: true) with { OfferAmount = s.OfferAmount },
            DisputeAction.Close => Close(DisputeOutcome.BuyerClosed, DisputeEventType.Closed),
            DisputeAction.ExpireReturn => Close(DisputeOutcome.ReturnNotShipped, DisputeEventType.ReturnExpired),
            DisputeAction.ItemReceived => Close(DisputeOutcome.ItemReceived, DisputeEventType.ItemReceived),
            DisputeAction.Decide => c.ForBuyer == true ? Close(DisputeOutcome.DecidedForBuyer, DisputeEventType.Decided, s.LineTotal) : Close(DisputeOutcome.DecidedForSeller, DisputeEventType.Decided),
            _ => throw new InvalidOperationException()
        };
        return new(DisputeResultCode.Success, change);
    }

    /// <summary>Trims; empty becomes null. Rejects control characters (tab/newlines allowed when multiline) and over-long text.</summary>
    public static bool TryText(string? input, int max, bool required, out string? value, bool multiline = true)
    {
        value = string.IsNullOrWhiteSpace(input) ? null : input.Trim();
        if (value == null) return !required;
        if (value.Length > max) return false;
        return !value.Any(ch => char.IsControl(ch) && !(multiline && ch is '\t' or '\r' or '\n'));
    }
}
