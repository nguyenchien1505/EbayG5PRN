-- H02: eBay "Member since"/"Joined". Existing users keep NULL (no trustworthy source date); new accounts get the insert time.
ALTER TABLE dbo.[User] ADD JoinedAtUtc datetime2(7) NULL CONSTRAINT DF_User_JoinedAt DEFAULT SYSUTCDATETIME();
GO
-- H02 seller block on every product page (load-tested): listings by seller and recent buyer/seller turns for response time.
CREATE INDEX IX_Product_Seller ON dbo.Product(sellerId, id) INCLUDE (categoryId, isAuction);
CREATE INDEX IX_Message_SellerTurns ON dbo.Message(ConversationSellerId, SentAtUtc)
    INCLUDE (ConversationId, ConversationBuyerId, Sequence, senderId) WHERE ConversationId IS NOT NULL;
