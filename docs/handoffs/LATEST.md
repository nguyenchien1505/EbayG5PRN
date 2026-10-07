# Handoff — F02: nhiều instance, key dùng chung, rate limit dùng chung

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `774e7f5`. Runtime trên `task/F02-multi-instance-shared-keys`; checkpoint `git log --oneline --grep=F02`. GitHub CI: xem mục Verification.
- Handoff trước: [F01](F01.md).

## Hoàn tất

- Contract: [SCALE_OUT](../architecture/SCALE_OUT.md). **Không có migration.**
- **Package mới:** `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` 8.0.31; lock file của Web và Database.Tests được cập nhật.
- **App:**
  - `Security/SharedRateLimit.cs`:
    - `RedisConnection` là singleton lazy, dùng chung cho readiness, limiter và key ring.
    - `SharedRateLimiter`: cửa sổ cố định trên Redis (Lua), dự phòng trong bộ nhớ của instance.
    - `[SharedRateLimit]` + middleware sau authentication, trước antiforgery.
  - **Policy:**
    - `auth` 10/60 giây theo IP: Login/Register.
    - `chat-api` 120/60 giây theo user: POST chat.
    - `chat-hub` 120/60 giây theo user: mọi invocation, trong `ChatHubFilter`.
  - Bỏ `AddRateLimiter`/`UseRateLimiter` của ASP.NET.
  - **DataProtection:** `SetApplicationName("EbayGroup5")`; `DataProtection:KeyStore=Redis` dùng `RedisXmlRepository`. Giá trị khác → app dừng khi khởi động.
  - `ReadinessProbe` dùng `RedisConnection`.
  - `chat.js` hiểu mã 429: hiện thông báo; tin chờ tự retry với backoff theo `retryableSend` của C04.
- **Stack:**
  - `compose.app.yaml`: `web` chạy `deploy.replicas: ${WEB_REPLICAS:-2}`, `DataProtection__KeyStore=Redis`; bỏ volume `dataprotection-keys`.
  - Nginx: `resolver 127.0.0.11` + `server web:8080 resolve` (zone), log thêm `upstream=`.
  - Smoke: thêm kiểm tra 12/12 `/Account/Me`. `Test-AppStack.ps1` kiểm ≥ 2 replica đang chạy và ≥ 2 upstream trong log Nginx.
- **Tests:**
  - `ScaleOutTests`: 5 test.
  - `Factory` và `ChatSocketHost` có `Redis:KeyPrefix` riêng (host test cùng fixture dùng chung prefix); `ChatSocketHost` nhận thêm `settings`.

## Verification

- [Evidence](../evidence/F02.md):
  - Test tập trung 94/94.
  - Mutation 2/2 bị bắt.
  - Stack thật 2 replica PASS: 2 upstream, XFF giả nhận 429 nhờ bộ đếm chung.
- **Test-CI local pass: 250/250 .NET + 20/20 Node**, build 0 warnings. GitHub CI: ghi sau khi chạy (commit docs-only `[skip ci]`).
- Không đụng `launchSettings.json`.

## Requirement

- **OPS-02: Done** (F04 sẽ thử vượt giới hạn).
- **Còn Pending:**
  - OPS-01: Nginx + 2 instance + chat xuyên instance đã đạt ở test hai host; đo qua stack dưới tải thuộc F05.
  - G5-N04 (F05). G5-N01 (F04).

## Cho task sau

- **F03:** test tích hợp và hành trình trình duyệt xuyên suốt. Có thể chạy trên stack của `Test-AppStack.ps1`.
- **F04:** HSTS khi có domain; thử vượt rate limit; key ring chưa được mã hóa khi lưu.
- **F05:** JMeter qua Nginx (2 replica); `RateLimits` có thể cần nới cho tài khoản test tải. Chỉ nới trong môi trường đo, không đổi mặc định.
- **F06:** K8s dùng chung `Redis:KeyPrefix` cho các pod; readiness `/health/ready`.

## Tiếp theo / Jira

- F01 + F02 → **KAN-26 / TV5 Hải Lê: hoàn tất local một phần** (còn F06, F07). Chưa sync live; không tạo worklog/review/issue.
