# Handoff — Đợt H (giống eBay sau bàn giao)

27/10/2026. Branch `task/H05-item-page` từ main `f80157b` (xem `git log --oneline --grep=H05`). Handoff trước [H04](H04.md) (quyết định 08/10, H00–H04).

## H05 — trang sản phẩm (Done)

- Contract [CATALOG §Quyết định H05](../architecture/CATALOG.md#quyết-định-h05-trang-sản-phẩm-giống-ebay); [evidence](../evidence/H05.md).
- Cart/Watchlist: người dùng giao agent chốt → dẫn tới `/Help/Unavailable/cart|watchlist`, không có giỏ hàng/theo dõi thật.
- Ảnh: `Product.images` = mảng JSON đường dẫn `/img/products/…` (lọc trong `CatalogImages.Parse`); 6 SVG tự vẽ; seed `H05-v1` trong `DemoSeedRunner` (sau `A06-v1`, có journal, chỉ ghi `images IS NULL`).
- File chính: `Views/Catalog/Details.cshtml`, `wwwroot/js/item.js`, `site.css` (khối H05), `CatalogController` (danh mục, ngày giao dự kiến từ `DisputeTiming`), `ICatalogService.cs`, `DemoSeedRunner.cs`, test `SearchResultsTests`/`DemoSeedTests`.

## Test

- Test-CI local trên nhánh đã gộp Q-SEC-002: audit sạch, 295/295 .NET + 20/20 Node; trình duyệt in-app desktop/mobile trên DB tạm. GitHub CI 40511849948 success ở attempt 2 (attempt 1 đỏ vì Q-OPS-001). Đã gộp main 27/10/2026 (H05 fast-forward, Q-UI-011 merge commit).

## Chặn / câu hỏi

- **Q-SEC-002 Answered:** nhánh `task/SEC-002-dotnet-8033` nâng .NET 8.0.33 / SDK 8.0.427 và package vá, đã gộp vào nhánh H05; Test-CI có audit pass.
- Q-UI-011 trả lời một phần trên nhánh `task/Q-UI-011-references` (ảnh live + Community, H06 → Planned). Mới: Q-BIZ-007 (eBay automated feedback). Q-UI-012, Q-OPS-001, Q-TEAM-002 vẫn Open.

## Bước tiếp theo

1. Q-OPS-001 đang sửa ở phiên riêng (nhánh `task/OPS-001-*`); lỗi rolling xuất hiện thường hơn trên 8.0.33.
2. H06 chốt contract theo UI_CANDIDATES §Q-UI-011; H07 sau cùng. Cluster `ebaygroup5` vẫn chạy G02 (chưa có 0015/0016, `H05-v1`).

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1` trước khi build (PATH hệ thống chỉ có SDK 10).
- Stage file cụ thể; `src/EbayGroup5.Web/Properties/launchSettings.json` là file của người dùng. Không chạy Test-CI cùng Test-AppStack.
- eBay chặn Chrome headless: không vượt chặn; ảnh mẫu chụp bằng trình duyệt in-app hoặc người dùng gửi, che thông tin cá nhân.
