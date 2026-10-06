-- E01 (DISPUTES.md): estimated delivery, eBay-style item-not-received / return requests on the original Dispute table, append-only timeline.
ALTER TABLE dbo.OrderTable ADD EstimatedDeliveryUtc datetime2(7) NULL;
ALTER TABLE dbo.OrderItem ADD CONSTRAINT UQ_OrderItem_Id_Order UNIQUE (id, orderId);
ALTER TABLE dbo.Dispute ADD
    OrderItemId int NULL,
    SellerId int NULL CONSTRAINT FK_Dispute_Seller REFERENCES dbo.[User](id),
    Kind varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
    Reason varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
    Preference varchar(6) COLLATE Latin1_General_100_BIN2 NULL,
    CreatedAtUtc datetime2(7) NULL,
    DueAtUtc datetime2(7) NULL,
    SellerRespondedAtUtc datetime2(7) NULL,
    EscalatedAtUtc datetime2(7) NULL,
    ClosedAtUtc datetime2(7) NULL,
    Outcome varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
    OfferAmount decimal(10,2) NULL,
    RefundAmount decimal(10,2) NULL,
    ReturnTracking nvarchar(100) NULL;
GO
-- Legacy orders: 5 business days (Mon–Fri) after orderDate, matching DisputeTiming defaults; 1900-01-01 was a Monday.
UPDATE dbo.OrderTable SET EstimatedDeliveryUtc = DATEADD(day,
    CASE ((DATEDIFF(day, '19000101', orderDate) % 7) + 7) % 7 WHEN 5 THEN 6 WHEN 6 THEN 5 ELSE 7 END, CAST(orderDate AS datetime2(7)))
WHERE orderDate IS NOT NULL AND EstimatedDeliveryUtc IS NULL;

ALTER TABLE dbo.Dispute ADD
    CONSTRAINT FK_Dispute_OrderItemOrder FOREIGN KEY (OrderItemId, orderId) REFERENCES dbo.OrderItem(id, orderId),
    -- Module rows only (OrderItemId set); legacy rows keep the baseline shape. Every predicate is NULL-safe: CHECK accepts UNKNOWN.
    CONSTRAINT CK_Dispute_Module CHECK (OrderItemId IS NULL OR (
        orderId IS NOT NULL AND raisedBy IS NOT NULL AND SellerId IS NOT NULL AND raisedBy <> SellerId AND CreatedAtUtc IS NOT NULL
        AND Kind IS NOT NULL AND Reason IS NOT NULL AND status IS NOT NULL
        AND ((Kind = 'ItemNotReceived' AND Reason = 'ItemNotReceived' AND Preference IS NOT NULL AND Preference IN ('Refund','Item') AND OfferAmount IS NULL AND ReturnTracking IS NULL)
          OR (Kind = 'Return' AND Preference IS NULL AND description IS NOT NULL AND LEN(LTRIM(RTRIM(description))) > 0
              AND Reason IN ('OrderedWrongItem','ChangedMind','NoLongerNeeded','BetterPrice','DoesntFit','Damaged','Defective','MissingParts','NotAsDescribed','WrongItem')))
        AND (description IS NULL OR LEN(description) <= 2000)
        AND status COLLATE Latin1_General_100_BIN2 IN ('Open','ReturnAccepted','ReturnShipped','Escalated','Closed')
        AND (Kind = 'Return' OR status COLLATE Latin1_General_100_BIN2 NOT IN ('ReturnAccepted','ReturnShipped'))
        AND ((status COLLATE Latin1_General_100_BIN2 = 'Closed' AND ClosedAtUtc IS NOT NULL AND DueAtUtc IS NULL AND OfferAmount IS NULL AND Outcome IS NOT NULL
              AND Outcome IN ('Refunded','PartialRefund','BuyerClosed','ItemReceived','SellerDeclined','ReturnNotShipped','DecidedForBuyer','DecidedForSeller'))
          OR (status COLLATE Latin1_General_100_BIN2 <> 'Closed' AND ClosedAtUtc IS NULL AND Outcome IS NULL AND DueAtUtc IS NOT NULL AND RefundAmount IS NULL))
        AND (status COLLATE Latin1_General_100_BIN2 <> 'Escalated' OR EscalatedAtUtc IS NOT NULL)
        AND (status COLLATE Latin1_General_100_BIN2 <> 'ReturnShipped' OR ReturnTracking IS NOT NULL)
        AND (OfferAmount IS NULL OR OfferAmount > 0)
        AND (RefundAmount IS NULL OR (RefundAmount > 0 AND Outcome IN ('Refunded','PartialRefund','DecidedForBuyer')))
        AND (ReturnTracking IS NULL OR LEN(LTRIM(RTRIM(ReturnTracking))) > 0)));
CREATE UNIQUE INDEX UX_Dispute_ActiveLine ON dbo.Dispute(OrderItemId) WHERE OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL;
CREATE UNIQUE INDEX UX_Dispute_LineKind ON dbo.Dispute(OrderItemId, Kind) WHERE OrderItemId IS NOT NULL;
CREATE INDEX IX_Dispute_Buyer ON dbo.Dispute(raisedBy, id DESC) WHERE OrderItemId IS NOT NULL;
CREATE INDEX IX_Dispute_Seller ON dbo.Dispute(SellerId, id DESC) WHERE OrderItemId IS NOT NULL;
CREATE INDEX IX_Dispute_Due ON dbo.Dispute(DueAtUtc) INCLUDE (status, SellerRespondedAtUtc) WHERE OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL;

CREATE TABLE dbo.DisputeEvent (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DisputeEvent PRIMARY KEY,
    DisputeId int NOT NULL CONSTRAINT FK_DisputeEvent_Dispute REFERENCES dbo.Dispute(id),
    ActorId int NULL CONSTRAINT FK_DisputeEvent_Actor REFERENCES dbo.[User](id),
    ActorRole varchar(6) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Type varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Message nvarchar(2000) NULL,
    Amount decimal(10,2) NULL,
    Tracking nvarchar(100) NULL,
    CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_DisputeEvent_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_DisputeEvent_Actor CHECK ((ActorRole = 'System' AND ActorId IS NULL) OR (ActorRole IN ('Buyer','Seller','Admin') AND ActorId IS NOT NULL)),
    CONSTRAINT CK_DisputeEvent_Type CHECK (Type IN ('Opened','Message','TrackingAdded','Refunded','ReturnAccepted','OfferMade','OfferAccepted','OfferDeclined',
        'Declined','ReturnShipped','Escalated','Closed','ReturnExpired','ItemReceived','Decided','AutoNotice')),
    CONSTRAINT CK_DisputeEvent_Message CHECK (Message IS NULL OR LEN(LTRIM(RTRIM(Message))) > 0),
    CONSTRAINT CK_DisputeEvent_Amount CHECK (Amount IS NULL OR Amount > 0),
    CONSTRAINT CK_DisputeEvent_Tracking CHECK (Tracking IS NULL OR LEN(LTRIM(RTRIM(Tracking))) > 0)
);
CREATE INDEX IX_DisputeEvent_Dispute ON dbo.DisputeEvent(DisputeId, Id);
GO
-- Requests are never physically deleted; identity fields are fixed and a closed request cannot be reopened (eBay Help 4042).
CREATE TRIGGER dbo.TR_Dispute_Guard ON dbo.Dispute AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF NOT EXISTS (SELECT 1 FROM inserted) AND EXISTS (SELECT 1 FROM deleted WHERE OrderItemId IS NOT NULL)
        THROW 51012, 'Dispute requests cannot be deleted.', 1;
    IF EXISTS (SELECT 1 FROM deleted d JOIN inserted i ON i.id = d.id
               WHERE (d.OrderItemId IS NOT NULL OR i.OrderItemId IS NOT NULL) AND (
                   EXISTS (SELECT d.OrderItemId, d.orderId, d.raisedBy, d.SellerId, d.Kind, d.Reason, d.Preference, d.CreatedAtUtc
                           EXCEPT SELECT i.OrderItemId, i.orderId, i.raisedBy, i.SellerId, i.Kind, i.Reason, i.Preference, i.CreatedAtUtc)
                   -- A closed request is frozen entirely.
                   OR (d.ClosedAtUtc IS NOT NULL AND EXISTS (
                       SELECT d.status, d.description, d.resolution, d.DueAtUtc, d.SellerRespondedAtUtc, d.EscalatedAtUtc, d.ClosedAtUtc, d.Outcome, d.OfferAmount, d.RefundAmount, d.ReturnTracking
                       EXCEPT SELECT i.status, i.description, i.resolution, i.DueAtUtc, i.SellerRespondedAtUtc, i.EscalatedAtUtc, i.ClosedAtUtc, i.Outcome, i.OfferAmount, i.RefundAmount, i.ReturnTracking))))
        THROW 51013, 'Dispute identity is fixed and closed requests cannot be reopened.', 1;
END
GO
CREATE TRIGGER dbo.TR_DisputeEvent_AppendOnly ON dbo.DisputeEvent AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51014, 'Dispute timeline is append-only.', 1;
END
GO
GRANT SELECT, INSERT ON OBJECT::dbo.DisputeEvent TO ebay_app;
