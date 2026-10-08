-- H01: eBay R1 feedback details. "Did this item arrive on time?" and three buyer-rated detailed seller ratings, all optional.
ALTER TABLE dbo.SellerFeedback ADD
    ArrivedOnTime bit NULL,
    DescriptionRating tinyint NULL,
    ShippingTimeRating tinyint NULL,
    CommunicationRating tinyint NULL;
GO
ALTER TABLE dbo.SellerFeedback ADD CONSTRAINT CK_SellerFeedback_DetailRatings CHECK (
    (DescriptionRating IS NULL OR DescriptionRating BETWEEN 1 AND 5) AND
    (ShippingTimeRating IS NULL OR ShippingTimeRating BETWEEN 1 AND 5) AND
    (CommunicationRating IS NULL OR CommunicationRating BETWEEN 1 AND 5));
GO
-- Same rule as 0009, extended to the new columns: the buyer's submission never changes after insert.
ALTER TRIGGER dbo.TR_SellerFeedback_Immutable ON dbo.SellerFeedback AFTER UPDATE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM inserted i JOIN deleted d ON d.Id=i.Id
        WHERE i.OrderItemId<>d.OrderItemId OR i.BuyerId<>d.BuyerId OR i.SellerId<>d.SellerId
            OR i.Sentiment COLLATE Latin1_General_100_BIN2<>d.Sentiment COLLATE Latin1_General_100_BIN2
            OR ISNULL(i.Comment,N'') COLLATE Latin1_General_100_BIN2<>ISNULL(d.Comment,N'') COLLATE Latin1_General_100_BIN2
            OR (d.Comment IS NULL AND i.Comment IS NOT NULL) OR (d.Comment IS NOT NULL AND i.Comment IS NULL)
            OR i.CreatedAtUtc<>d.CreatedAtUtc
            OR ISNULL(CAST(i.ArrivedOnTime AS tinyint),2)<>ISNULL(CAST(d.ArrivedOnTime AS tinyint),2)
            OR ISNULL(i.DescriptionRating,0)<>ISNULL(d.DescriptionRating,0)
            OR ISNULL(i.ShippingTimeRating,0)<>ISNULL(d.ShippingTimeRating,0)
            OR ISNULL(i.CommunicationRating,0)<>ISNULL(d.CommunicationRating,0)
            OR (d.ReplyText IS NOT NULL AND (i.ReplyText IS NULL
                OR i.ReplyText COLLATE Latin1_General_100_BIN2<>d.ReplyText COLLATE Latin1_General_100_BIN2 OR i.RepliedAtUtc<>d.RepliedAtUtc)))
        THROW 51009, 'Seller feedback and replies are immutable.', 1;
END
GO
