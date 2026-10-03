# Handoff — B03

03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Baseline 4b1d31d; task/B03-shell, fast-forward main after verification. Checkpoint: git log --oneline --grep=B03.

## Hoàn tất

- [SHELL](../architecture/SHELL.md), [evidence](../evidence/B03.md). B02 handoff archived in B02.md.
- White header, brand wordmark, search pill, native My eBay details menu, account-aware greeting, footer and skip link. Only active account routes shown. Auth pages keep simplified S1/L1 form, omit search.
- Home welcoming/empty state; Home/Search encodes/trims/limits query, explicitly says search not available yet. B04 supplies catalog search. No false zero-result query or invented products.
- GET status re-execution renders 404 recovery view while preserving HTTP code. Non-404 bodyless statuses stay bodyless; POST and exception JSON unchanged.
- Build 0 warnings/errors; 29 SQL/HTTP tests pass. Browser desktop/mobile 390 and width check320; menu keyboard, anonymous account redirect, search query and 404 verified. Screenshots local ignored artifacts/b03.
- No schema/seed/dependency change. SUP-01 stays Done; UI-01/SUP-02–05 Pending.

## Tiếp theo B04

Read B04, A06 seed/catalog entities and SHELL. Public product-detail reference still needs source check per UI_REFERENCE; do not infer it from approved S1/L1. Header does not yet have category filtering. Replace Home/Search unavailable state with catalog query/filters/pagination in B04.

No subagents. One task per continuation. No fake links into future orders/messages/admin modules. Correct L1 user screenshot is eBay Welcome back; Agent Router screenshot was mistaken. Other approved U1/M1/R1/P1/D1/D2/S1 references remain in docs.

## Vận hành

Private origin Twiash/EbayGroup5; gh authenticated. SDK . scripts/Use-LocalDotnet.ps1; . scripts/Use-LocalInfrastructure.ps1 loads config without exposing values. Local SQL/Redis and isolated test DBs retained. .env.auth contains local demo credentials, ignored. No secret output. Preview stop after verification. F01/F02 proxy trust/HTTPS/shared keys/distributed limiting remain Pending; TV1–TV5/deadline questions open.

PASS GitHub CI Ubuntu [run 37114724015](https://github.com/Twiash/EbayGroup5/actions/runs/37114724015) tại 7dbfa5f: build/tests/TRX upload thành công, job 1m45s. Verify-Planning, Verify-Migrations và git diff --check pass; preview đã dừng.
