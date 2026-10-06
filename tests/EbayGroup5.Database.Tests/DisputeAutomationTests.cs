using System.Net;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using Microsoft.Data.SqlClient;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>E04: overdue System commands and automatic notices (DISPUTE_AUTOMATION.md, G5-08).</summary>
public sealed class DisputeAutomationTests
{
    private static readonly DisputeTiming Timing = new();

    private sealed class ShiftedClock(TimeSpan offset) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => TimeProvider.System.GetUtcNow() + offset;
    }

    private static async Task<T> With<T>(SetupData s, Func<DisputeService, Task<T>> run)
    {
        await using var db = Open(s.Target);
        return await run(new DisputeService(db, TimeProvider.System, Timing));
    }

    private static async Task<DisputeAutomationResult> Run(SetupData s, TimeSpan offset, int batch = 100)
    {
        await using var db = Open(s.Target);
        var clock = new ShiftedClock(offset);
        var automation = new DisputeAutomationService(db, clock, Timing, new DisputeService(db, clock, Timing),
            Microsoft.Extensions.Options.Options.Create(new DisputeAutomationOptions { BatchSize = batch }));
        return await automation.RunOnceAsync();
    }

    private static async Task<int> Notify(SetupData s, int dispute)
    {
        await using var db = Open(s.Target);
        return await new DisputeAutomationService(db, TimeProvider.System, Timing, new DisputeService(db, TimeProvider.System, Timing),
            Microsoft.Extensions.Options.Options.Create(new DisputeAutomationOptions())).NotifyAsync(dispute);
    }

    private static readonly TimeSpan Later = TimeSpan.FromDays(10); // Past 3 and 5 business days from any weekday.
    private static void EstimatedDaysAgo(SetupData s, int order, int days) =>
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-{days},SYSUTCDATETIME()) WHERE id={order}");
    private static int AddLine(SetupData s, int order)
    {
        SqlFixture.Execute(s.Target, $"INSERT dbo.OrderItem(orderId,productId,quantity,unitPrice) VALUES({order},{s.Product},1,10.00)");
        return int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MAX(id) FROM dbo.OrderItem WHERE orderId={order}"));
    }
    private static async Task<int> DeliveredLine(SetupData s)
    {
        int order;
        await using (var db = Open(s.Target))
            order = (await new CheckoutService(db, Timing).PlaceAsync(s.Buyer, new(s.Product, s.Address, 1, 49.90m, Guid.NewGuid()))).OrderId!.Value;
        await Deliver(s, order);
        return int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MIN(id) FROM dbo.OrderItem WHERE orderId={order}"));
    }
    private static async Task<int> OpenInr(SetupData s, int line) =>
        (await With(s, x => x.OpenAsync(s.Buyer, new(line, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, null)))).DisputeId!.Value;
    private static async Task<int> OpenReturn(SetupData s, int line) =>
        (await With(s, x => x.OpenAsync(s.Buyer, new(line, DisputeKind.Return, DisputeReason.Damaged, null, "Cracked")))).DisputeId!.Value;
    private static async Task Act(SetupData s, int actor, int dispute, DisputeCommand command) =>
        Assert.Equal(DisputeResultCode.Success, (await With(s, x => x.ActAsync(actor, dispute, command))).Code);
    private static string Keys(SetupData s, int dispute) => SqlFixture.Scalar(s.Target,
        $"SELECT ISNULL(STRING_AGG(NoticeKey,',') WITHIN GROUP (ORDER BY Id),'') FROM dbo.DisputeEvent WHERE DisputeId={dispute} AND Type='AutoNotice'");
    private static string State(SetupData s, int dispute) => SqlFixture.Scalar(s.Target,
        $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT) FROM dbo.Dispute WHERE id={dispute}");
    private static string EventCount(SetupData s) => SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.DisputeEvent");

    [Fact]
    public void Expected_notices_cover_only_the_current_state()
    {
        var now = new DateTime(2026, 10, 6, 9, 0, 0, DateTimeKind.Utc);
        var due = now.AddDays(3);
        string Keys(DisputeKind kind, DisputeStatus status, DateTime? at, bool responded = false) =>
            string.Join(",", DisputeNotices.Expected(kind, status, at, responded, now, Timing).Select(x => x.Key));
        Assert.Equal("Received", Keys(DisputeKind.ItemNotReceived, DisputeStatus.Open, due));
        Assert.Equal("Received", Keys(DisputeKind.ItemNotReceived, DisputeStatus.Open, now.AddMinutes(-1))); // Silent seller: the System escalation handles it.
        Assert.Equal("Received,Due:Open", Keys(DisputeKind.Return, DisputeStatus.Open, now, responded: true));
        Assert.Equal("Received", Keys(DisputeKind.Return, DisputeStatus.Open, due, responded: true));
        Assert.Equal("ReturnAccepted", Keys(DisputeKind.Return, DisputeStatus.ReturnAccepted, now.AddMinutes(-1)));
        Assert.Equal("ReturnShipped", Keys(DisputeKind.Return, DisputeStatus.ReturnShipped, due));
        Assert.Equal("ReturnShipped,Due:ReturnShipped", Keys(DisputeKind.Return, DisputeStatus.ReturnShipped, now));
        Assert.Equal("Escalated", Keys(DisputeKind.ItemNotReceived, DisputeStatus.Escalated, now.AddHours(48)));
        Assert.Equal("", Keys(DisputeKind.Return, DisputeStatus.Closed, null));
        Assert.Equal("", Keys(DisputeKind.Return, DisputeStatus.Open, null));

        var inr = DisputeNotices.Expected(DisputeKind.ItemNotReceived, DisputeStatus.Open, due, false, now, Timing)[0].Message;
        var ret = DisputeNotices.Expected(DisputeKind.Return, DisputeStatus.Open, due, false, now, Timing)[0].Message;
        Assert.Contains("until Oct 09, 2026 to send tracking details, a refund or a message", inr);
        Assert.Contains("until Oct 09, 2026 to accept the return, refund or offer a partial refund", ret);
        Assert.Contains("the buyer can ask eBay to step in", inr);
        var demo = new DisputeTiming(new() { DemoBusinessDaySeconds = 40 });
        Assert.Contains("by Oct 08, 2026 09:00:00 UTC", DisputeNotices.Expected(DisputeKind.Return, DisputeStatus.Escalated, now.AddHours(48), false, now, demo)[0].Message);
        Assert.All(new[] { DisputeStatus.Open, DisputeStatus.ReturnAccepted, DisputeStatus.ReturnShipped, DisputeStatus.Escalated },
            st => Assert.All(DisputeNotices.Expected(DisputeKind.Return, st, now, true, now, Timing), n => Assert.InRange(n.Message.Length, 1, DisputeLimits.MaxText)));
    }

    [Fact]
    public async Task Runs_escalate_silent_sellers_expire_unshipped_returns_and_notify_once_per_state()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var silent = await OpenInr(s, s.Item);
        var answered = await OpenInr(s, AddLine(s, s.Order));
        var accepted = await OpenReturn(s, await DeliveredLine(s));
        var shipped = await OpenReturn(s, await DeliveredLine(s));
        var closed = await OpenReturn(s, await DeliveredLine(s));
        await Act(s, s.Seller, answered, new(DisputeAction.Message, "Checking with the carrier."));
        await Act(s, s.Seller, accepted, new(DisputeAction.AcceptReturn));
        await Act(s, s.Seller, shipped, new(DisputeAction.AcceptReturn));
        await Act(s, s.Buyer, shipped, new(DisputeAction.MarkReturnShipped, Tracking: "RT-1"));
        await Act(s, s.Buyer, closed, new(DisputeAction.Close));

        // Now: only current-state notices; earlier steps are not back-filled.
        Assert.Equal(new DisputeAutomationResult(0, 0, 4), await Run(s, TimeSpan.Zero));
        Assert.Equal(("Received", "Received", "ReturnAccepted", "ReturnShipped", ""), (Keys(s, silent), Keys(s, answered), Keys(s, accepted), Keys(s, shipped), Keys(s, closed)));
        Assert.Contains("We received this request and let the seller know.", SqlFixture.Scalar(s.Target, $"SELECT Message FROM dbo.DisputeEvent WHERE DisputeId={silent} AND NoticeKey='Received'"));
        Assert.Equal("System|", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(ActorRole,'|',ActorId) FROM dbo.DisputeEvent WHERE DisputeId={silent} AND NoticeKey='Received'"));
        var events = EventCount(s);
        Assert.Equal(new DisputeAutomationResult(0, 0, 0), await Run(s, TimeSpan.Zero));
        Assert.Equal(events, EventCount(s));

        // Past the deadlines: escalate the silent seller, expire the unshipped return, tell the buyer where eBay can step in.
        Assert.Equal(new DisputeAutomationResult(1, 1, 3), await Run(s, Later));
        Assert.Equal(("Escalated|", "Open|", "Closed|ReturnNotShipped", "ReturnShipped|", "Closed|BuyerClosed"),
            (State(s, silent), State(s, answered), State(s, accepted), State(s, shipped), State(s, closed)));
        Assert.Equal(("Received,Escalated", "Received,Due:Open", "ReturnAccepted", "ReturnShipped,Due:ReturnShipped", ""),
            (Keys(s, silent), Keys(s, answered), Keys(s, accepted), Keys(s, shipped), Keys(s, closed)));
        Assert.Equal("System|", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(ActorRole,'|',ActorId) FROM dbo.DisputeEvent WHERE DisputeId={silent} AND Type='Escalated'"));
        Assert.Equal("System", SqlFixture.Scalar(s.Target, $"SELECT ActorRole FROM dbo.DisputeEvent WHERE DisputeId={accepted} AND Type='ReturnExpired'"));
        Assert.Contains("eBay is reviewing this request", SqlFixture.Scalar(s.Target, $"SELECT Message FROM dbo.DisputeEvent WHERE DisputeId={silent} AND NoticeKey='Escalated'"));
        events = EventCount(s);
        for (var i = 0; i < 3; i++) Assert.Equal(new DisputeAutomationResult(0, 0, 0), await Run(s, Later));
        Assert.Equal(events, EventCount(s));

        // Details show the notices to both parties (the E03 timeline reads them as eBay updates).
        var detail = (await With(s, x => x.FindAsync(s.Seller, silent, 1)))!;
        Assert.Equal((DisputeActor.System, DisputeEventType.AutoNotice), (detail.Events[0].ActorRole, detail.Events[0].Type));
        Assert.Equal(DisputeStatus.Escalated, detail.Status);
    }

    [Fact]
    public async Task Parallel_replicas_and_small_batches_never_duplicate_work()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var ids = new List<int> { await OpenInr(s, s.Item) };
        for (var i = 0; i < 5; i++) ids.Add(await OpenInr(s, AddLine(s, s.Order)));

        // Batches: one request per step and run until nothing is left.
        Assert.Equal(new DisputeAutomationResult(0, 0, 1), await Run(s, TimeSpan.Zero, batch: 1));
        Assert.Equal("Received", Keys(s, ids[0]));
        Assert.Equal("", Keys(s, ids[1]));

        // Four replicas past the deadline at once: each request escalates once and gets each notice once.
        var results = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Run(s, Later))));
        Assert.Equal(6, results.Sum(x => x.Escalated));
        Assert.Equal("6", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE Type='Escalated'"));
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM (SELECT DisputeId, NoticeKey FROM dbo.DisputeEvent WHERE NoticeKey IS NOT NULL GROUP BY DisputeId, NoticeKey HAVING COUNT(*) > 1) d"));
        Assert.All(ids, id => Assert.EndsWith("Escalated", Keys(s, id)));
        Assert.All(ids, id => Assert.Equal("Escalated|", State(s, id)));
        var events = EventCount(s);
        Assert.Equal(new DisputeAutomationResult(0, 0, 0), await Run(s, Later));
        Assert.Equal(events, EventCount(s));

        // Concurrent NotifyAsync for one request: exactly one write in total.
        var fresh = await OpenInr(s, AddLine(s, s.Order));
        var written = await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Task.Run(() => Notify(s, fresh))));
        Assert.Equal(1, written.Sum());
        Assert.Equal("Received", Keys(s, fresh));
        Assert.Equal(0, await Notify(s, int.MaxValue));
    }

    [Fact]
    public async Task Notice_key_constraints_index_and_grants()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        var id = await OpenInr(s, s.Item);
        string Insert(string role, string type, string key, string message = "N'Update'", string actor = "NULL") =>
            $"INSERT dbo.DisputeEvent(DisputeId,ActorId,ActorRole,Type,Message,NoticeKey) VALUES({id},{actor},'{role}','{type}',{message},{key})";
        SqlFixture.Execute(s.Target, Insert("System", "AutoNotice", "'Received'"));
        foreach (var sql in new[]
        {
            Insert("System", "AutoNotice", "'Received'"), // Same request and key.
            Insert("System", "AutoNotice", "NULL"),
            Insert("System", "AutoNotice", "'Bogus'"),
            Insert("System", "AutoNotice", "'Escalated'", message: "NULL"),
            Insert("Buyer", "AutoNotice", "'Escalated'", actor: s.Buyer.ToString()),
            Insert("System", "Escalated", "'Escalated'"),
        })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, sql));
        Assert.Equal("varchar|20|YES|Latin1_General_100_BIN2", SqlFixture.Scalar(s.Target,
            "SELECT CONCAT(DATA_TYPE,'|',CHARACTER_MAXIMUM_LENGTH,'|',IS_NULLABLE,'|',COLLATION_NAME) FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME='DisputeEvent' AND COLUMN_NAME='NoticeKey'"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM sys.indexes WHERE name='UX_DisputeEvent_Notice' AND is_unique=1 AND has_filter=1"));
        // Existing events (no key) are untouched and the timeline stays append-only.
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE DisputeId={id} AND Type='Opened' AND NoticeKey IS NULL"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.DisputeEvent SET NoticeKey=NULL WHERE DisputeId={id}"));

        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, Insert("System", "AutoNotice", "'Escalated'"));
        Assert.Equal("Received,Escalated", Keys(s, id));
    }

    [Fact]
    public async Task Http_actions_post_notices_right_away_and_the_background_worker_escalates_overdue_requests()
    {
        var s = await Setup();
        EstimatedDaysAgo(s, s.Order, 2);
        using (var factory = new AccountHttpTests.Factory(s.Target))
        {
            using var buyer = await Login(factory, "buyer");
            using var seller = await Login(factory, "seller");
            var form = await buyer.GetStringAsync($"/Disputes/NotReceived?orderItemId={s.Item}");
            var opened = await buyer.PostAsync("/Disputes/Open", new FormUrlEncodedContent(new Dictionary<string, string>
            { ["OrderItemId"] = s.Item.ToString(), ["Kind"] = "ItemNotReceived", ["Preference"] = "Refund", ["__RequestVerificationToken"] = Field(form) }));
            Assert.Equal(HttpStatusCode.Redirect, opened.StatusCode);
            var id = int.Parse(SqlFixture.Scalar(s.Target, "SELECT id FROM dbo.Dispute"));
            Assert.Equal("Received", Keys(s, id));
            var page = WebUtility.HtmlDecode(await seller.GetStringAsync($"/Disputes/Details/{id}"));
            Assert.Contains("eBay sent an automatic update.", page);
            Assert.Contains("We received this request and let the seller know.", page);

            var line = await DeliveredLine(s);
            var ret = await OpenReturn(s, line);
            var reply = await seller.GetStringAsync($"/Disputes/Details/{ret}?reply=AcceptReturn");
            Assert.Equal(HttpStatusCode.Redirect, (await seller.PostAsync("/Disputes/Act", new FormUrlEncodedContent(new Dictionary<string, string>
                { ["DisputeId"] = ret.ToString(), ["Action"] = "AcceptReturn", ["__RequestVerificationToken"] = Field(reply) }))).StatusCode);
            Assert.Equal("ReturnAccepted", Keys(s, ret)); // Opened outside HTTP, so no "Received": notices follow the current state only.
            Assert.Contains("The return was accepted. The buyer should ship the item back", WebUtility.HtmlDecode(await buyer.GetStringAsync($"/Disputes/Details/{ret}")));
            Assert.Equal("Open|", State(s, id)); // Worker is off in the default test host.
        }

        // A host with the worker on runs a pass at start-up: the overdue silent request moves to eBay.
        var overdue = int.Parse(SqlFixture.Scalar(s.Target, "SELECT MIN(id) FROM dbo.Dispute"));
        SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE id={overdue}");
        using var withWorker = new AccountHttpTests.Factory(s.Target, automation: true);
        using var client = withWorker.CreateClient(new() { AllowAutoRedirect = false, BaseAddress = new Uri("https://localhost") });
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/Account/Login")).StatusCode);
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (State(s, overdue) != "Escalated|" && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.Equal("Escalated|", State(s, overdue));
        while (!Keys(s, overdue).EndsWith("Escalated") && DateTime.UtcNow < deadline) await Task.Delay(200);
        Assert.Equal("Received,Escalated", Keys(s, overdue));
    }
}
