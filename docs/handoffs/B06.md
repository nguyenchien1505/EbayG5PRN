# Handoff — B06 history and simulated fulfillment

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline27d5399; branch task/B06-order-history-delivery. Checkpoint `git log --oneline --grep=B06`. Handoff B05 giữ tại [B05](B05.md).

## Hoàn tất

- [ORDERS](../architecture/ORDERS.md), Application Orders DTO/service + Infrastructure EF transaction + OrdersController/Razor. Buyer Purchases/Seller Sales/detail owned; role/CSRF/no-store; foreign/missing404, state409. Filter status/page20 ID descending; detail50 lines max, invalid legacy readonly.
- DbUp0005 additive OrderTable.SellerId snapshot/FK + history indexes, nullable backfill chỉ same-Seller all lines; mixed/missing NULL. B05 checkout capture SellerId trong existing transaction. Giá/owner historical không phụ thuộc current listing. Original17 schema/seedv1/script0002–4 giữ bytes.
- Fresh seed runner ancillary snapshot chỉ Buyer seed mới trong transaction, skip schema cũ trước0005; rerun không repair user data. Migration local applied1/runtime login verified. Không EnsureDeleted/drop/reset DB/volume, không EF migration.
- Seller Pending→Shipped tạo một Demo Carrier/DEMO-B06-ID/EstimatedArrival NULL; Buyer Shipped→Delivered + Shipping Delivered + COD Paid/PaidAt UTC atomic. Serialize order lock, validate owned address/items/total/single COD/shipping. Retry/concurrent idempotent, rollback faults. Không trừ inventory lần hai/cancel/refund/real payment/real delivery.
- PLAN sửa persisted Pending (Placed semantic cũ), không sửa seed trạng thái. P1 đã duyệt, card history theo P1, detail reuse C1/shell; header My eBay/confirmation links. Không fake C/D/E actions. Mobile responsive từ cùng mẫu, không hỏi lại P1.
- 70/70 SQL/HTTP tests pass23s (51 regression+19 B06) với Test-CI container riêng2048MB/3GB; locked restore/build0 warnings/errors. [Evidence](../evidence/B06.md), metadata/upgrades/seed/concurrency/rollback/ownership/HTTP đã kiểm chứng.
- Browser DB preview riêng: Seller ship → Buyer receive Order#5Delivered/COD Paid, total99.80, filter/clear/menu keyboard; desktop +390/320 không overflow. artifacts/b06/*.png/log/preview creds ignored. Preview đã kết thúc, không ghi demo orders qua browser.

## Vận hành/checkpoint

- Verify-Planning108 Markdown/39 cards/source17 tables, Verify-Migrations6 immutable SQL assets và diff --check pass. GitHub CI đang chuẩn bị push; ghi run/headSha cuối khi pass.
- SQL local retained DBs gây repeated prelogin reset trên cấu hình2048MB/3GB; override ignored artifacts/b06/compose-memory.yaml dùng4096MB/6GB cho kết nối/preview/migration, giữ named volume ebaygroup5-local_sqlserver-data. Cấu hình repo không đổi. Giữ override local4096MB/6GB hiện tại để các DB retained kết nối ổn định; container healthy và named volume cũ đã kiểm tra. Compose up không kèm override sẽ quay về2048MB/3GB và có thể tái hiện prelogin reset. Không xóa DB để giải quyết RAM.
- User untracked src/EbayGroup5.Web/Properties/launchSettings.json giữ nguyên, không stage. .env/.env.database/.env.auth và generated preview secrets không in/commit. SDK/config via Use-LocalDotnet/Use-LocalInfrastructure.

## Jira và bước tiếp

- User yêu cầu đồng bộ Jira theo đợt task để phân chia thành viên, không sync sau mỗi task; mốc/số task chưa ấn định. [OWNERSHIP](../team/OWNERSHIP.md) lưu quy ước. B05+B06 đã hoàn tất local/KAN-11 parentKAN-4/owner Tran Van Dat, chờ một đợt sync. Không ghi Jira hoặc worklog trong phiên; không tạo ticket mới.
- SUP-04Done, UI-01/SUP-05 và các C/D/E/F còn Pending. Không tự bắt đầu task khác. Commerce dependency cho D01/E01 đã có Delivered/COD evidence; task tiếp theo tùy user (thứ tự PLAN là C01).
- Commit/push/CI private origin được user cho phép; fast-forward main sau CI pass, không force-push. Không subagents.
