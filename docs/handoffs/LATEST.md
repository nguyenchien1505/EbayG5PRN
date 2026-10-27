# Handoff — Đợt H (giống eBay sau bàn giao)

27/10/2026. Branch `task/H05-item-page` từ main `f80157b` (xem `git log --oneline --grep=H05`). Handoff trước [H04](H04.md) (quyết định 08/10, H00–H04).

## H05 — trang sản phẩm (InProgress, chờ GitHub CI)

- Contract [CATALOG §Quyết định H05](../architecture/CATALOG.md#quyết-định-h05-trang-sản-phẩm-giống-ebay); [evidence](../evidence/H05.md).
- Cart/Watchlist: người dùng giao agent chốt → dẫn tới `/Help/Unavailable/cart|watchlist`, không có giỏ hàng/theo dõi thật.
- Ảnh: `Product.images` = mảng JSON đường dẫn `/img/products/…` (lọc trong `CatalogImages.Parse`); 6 SVG tự vẽ; seed `H05-v1` trong `DemoSeedRunner` (sau `A06-v1`, có journal, chỉ ghi `images IS NULL`).
- File chính: `Views/Catalog/Details.cshtml`, `wwwroot/js/item.js`, `site.css` (khối H05), `CatalogController` (danh mục, ngày giao dự kiến từ `DisputeTiming`), `ICatalogService.cs`, `DemoSeedRunner.cs`, test `SearchResultsTests`/`DemoSeedTests`.

## Test

- Test-CI local (SDK 8) 295/295 .NET + 20/20 Node, **bỏ qua NuGet audit**; trình duyệt in-app desktop/mobile trên DB tạm. Chưa chạy Test-AppStack, chưa push/GitHub CI.

## Chặn / câu hỏi

- **Q-SEC-002 Answered:** nhánh `task/SEC-002-dotnet-8033` nâng .NET 8.0.33 / SDK 8.0.427 và package vá, đã gộp vào nhánh H05; Test-CI có audit pass.
- Q-UI-011 (ảnh đơn mua thật, chặn H06), Q-UI-012 (upload ảnh), Q-OPS-001 (lỗi mạng rolling), Q-TEAM-002 vẫn Open.

## Bước tiếp theo

1. Push hai nhánh → GitHub CI cho H05 (đã gồm Q-SEC-002) → H05 Done, merge main.
2. H06 khi có ảnh Q-UI-011; H07 sau cùng. Cluster `ebaygroup5` vẫn chạy G02 (chưa có 0015/0016, `H05-v1`).

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1` trước khi build (PATH hệ thống chỉ có SDK 10).
- Stage file cụ thể; `src/EbayGroup5.Web/Properties/launchSettings.json` là file của người dùng. Không chạy Test-CI cùng Test-AppStack.
- eBay chặn Chrome headless: không vượt chặn; ảnh mẫu chụp bằng trình duyệt in-app hoặc người dùng gửi, che thông tin cá nhân.
