CREATE TABLE dbo.ChatConversation (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ChatConversation PRIMARY KEY,
    BuyerId int NOT NULL CONSTRAINT FK_ChatConversation_Buyer REFERENCES dbo.[User](id),
    SellerId int NOT NULL CONSTRAINT FK_ChatConversation_Seller REFERENCES dbo.[User](id),
    CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ChatConversation_Created DEFAULT SYSUTCDATETIME(),
    UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ChatConversation_Updated DEFAULT SYSUTCDATETIME(),
    LastSequence bigint NOT NULL CONSTRAINT DF_ChatConversation_LastSequence DEFAULT 0,
    BuyerReadSequence bigint NOT NULL CONSTRAINT DF_ChatConversation_BuyerRead DEFAULT 0,
    SellerReadSequence bigint NOT NULL CONSTRAINT DF_ChatConversation_SellerRead DEFAULT 0,
    ContextProductId int NULL CONSTRAINT FK_ChatConversation_Product REFERENCES dbo.Product(id),
    ContextOrderItemId int NULL CONSTRAINT FK_ChatConversation_OrderItem REFERENCES dbo.OrderItem(id),
    ContextTitle nvarchar(255) NULL,
    ContextUnitPrice decimal(10,2) NULL,
    CONSTRAINT UQ_ChatConversation_Pair UNIQUE (BuyerId,SellerId),
    CONSTRAINT UQ_ChatConversation_Membership UNIQUE (Id,BuyerId,SellerId),
    CONSTRAINT CK_ChatConversation_Members CHECK (BuyerId<>SellerId),
    CONSTRAINT CK_ChatConversation_Cursors CHECK (LastSequence>=0 AND BuyerReadSequence>=0 AND SellerReadSequence>=0 AND BuyerReadSequence<=LastSequence AND SellerReadSequence<=LastSequence),
    CONSTRAINT CK_ChatConversation_Context CHECK (
        (ContextProductId IS NULL AND ContextOrderItemId IS NULL AND ContextTitle IS NULL AND ContextUnitPrice IS NULL) OR
        (ContextProductId IS NOT NULL AND ContextTitle IS NOT NULL AND LEN(LTRIM(RTRIM(ContextTitle)))>0 AND ContextUnitPrice IS NOT NULL AND ContextUnitPrice>=0))
);
CREATE INDEX IX_ChatConversation_Buyer_Inbox ON dbo.ChatConversation(BuyerId,UpdatedAtUtc DESC,Id DESC);
CREATE INDEX IX_ChatConversation_Seller_Inbox ON dbo.ChatConversation(SellerId,UpdatedAtUtc DESC,Id DESC);
GRANT SELECT, INSERT, UPDATE ON OBJECT::dbo.ChatConversation TO ebay_app;

ALTER TABLE dbo.Message ADD ConversationId int NULL, ConversationBuyerId int NULL, ConversationSellerId int NULL,
    Sequence bigint NULL, ClientMessageId uniqueidentifier NULL, PayloadHash binary(32) NULL,
    SentAtUtc datetime2(7) NULL, TraceId varchar(32) NULL,
    ContextProductId int NULL, ContextOrderItemId int NULL, ContextTitle nvarchar(255) NULL, ContextUnitPrice decimal(10,2) NULL;
GO
ALTER TABLE dbo.Message ADD
    CONSTRAINT FK_Message_ConversationMembership FOREIGN KEY(ConversationId,ConversationBuyerId,ConversationSellerId) REFERENCES dbo.ChatConversation(Id,BuyerId,SellerId),
    CONSTRAINT FK_Message_ContextProduct FOREIGN KEY(ContextProductId) REFERENCES dbo.Product(id),
    CONSTRAINT FK_Message_ContextOrderItem FOREIGN KEY(ContextOrderItemId) REFERENCES dbo.OrderItem(id),
    CONSTRAINT CK_Message_ChatShape CHECK (
        (ConversationId IS NULL AND ConversationBuyerId IS NULL AND ConversationSellerId IS NULL AND Sequence IS NULL AND ClientMessageId IS NULL AND PayloadHash IS NULL AND SentAtUtc IS NULL AND TraceId IS NULL
            AND ContextProductId IS NULL AND ContextOrderItemId IS NULL AND ContextTitle IS NULL AND ContextUnitPrice IS NULL) OR
        (ConversationId IS NOT NULL AND ConversationBuyerId IS NOT NULL AND ConversationSellerId IS NOT NULL
            AND senderId IS NOT NULL AND receiverId IS NOT NULL
            AND ((senderId=ConversationBuyerId AND receiverId=ConversationSellerId) OR (senderId=ConversationSellerId AND receiverId=ConversationBuyerId))
            AND Sequence IS NOT NULL AND Sequence>0 AND ClientMessageId IS NOT NULL AND ClientMessageId<>'00000000-0000-0000-0000-000000000000'
            AND PayloadHash IS NOT NULL AND SentAtUtc IS NOT NULL AND [timestamp] IS NOT NULL
            AND TraceId IS NOT NULL AND LEN(TraceId)=32 AND TraceId COLLATE Latin1_General_100_BIN2 NOT LIKE '%[^0-9a-f]%'
            AND content IS NOT NULL AND DATALENGTH(content)<=8000 AND LEN(LTRIM(RTRIM(content)))>0
            AND ((ContextProductId IS NULL AND ContextOrderItemId IS NULL AND ContextTitle IS NULL AND ContextUnitPrice IS NULL) OR
                (ContextProductId IS NOT NULL AND ContextTitle IS NOT NULL AND LEN(LTRIM(RTRIM(ContextTitle)))>0 AND ContextUnitPrice IS NOT NULL AND ContextUnitPrice>=0)
            )
        )
    );
CREATE UNIQUE INDEX UX_Message_ChatSequence ON dbo.Message(ConversationId,Sequence) WHERE ConversationId IS NOT NULL;
CREATE UNIQUE INDEX UX_Message_ClientMessage ON dbo.Message(ConversationId,senderId,ClientMessageId) WHERE ConversationId IS NOT NULL;
CREATE INDEX IX_Message_Trace ON dbo.Message(TraceId,SentAtUtc,id) WHERE TraceId IS NOT NULL;
