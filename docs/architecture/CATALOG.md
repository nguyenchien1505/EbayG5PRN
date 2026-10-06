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
