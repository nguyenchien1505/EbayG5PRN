using EbayGroup5.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace EbayGroup5.Infrastructure.Persistence;

public sealed class EbayDbContext(DbContextOptions<EbayDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("User", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.Username).HasColumnName("username").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.Email).HasColumnName("email").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.HasIndex(x => x.Email).IsUnique().HasFilter(null);
            entity.Property(x => x.Password).HasColumnName("password").HasColumnType("nvarchar(255)").HasMaxLength(255);
            entity.Property(x => x.Role).HasColumnName("role").HasColumnType("nvarchar(20)").HasMaxLength(20);
            entity.Property(x => x.AvatarURL).HasColumnName("avatarURL").HasColumnType("nvarchar(max)");
        });
        modelBuilder.Entity<Address>(entity =>
        {
            entity.ToTable("Address", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.UserId).HasColumnName("userId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.FullName).HasColumnName("fullName").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.Phone).HasColumnName("phone").HasColumnType("nvarchar(20)").HasMaxLength(20);
            entity.Property(x => x.Street).HasColumnName("street").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.City).HasColumnName("city").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.State).HasColumnName("state").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.Country).HasColumnName("country").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.IsDefault).HasColumnName("isDefault").HasColumnType("bit");
        });
        modelBuilder.Entity<Category>(entity =>
        {
            entity.ToTable("Category", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.Name).HasColumnName("name").HasColumnType("nvarchar(100)").HasMaxLength(100);
        });
        modelBuilder.Entity<Product>(entity =>
        {
            entity.ToTable("Product", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.Title).HasColumnName("title").HasColumnType("nvarchar(255)").HasMaxLength(255);
            entity.Property(x => x.Description).HasColumnName("description").HasColumnType("nvarchar(max)");
            entity.Property(x => x.Price).HasColumnName("price").HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.Images).HasColumnName("images").HasColumnType("nvarchar(max)");
            entity.Property(x => x.CategoryId).HasColumnName("categoryId").HasColumnType("int");
            entity.HasOne<Category>().WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.SellerId).HasColumnName("sellerId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.IsAuction).HasColumnName("isAuction").HasColumnType("bit");
            entity.Property(x => x.AuctionEndTime).HasColumnName("auctionEndTime").HasColumnType("datetime");
        });
        modelBuilder.Entity<OrderTable>(entity =>
        {
            entity.ToTable("OrderTable", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.BuyerId).HasColumnName("buyerId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.SellerId).HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.AddressId).HasColumnName("addressId").HasColumnType("int");
            entity.HasOne<Address>().WithMany().HasForeignKey(x => x.AddressId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.OrderDate).HasColumnName("orderDate").HasColumnType("datetime");
            entity.Property(x => x.TotalPrice).HasColumnName("totalPrice").HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.CheckoutKey).HasColumnType("uniqueidentifier");
            entity.Property(x => x.CheckoutFingerprint).HasColumnType("varchar(64)").HasMaxLength(64).IsUnicode(false);
            entity.HasIndex(x => new { x.BuyerId, x.CheckoutKey }).HasDatabaseName("UX_OrderTable_Buyer_CheckoutKey").IsUnique().HasFilter("[CheckoutKey] IS NOT NULL");
            entity.Property(x => x.EstimatedDeliveryUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("nvarchar(20)").HasMaxLength(20);
        });
        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.ToTable("OrderItem", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderId).HasColumnName("orderId").HasColumnType("int");
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ProductId).HasColumnName("productId").HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("int");
            entity.Property(x => x.UnitPrice).HasColumnName("unitPrice").HasColumnType("decimal(10,2)").HasPrecision(10, 2);
        });
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.ToTable("Payment", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderId).HasColumnName("orderId").HasColumnType("int");
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.UserId).HasColumnName("userId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.Method).HasColumnName("method").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("nvarchar(20)").HasMaxLength(20);
            entity.Property(x => x.PaidAt).HasColumnName("paidAt").HasColumnType("datetime");
        });
        modelBuilder.Entity<ShippingInfo>(entity =>
        {
            entity.ToTable("ShippingInfo", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderId).HasColumnName("orderId").HasColumnType("int");
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Carrier).HasColumnName("carrier").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.TrackingNumber).HasColumnName("trackingNumber").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.EstimatedArrival).HasColumnName("estimatedArrival").HasColumnType("datetime");
        });
        modelBuilder.Entity<ReturnRequest>(entity =>
        {
            entity.ToTable("ReturnRequest", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderId).HasColumnName("orderId").HasColumnType("int");
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.UserId).HasColumnName("userId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Reason).HasColumnName("reason").HasColumnType("nvarchar(max)");
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("nvarchar(20)").HasMaxLength(20);
            entity.Property(x => x.CreatedAt).HasColumnName("createdAt").HasColumnType("datetime");
        });
        modelBuilder.Entity<Bid>(entity =>
        {
            entity.ToTable("Bid", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.ProductId).HasColumnName("productId").HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.BidderId).HasColumnName("bidderId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BidderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Amount).HasColumnName("amount").HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.BidTime).HasColumnName("bidTime").HasColumnType("datetime");
        });
        modelBuilder.Entity<Review>(entity =>
        {
            entity.ToTable("Review", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.ProductId).HasColumnName("productId").HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ReviewerId).HasColumnName("reviewerId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ReviewerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.OrderItemId).HasColumnType("int");
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasIndex(x => x.OrderItemId).IsUnique().HasFilter("[OrderItemId] IS NOT NULL").HasDatabaseName("UX_Review_OrderItem");
            entity.Property(x => x.Rating).HasColumnName("rating").HasColumnType("int");
            entity.Property(x => x.Comment).HasColumnName("comment").HasColumnType("nvarchar(max)");
            entity.Property(x => x.CreatedAt).HasColumnName("createdAt").HasColumnType("datetime");
            entity.Property(x => x.HiddenAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.CommentRemovedAtUtc).HasColumnType("datetime2(7)");
            entity.ToTable(t => t.HasTrigger("TR_Review_NoDelete"));
        });
        modelBuilder.Entity<SellerFeedback>(entity =>
        {
            entity.ToTable("SellerFeedback", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderItemId).HasColumnType("int");
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasIndex(x => x.OrderItemId).IsUnique().HasDatabaseName("UQ_SellerFeedback_OrderItem");
            entity.Property(x => x.BuyerId).HasColumnType("int");
            entity.Property(x => x.SellerId).HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Sentiment).HasColumnType("varchar(8)").HasMaxLength(8).IsUnicode(false);
            entity.Property(x => x.Comment).HasColumnType("nvarchar(500)").HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.ReplyText).HasColumnType("nvarchar(500)").HasMaxLength(500);
            entity.Property(x => x.RepliedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.HiddenAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.CommentRemovedAtUtc).HasColumnType("datetime2(7)");
            entity.HasIndex(x => new { x.SellerId, x.Id }).IsDescending(false, true).HasDatabaseName("IX_SellerFeedback_Seller");
            entity.ToTable(t => t.HasTrigger("TR_SellerFeedback_Immutable"));
        });
        modelBuilder.Entity<ContentReport>(entity =>
        {
            entity.ToTable("ContentReport", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").UseIdentityColumn();
            entity.HasOne<Review>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<SellerFeedback>().WithMany().HasForeignKey(x => x.SellerFeedbackId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ReporterId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ResolvedById).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Reason).HasColumnType("varchar(12)").HasMaxLength(12).IsUnicode(false);
            entity.Property(x => x.Details).HasColumnType("nvarchar(500)").HasMaxLength(500);
            entity.Property(x => x.Status).HasColumnType("varchar(10)").HasMaxLength(10).IsUnicode(false).HasDefaultValue("Open");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.ResolvedAtUtc).HasColumnType("datetime2(7)");
            entity.HasIndex(x => new { x.ReviewId, x.ReporterId }).IsUnique().HasFilter("[ReviewId] IS NOT NULL").HasDatabaseName("UX_ContentReport_Review_Reporter");
            entity.HasIndex(x => new { x.SellerFeedbackId, x.ReporterId }).IsUnique().HasFilter("[SellerFeedbackId] IS NOT NULL").HasDatabaseName("UX_ContentReport_Feedback_Reporter");
        });
        modelBuilder.Entity<ModerationAudit>(entity =>
        {
            entity.ToTable("ModerationAudit", "dbo", t => t.HasTrigger("TR_ModerationAudit_AppendOnly"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").UseIdentityColumn();
            entity.HasOne<Review>().WithMany().HasForeignKey(x => x.ReviewId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<SellerFeedback>().WithMany().HasForeignKey(x => x.SellerFeedbackId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.AdminId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Action).HasColumnType("varchar(14)").HasMaxLength(14).IsUnicode(false);
            entity.Property(x => x.Note).HasColumnType("nvarchar(500)").HasMaxLength(500);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
        });
        modelBuilder.Entity<ChatConversation>(entity =>
        {
            entity.ToTable("ChatConversation", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.BuyerId).HasColumnType("int");
            entity.Property(x => x.SellerId).HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.BuyerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasIndex(x => new { x.BuyerId, x.SellerId }).IsUnique().HasDatabaseName("UQ_ChatConversation_Pair");
            entity.HasAlternateKey(x => new { x.Id, x.BuyerId, x.SellerId }).HasName("UQ_ChatConversation_Membership");
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.UpdatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
            entity.Property(x => x.LastSequence).HasColumnType("bigint").HasDefaultValue(0L);
            entity.Property(x => x.BuyerReadSequence).HasColumnType("bigint").HasDefaultValue(0L);
            entity.Property(x => x.SellerReadSequence).HasColumnType("bigint").HasDefaultValue(0L);
            entity.Property(x => x.ContextProductId).HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ContextProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ContextOrderItemId).HasColumnType("int");
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.ContextOrderItemId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ContextTitle).HasColumnType("nvarchar(255)").HasMaxLength(255);
            entity.Property(x => x.ContextUnitPrice).HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.HasIndex(x => new { x.BuyerId, x.UpdatedAtUtc, x.Id }).IsDescending(false, true, true).HasDatabaseName("IX_ChatConversation_Buyer_Inbox");
            entity.HasIndex(x => new { x.SellerId, x.UpdatedAtUtc, x.Id }).IsDescending(false, true, true).HasDatabaseName("IX_ChatConversation_Seller_Inbox");
        });
        modelBuilder.Entity<Message>(entity =>
        {
            entity.ToTable("Message", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.SenderId).HasColumnName("senderId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SenderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ReceiverId).HasColumnName("receiverId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ReceiverId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Content).HasColumnName("content").HasColumnType("nvarchar(max)");
            entity.Property(x => x.Timestamp).HasColumnName("timestamp").HasColumnType("datetime");
            entity.Property(x => x.ConversationId).HasColumnType("int");
            entity.Property(x => x.ConversationBuyerId).HasColumnType("int");
            entity.Property(x => x.ConversationSellerId).HasColumnType("int");
            entity.HasOne<ChatConversation>().WithMany().HasForeignKey(x => new { x.ConversationId, x.ConversationBuyerId, x.ConversationSellerId })
                .HasPrincipalKey(x => new { x.Id, x.BuyerId, x.SellerId }).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Sequence).HasColumnType("bigint");
            entity.Property(x => x.ClientMessageId).HasColumnType("uniqueidentifier");
            entity.Property(x => x.PayloadHash).HasColumnType("binary(32)").HasMaxLength(32).IsFixedLength();
            entity.Property(x => x.SentAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.TraceId).HasColumnType("varchar(32)").HasMaxLength(32).IsUnicode(false);
            entity.Property(x => x.ContextProductId).HasColumnType("int");
            entity.Property(x => x.ContextOrderItemId).HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ContextProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.ContextOrderItemId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ContextTitle).HasColumnType("nvarchar(255)").HasMaxLength(255);
            entity.Property(x => x.ContextUnitPrice).HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.HasIndex(x => new { x.ConversationId, x.Sequence }).IsUnique().HasFilter("[ConversationId] IS NOT NULL").HasDatabaseName("UX_Message_ChatSequence");
            entity.HasIndex(x => new { x.ConversationId, x.SenderId, x.ClientMessageId }).IsUnique().HasFilter("[ConversationId] IS NOT NULL").HasDatabaseName("UX_Message_ClientMessage");
            entity.HasIndex(x => new { x.TraceId, x.SentAtUtc, x.Id }).HasFilter("[TraceId] IS NOT NULL").HasDatabaseName("IX_Message_Trace");
        });
        modelBuilder.Entity<Coupon>(entity =>
        {
            entity.ToTable("Coupon", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.Code).HasColumnName("code").HasColumnType("nvarchar(50)").HasMaxLength(50);
            entity.Property(x => x.DiscountPercent).HasColumnName("discountPercent").HasColumnType("decimal(5,2)").HasPrecision(5, 2);
            entity.Property(x => x.StartDate).HasColumnName("startDate").HasColumnType("datetime");
            entity.Property(x => x.EndDate).HasColumnName("endDate").HasColumnType("datetime");
            entity.Property(x => x.MaxUsage).HasColumnName("maxUsage").HasColumnType("int");
            entity.Property(x => x.ProductId).HasColumnName("productId").HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientNoAction);
        });
        modelBuilder.Entity<Inventory>(entity =>
        {
            entity.ToTable("Inventory", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.ProductId).HasColumnName("productId").HasColumnType("int");
            entity.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Quantity).HasColumnName("quantity").HasColumnType("int");
            entity.Property(x => x.LastUpdated).HasColumnName("lastUpdated").HasColumnType("datetime");
        });
        modelBuilder.Entity<Feedback>(entity =>
        {
            entity.ToTable("Feedback", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.SellerId).HasColumnName("sellerId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.AverageRating).HasColumnName("averageRating").HasColumnType("decimal(3,2)").HasPrecision(3, 2);
            entity.Property(x => x.TotalReviews).HasColumnName("totalReviews").HasColumnType("int");
            entity.Property(x => x.PositiveRate).HasColumnName("positiveRate").HasColumnType("decimal(5,2)").HasPrecision(5, 2);
        });
        modelBuilder.Entity<Dispute>(entity =>
        {
            entity.ToTable("Dispute", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.OrderId).HasColumnName("orderId").HasColumnType("int");
            entity.HasOne<OrderTable>().WithMany().HasForeignKey(x => x.OrderId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.RaisedBy).HasColumnName("raisedBy").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.RaisedBy).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Description).HasColumnName("description").HasColumnType("nvarchar(max)");
            entity.Property(x => x.Status).HasColumnName("status").HasColumnType("nvarchar(20)").HasMaxLength(20);
            entity.Property(x => x.Resolution).HasColumnName("resolution").HasColumnType("nvarchar(max)");
            // E01 extension columns (DISPUTES.md).
            entity.Property(x => x.OrderItemId).HasColumnType("int");
            // SQL also enforces the composite FK_Dispute_OrderItemOrder (line belongs to orderId); EF maps the single-column link like Review.
            entity.HasOne<OrderItem>().WithMany().HasForeignKey(x => x.OrderItemId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.SellerId).HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.Kind).HasColumnType("varchar(16)").HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Reason).HasColumnType("varchar(16)").HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Preference).HasColumnType("varchar(6)").HasMaxLength(6).IsUnicode(false);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.DueAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.SellerRespondedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.EscalatedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.ClosedAtUtc).HasColumnType("datetime2(7)");
            entity.Property(x => x.Outcome).HasColumnType("varchar(16)").HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.OfferAmount).HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.RefundAmount).HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.ReturnTracking).HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.HasIndex(x => x.OrderItemId).IsUnique().HasFilter("[OrderItemId] IS NOT NULL AND [ClosedAtUtc] IS NULL").HasDatabaseName("UX_Dispute_ActiveLine");
            entity.ToTable(t => t.HasTrigger("TR_Dispute_Guard"));
        });
        modelBuilder.Entity<DisputeEvent>(entity =>
        {
            entity.ToTable("DisputeEvent", "dbo", t => t.HasTrigger("TR_DisputeEvent_AppendOnly"));
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnType("int").UseIdentityColumn();
            entity.HasOne<Dispute>().WithMany().HasForeignKey(x => x.DisputeId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.ActorId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.ActorRole).HasColumnType("varchar(6)").HasMaxLength(6).IsUnicode(false);
            entity.Property(x => x.Type).HasColumnType("varchar(16)").HasMaxLength(16).IsUnicode(false);
            entity.Property(x => x.Message).HasColumnType("nvarchar(2000)").HasMaxLength(2000);
            entity.Property(x => x.Amount).HasColumnType("decimal(10,2)").HasPrecision(10, 2);
            entity.Property(x => x.Tracking).HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.CreatedAtUtc).HasColumnType("datetime2(7)").HasDefaultValueSql("SYSUTCDATETIME()");
        });
        modelBuilder.Entity<Store>(entity =>
        {
            entity.ToTable("Store", "dbo");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Id).HasColumnName("id").HasColumnType("int").UseIdentityColumn();
            entity.Property(x => x.SellerId).HasColumnName("sellerId").HasColumnType("int");
            entity.HasOne<User>().WithMany().HasForeignKey(x => x.SellerId).OnDelete(DeleteBehavior.ClientNoAction);
            entity.Property(x => x.StoreName).HasColumnName("storeName").HasColumnType("nvarchar(100)").HasMaxLength(100);
            entity.Property(x => x.Description).HasColumnName("description").HasColumnType("nvarchar(max)");
            entity.Property(x => x.BannerImageURL).HasColumnName("bannerImageURL").HasColumnType("nvarchar(max)");
        });
    }
}
