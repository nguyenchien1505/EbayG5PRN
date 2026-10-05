# Handoff — B04 hoàn tất

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Baseline backend 5ed020f; task/B04-catalog, fast-forward main sau kiểm chứng. Checkpoint git log --oneline --grep=B04. Handoff backend cũ tại B04-backend.md.

## Hoàn tất

- CATALOG contract; Application DTO/ICatalogService, Infrastructure EF read query; MVC/API catalog/detail, Home redirects live catalog, shared header search.
- Search literal title/query100, category, pagination12 stable ID; valid fixed-price listing/seller only; detail/stock, USD demo, placeholders cho images NULL. Không schema/seed writes.
- Q-UI-005 Answered: user gửi ảnh product-detail thay PD1 và yêu cầu tiếp tục. Ảnh lớn trái/seller-title-price-stock phải, responsive một cột mobile. Không commit ảnh/account nguồn. Contact seller native entry/context; không gửi chat trước C04. Buy It Now disabled với explanation tới B05.
- 32 SQL/HTTP tests pass, 13.84s; locked restore/Release build 0 warnings/errors. Pagination giữ query/category; DTO/content encoding, invalid400/missing404 và tất cả security regressions.
- Browser desktop/detail/mobile390 inspected; Electronics2, Lamp1, keyboard Contact seller; no horizontal overflow mobile390/narrow320. Local ignored artifacts/b04/detail-desktop.png, detail-mobile.png, catalog-desktop.png. Không browser writes vào demo data.
- SUP-02 Done, UI-01/SUP-03–05 Pending. User untracked src/EbayGroup5.Web/Properties/launchSettings.json giữ nguyên/không stage.

## Tiếp theo B05

Đọc card B05, CATALOG, SECURITY, schema OrderTable/OrderItem/Inventory/Address/Payment; chốt transaction/state/idempotency contract trước code. Checkout phải dùng principal Buyer, tính tiền server, kiểm tra lại tồn kho trong transaction. Không lấy stock trên UI làm bằng chứng đặt hàng. Task UI checkout chưa có ảnh đã chốt, khảo sát/chốt theo AGENTS khi cần.

## Vận hành

Private origin Twiash/EbayGroup5; gh authenticated. SDK scripts/Use-LocalDotnet.ps1; config Use-LocalInfrastructure.ps1; tests Test-Database.ps1. Local SQL/Redis và test DBs retained, secrets ignored. No subagents, một task mỗi continuation. F01/F02 HTTPS/proxy/shared key/distributed limiting còn Pending. PROJECT_COMPARISON đã trả lời lý do cấu trúc khác code cũ; không sửa ProjectCode. TV1–TV5/deadline questions remain open.

PASS GitHub CI Ubuntu [run 37386896065](https://github.com/Twiash/EbayGroup5/actions/runs/37386896065) tại f939676, job 1m52s: build 0 warnings/errors, 32 tests và TRX upload thành công. Verify-Planning, Verify-Migrations, git diff --check pass; preview đã dừng.
