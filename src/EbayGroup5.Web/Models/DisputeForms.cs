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

    public static string Date(DateTime utc, DisputeTiming timing) =>
        utc.ToString(timing.IsDemo ? "MMM dd, yyyy HH:mm:ss 'UTC'" : "MMM dd, yyyy", CultureInfo.InvariantCulture);

    public static string Status(DisputeSummary request, DisputeTiming timing)
    {
        string Due() => request.DueAtUtc is { } due ? Date(due, timing) : "the deadline";
        return request.Status switch
        {
            DisputeStatus.Open => $"Waiting for the seller to respond by {Due()}",
            DisputeStatus.ReturnAccepted => $"Return accepted — ship it by {Due()}",
            DisputeStatus.ReturnShipped => $"Return shipped — waiting for the seller's refund by {Due()}",
            DisputeStatus.Escalated => "eBay is reviewing this request",
            _ => request.Outcome switch
            {
                DisputeOutcome.Refunded => "Closed — refunded",
                DisputeOutcome.PartialRefund => "Closed — partial refund",
                DisputeOutcome.BuyerClosed => "You closed this request",
                DisputeOutcome.ItemReceived => "Closed — item received",
                DisputeOutcome.SellerDeclined => "Closed — seller declined the return",
                DisputeOutcome.ReturnNotShipped => "Closed — return not shipped in time",
                DisputeOutcome.DecidedForBuyer => "Closed — decided in your favor",
                DisputeOutcome.DecidedForSeller => "Closed — decided in the seller's favor",
                _ => "Closed"
            }
        };
    }

    public static string? DemoNotice(DisputeTiming timing) => timing.DemoBusinessDay is { } day
        ? $"Demo timing: 1 business day = {day.TotalSeconds.ToString("0", CultureInfo.InvariantCulture)} seconds." : null;
}
