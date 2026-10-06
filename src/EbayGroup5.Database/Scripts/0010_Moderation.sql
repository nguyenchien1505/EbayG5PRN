ALTER TABLE dbo.Review ADD HiddenAtUtc datetime2(7) NULL, CommentRemovedAtUtc datetime2(7) NULL;
ALTER TABLE dbo.SellerFeedback ADD HiddenAtUtc datetime2(7) NULL, CommentRemovedAtUtc datetime2(7) NULL;
GO
-- Order-linked reviews are moderated by hiding, never physically deleted. Legacy rows keep baseline behaviour.
CREATE TRIGGER dbo.TR_Review_NoDelete ON dbo.Review AFTER DELETE AS
BEGIN
    SET NOCOUNT ON;
    IF EXISTS (SELECT 1 FROM deleted WHERE OrderItemId IS NOT NULL)
        THROW 51010, 'Order-linked reviews cannot be deleted; hide them instead.', 1;
END
GO
CREATE TABLE dbo.ContentReport (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ContentReport PRIMARY KEY,
    ReviewId int NULL CONSTRAINT FK_ContentReport_Review REFERENCES dbo.Review(id),
    SellerFeedbackId int NULL CONSTRAINT FK_ContentReport_SellerFeedback REFERENCES dbo.SellerFeedback(Id),
    ReporterId int NOT NULL CONSTRAINT FK_ContentReport_Reporter REFERENCES dbo.[User](id),
    Reason varchar(12) NOT NULL,
    Details nvarchar(500) NULL,
    Status varchar(10) NOT NULL CONSTRAINT DF_ContentReport_Status DEFAULT 'Open',
    CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ContentReport_Created DEFAULT SYSUTCDATETIME(),
    ResolvedAtUtc datetime2(7) NULL,
    ResolvedById int NULL CONSTRAINT FK_ContentReport_ResolvedBy REFERENCES dbo.[User](id),
    CONSTRAINT CK_ContentReport_Target CHECK ((ReviewId IS NULL AND SellerFeedbackId IS NOT NULL) OR (ReviewId IS NOT NULL AND SellerFeedbackId IS NULL)),
    CONSTRAINT CK_ContentReport_Reason CHECK (Reason COLLATE Latin1_General_100_BIN2 IN ('Offensive','PersonalInfo','Spam','Other')),
    CONSTRAINT CK_ContentReport_Details CHECK (Details IS NULL OR LEN(LTRIM(RTRIM(Details)))>0),
    CONSTRAINT CK_ContentReport_Status CHECK (
        (Status COLLATE Latin1_General_100_BIN2='Open' AND ResolvedAtUtc IS NULL AND ResolvedById IS NULL) OR
        (Status COLLATE Latin1_General_100_BIN2 IN ('Actioned','Dismissed') AND ResolvedAtUtc IS NOT NULL AND ResolvedById IS NOT NULL))
);
CREATE UNIQUE INDEX UX_ContentReport_Review_Reporter ON dbo.ContentReport(ReviewId,ReporterId) WHERE ReviewId IS NOT NULL;
CREATE UNIQUE INDEX UX_ContentReport_Feedback_Reporter ON dbo.ContentReport(SellerFeedbackId,ReporterId) WHERE SellerFeedbackId IS NOT NULL;
CREATE INDEX IX_ContentReport_Open ON dbo.ContentReport(Status,CreatedAtUtc DESC) INCLUDE (ReviewId,SellerFeedbackId,Reason);

CREATE TABLE dbo.ModerationAudit (
    Id int IDENTITY(1,1) NOT NULL CONSTRAINT PK_ModerationAudit PRIMARY KEY,
    ReviewId int NULL CONSTRAINT FK_ModerationAudit_Review REFERENCES dbo.Review(id),
    SellerFeedbackId int NULL CONSTRAINT FK_ModerationAudit_SellerFeedback REFERENCES dbo.SellerFeedback(Id),
    AdminId int NOT NULL CONSTRAINT FK_ModerationAudit_Admin REFERENCES dbo.[User](id),
    Action varchar(14) NOT NULL,
    Note nvarchar(500) NULL,
    CreatedAtUtc datetime2(7) NOT NULL CONSTRAINT DF_ModerationAudit_Created DEFAULT SYSUTCDATETIME(),
    CONSTRAINT CK_ModerationAudit_Target CHECK ((ReviewId IS NULL AND SellerFeedbackId IS NOT NULL) OR (ReviewId IS NOT NULL AND SellerFeedbackId IS NULL)),
    CONSTRAINT CK_ModerationAudit_Action CHECK (Action COLLATE Latin1_General_100_BIN2 IN ('Hide','RemoveComment','Restore','Dismiss')),
    CONSTRAINT CK_ModerationAudit_Note CHECK (Note IS NULL OR LEN(LTRIM(RTRIM(Note)))>0)
);
CREATE INDEX IX_ModerationAudit_Review ON dbo.ModerationAudit(ReviewId,Id) WHERE ReviewId IS NOT NULL;
CREATE INDEX IX_ModerationAudit_Feedback ON dbo.ModerationAudit(SellerFeedbackId,Id) WHERE SellerFeedbackId IS NOT NULL;
GO
-- Audit is append-only for every login, including administrators.
CREATE TRIGGER dbo.TR_ModerationAudit_AppendOnly ON dbo.ModerationAudit AFTER UPDATE, DELETE AS
BEGIN
    SET NOCOUNT ON;
    THROW 51011, 'Moderation audit is append-only.', 1;
END
GO
GRANT UPDATE (HiddenAtUtc, CommentRemovedAtUtc) ON OBJECT::dbo.SellerFeedback TO ebay_app;
GRANT SELECT, INSERT ON OBJECT::dbo.ContentReport TO ebay_app;
GRANT UPDATE (Status, ResolvedAtUtc, ResolvedById) ON OBJECT::dbo.ContentReport TO ebay_app;
GRANT SELECT, INSERT ON OBJECT::dbo.ModerationAudit TO ebay_app;
