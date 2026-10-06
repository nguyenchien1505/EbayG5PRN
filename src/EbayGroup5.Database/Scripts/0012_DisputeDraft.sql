-- E02 (DISPUTE_FORMS.md): one server-side draft per buyer, order line and request kind; deleted when the request opens.
CREATE TABLE dbo.DisputeDraft (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_DisputeDraft PRIMARY KEY,
    BuyerId int NOT NULL CONSTRAINT FK_DisputeDraft_Buyer REFERENCES dbo.[User](id),
    OrderItemId int NOT NULL CONSTRAINT FK_DisputeDraft_OrderItem REFERENCES dbo.OrderItem(id),
    Kind varchar(16) COLLATE Latin1_General_100_BIN2 NOT NULL,
    Reason varchar(16) COLLATE Latin1_General_100_BIN2 NULL,
    Preference varchar(6) COLLATE Latin1_General_100_BIN2 NULL,
    Details nvarchar(2000) NULL,
    UpdatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_DisputeDraft_Updated DEFAULT SYSUTCDATETIME(),
    CONSTRAINT UQ_DisputeDraft_Buyer_Line_Kind UNIQUE (BuyerId, OrderItemId, Kind),
    CONSTRAINT CK_DisputeDraft_Shape CHECK (
        (Kind = 'ItemNotReceived' AND (Reason IS NULL OR Reason = 'ItemNotReceived') AND (Preference IS NULL OR Preference IN ('Refund','Item')))
        OR (Kind = 'Return' AND Preference IS NULL AND (Reason IS NULL OR Reason IN
            ('OrderedWrongItem','ChangedMind','NoLongerNeeded','BetterPrice','DoesntFit','Damaged','Defective','MissingParts','NotAsDescribed','WrongItem')))),
    CONSTRAINT CK_DisputeDraft_Details CHECK (Details IS NULL OR LEN(LTRIM(RTRIM(Details))) > 0)
);
GRANT SELECT, INSERT, UPDATE, DELETE ON OBJECT::dbo.DisputeDraft TO ebay_app;
