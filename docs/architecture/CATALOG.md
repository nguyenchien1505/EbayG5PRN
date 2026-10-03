# B04 — Catalog contract

- Read-only public catalog/API `/api/v1/catalog`; fixed-price listings only (`IsAuction == false`), nonempty title, nonnegative price and existing Seller role. Legacy incomplete/auction listings excluded; detail returns 404 consistently.
- Query q trim/max100, category optional positive ID, page positive, size fixed12. Search literal title substring using EF parameterization, no wildcard semantics from user input. Sort ID ascending for stable pagination; count + bounded page, categories returned separately. Unknown category gives empty results; out-of-range page gives empty page, no silent reset.
- Currency USD for this demo (schema has no currency column). No shipping/condition/rating claims without source data. Images remain placeholders until an image storage contract exists; do not render arbitrary legacy URLs/HTML.
- Stock sums nonnegative quantities as long; NULL/missing inventory gives zero. This is display only, B05 must recheck stock transactionally.
- Application defines DTO/service interface, Infrastructure owns EF read query; Web owns MVC/API adapters. No schema/seed writes. Cancellation propagated. Tests real isolated SQL cover filtering, literal search, pagination, incomplete data, detail/stock and 404.
- UI Q-UI-005: user sẽ gửi ảnh khác, PD1 chưa được duyệt; U1 already approved for future messaging entry. No checkout/chat implementation in B04. UI work dependent on reference approval remains pending.
