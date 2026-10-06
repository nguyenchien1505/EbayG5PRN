using EbayGroup5.Application.Disputes;

namespace EbayGroup5.Web.Models;

// E05 (DISPUTE_ADMIN.md): only these fields are bound; the admin's identity and the request state come from the server.
public sealed class AdminDisputeForm
{
    public int DisputeId { get; set; }
    [Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] public DisputeAction Action { get; set; }
    public bool? ForBuyer { get; set; }
    public string? Message { get; set; }

    /// <summary>Admin pages post only these two commands; Decide/Message rules stay in DisputeRules (E01).</summary>
    public static bool Postable(DisputeAction action) => action is DisputeAction.Decide or DisputeAction.Message;
}

public sealed record AdminDisputeQueuePage(DisputeQueue Queue, DisputeTiming Timing, DateTime NowUtc);
public sealed record AdminDisputePage(DisputeDetail Detail, AdminDisputeForm? Form, DisputeTiming Timing, DateTime NowUtc);
