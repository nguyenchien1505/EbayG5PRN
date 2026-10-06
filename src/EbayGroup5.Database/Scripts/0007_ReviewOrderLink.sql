ALTER TABLE dbo.OrderItem ADD CONSTRAINT UQ_OrderItem_Id_Product UNIQUE (id, productId);
ALTER TABLE dbo.Review ADD OrderItemId int NULL;
GO
ALTER TABLE dbo.Review ADD
    CONSTRAINT FK_Review_OrderItemProduct FOREIGN KEY (OrderItemId, productId) REFERENCES dbo.OrderItem(id, productId),
    CONSTRAINT CK_Review_OrderLink CHECK (
        OrderItemId IS NULL OR
        (productId IS NOT NULL AND reviewerId IS NOT NULL AND rating IS NOT NULL AND rating BETWEEN 1 AND 5 AND createdAt IS NOT NULL
            AND (comment IS NULL OR (DATALENGTH(comment)<=4000 AND LEN(LTRIM(RTRIM(comment)))>0))));
CREATE UNIQUE INDEX UX_Review_OrderItem ON dbo.Review(OrderItemId) WHERE OrderItemId IS NOT NULL;
