using System.Text;
using System.Text.Json;
using EbayGroup5.Application.Chat;
using EbayGroup5.Core.Entities;
using EbayGroup5.Infrastructure.Persistence;
using DbUp.Engine;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace EbayGroup5.Database.Tests;

public sealed class ChatSchemaTests
{
    private sealed record Fixture(string Target, int Buyer, int Seller, int OtherSeller, int Conversation);
    private static Fixture Setup()
    {
        var target = SqlFixture.NewTarget();
        MigrationRunner.Run(target); DemoSeedRunner.Run(target);
        SqlFixture.Execute(target, "INSERT dbo.[User](email,role) VALUES(N'second.seller@c01.example.invalid',N'Seller')");
        var buyer = int.Parse(SqlFixture.Scalar(target,"SELECT id FROM dbo.[User] WHERE role='Buyer'"));
        var seller = int.Parse(SqlFixture.Scalar(target,"SELECT id FROM dbo.[User] WHERE email='seller.a06@example.invalid'"));
        var other = int.Parse(SqlFixture.Scalar(target,"SELECT id FROM dbo.[User] WHERE email='second.seller@c01.example.invalid'"));
        SqlFixture.Execute(target,$"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({buyer},{seller})");
        return new(target,buyer,seller,other,int.Parse(SqlFixture.Scalar(target,"SELECT Id FROM dbo.ChatConversation")));
    }

    // Test-only SQL insert: exercises constraints/provider mapping, not a production send service.
    private static int Insert(Fixture f, long sequence, Guid key, int? sender = null, int? receiver = null, int? copiedBuyer = null,
        string? content = "Schema test message", string? trace = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", byte[]? hash = null, bool missingHash = false)
    {
        using var connection = new SqlConnection(f.Target); connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT dbo.Message(senderId,receiverId,content,[timestamp],ConversationId,ConversationBuyerId,ConversationSellerId,Sequence,ClientMessageId,PayloadHash,SentAtUtc,TraceId)
            OUTPUT inserted.id VALUES(@sender,@receiver,@content,SYSUTCDATETIME(),@conversation,@buyer,@seller,@sequence,@key,@hash,SYSUTCDATETIME(),@trace);
            """;
        command.Parameters.AddWithValue("@sender",sender ?? f.Buyer); command.Parameters.AddWithValue("@receiver",receiver ?? f.Seller);
        command.Parameters.AddWithValue("@content",(object?)content ?? DBNull.Value);
        command.Parameters.AddWithValue("@conversation",f.Conversation); command.Parameters.AddWithValue("@buyer",copiedBuyer ?? f.Buyer);
        command.Parameters.AddWithValue("@seller",f.Seller); command.Parameters.AddWithValue("@sequence",sequence); command.Parameters.AddWithValue("@key",key);
        command.Parameters.Add("@hash",System.Data.SqlDbType.Binary,32).Value = missingHash ? DBNull.Value : hash ?? new byte[32];
        command.Parameters.AddWithValue("@trace",(object?)trace ?? DBNull.Value);
        return Convert.ToInt32(command.ExecuteScalar());
    }

    [Theory]
    [InlineData("outsiderSender")]
    [InlineData("outsiderReceiver")]
    [InlineData("forgedPair")]
    [InlineData("sameSenderReceiver")]
    [InlineData("emptyKey")]
    [InlineData("zeroSequence")]
    [InlineData("nullContent")]
    [InlineData("blankContent")]
    [InlineData("overlongContent")]
    [InlineData("invalidTrace")]
    [InlineData("missingHash")]
    public void Modern_rows_reject_invalid_membership_and_shape(string invalid)
    {
        var f = Setup();
        var exception = Assert.Throws<SqlException>(() => Insert(f, invalid=="zeroSequence" ? 0 : 1,
            invalid=="emptyKey" ? Guid.Empty : Guid.NewGuid(),
            sender:invalid is "outsiderSender" or "forgedPair" ? f.OtherSeller : f.Buyer,
            receiver:invalid=="outsiderReceiver" ? f.OtherSeller : invalid=="sameSenderReceiver" ? f.Buyer : f.Seller,
            copiedBuyer:invalid=="forgedPair" ? f.OtherSeller : f.Buyer,
            content:invalid switch { "nullContent"=>null, "blankContent"=>"  ", "overlongContent"=>new string('x',4001), _=>"Schema test message" },
            trace:invalid=="invalidTrace" ? new string('g',32) : new string('a',32), missingHash:invalid=="missingHash"));
        Assert.Equal(547,exception.Number);
        if(invalid=="forgedPair") Assert.Contains("FK_Message_ConversationMembership",exception.Message);
        Assert.Equal("0",SqlFixture.Scalar(f.Target,"SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public void Conversation_pair_and_read_cursors_are_constrained()
    {
        var f = Setup();
        Assert.Contains(Assert.Throws<SqlException>(()=>SqlFixture.Execute(f.Target,$"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({f.Buyer},{f.Seller})")).Number,new[]{2601,2627});
        Assert.Equal(547,Assert.Throws<SqlException>(()=>SqlFixture.Execute(f.Target,$"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({f.Buyer},{f.Buyer})")).Number);
        Assert.Equal(547,Assert.Throws<SqlException>(()=>SqlFixture.Execute(f.Target,$"UPDATE dbo.ChatConversation SET BuyerReadSequence=1 WHERE Id={f.Conversation}")).Number);
        Assert.Equal(547,Assert.Throws<SqlException>(()=>SqlFixture.Execute(f.Target,$"UPDATE dbo.ChatConversation SET SellerReadSequence=-1 WHERE Id={f.Conversation}")).Number);
        Assert.Equal(547,Assert.Throws<SqlException>(()=>SqlFixture.Execute(f.Target,$"UPDATE dbo.ChatConversation SET ContextTitle=N'Partial' WHERE Id={f.Conversation}")).Number);
        SqlFixture.Execute(f.Target,$"UPDATE dbo.ChatConversation SET LastSequence=2, BuyerReadSequence=1, SellerReadSequence=2 WHERE Id={f.Conversation}");
        Assert.Equal("1|2",SqlFixture.Scalar(f.Target,$"SELECT CONCAT(BuyerReadSequence,'|',SellerReadSequence) FROM dbo.ChatConversation WHERE Id={f.Conversation}"));
    }

    [Fact]
    public void Unique_sequence_and_client_key_scope_allow_distinct_senders_and_conversations()
    {
        var f = Setup(); var key = Guid.NewGuid();
        Insert(f,1,key);
        Assert.Contains(Assert.Throws<SqlException>(()=>Insert(f,1,Guid.NewGuid())).Number,new[]{2601,2627});
        Assert.Contains(Assert.Throws<SqlException>(()=>Insert(f,2,key)).Number,new[]{2601,2627});
        Insert(f,2,key,sender:f.Seller,receiver:f.Buyer);
        SqlFixture.Execute(f.Target,$"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({f.Buyer},{f.OtherSeller})");
        var other = f with { Seller=f.OtherSeller, Conversation=int.Parse(SqlFixture.Scalar(f.Target,"SELECT MAX(Id) FROM dbo.ChatConversation")) };
        Insert(other,1,key);
        Assert.Equal("3",SqlFixture.Scalar(f.Target,"SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public async Task Mapping_roundtrips_max_utf16_text_context_precise_utc_and_big_sequence()
    {
        var f = Setup();
        await using var db = new EbayDbContext(new DbContextOptionsBuilder<EbayDbContext>().UseSqlServer(f.Target).Options);
        var conversation = await db.Set<ChatConversation>().SingleAsync();
        Assert.Equal(0,conversation.LastSequence); Assert.Equal(0,conversation.BuyerReadSequence);
        var item = await db.Set<OrderItem>().OrderBy(x=>x.Id).FirstAsync();
        var now = new DateTime(2026,10,6,1,2,3,DateTimeKind.Utc).AddTicks(1234567);
        var message = new Message { ConversationId=f.Conversation, ConversationBuyerId=f.Buyer, ConversationSellerId=f.Seller,
            SenderId=f.Buyer,ReceiverId=f.Seller,Content=string.Concat(Enumerable.Repeat("😀",2000)), Timestamp=now,SentAtUtc=now,
            Sequence=9007199254740993, ClientMessageId=Guid.NewGuid(),PayloadHash=Enumerable.Repeat((byte)42,32).ToArray(),TraceId=new string('b',32),
            ContextProductId=item.ProductId,ContextOrderItemId=item.Id,ContextTitle="Snapshot title",ContextUnitPrice=item.UnitPrice };
        conversation.LastSequence=message.Sequence.Value; conversation.UpdatedAtUtc=now;
        db.Add(message); await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var loaded = await db.Set<Message>().SingleAsync();
        Assert.Equal(4000,loaded.Content!.Length); Assert.Equal(message.Sequence,loaded.Sequence); Assert.Equal(now.Ticks,loaded.SentAtUtc!.Value.Ticks);
        Assert.Equal(message.PayloadHash,loaded.PayloadHash); Assert.Equal(item.Id,loaded.ContextOrderItemId); Assert.Equal("Snapshot title",loaded.ContextTitle);
        Assert.Equal("binary(32)",db.Model.FindEntityType(typeof(Message))!.FindProperty(nameof(Message.PayloadHash))!.GetColumnType());
        Assert.Equal("datetime2(7)",db.Model.FindEntityType(typeof(Message))!.FindProperty(nameof(Message.SentAtUtc))!.GetColumnType());
    }

    [Fact]
    public void Upgrade_preserves_legacy_messages_without_inventing_membership_or_importing_them()
    {
        var target=SqlFixture.NewTarget(); SqlFixture.CreateEmpty(target);
        var scripts=new List<SqlScript>{new("0001_Baseline.sql",Baseline.ReadTableDdl())}; var assembly=typeof(MigrationRunner).Assembly;
        foreach(var resource in assembly.GetManifestResourceNames().Where(x=>x.Contains(".Scripts.")&&!x.EndsWith("0006_ChatConversation.sql")).Order())
        {
            using var reader=new StreamReader(assembly.GetManifestResourceStream(resource)!);
            scripts.Add(new(resource[(resource.IndexOf(".Scripts.",StringComparison.Ordinal)+9)..],reader.ReadToEnd()));
        }
        Assert.True(MigrationRunner.BuildEngine(target,scripts).PerformUpgrade().Successful); DemoSeedRunner.Run(target);
        SqlFixture.Execute(target,"INSERT dbo.Message(senderId,receiverId,content,[timestamp]) VALUES(1,2,N'legacy message','20260901'),(3,1,NULL,NULL),(1,1,REPLICATE(CAST(N'x' AS nvarchar(max)),5000),NULL),(NULL,NULL,N'  ',NULL)");
        var metadata=SqlFixture.Rows(target,SqlFixture.Metadata);
        var original=SqlFixture.Rows(target,"SELECT CONCAT(id,'|',senderId,'|',receiverId,'|',content,'|',CONVERT(nvarchar(30),[timestamp],126)) FROM dbo.Message");
        Assert.Equal(1,MigrationRunner.Run(target));
        Assert.True(metadata.IsSubsetOf(SqlFixture.Rows(target,SqlFixture.Metadata)));
        Assert.True(original.SetEquals(SqlFixture.Rows(target,"SELECT CONCAT(id,'|',senderId,'|',receiverId,'|',content,'|',CONVERT(nvarchar(30),[timestamp],126)) FROM dbo.Message")));
        Assert.Equal("4",SqlFixture.Scalar(target,"SELECT COUNT(*) FROM dbo.Message WHERE ConversationId IS NULL AND ClientMessageId IS NULL AND SentAtUtc IS NULL"));
        Assert.Equal("0",SqlFixture.Scalar(target,"SELECT COUNT(*) FROM dbo.ChatConversation"));
        Assert.Equal(0,MigrationRunner.Run(target)); Assert.False(DemoSeedRunner.Run(target));
        SqlFixture.Execute(target,"INSERT dbo.Message(content) VALUES(N'new legacy-compatible row')");
        Assert.Equal("5",SqlFixture.Scalar(target,"SELECT COUNT(*) FROM dbo.Message WHERE ConversationId IS NULL"));
    }

    [Fact]
    public void Cursor_pages_use_exclusive_sequences_and_a_captured_high_watermark()
    {
        var f=Setup();
        SqlFixture.Execute(f.Target,$"""
            BEGIN TRANSACTION;
            DECLARE @i int=1;
            WHILE @i<=125
            BEGIN
                INSERT dbo.Message(senderId,receiverId,content,[timestamp],ConversationId,ConversationBuyerId,ConversationSellerId,Sequence,ClientMessageId,PayloadHash,SentAtUtc,TraceId)
                VALUES({f.Buyer},{f.Seller},CONCAT(N'message ',@i),'20261001',{f.Conversation},{f.Buyer},{f.Seller},@i,NEWID(),HASHBYTES('SHA2_256',CONCAT('-',CHAR(10),'-',CHAR(10),'message ',@i)),'20261001','aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
                SET @i+=1;
            END;
            UPDATE dbo.ChatConversation SET LastSequence=125 WHERE Id={f.Conversation};
            COMMIT;
            """);
        var newest=SqlFixture.Rows(f.Target,$"SELECT CONVERT(varchar(20),Sequence) FROM (SELECT TOP(50) Sequence FROM dbo.Message WHERE ConversationId={f.Conversation} AND Sequence<=125 ORDER BY Sequence DESC) p");
        Assert.True(Enumerable.Range(76,50).Select(x=>x.ToString()).ToHashSet().SetEquals(newest));
        var before=SqlFixture.Rows(f.Target,$"SELECT CONVERT(varchar(20),Sequence) FROM (SELECT TOP(50) Sequence FROM dbo.Message WHERE ConversationId={f.Conversation} AND Sequence<76 AND Sequence<=125 ORDER BY Sequence DESC) p");
        Assert.True(Enumerable.Range(26,50).Select(x=>x.ToString()).ToHashSet().SetEquals(before));
        var after=SqlFixture.Rows(f.Target,$"SELECT CONVERT(varchar(20),Sequence) FROM (SELECT TOP(100) Sequence FROM dbo.Message WHERE ConversationId={f.Conversation} AND Sequence>50 AND Sequence<=125 ORDER BY Sequence ASC) p");
        Assert.True(Enumerable.Range(51,75).Select(x=>x.ToString()).ToHashSet().SetEquals(after));
        Insert(f,126,Guid.NewGuid()); SqlFixture.Execute(f.Target,$"UPDATE dbo.ChatConversation SET LastSequence=126 WHERE Id={f.Conversation}");
        Assert.Equal("125",SqlFixture.Scalar(f.Target,$"SELECT MAX(Sequence) FROM dbo.Message WHERE ConversationId={f.Conversation} AND Sequence<=125"));
        Assert.Equal("126",SqlFixture.Scalar(f.Target,$"SELECT MIN(Sequence) FROM dbo.Message WHERE ConversationId={f.Conversation} AND Sequence>125"));
        Assert.Equal("1",SqlFixture.Scalar(f.Target,"SELECT COUNT(*) FROM sys.indexes WHERE name='UX_Message_ChatSequence' AND is_unique=1 AND has_filter=1"));
    }

    // Transaction prototype in tests fixes the C02 persistence algorithm without adding a runtime endpoint/service.
    private static async Task<long> PrototypeSend(Fixture f,Guid key,bool fail=false)
    {
        await using var connection=new SqlConnection(f.Target); await connection.OpenAsync();
        await using var transaction=await connection.BeginTransactionAsync();
        await using var command=connection.CreateCommand(); command.Transaction=(SqlTransaction)transaction;
        command.CommandText="""
            DECLARE @sequence bigint;
            SELECT @sequence=LastSequence FROM dbo.ChatConversation WITH(UPDLOCK,HOLDLOCK) WHERE Id=@conversation;
            IF EXISTS(SELECT 1 FROM dbo.Message WHERE ConversationId=@conversation AND senderId=@buyer AND ClientMessageId=@key)
                SELECT Sequence FROM dbo.Message WHERE ConversationId=@conversation AND senderId=@buyer AND ClientMessageId=@key;
            ELSE
            BEGIN
                SET @sequence+=1;
                INSERT dbo.Message(senderId,receiverId,content,[timestamp],ConversationId,ConversationBuyerId,ConversationSellerId,Sequence,ClientMessageId,PayloadHash,SentAtUtc,TraceId)
                VALUES(@buyer,@seller,N'prototype message',SYSUTCDATETIME(),@conversation,@buyer,@seller,@sequence,@key,HASHBYTES('SHA2_256',CONCAT('-',CHAR(10),'-',CHAR(10),'prototype message')),SYSUTCDATETIME(),'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa');
                UPDATE dbo.ChatConversation SET LastSequence=@sequence,UpdatedAtUtc=SYSUTCDATETIME() WHERE Id=@conversation;
                SELECT @sequence;
            END
            """;
        command.Parameters.AddWithValue("@conversation",f.Conversation); command.Parameters.AddWithValue("@buyer",f.Buyer); command.Parameters.AddWithValue("@seller",f.Seller); command.Parameters.AddWithValue("@key",key);
        var sequence=Convert.ToInt64(await command.ExecuteScalarAsync());
        if(fail) { await transaction.RollbackAsync(); throw new InvalidOperationException("Isolated prototype rollback"); }
        await transaction.CommitAsync(); return sequence;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Conversation_lock_prototype_serializes_writes_and_duplicate_keys(bool duplicate)
    {
        var f=Setup(); var key=Guid.NewGuid();
        var sequences=await Task.WhenAll(PrototypeSend(f,key),PrototypeSend(f,duplicate?key:Guid.NewGuid()));
        Assert.Equal(duplicate?new long[]{1,1}:new long[]{1,2},sequences.Order().ToArray());
        Assert.Equal(duplicate?"1":"2",SqlFixture.Scalar(f.Target,$"SELECT LastSequence FROM dbo.ChatConversation WHERE Id={f.Conversation}"));
        Assert.Equal(duplicate?"1":"2",SqlFixture.Scalar(f.Target,"SELECT COUNT(*) FROM dbo.Message"));
    }

    [Fact]
    public async Task Prototype_rollback_does_not_consume_sequence_or_client_key()
    {
        var f=Setup(); var key=Guid.NewGuid();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>PrototypeSend(f,key,true));
        Assert.Equal("0",SqlFixture.Scalar(f.Target,$"SELECT LastSequence FROM dbo.ChatConversation WHERE Id={f.Conversation}"));
        Assert.Equal("0",SqlFixture.Scalar(f.Target,"SELECT COUNT(*) FROM dbo.Message"));
        Assert.Equal(1,await PrototypeSend(f,key));
    }

    [Fact]
    public void Runtime_login_can_use_new_table_without_ddl_or_journal_or_delete_grants()
    {
        var f=Setup(); var login="EbayGroup5_TestApp_"+Guid.NewGuid().ToString("N");
        var password="Eg5!"+Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(24));
        AppAccess.Provision(f.Target,login,password);
        var app=new SqlConnectionStringBuilder(f.Target){UserID=login,Password=password,IntegratedSecurity=false}.ConnectionString;
        SqlFixture.Execute(app,$"INSERT dbo.ChatConversation(BuyerId,SellerId) VALUES({f.Buyer},{f.OtherSeller}); UPDATE dbo.ChatConversation SET BuyerReadSequence=0 WHERE Id={f.Conversation}");
        Assert.Equal("2",SqlFixture.Scalar(app,"SELECT COUNT(*) FROM dbo.ChatConversation"));
        Assert.Throws<SqlException>(()=>SqlFixture.Execute(app,"DELETE dbo.ChatConversation"));
        Assert.Throws<SqlException>(()=>SqlFixture.Execute(app,"ALTER TABLE dbo.ChatConversation ADD Forbidden int NULL"));
        Assert.Throws<SqlException>(()=>SqlFixture.Execute(app,"UPDATE dbo.SchemaVersions SET ScriptName=ScriptName"));
    }

    [Fact]
    public void Wire_cursor_preserves_bigint_without_javascript_number_rounding()
    {
        foreach(var cursor in new[]{"0","9007199254740993",long.MaxValue.ToString(System.Globalization.CultureInfo.InvariantCulture)})
        { Assert.True(ChatCursor.TryParse(cursor,out var sequence)); Assert.Equal(cursor,ChatCursor.Format(sequence)); }
        foreach(var invalid in new string?[]{null,"","01","+1","-1"," 1","1 ","1.0","1e2","9223372036854775808","١"}) Assert.False(ChatCursor.TryParse(invalid,out _));
        Assert.Throws<ArgumentOutOfRangeException>(()=>ChatCursor.Format(-1));
        var dto=new ChatMessageDto(1,1,"9007199254740993",Guid.NewGuid(),1,2,"Wire test",DateTimeOffset.UtcNow,new string('a',32),null);
        var json=JsonSerializer.Serialize(dto,new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Contains("\"sequence\":\"9007199254740993\"",json);
        Assert.Equal(dto.Sequence,JsonSerializer.Deserialize<ChatMessageDto>(json,new JsonSerializerOptions(JsonSerializerDefaults.Web))!.Sequence);
    }
}
