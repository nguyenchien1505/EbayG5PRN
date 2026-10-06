ALTER TABLE dbo.OrderTable ADD CheckoutKey uniqueidentifier NULL, CheckoutFingerprint varchar(64) NULL;
GO
CREATE UNIQUE INDEX UX_OrderTable_Buyer_CheckoutKey ON dbo.OrderTable(buyerId, CheckoutKey) WHERE CheckoutKey IS NOT NULL;
CREATE INDEX IX_Inventory_Product ON dbo.Inventory(productId, id) INCLUDE(quantity);
