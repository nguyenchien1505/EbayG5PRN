# Handoff — C02 chat service and authorized REST

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline main d6c8221; branch task/C02-chat-service-api. Checkpoint `git log --oneline --grep=C02`; main fast-forward sau verified GitHub CI. C01 handoff giữ tại [C01](C01.md).

## Hoàn tất C02

- Infrastructure/Chat/ChatService implements IChatService; registered Web/Program; [CHAT](../architecture/CHAT.md)/[SECURITY](../architecture/SECURITY.md). Two immutable slots, current SQL actor role/credential, no Admin bypass; new sends require active peer. Buyer opens listing or owned OrderItem using order Seller/price snapshot; pair range-lock/reuse; initial context unchanged.
- ChatApiController6 routes `/api/v1/chat`: authenticated tokenGET, Buyer openPOST, owned inbox/historyGET, send/readPOST. Actor from principal only, no client sender/receiver/reader/price/trace authority. API401/no redirect, endpoint role403, foreign/missing/Admin object404, invalid400/conflict409/generic500; no-store on all responses, X-CSRF-TOKEN JSON writes. Existing MVC forms unchanged.
- ChatApiFilter bounds declared/actual unknown/chunked32768 bytes before JSON binding; generic model/service errors. Token identity-bound, cookie/service current SQL role/nonempty credential checks. No global password/logout copied-cookie revocation claim.
- Send locks conversation UPDLOCK/HOLDLOCK then user/context HOLDLOCK; SHA256 canonical IDs+normalized Unicode trim/newline text, UUID scope conversation/sender, replay DTO before mutable peer/context lookup; changed payload409. ExecuteUpdate counter/read + insert in transaction, commit-before-ack/metadata log; rollback keeps key/counter. C03 publish only Replayed=false.
- History50/max100 exclusive before/after, captured committed watermark, limit+1, ASC, legacy excluded; decimal-string cursors >2^53. Inbox20/max50 keyset `cursorUtc` + `cursorId`, bounded N+1 unread counts. DTO own/peer read/unreadCount strings, monotonic MarkRead only actor slot +server ReaderId; no peer auto-read/inbox reorder on read.
- DbUp0006/entities/source17/seedv1/checksums unchanged. Metadata-only receipt IDs/seq/UTC/trace/replay, no content/token/credential. Hub/Redis/Origin/UI/outbox C03–C05 remain; all G5 chat requirements Pending.

## Verification and boundaries

- `. ./scripts/Use-LocalDotnet.ps1; ./scripts/Test-CI.ps1`: locked restore/build0 warnings/errors, **116/116 pass23s** (91 regression+25 C02), none skipped. [Evidence](../evidence/C02.md): real service2-connections/concurrent pair and send/retry; precommit rollback after SQL writes; access/context/paging/read/bigint/runtime app login/logs +7 SQL-backed HTTP cases. Initial6 EF tracker conflicts fixed via transactional ExecuteUpdate, final green.
- Fresh SQL engine2048MB/container3GB `ebaygroup5-ci-548f68742a7f472998a35caabcb6dd1e` stopped/retained. No local user DB writes/drop/reset/seed changes; migration not needed. TestServer/body evidence isn't deployed Kestrel/proxy/TLS/realtime evidence. Inbox N+1 count/performance F05 pending.
- Verify-Planning/Verify-Migrations/diff checks at final docs checkpoint; immutable7 SQL assets/39cards/source17 preserved. GitHub runtime SHA/run pending initially; append after push before main fast-forward. All G5-01/02/G5-N01/02/04/05 remain Pending.
- Local SQL ignored artifacts/b06/compose-memory.yaml engine4096MB/container6GB stays active; volume ebaygroup5-local_sqlserver-data unchanged. Test-CI fresh server standard2048MB/3GB. User untracked src/EbayGroup5.Web/Properties/launchSettings.json untouched/uncommitted, .env secrets ignored; no UI preview needed.

## Tiếp theo C03 / Jira

- C03: task/CHAT/SECURITY/C02 shared service/DTO/errors/tests; cookie WebSocket-only SkipNegotiation `/hubs/chat`, exact Origin scheme/host/port whitelist (missing fails closed), revalidate each invocation, server owned group join and recipient active/stale-subscription guard. Redis backplane only; typed MessageCommitted/ReadAdvanced ReaderId after SQL commit, no replay event.
- Catch-up SQL after cursor available; crash commit-before-publish recoverable. Shared keys/proxy/TLS/multi-instance F tasks, C04 approved U1/M1 UI, C05 reconnect/log/outbox separate. One task per continuation.
- Jira ledger user batch: B05+B06/KAN-11/TV1 Tran Van Dat; C01/KAN-13 and C02/KAN-14/TV2 Đoàn Công Huy. Threshold unspecified; no live Jira/worklog/review/duplicate issues C02.
- Commit/push/private GitHub CI authorized; main fast-forward/push only after exact runtime CI success; no force push/subagents.
