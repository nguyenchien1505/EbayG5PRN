using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Orders;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using EbayGroup5.Infrastructure.Orders;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static EbayGroup5.Database.Tests.ReviewTests;

namespace EbayGroup5.Database.Tests;

/// <summary>E01: DISPUTES.md rules, timing, schema and service.</summary>
public sealed class DisputeTests
{
    private static readonly DisputeTiming Timing = new();
    private static readonly DateTime T0 = new(2026, 10, 7, 9, 0, 0, DateTimeKind.Utc); // Wednesday

    private static DisputeSnapshot Snap(DisputeKind kind, DisputeStatus status, DateTime? due = null, bool responded = false, decimal? offer = null,
        DisputeReason? reason = null, DateTime? created = null) =>
        new(kind, reason ?? (kind == DisputeKind.ItemNotReceived ? DisputeReason.ItemNotReceived : DisputeReason.ChangedMind), status, created ?? T0,
            status == DisputeStatus.Closed ? null : due ?? T0.AddDays(5), responded, 10m, offer);

    // A well-formed command for each action, so only role/state decide the result.
    private static DisputeCommand Valid(DisputeAction action) => action switch
    {
        DisputeAction.Message => new(action, "Hello"),
        DisputeAction.Decide => new(action, "Decision note", ForBuyer: true),
        DisputeAction.AddTracking or DisputeAction.MarkReturnShipped => new(action, Tracking: "TRK-1"),
        DisputeAction.OfferPartial => new(action, Amount: 4.50m),
        _ => new(action)
    };

    [Fact]
    public void Decide_succeeds_exactly_when_role_and_state_allow_and_matches_available_actions()
    {
        var checkedCases = 0;
        foreach (var kind in Enum.GetValues<DisputeKind>())
        foreach (var status in Enum.GetValues<DisputeStatus>())
        foreach (var reason in kind == DisputeKind.Return ? new[] { DisputeReason.ChangedMind, DisputeReason.Damaged } : [DisputeReason.ItemNotReceived])
        foreach (var offer in new decimal?[] { null, 3m })
        foreach (var responded in new[] { false, true })
        foreach (var now in new[] { T0.AddDays(1), T0.AddDays(6), T0.AddDays(60) })
        {
            if (kind == DisputeKind.ItemNotReceived && (offer != null || status is DisputeStatus.ReturnAccepted or DisputeStatus.ReturnShipped)) continue;
            var s = Snap(kind, status, T0.AddDays(5), responded, offer, reason);
            foreach (var actor in Enum.GetValues<DisputeActor>())
            {
                var available = DisputeRules.AvailableActions(s, actor, now, Timing);
                foreach (var action in Enum.GetValues<DisputeAction>())
                {
                    var result = DisputeRules.Decide(s, actor, Valid(action), now, Timing);
                    var allowed = DisputeRules.StateAllows(s, actor, action, now, Timing);
                    Assert.Equal(allowed ? DisputeResultCode.Success : DisputeRules.RoleMayUse(actor, action) ? DisputeResultCode.Conflict : DisputeResultCode.Forbidden, result.Code);
                    if (action != DisputeAction.ItemReceived) Assert.Equal(allowed, available.Contains(action));
                    if (status == DisputeStatus.Closed) Assert.False(allowed);
                    checkedCases++;
                }
            }
        }
        Assert.True(checkedCases > 3000);
    }

    [Fact]
    public void Transition_table_matches_the_contract()
    {
        DisputeChange Ok(DisputeSnapshot s, DisputeActor actor, DisputeCommand c, DateTime? now = null)
        {
            var d = DisputeRules.Decide(s, actor, c, now ?? T0.AddDays(1), Timing);
            Assert.Equal(DisputeResultCode.Success, d.Code);
            return d.Change!;
        }
        DisputeResultCode Code(DisputeSnapshot s, DisputeActor actor, DisputeCommand c, DateTime? now = null) => DisputeRules.Decide(s, actor, c, now ?? T0.AddDays(1), Timing).Code;
        var inr = Snap(DisputeKind.ItemNotReceived, DisputeStatus.Open);
        var ret = Snap(DisputeKind.Return, DisputeStatus.Open, reason: DisputeReason.Damaged);

        var refund = Ok(inr, DisputeActor.Seller, new(DisputeAction.Refund));
        Assert.Equal((DisputeStatus.Closed, DisputeOutcome.Refunded, 10m, (DateTime?)null), (refund.Status, refund.Outcome!.Value, refund.RefundAmount!.Value, refund.DueAtUtc));
        Assert.Equal(DisputeStatus.Open, Ok(inr, DisputeActor.Seller, new(DisputeAction.AddTracking, Tracking: " TRK ")).Status);
        Assert.Equal("TRK", Ok(inr, DisputeActor.Seller, new(DisputeAction.AddTracking, Tracking: " TRK ")).Tracking);
        Assert.Equal(DisputeResultCode.Conflict, Code(inr, DisputeActor.Seller, new(DisputeAction.AcceptReturn)));

        // Item problems cannot be declined; change of mind can.
        Assert.Equal(DisputeResultCode.Conflict, Code(ret, DisputeActor.Seller, new(DisputeAction.Decline)));
        Assert.Equal(DisputeOutcome.SellerDeclined, Ok(ret with { Reason = DisputeReason.DoesntFit }, DisputeActor.Seller, new(DisputeAction.Decline)).Outcome);
        Assert.All(Enum.GetValues<DisputeReason>().Where(r => r != DisputeReason.ItemNotReceived), r =>
            Assert.Equal(r is DisputeReason.OrderedWrongItem or DisputeReason.ChangedMind or DisputeReason.NoLongerNeeded or DisputeReason.BetterPrice or DisputeReason.DoesntFit,
                DisputeRules.IsChangeOfMind(r)));

        var offered = Ok(ret, DisputeActor.Seller, new(DisputeAction.OfferPartial, "Partial", 4.5m));
        Assert.Equal((4.5m, DisputeEventType.OfferMade, DisputeStatus.Open), (offered.OfferAmount!.Value, offered.Event, offered.Status));
        var withOffer = ret with { OfferAmount = 4.5m };
        Assert.Equal(DisputeResultCode.Conflict, Code(withOffer, DisputeActor.Seller, new(DisputeAction.OfferPartial, Amount: 2m)));
        Assert.Equal((DisputeOutcome.PartialRefund, 4.5m), (Ok(withOffer, DisputeActor.Buyer, new(DisputeAction.AcceptOffer)).Outcome!.Value, Ok(withOffer, DisputeActor.Buyer, new(DisputeAction.AcceptOffer)).RefundAmount!.Value));
        var declined = Ok(withOffer, DisputeActor.Buyer, new(DisputeAction.DeclineOffer));
        Assert.Null(declined.OfferAmount); Assert.Equal(4.5m, declined.Amount);

        var accepted = Ok(withOffer, DisputeActor.Seller, new(DisputeAction.AcceptReturn));
        Assert.Equal((DisputeStatus.ReturnAccepted, (decimal?)null, new DateTime(2026, 10, 15, 9, 0, 0)), (accepted.Status, accepted.OfferAmount, accepted.DueAtUtc!.Value));
        var shipped = Ok(Snap(DisputeKind.Return, DisputeStatus.ReturnAccepted), DisputeActor.Buyer, new(DisputeAction.MarkReturnShipped, Tracking: "RET-1"));
        Assert.Equal((DisputeStatus.ReturnShipped, "RET-1", new DateTime(2026, 10, 13, 9, 0, 0)), (shipped.Status, shipped.ReturnTracking!, shipped.DueAtUtc!.Value));
        Assert.Equal(DisputeOutcome.Refunded, Ok(Snap(DisputeKind.Return, DisputeStatus.ReturnShipped), DisputeActor.Seller, new(DisputeAction.Refund)).Outcome);

        // Escalation: only after the due date; INR only within 21 business days of opening; the system only when the seller never responded.
        Assert.Equal(DisputeResultCode.Conflict, Code(inr, DisputeActor.Buyer, new(DisputeAction.Escalate), T0.AddDays(4)));
        var escalated = Ok(inr, DisputeActor.Buyer, new(DisputeAction.Escalate, "Seller did not help"), T0.AddDays(5));
        Assert.Equal((DisputeStatus.Escalated, true, T0.AddDays(7)), (escalated.Status, escalated.Escalates, escalated.DueAtUtc!.Value));
        var cutoff = Timing.AddBusinessDays(T0, 21);
        Assert.Equal(DisputeResultCode.Success, Code(inr, DisputeActor.Buyer, new(DisputeAction.Escalate), cutoff));
        Assert.Equal(DisputeResultCode.Conflict, Code(inr, DisputeActor.Buyer, new(DisputeAction.Escalate), cutoff.AddTicks(1)));
        Assert.Equal(DisputeResultCode.Success, Code(ret, DisputeActor.Buyer, new(DisputeAction.Escalate), T0.AddDays(90)));
        Assert.Equal(DisputeResultCode.Conflict, Code(Snap(DisputeKind.Return, DisputeStatus.ReturnAccepted), DisputeActor.Buyer, new(DisputeAction.Escalate), T0.AddDays(90)));
        Assert.Equal(DisputeResultCode.Success, Code(inr, DisputeActor.System, new(DisputeAction.Escalate), T0.AddDays(5)));
        Assert.Equal(DisputeResultCode.Conflict, Code(inr with { SellerResponded = true }, DisputeActor.System, new(DisputeAction.Escalate), T0.AddDays(5)));
        Assert.Equal(DisputeResultCode.Forbidden, Code(inr, DisputeActor.Seller, new(DisputeAction.Escalate), T0.AddDays(5)));

        var esc = Snap(DisputeKind.ItemNotReceived, DisputeStatus.Escalated);
        Assert.Equal(DisputeResultCode.Success, Code(esc, DisputeActor.Admin, new(DisputeAction.Message, "Reviewing")));
        Assert.Equal(DisputeResultCode.Conflict, Code(inr, DisputeActor.Admin, new(DisputeAction.Message, "Too early")));
        Assert.Equal((DisputeOutcome.DecidedForBuyer, 10m), (Ok(esc, DisputeActor.Admin, Valid(DisputeAction.Decide)).Outcome!.Value, Ok(esc, DisputeActor.Admin, Valid(DisputeAction.Decide)).RefundAmount!.Value));
        var forSeller = Ok(esc, DisputeActor.Admin, new(DisputeAction.Decide, "Tracking shows delivery", ForBuyer: false));
        Assert.Equal(DisputeOutcome.DecidedForSeller, forSeller.Outcome); Assert.Null(forSeller.RefundAmount);
        Assert.Equal(DisputeOutcome.Refunded, Ok(esc, DisputeActor.Seller, new(DisputeAction.Refund)).Outcome);
        Assert.Equal(DisputeOutcome.BuyerClosed, Ok(esc, DisputeActor.Buyer, new(DisputeAction.Close)).Outcome);
        Assert.Equal(DisputeOutcome.ItemReceived, Ok(esc, DisputeActor.Buyer, new(DisputeAction.ItemReceived)).Outcome);
        Assert.Equal(DisputeResultCode.Conflict, Code(ret, DisputeActor.Buyer, new(DisputeAction.ItemReceived)));
        Assert.Equal(DisputeOutcome.ReturnNotShipped, Ok(Snap(DisputeKind.Return, DisputeStatus.ReturnAccepted), DisputeActor.System, new(DisputeAction.ExpireReturn), T0.AddDays(5)).Outcome);
        Assert.Equal(DisputeResultCode.Conflict, Code(Snap(DisputeKind.Return, DisputeStatus.ReturnAccepted), DisputeActor.System, new(DisputeAction.ExpireReturn), T0.AddDays(4)));
    }

    [Theory]
    [InlineData(DisputeAction.Message, null, null, null, null)]
    [InlineData(DisputeAction.Message, "   ", null, null, null)]
    [InlineData(DisputeAction.Message, "bad\u0001text", null, null, null)]
    [InlineData(DisputeAction.Message, "ok", null, "TRK", null)]
    [InlineData(DisputeAction.Message, "ok", "1.00", null, null)]
    [InlineData(DisputeAction.Decide, "note", null, null, null)]
    [InlineData(DisputeAction.Close, null, null, null, true)]
    [InlineData(DisputeAction.AddTracking, null, null, null, null)]
    [InlineData(DisputeAction.AddTracking, null, null, "line\nbreak", null)]
    [InlineData(DisputeAction.OfferPartial, null, null, null, null)]
    [InlineData(DisputeAction.OfferPartial, null, "0", null, null)]
    [InlineData(DisputeAction.OfferPartial, null, "10.00", null, null)]
    [InlineData(DisputeAction.OfferPartial, null, "1.005", null, null)]
    public void Invalid_input_is_rejected_before_state(DisputeAction action, string? message, string? amount, string? tracking, bool? forBuyer)
    {
        var s = action == DisputeAction.Decide ? Snap(DisputeKind.Return, DisputeStatus.Escalated) : Snap(DisputeKind.Return, DisputeStatus.Open);
        var actor = action switch { DisputeAction.Decide => DisputeActor.Admin, DisputeAction.Close => DisputeActor.Buyer, DisputeAction.Message => DisputeActor.Buyer, _ => DisputeActor.Seller };
        if (action == DisputeAction.AddTracking) s = Snap(DisputeKind.ItemNotReceived, DisputeStatus.Open);
        var command = new DisputeCommand(action, message, amount == null ? null : decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture), tracking, forBuyer);
        Assert.Equal(DisputeResultCode.Invalid, DisputeRules.Decide(s, actor, command, T0.AddDays(1), Timing).Code);
        Assert.False(DisputeRules.TryText(new string('a', DisputeLimits.MaxText + 1), DisputeLimits.MaxText, false, out _));
        Assert.True(DisputeRules.TryText("multi\r\nline\tok", DisputeLimits.MaxText, true, out var kept)); Assert.Equal("multi\r\nline\tok", kept);
    }

    [Fact]
    public void Business_days_skip_weekends_and_demo_mode_uses_fixed_seconds()
    {
        DateTime D(int day, int hour = 9) => new(2026, 10, day, hour, 0, 0, DateTimeKind.Utc); // Oct 2026: 9 = Fri, 10 = Sat, 11 = Sun
        Assert.Equal(D(12), Timing.AddBusinessDays(D(9), 1));
        Assert.Equal(D(12), Timing.AddBusinessDays(D(10), 1));
        Assert.Equal(D(16), Timing.AddBusinessDays(D(11), 5));
        Assert.Equal(D(16), Timing.AddBusinessDays(D(10), 5));
        Assert.Equal(D(16), Timing.AddBusinessDays(D(9), 5));
        Assert.Equal(D(14), Timing.AddBusinessDays(D(9), 3));
        Assert.Equal(D(9), Timing.AddBusinessDays(D(9), 0));
        Assert.Equal(D(16), Timing.EstimatedDelivery(D(9)));
        var demo = new DisputeTiming(new() { DemoBusinessDaySeconds = 40, EstimatedDeliveryBusinessDays = 5 });
        Assert.True(demo.IsDemo); Assert.False(Timing.IsDemo);
        Assert.Equal(D(10).AddMinutes(2), demo.AddBusinessDays(D(10), 3));
        Assert.Equal(D(10).AddSeconds(200), demo.EstimatedDelivery(D(10)));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisputeTiming(new() { EstimatedDeliveryBusinessDays = 0 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DisputeTiming(new() { DemoBusinessDaySeconds = -1 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => Timing.AddBusinessDays(D(9), -1));
    }

    [Fact]
    public void Migration_backfills_estimated_delivery_like_the_csharp_calendar_and_keeps_legacy_disputes()
    {
        var target = SqlFixture.NewTarget();
        var remaining = SqlFixture.MigrateBefore(target, "0011");
        DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, """
            INSERT dbo.OrderTable(orderDate,status) SELECT DATEADD(day,n,'2026-10-05T13:45:10'),N'Pending' FROM (VALUES(0),(1),(2),(3),(4),(5),(6)) d(n);
            INSERT dbo.OrderTable(orderDate,status) VALUES(NULL,N'Pending');
            INSERT dbo.Dispute(orderId,raisedBy,description,status,resolution) SELECT TOP(1) id,buyerId,N'legacy',N'Open',NULL FROM dbo.OrderTable ORDER BY id;
            """);
        var metadata = SqlFixture.Rows(target, SqlFixture.Metadata);
        Assert.Equal(remaining, MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target, SqlFixture.Metadata)));
        var rows = SqlFixture.Rows(target, "SELECT CONCAT(CONVERT(varchar(30),orderDate,126),'|',CONVERT(varchar(30),EstimatedDeliveryUtc,126)) FROM dbo.OrderTable");
        var checkedRows = 0;
        foreach (var row in rows)
        {
            var parts = row.Split('|');
            if (parts[0] == "") { Assert.Equal("", parts[1]); continue; }
            var ordered = DateTime.SpecifyKind(DateTime.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);
            Assert.Equal(Timing.EstimatedDelivery(ordered), DateTime.SpecifyKind(DateTime.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc));
            checkedRows++;
        }
        Assert.True(checkedRows >= 7);
        Assert.Equal("legacy|Open|", SqlFixture.Scalar(target, "SELECT CONCAT(description,'|',status,'|',OrderItemId) FROM dbo.Dispute"));
        SqlFixture.Execute(target, "UPDATE dbo.Dispute SET status=N'anything'; DELETE dbo.Dispute;"); // Legacy rows keep baseline behaviour.
        Assert.Equal(0, MigrationRunner.Run(target));
    }

    private static DisputeService Service(SetupData s, EbayGroup5.Infrastructure.Persistence.EbayDbContext db) => new(db, TimeProvider.System, Timing);
    private static async Task<DisputeResult> OpenRequest(SetupData s, int buyer, OpenDisputeRequest request)
    {
        await using var db = Open(s.Target);
        return await Service(s, db).OpenAsync(buyer, request);
    }
    private static async Task<DisputeResult> Act(SetupData s, int actor, int dispute, DisputeCommand command)
    {
        await using var db = Open(s.Target);
        return await Service(s, db).ActAsync(actor, dispute, command);
    }
    private static async Task<DisputeDetail?> Find(SetupData s, int actor, int dispute)
    {
        await using var db = Open(s.Target);
        // E03 pages the timeline newest first; these checks read it oldest first (all fit on page 1).
        return await Service(s, db).FindAsync(actor, dispute) is { } detail ? detail with { Events = detail.Events.Reverse().ToList() } : null;
    }
    private static async Task<int> Place(SetupData s)
    {
        await using var db = Open(s.Target);
        return (await new CheckoutService(db, Timing).PlaceAsync(s.Buyer, new(s.Product, s.Address, 2, 49.90m, Guid.NewGuid()))).OrderId!.Value;
    }
    private static int Line(SetupData s, int order) => int.Parse(SqlFixture.Scalar(s.Target, $"SELECT MIN(id) FROM dbo.OrderItem WHERE orderId={order}"));
    private static void EstimatedDaysAgo(SetupData s, int order, int days) =>
        SqlFixture.Execute(s.Target, $"UPDATE dbo.OrderTable SET EstimatedDeliveryUtc=DATEADD(day,-{days},SYSUTCDATETIME()) WHERE id={order}");
    private static void DueNow(SetupData s, int dispute) => SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET DueAtUtc=DATEADD(minute,-1,SYSUTCDATETIME()) WHERE id={dispute}");
    private static OpenDisputeRequest Inr(int line, string? details = null) => new(line, DisputeKind.ItemNotReceived, DisputeReason.ItemNotReceived, DisputePreference.Refund, details);
    private static OpenDisputeRequest Ret(int line, DisputeReason reason = DisputeReason.Damaged, string? details = "Cracked case") => new(line, DisputeKind.Return, reason, null, details);

    [Fact]
    public async Task Checkout_and_shipping_store_the_estimated_delivery()
    {
        var s = await Setup();
        await using var db = Open(s.Target);
        var order = await db.Set<OrderTable>().AsNoTracking().SingleAsync(x => x.Id == s.Order);
        Assert.NotNull(order.EstimatedDeliveryUtc);
        // orderDate is datetime (≈3 ms precision) while the estimate is computed from the unrounded checkout instant.
        Assert.True((Timing.EstimatedDelivery(DateTime.SpecifyKind(order.OrderDate!.Value, DateTimeKind.Utc)) - order.EstimatedDeliveryUtc!.Value).Duration() < TimeSpan.FromMilliseconds(10));
        Assert.Equal(OrderOutcome.Success, (await new OrderService(db).ActAsync(s.Seller, OrderAction.Ship, s.Order)).Outcome);
        var arrival = await db.Set<ShippingInfo>().AsNoTracking().Where(x => x.OrderId == s.Order).Select(x => x.EstimatedArrival).SingleAsync();
        Assert.True((arrival!.Value - order.EstimatedDeliveryUtc.Value).Duration() < TimeSpan.FromMilliseconds(10));
        Assert.Equal(order.EstimatedDeliveryUtc, (await new OrderService(db).FindAsync(s.Buyer, OrderAudience.Buyer, s.Order))!.EstimatedDeliveryUtc);
        using var factory = new AccountHttpTests.Factory(s.Target);
        using var buyer = await Login(factory, "buyer");
        Assert.Contains("Estimated delivery: <strong>" + order.EstimatedDeliveryUtc.Value.ToString("ddd, MMM dd, yyyy", System.Globalization.CultureInfo.InvariantCulture),
            await buyer.GetStringAsync($"/Orders/Details/{s.Order}"));
    }

    [Fact]
    public async Task Item_not_received_opens_only_in_window_once_per_line_and_closes_when_received()
    {
        var s = await Setup();
        await using (var db = Open(s.Target))
        {
            var early = await Service(s, db).EligibilityAsync(s.Buyer, s.Item);
            Assert.False(early!.ItemNotReceived.CanOpen); Assert.Contains("after its estimated delivery date", early.ItemNotReceived.Reason);
            Assert.False(early.Return.CanOpen); Assert.Contains("after it's delivered", early.Return.Reason);
            Assert.Null(await Service(s, db).EligibilityAsync(s.OtherBuyer, s.Item));
            Assert.Null(await Service(s, db).EligibilityAsync(s.Seller, s.Item));
        }
        Assert.Equal(DisputeResultCode.Conflict, (await OpenRequest(s, s.Buyer, Inr(s.Item))).Code);
        EstimatedDaysAgo(s, s.Order, 31);
        Assert.Contains("30-day window", (await OpenRequest(s, s.Buyer, Inr(s.Item))).Error);
        EstimatedDaysAgo(s, s.Order, 1);

        Assert.Equal(DisputeResultCode.NotFound, (await OpenRequest(s, s.OtherBuyer, Inr(s.Item))).Code);
        Assert.Equal(DisputeResultCode.Forbidden, (await OpenRequest(s, s.Seller, Inr(s.Item))).Code);
        Assert.Equal(DisputeResultCode.NotFound, (await OpenRequest(s, s.Buyer, Inr(int.MaxValue))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Inr(s.Item) with { Preference = null })).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Inr(s.Item) with { Reason = DisputeReason.Damaged })).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Inr(s.Item, new string('x', 2001)))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Ret(s.Item) with { Preference = DisputePreference.Item })).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Ret(s.Item, details: "  "))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await OpenRequest(s, s.Buyer, Ret(s.Item, DisputeReason.ItemNotReceived))).Code);
        Assert.Equal("0", SqlFixture.Scalar(s.Target, "SELECT COUNT(*) FROM dbo.Dispute"));

        var opened = await OpenRequest(s, s.Buyer, Inr(s.Item, "  Tracking hasn't moved  "));
        Assert.Equal(DisputeResultCode.Success, opened.Code);
        var id = opened.DisputeId!.Value;
        var again = await OpenRequest(s, s.Buyer, Inr(s.Item));
        Assert.Equal((DisputeResultCode.Conflict, id), (again.Code, again.ExistingDisputeId!.Value));
        var detail = (await Find(s, s.Buyer, id))!;
        Assert.Equal((DisputeStatus.Open, DisputeActor.Buyer, "Tracking hasn't moved", DisputePreference.Refund, 49.90m), (detail.Status, detail.Viewer, detail.Details!, detail.Preference!.Value, detail.LineTotal));
        Assert.Equal(Timing.AddBusinessDays(detail.CreatedAtUtc, 3), detail.DueAtUtc);
        Assert.Equal([DisputeAction.Message, DisputeAction.Close], detail.Actions);
        Assert.Equal([DisputeEventType.Opened], detail.Events.Select(x => x.Type));
        Assert.Equal(DisputeActor.Seller, (await Find(s, s.Seller, id))!.Viewer);
        Assert.Contains(DisputeAction.Refund, (await Find(s, s.Seller, id))!.Actions);
        Assert.Equal(DisputeActor.Admin, (await Find(s, s.Admin, id))!.Viewer);
        Assert.Empty((await Find(s, s.Admin, id))!.Actions); // Admin acts only after escalation.
        Assert.Null(await Find(s, s.OtherBuyer, id));
        Assert.Null(await Find(s, s.OtherSeller, id));
        Assert.Equal(DisputeResultCode.NotFound, (await Act(s, s.OtherSeller, id, new(DisputeAction.Refund))).Code);
        Assert.Equal(DisputeResultCode.Forbidden, (await Act(s, s.Buyer, id, new(DisputeAction.Refund))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await Act(s, s.Buyer, id, new(DisputeAction.ItemReceived))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, id, new(DisputeAction.AddTracking, "Shipped late, sorry", Tracking: "LATE-1"))).Code);
        Assert.NotEqual("", SqlFixture.Scalar(s.Target, $"SELECT SellerRespondedAtUtc FROM dbo.Dispute WHERE id={id}"));

        // B06 Confirm received closes the request in the same transaction.
        await Deliver(s, s.Order);
        detail = (await Find(s, s.Buyer, id))!;
        Assert.Equal((DisputeStatus.Closed, DisputeOutcome.ItemReceived), (detail.Status, detail.Outcome!.Value));
        Assert.Equal([DisputeEventType.Opened, DisputeEventType.TrackingAdded, DisputeEventType.ItemReceived], detail.Events.Select(x => x.Type));
        Assert.Equal("LATE-1", detail.Events[1].Tracking);
        Assert.Empty(detail.Actions);
        Assert.Equal(DisputeResultCode.Conflict, (await Act(s, s.Buyer, id, new(DisputeAction.Message, "still there?"))).Code);
        await using (var db = Open(s.Target))
        {
            var after = (await Service(s, db).EligibilityAsync(s.Buyer, s.Item))!;
            Assert.False(after.ItemNotReceived.CanOpen); Assert.Contains("start a return", after.ItemNotReceived.Reason);
            Assert.True(after.Return.CanOpen);
        }

        // A second order: buyer closes, and a closed request of the same kind can't be reopened.
        var second = await Place(s); var line = Line(s, second); EstimatedDaysAgo(s, second, 2);
        var other = (await OpenRequest(s, s.Buyer, Inr(line) with { Preference = DisputePreference.Item })).DisputeId!.Value;
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Buyer, other, new(DisputeAction.Close))).Code);
        var reopen = await OpenRequest(s, s.Buyer, Inr(line));
        Assert.Equal((DisputeResultCode.Conflict, other), (reopen.Code, reopen.ExistingDisputeId!.Value));
        Assert.Contains("can't be reopened", reopen.Error);
    }

    [Fact]
    public async Task Return_flow_offer_ship_back_escalate_and_refund_with_a_full_timeline()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var id = (await OpenRequest(s, s.Buyer, Ret(s.Item))).DisputeId!.Value;
        Assert.Equal(DisputeResultCode.Conflict, (await Act(s, s.Seller, id, new(DisputeAction.Decline))).Code); // Damaged: Money Back Guarantee.
        Assert.Equal(DisputeResultCode.Invalid, (await Act(s, s.Seller, id, new(DisputeAction.OfferPartial, Amount: 49.90m))).Code); // Must be below the line total.
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, id, new(DisputeAction.OfferPartial, "Keep it for $10 back?", 10m))).Code);
        Assert.Equal(DisputeResultCode.Forbidden, (await Act(s, s.Seller, id, new(DisputeAction.AcceptOffer))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Buyer, id, new(DisputeAction.DeclineOffer))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, id, new(DisputeAction.AcceptReturn))).Code);
        Assert.Equal(DisputeResultCode.Conflict, (await Act(s, s.Buyer, id, new(DisputeAction.Escalate))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Buyer, id, new(DisputeAction.MarkReturnShipped, Tracking: "RET-77"))).Code);
        Assert.Equal(DisputeResultCode.Conflict, (await Act(s, s.Buyer, id, new(DisputeAction.Escalate))).Code); // Seller still has 3 business days.
        DueNow(s, id);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Buyer, id, new(DisputeAction.Escalate, "No refund yet"))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Admin, id, new(DisputeAction.Message, "We're reviewing this request."))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, id, new(DisputeAction.Refund))).Code);
        var detail = (await Find(s, s.Buyer, id))!;
        Assert.Equal((DisputeStatus.Closed, DisputeOutcome.Refunded, 49.90m, 49.90m, "RET-77"), (detail.Status, detail.Outcome!.Value, detail.RefundAmount!.Value, detail.LineTotal, detail.ReturnTracking!));
        Assert.NotNull(detail.EscalatedAtUtc); Assert.NotNull(detail.ClosedAtUtc); Assert.Null(detail.DueAtUtc); Assert.Null(detail.OfferAmount);
        Assert.Equal([DisputeEventType.Opened, DisputeEventType.OfferMade, DisputeEventType.OfferDeclined, DisputeEventType.ReturnAccepted, DisputeEventType.ReturnShipped,
            DisputeEventType.Escalated, DisputeEventType.Message, DisputeEventType.Refunded], detail.Events.Select(x => x.Type));
        Assert.Equal([DisputeActor.Buyer, DisputeActor.Seller, DisputeActor.Buyer, DisputeActor.Seller, DisputeActor.Buyer, DisputeActor.Buyer, DisputeActor.Admin, DisputeActor.Seller],
            detail.Events.Select(x => x.ActorRole));
        Assert.Equal((10m, "Keep it for $10 back?"), (detail.Events[1].Amount!.Value, detail.Events[1].Message!));
        Assert.Equal(49.90m, detail.Events[^1].Amount);
        // Payment is untouched: refunds are simulated.
        Assert.Equal("Paid|49.90", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(status,'|',amount) FROM dbo.Payment WHERE orderId={s.Order}"));
    }

    [Fact]
    public async Task Change_of_mind_decline_admin_decision_system_commands_and_concurrent_opens()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var declined = (await OpenRequest(s, s.Buyer, Ret(s.Item, DisputeReason.ChangedMind))).DisputeId!.Value;
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, declined, new(DisputeAction.Decline, "Final sale"))).Code);
        Assert.Equal(DisputeOutcome.SellerDeclined, (await Find(s, s.Buyer, declined))!.Outcome);

        // Admin decision on an escalated item-not-received request, writing the note to Dispute.resolution.
        var order = await Place(s); var line = Line(s, order); EstimatedDaysAgo(s, order, 3);
        var inr = (await OpenRequest(s, s.Buyer, Inr(line))).DisputeId!.Value;
        await using (var db = Open(s.Target))
        {
            Assert.Equal(DisputeResultCode.Conflict, (await Service(s, db).ActAsSystemAsync(inr, DisputeAction.Escalate)).Code); // Not due yet.
            Assert.Equal(DisputeResultCode.Forbidden, (await Service(s, db).ActAsSystemAsync(inr, DisputeAction.Refund)).Code);
        }
        DueNow(s, inr);
        await using (var db = Open(s.Target)) Assert.Equal(DisputeResultCode.Success, (await Service(s, db).ActAsSystemAsync(inr, DisputeAction.Escalate)).Code);
        await using (var db = Open(s.Target)) Assert.Equal(DisputeResultCode.Conflict, (await Service(s, db).ActAsSystemAsync(inr, DisputeAction.Escalate)).Code); // Idempotent.
        Assert.Equal(DisputeResultCode.Forbidden, (await Act(s, s.Seller, inr, new(DisputeAction.Decide, "x", ForBuyer: false))).Code);
        Assert.Equal(DisputeResultCode.Invalid, (await Act(s, s.Admin, inr, new(DisputeAction.Decide, ForBuyer: true))).Code);
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Admin, inr, new(DisputeAction.Decide, "Seller provided no tracking.", ForBuyer: true))).Code);
        Assert.Equal("Closed|DecidedForBuyer|99.80|Seller provided no tracking.", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(status,'|',Outcome COLLATE DATABASE_DEFAULT,'|',RefundAmount,'|',resolution) FROM dbo.Dispute WHERE id={inr}"));
        var events = (await Find(s, s.Buyer, inr))!.Events;
        Assert.Equal([(DisputeActor.Buyer, DisputeEventType.Opened), (DisputeActor.System, DisputeEventType.Escalated), (DisputeActor.Admin, DisputeEventType.Decided)],
            events.Select(x => (x.ActorRole, x.Type)));
        Assert.Equal("", SqlFixture.Scalar(s.Target, $"SELECT CONCAT(ActorId,'') FROM dbo.DisputeEvent WHERE DisputeId={inr} AND ActorRole='System'"));

        // System expiry of an accepted return the buyer never shipped.
        var third = await Place(s); await Deliver(s, third);
        var accepted = (await OpenRequest(s, s.Buyer, Ret(Line(s, third), DisputeReason.DoesntFit, "Too small"))).DisputeId!.Value;
        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Seller, accepted, new(DisputeAction.AcceptReturn))).Code);
        DueNow(s, accepted);
        await using (var db = Open(s.Target)) Assert.Equal(DisputeResultCode.Success, (await Service(s, db).ActAsSystemAsync(accepted, DisputeAction.ExpireReturn)).Code);
        Assert.Equal(DisputeOutcome.ReturnNotShipped, (await Find(s, s.Seller, accepted))!.Outcome);

        // Two concurrent opens for one line: exactly one request.
        var race = await Place(s); var raceLine = Line(s, race); EstimatedDaysAgo(s, race, 1);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => Task.Run(() => OpenRequest(s, s.Buyer, Inr(raceLine)))));
        Assert.Equal([DisputeResultCode.Success, DisputeResultCode.Conflict], results.Select(x => x.Code).Order());
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.Dispute WHERE OrderItemId={raceLine}"));
        Assert.Equal("1", SqlFixture.Scalar(s.Target, $"SELECT COUNT(*) FROM dbo.DisputeEvent e JOIN dbo.Dispute d ON d.id=e.DisputeId WHERE d.OrderItemId={raceLine}"));
    }

    [Fact]
    public async Task Schema_constraints_triggers_and_grants_protect_requests_and_timeline()
    {
        var s = await Setup();
        await Deliver(s, s.Order);
        var id = (await OpenRequest(s, s.Buyer, Ret(s.Item))).DisputeId!.Value;
        var cols = "orderId,raisedBy,SellerId,OrderItemId,Kind,Reason,Preference,description,status,CreatedAtUtc,DueAtUtc";
        string Row(string kind = "'Return'", string reason = "'Damaged'", string pref = "NULL", string details = "N'x'", string status = "N'Open'", int? item = null, int? order = null, int? seller = null) =>
            $"INSERT dbo.Dispute({cols}) VALUES({order ?? s.Order},{s.Buyer},{seller ?? s.Seller},{item ?? s.Item},{kind},{reason},{pref},{details},{status},SYSUTCDATETIME(),SYSUTCDATETIME())";
        var otherOrder = await Place(s); var otherLine = Line(s, otherOrder);
        foreach (var sql in new[]
        {
            Row(), // Second active request on the same line.
            Row(item: otherLine), // Line of another order.
            Row(kind: "'ItemNotReceived'", reason: "'Damaged'", pref: "'Refund'", item: otherLine, order: otherOrder),
            Row(kind: "'ItemNotReceived'", reason: "'ItemNotReceived'", item: otherLine, order: otherOrder), // Missing preference.
            Row(details: "NULL", item: otherLine, order: otherOrder), // Return without details.
            Row(reason: "'damaged'", item: otherLine, order: otherOrder),
            Row(status: "N'Resolved'", item: otherLine, order: otherOrder),
            Row(status: "N'Closed'", item: otherLine, order: otherOrder), // Closed without outcome.
            Row(kind: "'ItemNotReceived'", reason: "'ItemNotReceived'", pref: "'Refund'", status: "N'ReturnShipped'", item: otherLine, order: otherOrder),
            Row(seller: s.Buyer, item: otherLine, order: otherOrder),
            $"UPDATE dbo.Dispute SET status=N'Escalated' WHERE id={id}", // No EscalatedAtUtc.
            $"UPDATE dbo.Dispute SET OfferAmount=0 WHERE id={id}",
            $"UPDATE dbo.Dispute SET Kind='ItemNotReceived' WHERE id={id}",
            $"UPDATE dbo.Dispute SET raisedBy={s.OtherBuyer} WHERE id={id}",
            $"DELETE dbo.Dispute WHERE id={id}",
            $"INSERT dbo.DisputeEvent(DisputeId,ActorId,ActorRole,Type) VALUES({id},NULL,'Buyer','Message')",
            $"INSERT dbo.DisputeEvent(DisputeId,ActorId,ActorRole,Type) VALUES({id},{s.Admin},'System','Message')",
            $"INSERT dbo.DisputeEvent(DisputeId,ActorId,ActorRole,Type) VALUES({id},{s.Buyer},'Buyer','Resolved')",
            "UPDATE dbo.DisputeEvent SET Message=N'edited'",
            "DELETE dbo.DisputeEvent",
        })
            Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, sql));
        Assert.Equal("1|1", SqlFixture.Scalar(s.Target, "SELECT CONCAT((SELECT COUNT(*) FROM dbo.Dispute),'|',(SELECT COUNT(*) FROM dbo.DisputeEvent))"));

        Assert.Equal(DisputeResultCode.Success, (await Act(s, s.Buyer, id, new(DisputeAction.Close))).Code);
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET status=N'Open', ClosedAtUtc=NULL, Outcome=NULL, DueAtUtc=SYSUTCDATETIME() WHERE id={id}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, $"UPDATE dbo.Dispute SET resolution=N'rewritten' WHERE id={id}"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(s.Target, Row())); // Same kind on the same line, even when closed.
        // A request of the other kind is allowed once the line has no open request (schema level).
        SqlFixture.Execute(s.Target, Row(kind: "'ItemNotReceived'", reason: "'ItemNotReceived'", pref: "'Item'", details: "NULL"));

        var login = "EbayGroup5_TestApp_" + Guid.NewGuid().ToString("N");
        var password = "Eg5!" + Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(s.Target, login, password);
        var app = new SqlConnectionStringBuilder(s.Target) { UserID = login, Password = password, IntegratedSecurity = false }.ConnectionString;
        SqlFixture.Execute(app, $"INSERT dbo.DisputeEvent(DisputeId,ActorId,ActorRole,Type,Message) VALUES({id},{s.Buyer},'Buyer','Message',N'app insert')");
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, "DELETE dbo.DisputeEvent"));
        Assert.Throws<SqlException>(() => SqlFixture.Execute(app, $"DELETE dbo.Dispute WHERE id={id}"));
        Assert.Equal("3", SqlFixture.Scalar(app, $"SELECT COUNT(*) FROM dbo.DisputeEvent WHERE DisputeId={id}")); // Opened, Closed, app insert.
    }
}
