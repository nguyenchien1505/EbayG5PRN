# Handoff — C01 chat schema and contract

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline236637e; branch task/C01-chat-schema-contract. Checkpoint `git log --oneline --grep=C01`. Runtime/handoff B06 giữ tại [B06](B06.md).

## Hoàn tất C01

- [CHAT](../architecture/CHAT.md): exactly2 immutable Buyer/Seller slots, unique pair; principal/roles/context ownership bắt buộc C02/C03. Buyer open từ listing hoặc owned orderItem snapshot Seller; pre-buy không cần order, các item cùng pair dùng cùng conversation. Không Admin bypass, no client sender/receiver authority.
- DbUp0006 additive ChatConversation + Message nullable modern fields/compound membership FK/modern CHECK/unique sequence/client key/trace/inbox indexes. Original17 tables/constraints/schema SQL/seedv1 và scripts0002–5 bất biến. Legacy Message unchanged/unlinked, không đoán membership/import dữ liệu.
- Core entities/EF mappings + Application/Chat DTO/IChatService/ChatCursor/limits. Sequence BIGINT unique per conversation, server lock+counter transaction trước ack; wire decimal strings an toàn >2^53. UUID scope conversation/sender, payload hash canonical IDs+normalized plaintext, conflict409 service C02. Read cursors per slot0..LastSequence/monotonic.
- History50/max100 before/after exclusive, bounded queries/ASC response/captured high watermark; inbox keyset UpdatedUTC/Id20/max50, merge IDs vì activity có thể reorder. UTC datetime2(7), trace metadata only, no body/token logs; new table app SELECT/INSERT/UPDATE, no DELETE/DDL/journal grant.
- Contract REST/CSRF/error/status/SignalR WebSocket Origin/member guard và commit-before-event đã cố định cho C02/C03; Redis backplane không storage, SQL catch-up cho crash-after-commit. C01 chưa API/service/hub/Redis/UI/outbox, không claim end-to-end auth/realtime/reconnect/TLS.
- 91/91tests pass23s =70 regression+21 C01 schema/protocol/prototype tests; locked restore/build0 warnings/errors. [Evidence](../evidence/C01.md): populated B06 legacy preservation, FK/check/index uniqueness, precise mapping/wire, 125-row cursor prototype, two-connection serialized writes/replay/rollback, app permissions. Lỗi ngoặc CHECK lần đầu test rollback, sửa trước version apply.
- All G5 chat functional/nonfunctional requirements vẫn Pending; chỉ C01 Done. UI U1/M1 đã chốt nhưng không cần đọc/triển khai UI ở C01.

## Kiểm tra/vận hành

- Verify-Planning111 Markdown/39 cards/source17 tables, Verify-Migrations7 immutable assets và diff --check pass. Initialize-Database local applied1 migration/runtime login verified. GitHub CI đang chuẩn bị push, ghi run/head cuối khi pass.
- Local SQL vẫn override ignored artifacts/b06/compose-memory.yaml engine4096MB/container6GB cho470 retained DBs; volume ebaygroup5-local_sqlserver-data giữ nguyên, compose source không đổi. Test-CI SQL mới dùng chuẩn2048MB/3GB; stopped containers retained. Không drop/reset user DB/volume.
- User untracked src/EbayGroup5.Web/Properties/launchSettings.json giữ nguyên, không stage; .env/.env.database/.env.auth ignored, không in secrets. SDK/config via Use-LocalDotnet/Use-LocalInfrastructure. Không preview/UI server cần chạy C01.

## Tiếp theo C02 / Jira

- C02: đọc task/CHAT/SECURITY/entities, implement IChatService + authorized REST và SQL tests principal/membership/context/replay-payload/normalized text/cursor/MarkRead. Register service khi implement, không dùng SQL prototype tests làm service. HTTP401 cho API unauthenticated, unsafe CSRF header, no-store và bounded body.
- C03/C04/C05 riêng: hub/shared SQL service/Redis, approved U1/M1 UI, reconnect/catch-up/log/outbox evidence. Không tự triển khai trong C01. Xem giới hạn cookie revocation B02; không claim WSS hoặc E2EE local.
- Jira chờ một đợt task/evidence để phân chia thành viên; user chưa ấn định ngưỡng. Pending sync: B05+B06/KAN-11/TV1 Tran Van Dat; C01/KAN-13/TV2 Đoàn Công Huy. Không ghi live Jira/worklog/review hoặc tạo issue mới trong phiên.
- Commit/push/CI private origin đã được user cho phép; fast-forward main sau CI pass, không force-push. Không subagents.
