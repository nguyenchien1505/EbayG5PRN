namespace EbayGroup5.Application.Disputes;

/// <summary>Bound from the "Disputes" configuration section (DISPUTES.md, Thời gian).</summary>
public sealed class DisputeTimingOptions
{
    public int EstimatedDeliveryBusinessDays { get; set; } = 5;
    /// <summary>0 = real business days. When positive, one business day lasts this many seconds (demo only).</summary>
    public int DemoBusinessDaySeconds { get; set; }
}

/// <summary>Business-day arithmetic for dispute deadlines: Monday–Friday on the UTC calendar, no holidays (PLAN §3.3).</summary>
public sealed class DisputeTiming
{
    public const int SellerResponseBusinessDays = 3;
    public const int ReturnShipBusinessDays = 5;
    public const int RefundAfterReturnBusinessDays = 3;
    public const int ItemNotReceivedEscalationBusinessDays = 21;
    public static readonly TimeSpan OpenWindow = TimeSpan.FromDays(30);
    public static readonly TimeSpan EscalationReview = TimeSpan.FromHours(48);

    public DisputeTiming(DisputeTimingOptions? options = null)
    {
        options ??= new DisputeTimingOptions();
        if (options.EstimatedDeliveryBusinessDays is < 1 or > 60) throw new ArgumentOutOfRangeException(nameof(options), "EstimatedDeliveryBusinessDays must be 1–60.");
        if (options.DemoBusinessDaySeconds is < 0 or > 86_400) throw new ArgumentOutOfRangeException(nameof(options), "DemoBusinessDaySeconds must be 0–86400.");
        EstimatedDeliveryBusinessDays = options.EstimatedDeliveryBusinessDays;
        DemoBusinessDay = options.DemoBusinessDaySeconds == 0 ? null : TimeSpan.FromSeconds(options.DemoBusinessDaySeconds);
    }

    public int EstimatedDeliveryBusinessDays { get; }
    public TimeSpan? DemoBusinessDay { get; }
    public bool IsDemo => DemoBusinessDay != null;

    public DateTime AddBusinessDays(DateTime utc, int days)
    {
        if (days < 0) throw new ArgumentOutOfRangeException(nameof(days));
        if (DemoBusinessDay is { } demo) return utc + demo * days;
        var result = utc;
        for (var i = 0; i < days; i++)
        {
            result = result.AddDays(1);
            while (result.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) result = result.AddDays(1);
        }
        return result;
    }

    public DateTime EstimatedDelivery(DateTime orderedUtc) => AddBusinessDays(orderedUtc, EstimatedDeliveryBusinessDays);
}
