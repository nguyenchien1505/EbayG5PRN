using System.Data;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using EbayGroup5.Application.Checkout;
using EbayGroup5.Application.Disputes;
using EbayGroup5.Application.Feedback;
using EbayGroup5.Application.Orders;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Authentication;
using EbayGroup5.Infrastructure.Chat;
using EbayGroup5.Infrastructure.Checkout;
using EbayGroup5.Infrastructure.Disputes;
using EbayGroup5.Infrastructure.Orders;
using EbayGroup5.Infrastructure.Persistence;
using EbayGroup5.Infrastructure.Reputation;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace EbayGroup5.Load;

/// <summary>
/// F05 load dataset (LOAD.md). Orders, reviews/feedback and return requests go through the real services so every business rule and
/// constraint holds; only chat history is bulk-inserted, row by row valid for the 0006 constraints. Refuses to run twice on one database.
/// </summary>
public static class LoadSeed
{
    public const int Buyers = 50, Sellers = 20, ProductsPerSeller = 10;
    public static string BuyerEmail(int i) => $"load-buyer-{i:000}@load.example.invalid";
    public static string SellerEmail(int i) => $"load-seller-{i:00}@load.example.invalid";
    private static EbayDbContext Open(string sql) => new(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(sql, x => x.CommandTimeout(120)).Options);

    public static async Task RunAsync(string sql, string password, double scale, string output)
    {
        if (scale is <= 0 or > 1) throw new ArgumentException("--scale must be in (0, 1].");
        if (!AccountService.ValidPassword(password)) throw new ArgumentException("EBAY_LOAD_PASSWORD must be 12-128 characters.");
        var reviews = (int)Math.Round(10_000 * scale);
        var disputes = (int)Math.Round(1_000 * scale);
        var messages = (int)Math.Round(100_000 * scale);
        var watch = Stopwatch.StartNew();
        Directory.CreateDirectory(output);

        // 1. Accounts (same random password for every load user of this run).
        var buyers = new int[Buyers + 1];
        var sellers = new int[Sellers + 1];
        await using (var db = Open(sql))
        {
            if (await db.Set<User>().AnyAsync(x => x.Email == BuyerEmail(1).ToUpperInvariant()))
                throw new InvalidOperationException("Load data already exists; seed a fresh stack.");
            var accounts = new AccountService(db);
            for (var i = 1; i <= Buyers; i++)
                if (!await accounts.RegisterAsync($"Load Buyer {i:000}", BuyerEmail(i), password)) throw new InvalidOperationException("Cannot register a load buyer.");
            for (var i = 1; i <= Sellers; i++)
                if (!await accounts.RegisterAsync($"Load Seller {i:00}", SellerEmail(i), password)) throw new InvalidOperationException("Cannot register a load seller.");
            await db.Set<User>().Where(x => x.Email!.StartsWith("LOAD-SELLER-")).ExecuteUpdateAsync(x => x.SetProperty(u => u.Role, "Seller"));
            var ids = await db.Set<User>().Where(x => x.Email!.EndsWith("@LOAD.EXAMPLE.INVALID")).ToDictionaryAsync(x => x.Email!, x => x.Id);
            for (var i = 1; i <= Buyers; i++) buyers[i] = ids[BuyerEmail(i).ToUpperInvariant()];
            for (var i = 1; i <= Sellers; i++) sellers[i] = ids[SellerEmail(i).ToUpperInvariant()];
        }
        Console.WriteLine($"Accounts: {Buyers} buyers, {Sellers} sellers ({watch.Elapsed.TotalSeconds:0}s)");

        // 2. Catalog: 10 fixed-price products per seller with stock for every planned order.
        var products = new (int Id, decimal Price)[Sellers + 1, ProductsPerSeller];
        await using (var db = Open(sql))
        {
            var category = new Category { Name = "Load test" };
            db.Add(category);
            await db.SaveChangesAsync();
            for (var s = 1; s <= Sellers; s++)
                for (var p = 0; p < ProductsPerSeller; p++)
                {
                    var price = 10m + s + p / 10m;
                    var product = new Product { Title = $"Load item S{s:00}-{p}", Description = "Fictional load-test listing.", Price = price, CategoryId = category.Id, SellerId = sellers[s], IsAuction = false };
                    db.Add(product);
                    await db.SaveChangesAsync();
                    db.Add(new Inventory { ProductId = product.Id, Quantity = 1_000_000, LastUpdated = DateTime.UtcNow });
                    products[s, p] = (product.Id, price);
                }
            await db.SaveChangesAsync();
        }

        // 3. Orders through checkout -> ship -> receive; then a review + seller feedback, or a return request (90% closed by the buyer).
        var addresses = new int[Buyers + 1];
        for (var b = 1; b <= Buyers; b++)
        {
            await using var db = Open(sql);
            addresses[b] = await new CheckoutService(db).AddAddressAsync(buyers[b], new($"Load Buyer {b:000}", "0123456789", $"{b} Load Street", "Load City", null, "Vietnam"))
                ?? throw new InvalidOperationException("Cannot add a load address.");
        }
        var total = reviews + disputes;
        var done = 0;
        var timing = new DisputeTiming();
        // F05 finding (LOAD.md): concurrent ship/receive deadlocks on range locks (Dispute scan by orderId, Payment/ShippingInfo
        // HOLDLOCK gaps). Writers default to one at a time so the dataset builds; EBAY_LOAD_WRITERS > 1 reproduces the deadlocks.
        var writers = int.TryParse(Environment.GetEnvironmentVariable("EBAY_LOAD_WRITERS"), out var w) && w is >= 1 and <= 16 ? w : 1;
        await Parallel.ForEachAsync(Enumerable.Range(1, Buyers), new ParallelOptions { MaxDegreeOfParallelism = writers }, async (b, ct) =>
        {
            for (var j = b - 1; j < total; j += Buyers)
            {
                // Workers running at the same moment (adjacent buyers) buy from different sellers, so they rarely share an inventory row.
                var s = (j % Buyers + j / Buyers) % Sellers + 1;
                var (product, price) = products[s, (j / (Buyers * Sellers)) % ProductsPerSeller];
                var key = Guid.NewGuid(); // the same key on retry: checkout is idempotent per key
                var order = await Retry("checkout", async () =>
                {
                    await using var db = Open(sql);
                    var placed = await new CheckoutService(db, timing).PlaceAsync(buyers[b], new(product, addresses[b], 1, price, key), ct);
                    return placed.OrderId ?? throw new InvalidOperationException("Checkout failed: " + placed.Error);
                });
                var item = await Retry("ship/receive", async () =>
                {
                    await using var db = Open(sql);
                    var orders = new OrderService(db, TimeProvider.System, timing);
                    var status = await db.Set<OrderTable>().Where(x => x.Id == order).Select(x => x.Status).SingleAsync(ct);
                    if (status == "Pending" && (await orders.ActAsync(sellers[s], OrderAction.Ship, order, ct)).Outcome != OrderOutcome.Success) throw new InvalidOperationException("Ship failed.");
                    if (status != "Delivered" && (await orders.ActAsync(buyers[b], OrderAction.Receive, order, ct)).Outcome != OrderOutcome.Success) throw new InvalidOperationException("Receive failed.");
                    return await db.Set<OrderItem>().Where(x => x.OrderId == order).Select(x => x.Id).SingleAsync(ct);
                });
                await Retry(j < reviews ? "feedback" : "return request", async () =>
                {
                    await using var db = Open(sql);
                    if (j < reviews)
                    {
                        var left = await new FeedbackService(db, TimeProvider.System).SubmitAsync(buyers[b],
                            new(item, FeedbackSentiment.Positive, $"Load seller feedback {j}", 1 + j % 5, $"Load review {j}: as described."), ct);
                        if (left.Outcome != LeaveFeedbackOutcome.Success) throw new InvalidOperationException("Feedback failed: " + left.Error);
                    }
                    else
                    {
                        var service = new DisputeService(db, TimeProvider.System, timing);
                        var opened = await service.OpenAsync(buyers[b], new(item, DisputeKind.Return, DisputeReason.Damaged, null, $"Load return {j}: arrived damaged."), ct);
                        if (opened.Code != DisputeResultCode.Success) throw new InvalidOperationException("Return request failed: " + opened.Error);
                        if (j % 10 != 0)
                        {
                            var closed = await service.ActAsync(buyers[b], opened.DisputeId!.Value, new(DisputeAction.Close, "Resolved with the seller."), ct);
                            if (closed.Code != DisputeResultCode.Success) throw new InvalidOperationException("Closing a request failed: " + closed.Error);
                        }
                    }
                    return 0;
                });
                var count = Interlocked.Increment(ref done);
                if (count % 1000 == 0) Console.WriteLine($"Orders: {count}/{total} ({watch.Elapsed.TotalSeconds:0}s)");
            }
        });

        // 4. One conversation per buyer x seller, opened through the chat service (context = the seller's first product).
        var conversations = new int[Buyers + 1, Sellers + 1];
        await Parallel.ForEachAsync(Enumerable.Range(1, Buyers), new ParallelOptions { MaxDegreeOfParallelism = 8 }, async (b, ct) =>
        {
            for (var s = 1; s <= Sellers; s++)
            {
                conversations[b, s] = await Retry("open conversation", async () =>
                {
                    await using var db = Open(sql);
                    return (await new ChatService(db, NullLogger<ChatService>.Instance).OpenForProductAsync(buyers[b], products[s, 0].Id, ct)).Conversation.Id;
                });
            }
        });

        // 5. Chat history: the "hot" conversation (buyer 1 x seller 1) holds a fifth of all messages, the rest share the remainder.
        var hot = messages / 5;
        var others = Buyers * Sellers - 1;
        var started = DateTime.UtcNow.AddDays(-30);
        var table = MessageTable();
        await using (var connection = new SqlConnection(sql))
        {
            await connection.OpenAsync();
            var index = 0;
            for (var b = 1; b <= Buyers; b++)
                for (var s = 1; s <= Sellers; s++)
                {
                    var count = b == 1 && s == 1 ? hot : (messages - hot) / others + (index++ < (messages - hot) % others ? 1 : 0);
                    var conversation = conversations[b, s];
                    for (var n = 1; n <= count; n++)
                    {
                        var fromBuyer = n % 2 == 1;
                        var content = $"Load message {n} in conversation {conversation}.";
                        var at = started.AddSeconds(30L * n);
                        table.Rows.Add(fromBuyer ? buyers[b] : sellers[s], fromBuyer ? sellers[s] : buyers[b], content, at, conversation, buyers[b], sellers[s],
                            (long)n, Guid.NewGuid(), SHA256.HashData(Encoding.UTF8.GetBytes(content)), at, Guid.NewGuid().ToString("N"));
                    }
                    if (table.Rows.Count >= 20_000) { await Bulk(connection, table); table.Clear(); }
                }
            if (table.Rows.Count > 0) await Bulk(connection, table);
            await using var cursors = connection.CreateCommand();
            cursors.CommandTimeout = 300;
            cursors.CommandText = """
                UPDATE c SET LastSequence=m.LastSequence, BuyerReadSequence=m.LastSequence, SellerReadSequence=m.LastSequence, UpdatedAtUtc=m.LastAt
                FROM dbo.ChatConversation c
                JOIN (SELECT ConversationId, MAX(Sequence) AS LastSequence, MAX(SentAtUtc) AS LastAt FROM dbo.Message WHERE ConversationId IS NOT NULL GROUP BY ConversationId) m
                  ON m.ConversationId=c.Id;
                """;
            await cursors.ExecuteNonQueryAsync();
        }
        Console.WriteLine($"Chat history: {messages} messages ({watch.Elapsed.TotalSeconds:0}s)");

        // 6. Inputs for JMeter (one row per buyer thread) and the realtime client (pair i = buyer i x seller i). Artifacts only.
        var users = new StringBuilder("email,password,conversationId,productId,sellerId\n");
        for (var b = 1; b <= Buyers; b++)
        {
            var s = (b - 1) % Sellers + 1;
            users.Append(CultureInfo.InvariantCulture, $"{BuyerEmail(b)},{password},{conversations[b, s]},{products[s, b % ProductsPerSeller].Id},{sellers[s]}\n");
        }
        await File.WriteAllTextAsync(Path.Combine(output, "jmeter-users.csv"), users.ToString());
        var pairs = new StringBuilder("buyerEmail,sellerEmail,conversationId,buyerId,sellerId\n");
        for (var i = 1; i <= Sellers; i++) pairs.Append(CultureInfo.InvariantCulture, $"{BuyerEmail(i)},{SellerEmail(i)},{conversations[i, i]},{buyers[i]},{sellers[i]}\n");
        await File.WriteAllTextAsync(Path.Combine(output, "chat-pairs.csv"), pairs.ToString());

        Dictionary<string, object> dataset = new() { ["scale"] = scale, ["seedSeconds"] = Math.Round(watch.Elapsed.TotalSeconds), ["writers"] = writers };
        await using (var connection = new SqlConnection(sql))
        {
            await connection.OpenAsync();
            foreach (var (name, query) in new[]
            {
                ("loadUsers", "SELECT COUNT(*) FROM dbo.[User] WHERE email LIKE N'%@LOAD.EXAMPLE.INVALID'"),
                ("orders", "SELECT COUNT(*) FROM dbo.OrderTable"), ("reviews", "SELECT COUNT(*) FROM dbo.Review"),
                ("sellerFeedback", "SELECT COUNT(*) FROM dbo.SellerFeedback"), ("disputes", "SELECT COUNT(*) FROM dbo.Dispute WHERE OrderItemId IS NOT NULL"),
                ("openDisputes", "SELECT COUNT(*) FROM dbo.Dispute WHERE OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL"),
                ("conversations", "SELECT COUNT(*) FROM dbo.ChatConversation"), ("messages", "SELECT COUNT(*) FROM dbo.Message WHERE ConversationId IS NOT NULL"),
                ("hotConversationMessages", $"SELECT COUNT(*) FROM dbo.Message WHERE ConversationId={conversations[1, 1]}"),
            })
            {
                await using var command = connection.CreateCommand();
                command.CommandText = query;
                dataset[name] = Convert.ToInt64(await command.ExecuteScalarAsync(), CultureInfo.InvariantCulture);
            }
        }
        // Deadlock victims retried per service step (F05 finding: concurrent writers deadlock; see LOAD.md).
        dataset["deadlockRetries"] = new SortedDictionary<string, int>(retries);
        await File.WriteAllTextAsync(Path.Combine(output, "dataset.json"), JsonSerializer.Serialize(dataset, new JsonSerializerOptions { WriteIndented = true }));
        Console.WriteLine("Dataset: " + string.Join(", ", dataset.Select(x => $"{x.Key}={(x.Value is IDictionary<string, int> d ? JsonSerializer.Serialize(d) : x.Value)}")));
    }

    /// <summary>Retries a step that lost a deadlock or hit a transient SQL error; every step is safe to repeat.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> retries = new();
    private static async Task<T> Retry<T>(string name, Func<Task<T>> step)
    {
        for (var attempt = 1; ; attempt++)
        {
            try { return await step(); }
            catch (Exception ex) when (Transient(ex))
            {
                if (attempt >= 10) throw new InvalidOperationException($"Step '{name}' still deadlocks after {attempt} attempts.", ex);
                retries.AddOrUpdate(name, 1, (_, n) => n + 1);
                await Task.Delay(50 * attempt + Random.Shared.Next(0, 200));
            }
        }
    }

    public static bool Transient(Exception? ex)
    {
        for (; ex != null; ex = ex.InnerException)
            if (ex is SqlException sql && sql.Errors.Cast<SqlError>().Any(e => e.Number is 1205 or 1222 or -2)) return true;
        return false;
    }

    private static DataTable MessageTable()
    {
        var table = new DataTable();
        foreach (var (name, type) in new (string, Type)[] { ("senderId", typeof(int)), ("receiverId", typeof(int)), ("content", typeof(string)), ("timestamp", typeof(DateTime)),
            ("ConversationId", typeof(int)), ("ConversationBuyerId", typeof(int)), ("ConversationSellerId", typeof(int)), ("Sequence", typeof(long)),
            ("ClientMessageId", typeof(Guid)), ("PayloadHash", typeof(byte[])), ("SentAtUtc", typeof(DateTime)), ("TraceId", typeof(string)) })
            table.Columns.Add(name, type);
        return table;
    }

    private static async Task Bulk(SqlConnection connection, DataTable table)
    {
        // CheckConstraints keeps every 0006 CHECK/FK enforced on the bulk path.
        using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints | SqlBulkCopyOptions.FireTriggers, null)
            { DestinationTableName = "dbo.Message", BatchSize = 5_000, BulkCopyTimeout = 300 };
        foreach (DataColumn column in table.Columns) bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
        await bulk.WriteToServerAsync(table);
    }
}
