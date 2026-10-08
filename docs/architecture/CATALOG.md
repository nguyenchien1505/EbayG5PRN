# B04 — Catalog contract

- Read-only public catalog/API `/api/v1/catalog`; fixed-price listings only (`IsAuction == false`), nonempty title, nonnegative price and existing Seller role. Legacy incomplete/auction listings excluded; detail returns 404 consistently.
- Query q trim/max100, category optional positive ID, page positive, size fixed12. Search literal title substring using EF parameterization, no wildcard semantics from user input. Sort ID ascending for stable pagination; count + bounded page, categories returned separately. Unknown category gives empty results; out-of-range page gives empty page, no silent reset.
- Currency USD for this demo (schema has no currency column). No shipping/condition/rating claims without source data. Images remain placeholders until an image storage contract exists; do not render arbitrary legacy URLs/HTML.
- Stock sums nonnegative quantities as long; NULL/missing inventory gives zero. This is display only, B05 must recheck stock transactionally.
- Application defines DTO/service interface, Infrastructure owns EF read query; Web owns MVC/API adapters. No schema/seed writes. Cancellation propagated. Tests real isolated SQL cover filtering, literal search, pagination, incomplete data, detail/stock and 404.
- Q-UI-005 answered 03/10/2026: user supplied replacement product screenshot and requested continuation. Use large left image/right title-seller-price-stock grouping; no original image/account data committed. PD1 internet proposal superseded. Catalog grid is simplified clone design consistent with existing shell.
- Home and header lead to live catalog; legacy Home/Search redirects with query intact. Filters preserve query/category during pagination. Invalid MVC input returns 400 with correction guidance; missing listing 404.
- Contact seller expands an accessible native panel with seller/item context and honest messaging-unavailable notice until C04. B05 enables Buy It Now for displayed in-stock items and links authenticated Buyer checkout; stock is rechecked by [CHECKOUT](CHECKOUT.md). Out-of-stock button remains disabled. No cart/watchlist/ads/rating/condition/shipping promises without data.
- D02 (06/10/2026): Catalog detail hiển thị rating/review từ review gắn đơn đã giao ([REVIEWS](REVIEWS.md)); đây là nguồn dữ liệu thật nên không còn thuộc "rating claims without source data". Lưới catalog vẫn không hiển thị rating.

- D03 (06/10/2026): cạnh tên seller hiện `(score) x% positive feedback` hoặc "No feedback yet" từ [FEEDBACK](FEEDBACK.md); chưa có link hồ sơ (D04).

- D04 (06/10/2026): badge seller hiện `(score)` tích luỹ + "x% positive feedback (12 months)" và link `/Sellers/Feedback/{sellerId}` ([FEEDBACK](FEEDBACK.md#chi-tiết-triển-khai-d04)).

## Quyết định H04 (09/10/2026, trang kết quả tìm kiếm giống eBay)

Theo [ADR-003](../decisions/ADR-003-ui-fidelity-references.md); mẫu SR-1…3 trong [references](../references/ui/INDEX.md). Thay lưới catalog giản lược B04.

- **Tham số** `/Catalog`: `q`, `categoryId`, `seller` (H02), `sort` = best | price_asc | price_desc | newest, `format` = all | auction | bin, `minPrice`/`maxPrice` (0–100.000.000, min ≤ max, gồm cả hai đầu), `ipp` = 12 | 24 | 48 (eBay 60/120/240; giữ 12 mặc định cho dữ liệu demo nhỏ), `view` = list | gallery, `page`. Giá trị sai → 400 như B04.
- **Best Match** (xấp xỉ của clone): tiêu đề bắt đầu bằng từ khóa trước, rồi theo thứ tự listing. Price + Shipping = giá (clone giao miễn phí). Time: newly listed = listing mới nhất.
- **Auction**: clone chỉ có listing giá cố định, tab Auction luôn rỗng (trạng thái thật, không giả).
- **Cột lọc**: Category kèm số kết quả của cùng tìm kiếm khi bỏ lọc danh mục; Price (min/max); Buying Format. Không hiện Condition, Item location, Delivery options (schema không có dữ liệu).
- **Dòng kết quả (list)**: ảnh (H05), nút tim → thông báo Watchlist, tiêu đề, sao + số đánh giá (D06), giá, "Buy It Now", "Free delivery" (đúng với COD clone), "N sold" từ dòng đơn của seller, seller `x% positive (điểm gọn)`. Gallery dùng cùng dữ liệu dạng lưới.
- Đầu trang: "N results for q in category", "Save this search" → thông báo; tab All/Auction/Buy It Now; Sort ▾ và chế độ xem ▾ (menu `details`); Popular Filters = danh mục có kết quả; phân trang số + mũi tên, "Page x of y", Items Per Page. Trống: "No exact matches found".
