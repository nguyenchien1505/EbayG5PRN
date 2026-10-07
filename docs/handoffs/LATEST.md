# Handoff — F01: Docker image ứng dụng và Nginx HTTPS

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `df34f1a`. Runtime `a6dd65b` trên `task/F01-app-image-nginx-https`, fast-forward vào main sau khi [CI37558156027](https://github.com/Twiash/EbayGroup5/actions/runs/37558156027) pass. Checkpoint `git log --oneline --grep=F01`.
- Handoff trước: [E05](E05.md) (nhóm E xong).

## Hoàn tất

- Contract: [DEPLOYMENT](../architecture/DEPLOYMENT.md). Runbook: [LOCAL_INFRA](../runbooks/LOCAL_INFRA.md#app-image-và-nginx-https-f01-từ-07102026). **Không có migration.**
- **Image:**
  - `Dockerfile` có target `web` và `migrate`; SDK 8.0.425 / ASP.NET 8.0.31 pin theo digest.
  - Restore locked-mode; user `app`.
  - `.dockerignore` loại secrets/.env/artifacts/tests/docs (trừ schema nguồn).
- **Compose:**
  - `compose.app.yaml` dùng kèm `compose.yaml`: migrate → web (healthcheck `bash` `/health/ready`) → nginx `1.28.3-alpine` pin digest.
  - Mạng `app` có subnet cố định, Nginx dùng IP tĩnh; chỉ Nginx publish trên `127.0.0.1`.
  - Volume `dataprotection-keys`.
- **Nginx** `deploy/nginx/templates/default.conf.template`:
  - 8080 → 308; 8443 TLS 1.2/1.3 + HTTP/2.
  - WSS cho `/hubs/chat`.
  - Ghi đè XFF/XProto/Host **trong từng location** (Nginx không kế thừa header khi location tự khai báo).
  - `server_tokens off`; log không có query string.
- **App:**
  - `Security/ReverseProxy.cs`: forwarded headers chỉ khi có `ReverseProxy:KnownProxies/KnownNetworks`; rỗng = tắt, vì ASP.NET tin mọi nguồn khi danh sách rỗng.
  - `ReadinessProbe`: SQL + Redis → `/health/ready` 200/503, `no-store`.
  - Log `Microsoft.EntityFrameworkCore` hạ xuống Warning.
- **Scripts:**
  - `New-LocalTlsCertificate.ps1`: CA local không giữ khóa, server 397 ngày.
  - `Start-AppStack.ps1 [-Stop]`.
  - `Test-AppStack.ps1`: stack cô lập + `tests/EbayGroup5.StackSmoke`.
  - `Clear-TestArtifacts.ps1` gỡ được các project `ebaygroup5-stack-*`.
- **CI:** thêm bước "App image + Nginx HTTPS/WSS stack smoke" (timeout 35 phút) và upload log khi fail.
- **Tests:** `ReverseProxyTests` (3 test); `AccountHttpTests.Factory` nhận thêm `settings`.

## Verification

- [Evidence](../evidence/F01.md):
  - Test tích hợp 26/26; mutation "tin mọi nguồn" bị bắt.
  - Stack thật PASS 12/12 sau khi sửa 3 lỗi do smoke phát hiện: healthcheck dash, log EF, kế thừa header Nginx.
- **Test-CI local pass: 245/245 .NET + 20/20 Node**, build 0 warnings. **GitHub [CI37558156027](https://github.com/Twiash/EbayGroup5/actions/runs/37558156027) exact runtime `a6dd65b` success**: 5m09s, 245/245 .NET + 20/20 Node, **stack smoke 12/12**. Docs-only `[skip ci]` sau CI.
- **Chưa chạy `Start-AppStack.ps1` trên dữ liệu thật của người dùng** (port 8080/8443 cố định); để người dùng chạy khi cần. Smoke cô lập đã kiểm đầu cuối cùng image và cấu hình đó.
- Không kiểm bằng browser: CA local chưa được tin, và việc cài CA thuộc quyền người dùng.
- Không đụng `launchSettings.json`.

## Cho task sau (F02)

- Chạy hai replica web sau Nginx (upstream nhiều server, hoặc scale `web` với DNS của compose).
- DataProtection key dùng chung: hiện là volume của một instance; F02 cần key ring dùng chung (volume dùng chung hoặc Redis), có thể kèm mã hóa key.
- Rate limit dùng chung: limiter hiện ở bộ nhớ từng process; Hub chưa bị giới hạn invocation (OPS-02).
- Chat xuyên instance đã có Redis backplane (C03); cần smoke hai instance.
- HSTS khi có domain (F04).

## Tiếp theo / Jira

- F01 → **KAN-26 / TV5 Hải Lê: hoàn tất local một phần** (còn F02, F06, F07). Chưa sync live; không tạo worklog/review/issue.
