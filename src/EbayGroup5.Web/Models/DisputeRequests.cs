using System.Globalization;
using EbayGroup5.Application.Disputes;

namespace EbayGroup5.Web.Models;

// E03 (DISPUTE_DETAILS.md): only these fields are bound; the actor's role and the request state always come from the server.
public sealed class DisputeActForm
{
    public int DisputeId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] public DisputeAction Action { get; set; }
    public string? Message { get; set; }
    /// <summary>Parsed with the invariant culture (US $ amounts such as 12.50) whatever the server culture is.</summary>
    public string? Amount { get; set; }
    public string? Tracking { get; set; }
    /// <summary>Reason code for Escalate/Close (DisputeRequestText.EscalateReasons/CloseReasons).</summary>
    public string? Reason { get; set; }
}

public sealed record DisputeDetailsPage(DisputeDetail Detail, DisputeAction? Reply, DisputeActForm? Form, DisputeTiming Timing);
public sealed record DisputeListPage(DisputeList List, DisputeTiming Timing);
public sealed record DisputeStep(string Title, string Text);

/// <summary>English copy for the request details page, list and timeline (D2/H1 labels; clone wording where eBay shows none).</summary>
public static class DisputeRequestText
{
    /// <summary>Commands a participant may post; system and admin commands never come from these pages.</summary>
    public static readonly IReadOnlyList<DisputeAction> SellerReplies =
        [DisputeAction.AcceptReturn, DisputeAction.Refund, DisputeAction.OfferPartial, DisputeAction.AddTracking, DisputeAction.Decline, DisputeAction.Message];
    public static readonly IReadOnlyList<DisputeAction> BuyerReplies =
        [DisputeAction.AcceptOffer, DisputeAction.DeclineOffer, DisputeAction.MarkReturnShipped, DisputeAction.Message];
    public static bool Postable(DisputeAction action) => SellerReplies.Contains(action) || BuyerReplies.Contains(action) || action is DisputeAction.Escalate or DisputeAction.Close;

    public static readonly IReadOnlyList<(string Code, string Label)> EscalateReasons =
    [
        ("NoResponse", "The seller hasn't responded"), ("NoRefund", "I haven't received a refund"),
        ("Disagree", "I don't agree with the seller's response"), ("Other", "Something else")
    ];
    public static readonly IReadOnlyList<(string Code, string Label)> CloseReasons =
        [("Resolved", "My problem was resolved"), ("ChangedMind", "I changed my mind"), ("Other", "Something else")];
    public static IReadOnlyList<(string Code, string Label)>? ReasonsFor(DisputeAction action) =>
        action == DisputeAction.Escalate ? EscalateReasons : action == DisputeAction.Close ? CloseReasons : null;
    /// <summary>Optional note length when a reason label goes first, so reason + note stay within the E01 message limit.</summary>
    public const int MaxReasonNote = 1900;

    public static string Money(decimal amount) => "US $" + amount.ToString("N2", CultureInfo.InvariantCulture);

    public static IReadOnlyList<DisputeAction> Replies(DisputeDetail d) =>
        (d.Viewer == DisputeActor.Seller ? SellerReplies : BuyerReplies).Where(d.Actions.Contains).ToList();

    public static string Heading(DisputeDetail d) => (d.Viewer, d.Kind) switch
    {
        (DisputeActor.Seller, DisputeKind.Return) => "You have a return request",
        (DisputeActor.Seller, _) => "You have an item not received request",
        (_, DisputeKind.Return) => "Your return request",
        _ => "Your item not received request"
    };

    /// <summary>Top line of the details page: the D2 summary while the seller must respond, otherwise the status label.</summary>
    public static string Headline(DisputeDetail d, DisputeTiming timing) => d is { Viewer: DisputeActor.Seller, Status: DisputeStatus.Open, OfferAmount: null }
        ? d.Kind == DisputeKind.ItemNotReceived ? "Your buyer hasn't received their item."
            : DisputeRules.IsChangeOfMind(d.Reason) ? "Your buyer would like to return this item."
            : "There's something wrong with the item your buyer's received, so they'd like to return it."
        : DisputeText.Status(d.Status, d.Outcome, d.DueAtUtc, d.OfferAmount, timing, d.Viewer);

    /// <summary>What the viewer should know or do next (DISPUTE_DETAILS.md, Trang chi tiết).</summary>
    public static string Guidance(DisputeDetail d, DisputeTiming timing)
    {
        var seller = d.Viewer == DisputeActor.Seller;
        var due = d.DueAtUtc is { } at ? DisputeText.Date(at, timing) : "the deadline";
        return d.Status switch
        {
            DisputeStatus.Open when seller && d.OfferAmount is { } offer => $"You offered a partial refund of {Money(offer)}. The buyer can accept or decline it.",
            DisputeStatus.Open when seller => $"Please respond by {due} or we may step in to help.",
            DisputeStatus.Open when d.OfferAmount is { } offer => $"The seller offered a partial refund of {Money(offer)}. You keep the item if you accept.",
            // The status line above already shows the deadline; say what the seller can do instead.
            DisputeStatus.Open when d.Kind == DisputeKind.ItemNotReceived => "We let the seller know. They can send tracking details, a refund or a message.",
            DisputeStatus.Open => "We let the seller know. They can accept the return, refund you or offer a partial refund.",
            DisputeStatus.ReturnAccepted when seller => $"The buyer has until {due} to ship the item back. Refund the buyer after you get it.",
            DisputeStatus.ReturnAccepted => $"The seller accepted your return. Ship the item back and add tracking by {due}.",
            DisputeStatus.ReturnShipped when seller => $"The item is on its way back. Refund the buyer by {due}.",
            DisputeStatus.ReturnShipped => $"The seller has until {due} to refund you after getting the item back.",
            // The status line already says eBay is reviewing; give the timing and what both sides can still do.
            DisputeStatus.Escalated => $"We usually respond within 48 hours{(d.DueAtUtc is { } review ? " (by " + DisputeText.Date(review, timing) + ")" : "")}. You can still message the {(seller ? "buyer" : "seller")} here.",
            _ => "This request is closed."
        };
    }

    public static string ReplyLabel(DisputeAction action, DisputeDetail d) => action switch
    {
        DisputeAction.AcceptReturn => "Accept the return",
        DisputeAction.Refund when d.Status == DisputeStatus.ReturnShipped => "Refund the buyer",
        DisputeAction.Refund => "Give a full refund",
        DisputeAction.OfferPartial => "Offer a partial refund",
        DisputeAction.AddTracking => "Provide tracking information",
        DisputeAction.Decline => "Decline the return",
        DisputeAction.Message => d.Viewer == DisputeActor.Seller ? "Send the buyer a message" : "Send the seller a message",
        DisputeAction.AcceptOffer => "Accept the partial refund",
        DisputeAction.DeclineOffer => "Decline the offer",
        DisputeAction.MarkReturnShipped => "I shipped the item",
        DisputeAction.Escalate => "Ask eBay to step in and help",
        DisputeAction.Close => "Close your request",
        _ => action.ToString()
    };

    public static string ReplyHelp(DisputeAction action, DisputeDetail d, DisputeTiming timing) => action switch
    {
        DisputeAction.AcceptReturn => "The buyer sends the item back. Wait to get the item back before you refund the buyer.",
        DisputeAction.Refund when d.Status == DisputeStatus.ReturnShipped => $"Refund {Money(d.LineTotal)} now that the item is on its way back.",
        DisputeAction.Refund when d.Kind == DisputeKind.Return => "You fully refund the buyer to close this request. The buyer keeps the item.",
        DisputeAction.Refund => "You fully refund the buyer to close this request.",
        DisputeAction.OfferPartial => "Offer an amount. The buyer keeps the item.",
        DisputeAction.AddTracking => "Let the buyer know the item is on its way.",
        DisputeAction.Decline => $"The buyer's reason ({DisputeText.Reason(d.Reason)}) lets you decline. This closes the request.",
        DisputeAction.Message => d.Viewer == DisputeActor.Seller ? "You can reply to the buyer." : "You can reply to the seller.",
        DisputeAction.AcceptOffer => d.OfferAmount is { } offer ? $"Get {Money(offer)} back and keep the item. This closes the request." : "Keep the item and close the request.",
        DisputeAction.DeclineOffer => "The seller can respond another way.",
        DisputeAction.MarkReturnShipped => $"Add the return tracking number{(d.DueAtUtc is { } due ? " by " + DisputeText.Date(due, timing) : "")}.",
        DisputeAction.Escalate => "Tell us what's wrong. We review and usually respond within 48 hours.",
        DisputeAction.Close => "Tell us why you're closing this request. Closed requests can't be reopened.",
        _ => ""
    };

    public static string Confirm(DisputeAction action, DisputeDetail d) => action switch
    {
        DisputeAction.Message => "Send message",
        DisputeAction.AddTracking => "Add tracking",
        DisputeAction.Refund => "Refund " + Money(d.LineTotal),
        DisputeAction.AcceptReturn => "Accept return",
        DisputeAction.OfferPartial => "Send offer",
        DisputeAction.AcceptOffer => "Accept offer",
        DisputeAction.DeclineOffer => "Decline offer",
        DisputeAction.Decline => "Decline return",
        DisputeAction.MarkReturnShipped => "Confirm shipment",
        DisputeAction.Escalate => "Confirm",
        DisputeAction.Close => "Close request",
        _ => "Continue"
    };

    public static string Notice(DisputeAction action) => action switch
    {
        DisputeAction.Message => "Your message was sent.",
        DisputeAction.AddTracking => "Tracking details were added.",
        DisputeAction.Refund => "You refunded the buyer (simulated). This request is closed.",
        DisputeAction.AcceptReturn => "You accepted the return.",
        DisputeAction.OfferPartial => "Your partial refund offer was sent.",
        DisputeAction.AcceptOffer => "You accepted the partial refund (simulated). This request is closed.",
        DisputeAction.DeclineOffer => "You declined the offer.",
        DisputeAction.Decline => "You declined the return. This request is closed.",
        DisputeAction.MarkReturnShipped => "Your return shipment was recorded.",
        DisputeAction.Escalate => "You asked eBay to step in. We usually respond within 48 hours.",
        DisputeAction.Close => "Your request is closed.",
        _ => "Done."
    };

    /// <summary>Reason label first, then the optional note after a blank line (the event keeps both in Message).</summary>
    public static string Compose(string label, string? note) => string.IsNullOrWhiteSpace(note) ? label : label + "\n\n" + note.Trim();

    public static IReadOnlyList<DisputeStep> Steps(DisputeKind kind) => kind == DisputeKind.ItemNotReceived
        ?
        [
            new("Estimated delivery date passes", "The buyer can report the item after this date, for up to 30 days."),
            new("Report it", "The seller is told right away."),
            new("Seller has 3 business days", "They can send tracking details, a refund or a message."),
            new("Ask eBay to step in", "If it isn't resolved, we review and usually respond within 48 hours.")
        ]
        :
        [
            new("Start a return", "The buyer tells the seller what's wrong."),
            new("Seller has 3 business days", "They can accept the return, refund or offer a partial refund."),
            new("Send the item back", "The buyer ships it with tracking."),
            new("Get your refund", "The seller refunds after getting the item back. eBay can step in if it isn't resolved.")
        ];

    /// <summary>Index of the current step, or null when the request is closed.</summary>
    public static int? CurrentStep(DisputeDetail d) => d.Status switch
    {
        DisputeStatus.Open => d.Kind == DisputeKind.ItemNotReceived ? 2 : 1,
        DisputeStatus.ReturnAccepted => 2,
        DisputeStatus.ReturnShipped => 3,
        DisputeStatus.Escalated => 3,
        _ => null
    };

    public static string Actor(DisputeActor role, DisputeActor viewer) => role == viewer ? "You" : role switch
    {
        DisputeActor.Buyer => "The buyer",
        DisputeActor.Seller => "The seller",
        _ => "eBay"
    };

    /// <summary>One sentence per event; amounts are simulated refunds (no money moves).</summary>
    public static string Event(DisputeEventView e, DisputeDetail d) => $"{Actor(e.ActorRole, d.Viewer)} {Verb(e, d)}.";

    /// <summary>E05 audit trail: role and username of whoever acted (System is the automation job).</summary>
    public static string AuditActor(DisputeEventView e) => e.ActorRole switch
    {
        DisputeActor.System => "eBay (automatic)",
        DisputeActor.Admin => "Admin " + (e.ActorName ?? "(unknown)"),
        _ => e.ActorRole + " " + (e.ActorName ?? "(unknown)")
    };

    public static string Verb(DisputeEventView e, DisputeDetail d)
    {
        var amount = e.Amount is { } a ? Money(a) : null;
        string Favor(string party) => (d.Viewer == DisputeActor.Buyer && party == "buyer") || (d.Viewer == DisputeActor.Seller && party == "seller") ? "your" : $"the {party}'s";
        var verb = e.Type switch
        {
            DisputeEventType.Opened => d.Kind == DisputeKind.ItemNotReceived ? "reported that the item didn't arrive" : "started a return",
            DisputeEventType.Message => "sent a message",
            DisputeEventType.TrackingAdded => "added tracking details",
            DisputeEventType.Refunded => $"refunded {amount} (simulated)",
            DisputeEventType.ReturnAccepted => "accepted the return",
            DisputeEventType.OfferMade => $"offered a partial refund of {amount}",
            DisputeEventType.OfferAccepted => $"accepted the partial refund of {amount} (simulated)",
            DisputeEventType.OfferDeclined => amount == null ? "declined the partial refund offer" : $"declined the partial refund offer of {amount}",
            DisputeEventType.Declined => "declined the return",
            DisputeEventType.ReturnShipped => "shipped the item back",
            DisputeEventType.Escalated => e.ActorRole == DisputeActor.System ? "stepped in because the seller didn't respond in time" : "asked eBay to step in",
            DisputeEventType.Closed => "closed the request",
            DisputeEventType.ReturnExpired => "closed the request because the item wasn't shipped back in time",
            DisputeEventType.ItemReceived => "confirmed the item arrived",
            DisputeEventType.Decided => amount != null ? $"decided this request in {Favor("buyer")} favor and refunded {amount} (simulated)" : $"decided this request in {Favor("seller")} favor",
            DisputeEventType.AutoNotice => "sent an automatic update",
            _ => "updated the request"
        };
        return verb;
    }

    public static string KindTab(DisputeKind? kind) => kind switch
    {
        DisputeKind.Return => "Returns",
        DisputeKind.ItemNotReceived => "Requests and disputes",
        _ => "All"
    };
}
