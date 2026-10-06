# Handoff — B05 Buy Now/address/COD

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline06bb5f3; runtime6d14618; main fast-forward từ task/B05-buy-now-cod sau CI pass. Checkpoint `git log --oneline --grep=B05`. Handoff Jira cũ [JIRA-team-backlog](JIRA-team-backlog.md), runtime B04 [B04](B04.md).

## Hoàn tất

- [CHECKOUT](../architecture/CHECKOUT.md): principal Buyer; một item quantity1–99; USD/giá server; owned address thêm mới/chọn; COD Pending/NULL PaidAt, order Pending. Không real payment/delivery, không history/state B06.
- DbUp0004 nullable key/fingerprint OrderTable + filtered unique Buyer/key + Inventory index; giữ nguyên17 bảng nguồn/seedv1. Migration local applied1, runtime login verified. Không EF migration/reset/drop DB/volume.
- Transaction khóa Buyer → Product → Seller/Address → Inventory; recheck listing/price/stock; total decimal10,2; snapshot unit price. Replay cùng payload trả order cũ, khác payload409, Buyer khác UUID scope riêng. Exception rollback all writes, retry key được.
- MVC checkout/add-address/confirmation no-store; global antiforgery, Buyer policy, owned order404, overposting ignored. Detail Buy It Now links checkout; out-of-stock disabled. Giá/stock409 hiển thị lỗi và quote mới.
- Q-UI-006 Answered: user “Chốt C1 và bố cục đề xuất”; Pay with card official GIF, không ghi năm ảnh; eBay Connect2023/Help hỗ trợ. Ship to/item/COD/summary; two-column desktop/one-column mobile; Add address giữ quantity/chọn address mới. Không hỏi lại C1/Q-UI-005/L1/S1/P1.
- 51 SQL/HTTP tests pass19.9178s (32 regression +19 B05); locked restore pass/Release build0 warnings/errors. Browser DB preview cô lập: login-return checkout/new-address/quantity2=$99.80/order#5Pending. 390/320 không overflow; artifacts/b05/*.png/log ignored.
- Migration upgrade từ B04 seeded bảo toàn metadata/orders/items/payments/stock; trigger Payment failure rollback và concurrency/replay hai connection đã kiểm chứng. [Evidence](../evidence/B05.md). SUP-03Done, SUP-04/UI-01Pending.

## Kiểm tra/checkpoint

- Verify-Planning (105 Markdown files), Verify-Migrations (5 immutable SQL assets) và diff --check pass. Test-CI.ps1 local container riêng cấu hình chuẩn2048MB/3GB: build0 warnings/errors, 51/51tests pass16s; stopped container retained. GitHub [CI37397925776](https://github.com/Twiash/EbayGroup5/actions/runs/37397925776) tại runtime6d14618 pass: job2m20s, build0 warnings/errors, 51/51tests pass37s và TRX upload thành công. Commit tài liệu sau CI dùng [skip ci], chỉ checks docs/source integrity.
- Local lần đầu migration cần GO (đã sửa trước apply). SQL retained test DBs gây resource-pool memory trong một run; override ignored artifacts/b05/compose-memory.yaml tăng engine2048→4096MB/container3→6GB trong kiểm chứng. Cấu hình repo không đổi; không xóa DB. Đã khôi phục engine2048MB/container3GB, đúng named volume cũ và SQL healthy; preview đã dừng.
- Preview riêng không ghi dữ liệu vào EbayGroup5 demo. User untracked src/EbayGroup5.Web/Properties/launchSettings.json giữ nguyên/không stage. .env/.env.database/.env.auth giữ ignored, không in secrets.

## Tiếp theo

- Chỉ B05 trong phiên này. Khi user yêu cầu B06: đọc task B06/CHECKOUT/SECURITY/P1; chốt state transitions/order ownership trước code. Đơn B05 Pending, COD Pending; không ShippingInfo cho đến B06.
- Jira mapping KAN-11 parentKAN-4/owner Tran Van Dat chứa B05+B06; không chuyển Done. Không cập nhật Jira hay tạo worklog trong phiên B05; trạng thái mapping là lần đồng bộ Jira trước. Không tạo ticket trùng.
- Private origin Twiash/EbayGroup5; commit/push/CI đã được user cho phép; main fast-forward/push sau CI pass, không force-push. SDK/config qua Use-LocalDotnet/Use-LocalInfrastructure. Không subagents.
