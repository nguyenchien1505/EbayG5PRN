using System.ComponentModel.DataAnnotations;
using System.Globalization;
using EbayGroup5.Application.Disputes;

namespace EbayGroup5.Web.Models;

// Only these fields are bound; buyer, seller, order and deadlines always come from the server.
public sealed class DisputeForm
{
    [Range(1, int.MaxValue)] public int OrderItemId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] public DisputeKind Kind { get; set; }
    public DisputeReason? Reason { get; set; }
    public DisputePreference? Preference { get; set; }
    public string? Details { get; set; }
}

public sealed record DisputeFormPage(DisputeEligibility Line, DisputeKind Kind, DisputeForm Form, bool DraftRestored, DisputeTiming Timing);
public sealed record DisputeUnavailablePage(DisputeEligibility Line, DisputeKind Kind, string? Reason, DisputeTiming Timing);

/// <summary>English UI copy for requests (E1m reason labels, DISPUTE_FORMS.md status labels).</summary>
public static class DisputeText
{
    public static readonly IReadOnlyList<DisputeReason> ReturnReasons =
    [
        DisputeReason.OrderedWrongItem, DisputeReason.ChangedMind, DisputeReason.NoLongerNeeded, DisputeReason.BetterPrice, DisputeReason.DoesntFit,
        DisputeReason.Damaged, DisputeReason.Defective, DisputeReason.MissingParts, DisputeReason.NotAsDescribed, DisputeReason.WrongItem
    ];

    public static string Reason(DisputeReason reason) => reason switch
    {
        DisputeReason.ItemNotReceived => "Item not received",
        DisputeReason.OrderedWrongItem => "Ordered the wrong item",
        DisputeReason.ChangedMind => "Changed my mind",
        DisputeReason.NoLongerNeeded => "No longer needed",
        DisputeReason.BetterPrice => "Found a better price",
        DisputeReason.DoesntFit => "Doesn't fit",
        DisputeReason.Damaged => "Item damaged",
        DisputeReason.Defective => "Item defective",
        DisputeReason.MissingParts => "Missing parts or pieces",
        DisputeReason.NotAsDescribed => "Item didn't match description",
        DisputeReason.WrongItem => "Received the wrong item",
        _ => reason.ToString()
    };

    public static string Kind(DisputeKind kind) => kind == DisputeKind.ItemNotReceived ? "Item not received request" : "Return request";

    public static string Date(DateTime utc, DisputeTiming timing) => DisputeNotices.Date(utc, timing);

    public static string Status(DisputeSummary request, DisputeTiming timing, DisputeActor viewer = DisputeActor.Buyer) =>
        Status(request.Status, request.Outcome, request.DueAtUtc, null, timing, viewer);

    /// <summary>Buyer labels from DISPUTE_FORMS.md; seller labels from DISPUTE_DETAILS.md (E03); admin labels from DISPUTE_ADMIN.md (E05).</summary>
    public static string Status(DisputeStatus status, DisputeOutcome? outcome, DateTime? dueAtUtc, decimal? offerAmount, DisputeTiming timing, DisputeActor viewer)
    {
        string Due() => dueAtUtc is { } due ? Date(due, timing) : "the deadline";
        var seller = viewer == DisputeActor.Seller;
        if (viewer is DisputeActor.Admin or DisputeActor.System)
            return (status, outcome) switch
            {
                (DisputeStatus.Escalated, _) => "Waiting for an eBay decision",
                (DisputeStatus.Closed, DisputeOutcome.DecidedForBuyer) => "Closed — decided for the buyer",
                (DisputeStatus.Closed, DisputeOutcome.DecidedForSeller) => "Closed — decided for the seller",
                (DisputeStatus.Closed, DisputeOutcome.BuyerClosed) => "Buyer closed this request",
                (DisputeStatus.Closed, DisputeOutcome.SellerDeclined) => "Closed — seller declined the return",
                (DisputeStatus.Closed, DisputeOutcome.ItemReceived) => "Closed — buyer confirmed the item arrived",
                (DisputeStatus.Open, _) => $"Waiting for the seller to respond by {Due()}",
                (DisputeStatus.ReturnAccepted, _) => $"Return accepted — buyer ships by {Due()}",
                (DisputeStatus.ReturnShipped, _) => $"Item shipped back — seller refunds by {Due()}",
                _ => Status(status, outcome, dueAtUtc, offerAmount, timing, DisputeActor.Buyer)
            };
        return status switch
        {
            DisputeStatus.Open when offerAmount != null => seller ? "Partial refund offered — waiting for the buyer" : "The seller offered a partial refund",
            DisputeStatus.Open => seller ? $"Respond by {Due()}" : $"Waiting for the seller to respond by {Due()}",
            DisputeStatus.ReturnAccepted => seller ? $"Return accepted — buyer ships by {Due()}" : $"Return accepted — ship it by {Due()}",
            DisputeStatus.ReturnShipped => seller ? $"Item shipped back — refund by {Due()}" : $"Return shipped — waiting for the seller's refund by {Due()}",
            DisputeStatus.Escalated => "eBay is reviewing this request",
            _ => outcome switch
            {
                DisputeOutcome.Refunded => "Closed — refunded",
                DisputeOutcome.PartialRefund => "Closed — partial refund",
                DisputeOutcome.BuyerClosed => seller ? "Buyer closed this request" : "You closed this request",
                DisputeOutcome.ItemReceived => "Closed — item received",
                DisputeOutcome.SellerDeclined => seller ? "Closed — you declined the return" : "Closed — seller declined the return",
                DisputeOutcome.ReturnNotShipped => "Closed — return not shipped in time",
                DisputeOutcome.DecidedForBuyer => seller ? "Closed — decided in the buyer's favor" : "Closed — decided in your favor",
                DisputeOutcome.DecidedForSeller => seller ? "Closed — decided in your favor" : "Closed — decided in the seller's favor",
                _ => "Closed"
            }
        };
    }

    public static string? DemoNotice(DisputeTiming timing) => timing.DemoBusinessDay is { } day
        ? $"Demo timing: 1 business day = {day.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds." : null;
}
