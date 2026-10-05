# Handoff — B04 backend checkpoint

03/10/2026. Baseline 8bd9f9a; task/B04-catalog. Checkpoint git log --oneline --grep=B04. Task InProgress, not Done.

## Hoàn tất

- CATALOG contract, Application ICatalogService/DTOs, Infrastructure CatalogService SQL query, public CatalogApiController at /api/v1/catalog.
- Fixed-price valid seller/title/price only, title literal contains, category filter, stable ID pagination12, detail/stock. Limits/cancellation and ProblemDetails 400/404. No DB migration/seed change.
- 31 SQL/HTTP tests pass. EF constructor projection translation failure fixed with property initialization; full SQL tests rerun pass.
- PROJECT_COMPARISON explains old two-host Frontend HTTP Backend versus current layered single Web host, with tradeoffs and actual incompleteness.

## Tiếp tục B04

Q-UI-005: user replied “Tôi sẽ gửi ảnh khác”; wait for replacement image. PD1 not approved. Proposed reference was: PD1 https://ir.ebaystatic.com/pictures/hk/new-to-ebay/P3-6.png from official https://www.ebay.com.hk/import/newtoebay (undated) shown in chat. Asked user to approve simplified image-left/title-price-stock-seller-right; contact U1 already approved. Do not assume timeout means approved. Implement MVC catalog/detail, connect header/Home search, contact seller entry and responsive browser checks after answer. SUP-02/UI-01 Pending.

B03 current UI still honestly says marketplace/search unavailable. No half-wired UI. Backend API is ready. Preserve user's untracked src/EbayGroup5.Web/Properties/launchSettings.json; did not create/modify/stage it.

## Vận hành

Private origin Twiash/EbayGroup5; authenticated gh. SDK via scripts/Use-LocalDotnet.ps1, config via Use-LocalInfrastructure.ps1, tests via Test-Database.ps1. Secrets ignored, never print. Local SQL/Redis and isolated test DBs retained. No preview running. No subagents. Prior handoff B03.md. Full comparison question answered from read-only old source; never alter ProjectCode.

PASS CI Ubuntu [run 37115704600](https://github.com/Twiash/EbayGroup5/actions/runs/37115704600) tại 5ebc54f, job 2m4s; build/test và artifact thành công. Verify-Planning, Verify-Migrations, diff whitespace pass. User sẽ gửi ảnh khác cho Q-UI-005; PD1 không được duyệt.
