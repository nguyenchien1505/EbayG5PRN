-- E04 (DISPUTE_AUTOMATION.md): automatic notices carry a key; one notice per request and key, whichever replica writes first.
-- IS NOT NULL is explicit: NULL IN (...) is UNKNOWN, which a CHECK would let through.
ALTER TABLE dbo.DisputeEvent ADD NoticeKey varchar(20) COLLATE Latin1_General_100_BIN2 NULL;
GO
ALTER TABLE dbo.DisputeEvent WITH CHECK ADD CONSTRAINT CK_DisputeEvent_Notice CHECK (
    (NoticeKey IS NULL AND Type <> 'AutoNotice')
    OR (NoticeKey IS NOT NULL AND NoticeKey IN ('Received','ReturnAccepted','ReturnShipped','Escalated','Due:Open','Due:ReturnShipped')
        AND Type = 'AutoNotice' AND ActorRole = 'System' AND Message IS NOT NULL));
CREATE UNIQUE INDEX UX_DisputeEvent_Notice ON dbo.DisputeEvent(DisputeId, NoticeKey) WHERE NoticeKey IS NOT NULL;
