# F01 — Docker image ứng dụng và Nginx HTTPS

Contract do agent soạn ngày 07/10/2026 trên main `df34f1a`, trước khi sửa source.

**Dependency:**

- C05 [CHAT](CHAT.md): WS-only, Origin allowlist, Redis backplane.
- D05/E05: admin + AdminNetwork.
- B02 [SECURITY](SECURITY.md): "Forwarded headers are not trusted; deployed proxy/network configuration belongs to F01".

**Quyết định đã duyệt:** PLAN §6–7 và ADR-001:

- Docker/Nginx local; pin image, không dùng latest.
- Compose có chế độ dev nhẹ.
- CI build image.
- Image gắn commit SHA (CD ở F07).

Ngoài phạm vi:

- Nhiều instance, DataProtection key dùng chung, rate limit cho Hub (F02).
- K8s (F06), CD (F07). Từ F06, template Nginx nhận `${NGINX_RESOLVER}`/`${WEB_UPSTREAM}` (compose: `127.0.0.11`/`web:8080`), DNS `valid=5s`, `proxy_connect_timeout 2s` và `max_fails=1 fail_timeout=5s` để bỏ nhanh replica đã thay ([K8S](K8S.md)).
- Đo tải (F05), kiểm thử bảo mật (F04).
- Chứng chỉ công khai: clone chỉ chạy local.

Requirement:

- **G5-N01**: phần HTTPS/WSS qua proxy. G5-N01 còn F04 nên vẫn Pending.
- **OPS-01**: phần Nginx. Phân tải hai instance là F02, nên vẫn Pending.

## Thành phần

| File | Nội dung |
|---|---|
| `Dockerfile` | Multi-stage. Build trên `dotnet/sdk:8.0.427-bookworm-slim@sha256:ff39…`, `dotnet restore --locked-mode` chỉ cho Web và Database. Target **`web`** và **`migrate`** đều trên `dotnet/aspnet:8.0.31-bookworm-slim@sha256:a3cd…`, chạy user `app` (không root). Web nghe HTTP 8080 trong mạng nội bộ |
| `.dockerignore` | Loại `bin/obj`, `artifacts`, `secrets`, `.env*`, `.git`, `tests`, `docs` (trừ bản schema nguồn mà project Database embed), `src/EbayGroup5.Web/Properties` |
| `deploy/nginx/templates/default.conf.template` | `nginx:1.28.3-alpine@sha256:a8b3…`. Port 8080 → 308 sang HTTPS. Port 8443 TLS 1.2/1.3 (HTTP/2). `/hubs/chat` có Upgrade/Connection cho WebSocket, read timeout 120 giây. Bật `server_tokens off`. Log không có query string. **Ghi đè** `X-Forwarded-For` bằng `$remote_addr`, đặt `X-Forwarded-Proto https`. `client_max_body_size 1m` |
| `compose.app.yaml` | Dùng kèm `compose.yaml`: `migrate` (chạy một lần) → `web` (healthcheck `/health/ready`) → `nginx`. Mạng `app` có subnet cố định để Nginx có IP tĩnh. Web nằm ở cả `default` (SQL/Redis) và `app`. Chỉ Nginx publish port, trên `127.0.0.1`. Từ F02: 2 replica web và key ring trên Redis (xem [SCALE_OUT](SCALE_OUT.md)); volume `dataprotection-keys` của F01 đã bỏ |
| `scripts/New-LocalTlsCertificate.ps1` | Dùng .NET tạo CA local (ECDSA P-256, 1 năm) và chứng chỉ server ký bởi CA đó (SAN `localhost`, `127.0.0.1`, `::1`; EKU serverAuth; 397 ngày). Ghi PEM vào `secrets/tls/` (đã ignore). **Không lưu khóa của CA**, nên CA không ký thêm được chứng chỉ nào khác. Script không tự cài CA vào trust store; runbook ghi lệnh để người dùng tự chạy |
| `scripts/Start-AppStack.ps1` | Chạy stack demo trên project `ebaygroup5-local` của người dùng với `--env-file .env --env-file .env.database`; tham số `-Stop` để dừng web/nginx. Không đụng volume |
| `scripts/Test-AppStack.ps1` + `tests/EbayGroup5.StackSmoke` | Smoke test cô lập, xem mục Kiểm tra |

## Thay đổi ứng dụng

- **Forwarded headers**: chỉ bật khi cấu hình `ReverseProxy:KnownProxies` (IP) hoặc `ReverseProxy:KnownNetworks` (CIDR).
  - Lý do: ASP.NET tin **mọi** nguồn khi cả hai danh sách đều rỗng, nên khi không cấu hình thì không gọi middleware.
  - Khi bật: chỉ áp `XForwardedFor | XForwardedProto`, `ForwardLimit = 1`, chỉ nhận từ proxy đã khai báo.
  - Kết quả: `RemoteIpAddress` là IP client mà Nginx thấy, nên AdminNetwork và rate limit dùng đúng IP. Scheme là https, nên cookie `Secure` hoạt động đúng.
  - Header từ nguồn không khai báo bị bỏ qua.
- **`/health/ready`**: kiểm tra SQL (`CanConnectAsync`) và Redis (PING qua một kết nối dùng riêng cho readiness, tạo lazy).
  - Kết quả: 200 `Ready` hoặc 503 `Not ready`; không trả chi tiết lỗi; `no-store`.
  - `/health/live` giữ nguyên.
- **Cấu hình compose cho web:**
  - `ASPNETCORE_ENVIRONMENT=Production` (cookie `Secure` Always).
  - `Chat:AllowedOrigins` = `https://localhost:{HTTPS_PORT}`, `https://127.0.0.1:{HTTPS_PORT}`.
  - `Security:AdminAllowedAddresses` = gateway của mạng `app`. Trên Docker Desktop, kết nối từ máy host vào port đã publish xuất hiện từ gateway. Như vậy "mạng demo" chính là máy chạy Docker; chỉ IP thật mới qua được, header giả mạo không có tác dụng.

**HSTS:** không bật cho `localhost`. HSTS bị trình duyệt cache theo host và sẽ ảnh hưởng mọi dự án dev khác trên localhost. Khi triển khai có domain thật thì bật ở Nginx; ghi chú cho F04.

**Ghi nhận khi triển khai (07/10/2026):**

- **Nginx chỉ kế thừa `proxy_set_header` từ khối `server` khi `location` không khai báo header nào.** Hai location đều khai báo Upgrade/Connection, nên Host và `X-Forwarded-*` phải lặp lại trong từng location.
  - Lần chạy smoke đầu nhận 400 (host filtering) vì app nhận `Host: ebaygroup5_web`. Nếu chưa phát hiện, XFF cũng sẽ không được ghi đè.
  - Smoke đã bắt lỗi này trước khi commit.
- **F03:** `Host` phải là `$http_host` (giữ port công khai). Với `$host`, các redirect tuyệt đối do app tạo (ví dụ về Login) bị mất port. Lỗi được journey phát hiện; smoke kiểm thêm origin của redirect.
- **Healthcheck dùng `bash -c`** (không dùng `CMD-SHELL`): `/bin/sh` là dash, không có `/dev/tcp`.
- **Log EF Core:** `Microsoft.EntityFrameworkCore` đặt mức Warning trong `appsettings.json`, vì ở Production EF log toàn bộ câu SQL (không kèm tham số) ở mức Information.
- **IP gateway:** Nginx ghi nhận kết nối từ host xuất hiện từ gateway của mạng `app` (ví dụ `172.30.240.1` trong project test). Điều này xác nhận cấu hình `Security:AdminAllowedAddresses`.

## Cách chạy (runbook)

```powershell
pwsh -NoProfile -File scripts/New-LocalTlsCertificate.ps1          # một lần; tạo secrets/tls
pwsh -NoProfile -File scripts/Start-AppStack.ps1                    # build + migrate + web + nginx
# https://localhost:8443  (HTTP 8080 tự chuyển sang HTTPS)
pwsh -NoProfile -File scripts/Start-AppStack.ps1 -Stop              # dừng web/nginx; SQL/Redis giữ nguyên
```

Muốn trình duyệt tin chứng chỉ (người dùng tự quyết, vì đây là thay đổi thiết lập bảo mật của máy):

```powershell
Import-Certificate -FilePath secrets/tls/ca.crt -CertStoreLocation Cert:\CurrentUser\Root
```

## Kiểm tra

- **Test tích hợp** (xUnit, TestServer):
  - Khi không cấu hình proxy: `X-Forwarded-For` bị bỏ qua (admin từ 127.0.0.1 vẫn qua, header giả `203.0.113.7` không có tác dụng).
  - Khi cấu hình proxy `172.30.250.10`: request từ proxy đó với XFF `203.0.113.7` → admin 403; XFF `127.0.0.1` → admin được phép. Cùng header đó gửi từ IP không khai báo thì bị bỏ qua.
  - `/health/ready`: 200 khi có SQL và Redis; 503 khi DB không tồn tại, không lộ chi tiết.
- **`scripts/Test-AppStack.ps1`** (cũng chạy trong GitHub CI):
  - Tạo project `ebaygroup5-stack-<32 hex>` với mật khẩu ngẫu nhiên, port trống ngẫu nhiên, subnet riêng và chứng chỉ tạm.
  - Build image thật, chạy migrate → web → nginx, sau đó chạy `EbayGroup5.StackSmoke`:
    - TLS hợp lệ theo CA tạm: chain + tên; không bỏ qua kiểm tra chứng chỉ.
    - HTTP → 308 về đúng URL HTTPS.
    - `/health/live`, `/health/ready` qua HTTPS. Header `Server` không có version.
    - Register + login qua HTTPS: cookie auth có `secure`, `httponly`, `samesite=lax`; `/Account/Me` 200.
    - **WSS** `/hubs/chat` với cookie và Origin đúng → SignalR handshake `{}`. Origin sai → bị từ chối.
    - **XFF giả mạo** (mỗi request một IP khác) vẫn bị gom chung một partition rate limit → nhận 429 trước request thứ 12 (Nginx ghi đè header).
  - Migrate thoát mã 0.
  - **Pass**: gỡ đúng project đó (container, network, đúng tên volume `<project>_*`, image tag tạm).
  - **Fail**: giữ lại để chẩn đoán và ghi log vào `artifacts/stack/<hex>/`. `Clear-TestArtifacts.ps1` liệt kê và gỡ các project `ebaygroup5-stack-*`.
- **Thủ công:** `Start-AppStack.ps1` trên dữ liệu thật của người dùng, kiểm tra bằng `curl --cacert`.
