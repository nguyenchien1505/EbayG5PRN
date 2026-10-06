ALTER TABLE dbo.OrderTable ADD SellerId int NULL;
GO
ALTER TABLE dbo.OrderTable ADD CONSTRAINT FK_OrderTable_Seller FOREIGN KEY (SellerId) REFERENCES dbo.[User](id);
UPDATE o SET SellerId=s.SellerId
FROM dbo.OrderTable o
JOIN (
    SELECT i.orderId, MIN(p.sellerId) AS SellerId
    FROM dbo.OrderItem i LEFT JOIN dbo.Product p ON p.id=i.productId
    GROUP BY i.orderId
    HAVING COUNT(*)=COUNT(p.sellerId) AND MIN(p.sellerId)=MAX(p.sellerId)
) s ON s.orderId=o.id;
CREATE INDEX IX_OrderTable_Buyer_Id ON dbo.OrderTable(buyerId,id);
CREATE INDEX IX_OrderTable_Seller_Id ON dbo.OrderTable(SellerId,id);
CREATE INDEX IX_OrderItem_Order ON dbo.OrderItem(orderId,id);
CREATE INDEX IX_Payment_Order ON dbo.Payment(orderId,id);
CREATE INDEX IX_ShippingInfo_Order ON dbo.ShippingInfo(orderId,id);
