-- Q-PERF-001 (LOAD.md): Confirm received looks up the order's open item-not-received requests by orderId. Without this index the
-- lookup scanned dbo.Dispute under its lock and deadlocked concurrent orders. Additive only.
CREATE INDEX IX_Dispute_Order ON dbo.Dispute(orderId, id);
