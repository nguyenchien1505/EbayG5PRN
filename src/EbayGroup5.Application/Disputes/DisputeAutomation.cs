using System.Globalization;

namespace EbayGroup5.Application.Disputes;

/// <summary>Bound from "Disputes:Automation" (DISPUTE_AUTOMATION.md, Job).</summary>
public sealed class DisputeAutomationOptions
{
    public bool Enabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 60;
    public int BatchSize { get; set; } = 100;

    public DisputeAutomationOptions Validated()
    {
        if (IntervalSeconds is < 1 or > 3600) throw new ArgumentOutOfRangeException(nameof(IntervalSeconds), "Disputes:Automation:IntervalSeconds must be 1–3600.");
        if (BatchSize is < 1 or > 1000) throw new ArgumentOutOfRangeException(nameof(BatchSize), "Disputes:Automation:BatchSize must be 1–1000.");
        return this;
    }
}

public sealed record DisputeAutomationResult(int Escalated, int Expired, int Notices)
{
    public bool Any => Escalated + Expired + Notices > 0;
}

/// <summary>E04: overdue System commands and automatic notices; safe to run repeatedly and on several replicas at once.</summary>
public interface IDisputeAutomation
{
    /// <summary>One pass: overdue Escalate/ExpireReturn, then missing notices for open requests (each step at most BatchSize requests).</summary>
    Task<DisputeAutomationResult> RunOnceAsync(CancellationToken cancellationToken = default);
    /// <summary>Writes the notices the request's current state still lacks; returns how many were written.</summary>
    Task<int> NotifyAsync(int disputeId, CancellationToken cancellationToken = default);
}

public sealed record DisputeNotice(string Key, string Message);

/// <summary>Which automatic notices a request should have in its current state, and their neutral wording (both parties read the timeline).</summary>
public static class DisputeNotices
{
    public const string Received = "Received", ReturnAccepted = "ReturnAccepted", ReturnShipped = "ReturnShipped", Escalated = "Escalated",
        OpenDue = "Due:Open", ReturnShippedDue = "Due:ReturnShipped";

    /// <summary>Same date format as the request pages (time of day shown in demo timing).</summary>
    public static string Date(DateTime utc, DisputeTiming timing) =>
        utc.ToString(timing.IsDemo ? "MMM dd, yyyy HH:mm:ss 'UTC'" : "MMM dd, yyyy", CultureInfo.InvariantCulture);

    /// <summary>Notices for the current state only; past steps are never back-filled and closed requests get none.</summary>
    public static IReadOnlyList<DisputeNotice> Expected(DisputeKind kind, DisputeStatus status, DateTime? dueAtUtc, bool sellerResponded, DateTime nowUtc, DisputeTiming timing)
    {
        if (dueAtUtc is not { } due) return [];
        var date = Date(due, timing);
        var overdue = nowUtc >= due;
        var notices = new List<DisputeNotice>();
        switch (status)
        {
            case DisputeStatus.Open:
                notices.Add(new(Received, kind == DisputeKind.ItemNotReceived
                    ? $"We received this request and let the seller know. The seller has until {date} to send tracking details, a refund or a message. If it isn't resolved by then, the buyer can ask eBay to step in."
                    : $"We received this return request and let the seller know. The seller has until {date} to accept the return, refund or offer a partial refund. If it isn't resolved by then, the buyer can ask eBay to step in."));
                if (overdue && sellerResponded)
                    notices.Add(new(OpenDue, "The seller's time to respond has ended. If the problem isn't resolved, the buyer can ask eBay to step in."));
                break;
            case DisputeStatus.ReturnAccepted:
                notices.Add(new(ReturnAccepted, $"The return was accepted. The buyer should ship the item back and add tracking by {date}, or this request will close."));
                break;
            case DisputeStatus.ReturnShipped:
                notices.Add(new(ReturnShipped, $"The item is on its way back. The seller has until {date} to refund the buyer."));
                if (overdue) notices.Add(new(ReturnShippedDue, "The seller's time to refund has ended. The buyer can ask eBay to step in."));
                break;
            case DisputeStatus.Escalated:
                notices.Add(new(Escalated, $"eBay is reviewing this request and usually responds within 48 hours (by {date}). Both sides can still send messages here."));
                break;
        }
        return notices;
    }
}
