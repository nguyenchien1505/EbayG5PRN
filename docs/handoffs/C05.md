# Handoff — C05 reconnect, tải bù, metadata trace

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline main/originccf1264; runtimec2717b9, task/C05-chat-recovery →main sau CI success. Checkpoint `git log --oneline --grep=C05`; C04 archived [C04](C04.md), dependency runtime2c09a7f/[CI37413352483](https://github.com/Twiash/EbayGroup5/actions/runs/37413352483) exact success verified before code.

## Hoàn tất

- [CHAT](../architecture/CHAT.md) C05 contract chốt trước code; **không outbox**, SQL durable receipt +Redis/event best effort, foreground SQL reconciliation15s. Không browser persistent content storage; pending/draft memory-only, UI nhắc giữ trang đến Sent. Reload chưa commit có thể mất pending/draft; tin đã commit đọc lại SQL.
- wwwroot/js/chat-recovery.mjs/chat.js: single recovery loop/start/subscription sync, rejoin before catch-up, bounded jitter1/2/4/8/16/30s cap30s; hidden/offline/closed pause, online/focus/visible/manual wake, pagehide stop/BFCache reload. REST fallback/fetch timeout15s, owned summary restores missing read/unread event.10x100/batch +250ms continuation, stale selection/closed guards, cached selection keeps contiguous cursor.
- chat-state.mjs: own echo/history reconcile conversation+sender+UUID; no foreign-conversation pending removal. caughtUp+summary watermark guards prevent read while incomplete/failed catch-up. Exact immutable UUID/payload transient0/429/5xx auto retry after reconciliation;400/403/404/409 manual;401 stop/clear memory/no event repopulation.
- ChatService logs receipt/replay recordTrace +requestTrace, after-history metadata; ChatRealtime publish message ID/sequence/UTC/trace/result enqueued/catch-up-required, no raw error/body/token/credential. [Recovery/operator runbook](../runbooks/CHAT_RECOVERY.md). No schema/migration/seed/source17/NuGet/vendor changes.

## Verification

- [Evidence](../evidence/C05.md). Test-CI twice full pass, final **140/140 .NET,0 skipped,32s +14/14 Node**, locked Release build0 warnings/errors.3 new SQL/WS/restart/log tests +6 Node recovery cases; original regressions retained. Source17/7 assets/vendor/planning119 Markdown39cards/diff pass. JS session-ended event/label guards afterward Node+browser401 verified; no server edits.
- Fresh SQL+Redis CI containers12c24a... and6022ae... stopped/retained. Fresh visual DB `EbayGroup5_Test_C05Visuala1ecd7ca92f7433496a5981e34248121`, fake accounts/24conversations; ignored artifacts/c05 fixture scoped app login/keys/fault helpers. No user DB/volume reset/drop. Ignored b06 compose-memory.yaml unchanged.
- Actual browser: Connected lost-event SQL commit appears automatically; verified process restart with1050 offline rows loads all via continuation1101 retained bubbles, no Refresh/reload and draft retained. WS blocked fixture uses REST; injected lost-ack after real commit transitions Not sent→one Sent, SQL Last1178/Rows1178/LostAckRows1. Reload final50 newest/Connected/Sent once/draft empty;320px no horizontal overflow. Fake logout in second tab+401 clears bubbles/composer/draft/stops recovery.
- Early browser illegal timer invocation fixed wrappers and recovery scenarios rerun; helper executable lock resolved stopping only verified preview process. Ignored final-recovery.jpg/mobile-320.jpg and earlier failure/recovery screenshots retained. Node covers hidden/offline/coalescing/auth-stop; no every-browser lifecycle/perf claim.
- **G5-01/G5-02 Done**. G5-N01/N02/N04/N05 Pending F01–F05 TLS/WSS/deployed keys/scale/dataset/latency/log collection. Local Activity/record trace linkage tested, not deployed operational log acceptance. UI-01 project-wide Pending.
- **[CI37415620094](https://github.com/Twiash/EbayGroup5/actions/runs/37415620094) exact runtimec2717b92af775ea8f535c5cbdb36a2f3bea574cb success**: job3m29s/build0 warnings/errors/140 .NET1m42s+14 Node/source17/7 assets/vendor/planning119/TRX upload. Final docs-only [skip ci], main FF after success; no runtime changes. User launchSettings.json remains untracked/untouched; artifacts/config/secrets ignored. Private origin push authorized, no force push. Preview/tabs closed, viewport reset, test DB/container retained.

## Tiếp theo / Jira

- C05 only complete; wait user's next task, do not auto C06/D/F. F tasks own deployment/TLS/shared persistent keys/readiness/log retention/perf; B02 copied-cookie/global logout limits remain. Recovery has no reliable event/outbox/offline durable browser draft guarantee; documented UI contract.
- Jira pending batch: B05+B06/KAN-11/TV1 Tran Van Dat; C01/KAN-13, C02/KAN-14, C03+C04/KAN-15, **C05/KAN-16 locally complete**/TV2 Đoàn Công Huy. User threshold unspecified, no live Jira sync/worklog/review/duplicates.
