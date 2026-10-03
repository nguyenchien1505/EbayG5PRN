# Handoff — B01

03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Nền 553f266, task/B01-auth; merge fast-forward main sau kiểm tra. Checkpoint: git log --oneline --grep=B01.

## Hoàn tất

- [AUTH](../architecture/AUTH.md), [runbook](../runbooks/AUTH.md), [evidence](../evidence/B01.md).
- AccountService: normalized email, Buyer-only register, PasswordHasher V3 210k iterations; NULL/malformed/unknown role từ chối; generic login failure, compare-and-update rehash. Không schema/source/seed change.
- Cookie HttpOnly/Lax/Secure production, 30 phút/nonpersistent; antiforgery POST toàn MVC; local redirect, claim ID/name/role từ SQL. Forms DTO riêng.
- Register S1 và Login L1, success notice/error/password clearing, Me view và Sign out POST; navigation hoạt động. Q-UI-004 Answered: ảnh eBay Welcome back user gửi thay ảnh Agent Router nhầm. Không commit ảnh/email cá nhân; không social/reset/persistent login.
- DemoCredentials + Enable-DemoAuth.ps1: sinh .env.auth ignored, password riêng cho ba role, chỉ NULL/đúng identity/A06 journal, transaction/no-overwrite. Local enabled 3 rồi rerun 0; không đưa secrets vào chat.

## Kiểm chứng

18 SQL/HTTP cases pass, locked restore/build; docs/hash/whitespace pass. Browser register→login Buyer→logout pass; mobile login 390px ổn. Một user Browser Test hư cấu giữ trong demo DB; preview đã dừng. Ảnh local artifacts/b01/login-mobile.png ignored.
SQL tests tiếp tục giữ DB riêng, không DROP/prune/reset. NuGet Mvc.Testing 8.0.31 cùng test project A07 để CI chạy đủ 18.

## Tiếp theo B02

Đọc card B02 và AUTH, đúng requirement/source cần thiết; không nạp toàn repo/PLAN. B02 bổ sung authorization policies/object checks và security/chống abuse. SUP-01 vẫn Pending tới B02; UI-01 vẫn Pending toàn dự án.
Cookie chưa revoke bản copy sau logout, shared Data Protection keys/HTTPS proxy ở F01/F02; không social/reset/email verification. Không tự làm toàn backlog.

## Vận hành

GitHub private Twiash/EbayGroup5 origin. SDK . scripts/Use-LocalDotnet.ps1; config . scripts/Use-LocalInfrastructure.ps1; Development cho HTTP loopback. Initialize/seed/Enable-DemoAuth riêng, không startup migration/seed. Giữ .env.auth riêng tư.
SQL/Redis local healthy; immutable SQL checksum 4 assets. UI U1/M1/R1/P1/D1/D2/S1/L1 đã chốt. TV1–TV5/deadline còn chờ. Handoff A07 lưu riêng.
