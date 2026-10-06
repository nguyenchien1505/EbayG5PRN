CREATE TABLE dbo.SellerFeedback (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_SellerFeedback PRIMARY KEY,
    OrderItemId int NOT NULL CONSTRAINT FK_SellerFeedback_OrderItem REFERENCES dbo.OrderItem(id),
    BuyerId int NOT NULL CONSTRAINT FK_SellerFeedback_Buyer REFERENCES dbo.[User](id),
    SellerId int NOT NULL CONSTRAINT FK_SellerFeedback_Seller REFERENCES dbo.[User](id),
    Sentiment varchar(8) NOT NULL,
    Comment nvarchar(500) NULL,
    CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_SellerFeedback_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_SellerFeedback_OrderItem UNIQUE (OrderItemId),
    CONSTRAINT CK_SellerFeedback_Members CHECK (BuyerId<>SellerId),
    CONSTRAINT CK_SellerFeedback_Sentiment CHECK (Sentiment COLLATE Latin1_General_100_BIN2 IN ('Positive','Neutral','Negative')),
    CONSTRAINT CK_SellerFeedback_Comment CHECK (Comment IS NULL OR LEN(LTRIM(RTRIM(Comment)))>0)
);
CREATE INDEX IX_SellerFeedback_Seller ON dbo.SellerFeedback(SellerId,Id DESC) INCLUDE (Sentiment);
GRANT SELECT, INSERT ON OBJECT::dbo.SellerFeedback TO ebay_app;
