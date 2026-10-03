# Handoff — B02

03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Baseline 6e920e8; task/B02-security, fast-forward main after checks. Checkpoint: git log --oneline --grep=B02.

## Hoàn tất

- [SECURITY contract](../architecture/SECURITY.md), [evidence](../evidence/B02.md). B01 handoff archived in B01.md.
- Account Details loads SQL resource; own only, foreign/missing 404. Buyer/Seller/AdminDemo policies; Admin direct-IP gate on authenticated requests defaults loopback IPv4/IPv6; missing IP denied, no trusted forwarded headers.
- Cookie DB validation each request: role changes/deleted user/NULL credential invalidate session. Forbidden 403; generic exception response in all environments.
- Login/register POST combined rate 10/minute/direct-IP/process; no queue, 429; global antiforgery retained.
- 28 SQL/HTTP tests pass; build zero warnings/errors. No schema or seed changes. SUP-01 Done; SUP-02–05/UI-01 Pending.

## Giới hạn

Admin policy probes are test-only; no business admin screen added. Order/chat/dispute ownership checks belong to later tasks. Proxy trust, HTTPS, shared keys, distributed limiting F01/F02. Copied cookie after logout/password change is not globally revoked.

## Tiếp theo

B03 shell/navigation; read its card and A03 UI contracts. Correct L1 is eBay Welcome back supplied by user, not mistaken Agent Router image; S1 approved. No need ask again. No subagents; one task per continuation.

GitHub private Twiash/EbayGroup5 origin; gh authenticated. SDK . scripts/Use-LocalDotnet.ps1; local config . scripts/Use-LocalInfrastructure.ps1. SQL/Redis local retained; .env.auth contains separate local demo credentials, ignored. Never print secrets. Test databases retained. TV1–TV5/deadline questions remain open.

PASS GitHub Actions Ubuntu [run 37114282385](https://github.com/Twiash/EbayGroup5/actions/runs/37114282385) tại 1dd0922: build/test và TRX upload thành công, job 1m58s. Verify-Planning, Verify-Migrations và git diff --check pass.
