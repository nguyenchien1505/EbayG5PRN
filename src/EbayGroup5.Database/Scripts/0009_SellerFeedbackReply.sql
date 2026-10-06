ALTER TABLE dbo.SellerFeedback ADD ReplyText nvarchar(500) NULL, RepliedAtUtc datetime2(7) NULL;
GO
ALTER TABLE dbo.SellerFeedback ADD CONSTRAINT CK_SellerFeedback_Reply CHECK (
    (ReplyText IS NULL AND RepliedAtUtc IS NULL) OR
    (ReplyText IS NOT NULL AND RepliedAtUtc IS NOT NULL AND LEN(LTRIM(RTRIM(ReplyText)))>0));
GO
-- Feedback and a posted reply are permanent for every login; moderation columns added later stay updatable.
CREATE TRIGGER dbo.TR_SellerFeedback_Immutable ON dbo.SellerFeedback AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
        WHERE i.OrderItemId<>d.OrderItemId OR i.BuyerId<>d.BuyerId OR i.SellerId<>d.SellerId
            OR i.Sentiment COLLATE Latin1_General_100_BIN2<>d.Sentiment COLLATE Latin1_General_100_BIN2
            OR ISNULL(i.Comment,N'') COLLATE Latin1_General_100_BIN2<>ISNULL(d.Comment,N'') COLLATE Latin1_General_100_BIN2
            OR (d.Comment IS NULL AND i.Comment IS NOT NULL) OR (d.Comment IS NOT NULL AND i.Comment IS NULL)
            OR i.CreatedAtUtc<>d.CreatedAtUtc
            OR (d.ReplyText IS NOT NULL AND (i.ReplyText IS NULL
                OR i.ReplyText COLLATE Latin1_General_100_BIN2<>d.ReplyText COLLATE Latin1_General_100_BIN2 OR i.RepliedAtUtc<>d.RepliedAtUtc)))
        THROW 51009, 'Seller feedback and replies are immutable.', 1;
END
GO
GRANT UPDATE (ReplyText, RepliedAtUtc) ON OBJECT::dbo.SellerFeedback TO ebay_app;
