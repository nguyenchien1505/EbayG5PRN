# F02 — Nhiều instance, key dùng chung, rate limit dùng chung

Contract do agent soạn ngày 07/10/2026 trên main `774e7f5`, trước khi sửa source. Task này dựa trên:

- [DEPLOYMENT](DEPLOYMENT.md) (F01): image, Nginx, `compose.app.yaml`, smoke `Test-AppStack.ps1`.
- [CHAT](CHAT.md): Redis backplane C03, WS-only + SkipNegotiation nên không cần sticky session; hai Kestrel host đã có test xuyên instance.

Requirement:

- **OPS-01:** Nginx phân tải ít nhất hai app instance; chat xuyên instance.
- **OPS-02:** HTTP trả 429 theo policy; Hub giới hạn invocation; counter dùng chung. Phần kiểm thử bảo mật (F04) vẫn còn.
- **G5-N04:** phần nhiều instance. Đo dataset lớn thuộc F05.

Acceptance của task: **đổi instance không mất login; Hub cũng bị giới hạn.**

Ngoài phạm vi:

- K8s (F06).
- Đo tải (F05).
- Mã hóa key ring khi lưu bằng chứng chỉ/KMS: ghi là giới hạn của bản demo local.

## Quyết định

**1. Hai replica web.**

- `compose.app.yaml`: `web` dùng `deploy.replicas: ${WEB_REPLICAS:-2}`.
- Nginx dùng `resolver 127.0.0.11` (Docker DNS) với `upstream { zone …; server web:8080 resolve; }` (có trong nginx open source từ 1.27.3). Nginx phân tải round-robin trên mọi IP của `web` và tự cập nhật khi replica đổi IP.
- Log Nginx thêm `upstream=$upstream_addr` để kiểm chứng phân tải.

**2. DataProtection key ring dùng chung.**

- `SetApplicationName("EbayGroup5")` cố định, để mọi instance cùng một "purpose".
- Khi `DataProtection:KeyStore = Redis`: key ring lưu ở Redis (`RedisXmlRepository`, package `Microsoft.AspNetCore.DataProtection.StackExchangeRedis` 8.0.33), key `{Redis:KeyPrefix}:DataProtection-Keys`.
- Compose bật Redis; dev (`dotnet run`) giữ file system như cũ.
- Bỏ volume `dataprotection-keys` của F01.
- **Giới hạn:** key lưu dạng XML **không mã hóa**. Bù lại, Redis có mật khẩu và chỉ nằm trong mạng nội bộ. Mất volume Redis đồng nghĩa mọi người phải đăng nhập lại.
- Đổi application name làm cookie cũ trên máy dev mất hiệu lực một lần.

**3. Rate limit dùng chung** (thay `AddRateLimiter` chạy trong bộ nhớ của từng process):

- Thuật toán: cửa sổ cố định trên Redis. Một script Lua thực hiện `INCR` + `PEXPIRE` atomic, key `{prefix}:rl:{policy}:{window}:{partition}`.
- Middleware chạy ngay sau authentication (vị trí cũ của `UseRateLimiter`), **trước** antiforgery/MVC, nên request bị từ chối vì thiếu token vẫn bị đếm.
- Endpoint khai báo bằng `[SharedRateLimit("policy")]`.
- **Redis không sẵn sàng:** chuyển sang cửa sổ cố định trong bộ nhớ của instance. Vẫn giới hạn, chỉ là không dùng chung; log cảnh báo tối đa một lần mỗi phút, không kèm partition.
  - Không fail-closed, để mất Redis không khóa đăng nhập.
  - Không fail-open, để không mất giới hạn.

| Policy | Áp cho | Partition | Mặc định |
|---|---|---|---|
| `auth` | POST `/Account/Login`, `/Account/Register` | IP client (sau forwarded headers F01) | 10 / 60 giây (giữ như B02) |
| `chat-api` | POST chat REST: mở hội thoại, gửi tin, đánh dấu đã đọc | user id | 120 / 60 giây |
| `chat-hub` | **mọi invocation Hub** (`ChatHubFilter`) | user id | 120 / 60 giây |

- Cấu hình: `RateLimits:{policy}:PermitLimit` / `WindowSeconds`. Giá trị phải nằm trong 1–10000 và 1–3600; sai thì app dừng khi khởi động.
- **Kết quả khi bị chặn:**
  - HTTP: 429, có `Retry-After` (số giây còn lại của cửa sổ). Chat REST trả thêm ProblemDetails "Too many requests."
  - Hub: `HubException("429: You're doing that too often. Wait a moment and try again.")`. Kết nối vẫn mở.
  - Client chat hiện thông báo tương ứng. Tin đang chờ được giữ nguyên (cùng UUID, gửi lại không bị trùng) và tự thử lại với backoff, vì C04 đã xếp 429 vào nhóm lỗi tự retry (`retryableSend`, đã có Node test).
- `Redis:KeyPrefix` (mặc định `EbayGroup5`) tách key giữa các môi trường. Mỗi host test có prefix riêng, nên các test không dùng chung bộ đếm.

**4. Kết nối Redis dùng chung** (`RedisConnection` singleton, lazy, `AbortOnConnectFail=false`) cho readiness, rate limit và key ring. Backplane SignalR giữ kết nối riêng như C03.

## Kiểm tra

- **Rate limiter (Redis thật):**
  - Hai instance limiter dùng chung prefix → tổng permit đúng bằng giới hạn.
  - Partition và policy độc lập với nhau.
  - Hết cửa sổ thì mở lại.
  - Redis không tới được → giới hạn trong bộ nhớ vẫn chặn.
- **HTTP với hai host** (cùng prefix Redis):
  - Login ở host A, cookie dùng được ở host B (`/Account/Me` 200).
  - Key ring khác nhau (prefix khác) → host B không nhận cookie (đối chứng).
  - Giới hạn `auth` dùng chung: 10 request chia cho A và B, request thứ 11 → 429 kèm `Retry-After`.
  - Test 429 cũ của B02 vẫn pass.
- **Hub:** với giới hạn nhỏ, invocation vượt ngưỡng → `429:` và kết nối vẫn dùng được sau khi cửa sổ mở lại. Chat REST vượt ngưỡng → 429 ProblemDetails.
- **Stack thật** (`Test-AppStack.ps1`, 2 replica):
  - 12 lần `/Account/Me` liên tiếp đều 200, và log Nginx cho thấy ≥ 2 upstream khác nhau phục vụ các request đó: đổi instance không mất login.
  - Kiểm tra XFF giả của F01 nhận 429 trước request thứ 12, chứng minh bộ đếm dùng chung. Nếu không dùng chung, round-robin hai instance cho tới 20 request.
  - WSS handshake vẫn qua.
- **Chat xuyên instance:** dẫn chứng các test hai host C03 (Redis backplane); stack thật dùng cùng backplane.
