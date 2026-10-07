# F06 — Kubernetes local và migration Job

Contract do agent soạn ngày 07/10/2026 trên main `695aef9`, trước khi sửa source.

Dựa trên [DEPLOYMENT](DEPLOYMENT.md) (F01: image `web`/`migrate`, Nginx HTTPS/WSS, smoke), [SCALE_OUT](SCALE_OUT.md) (F02: 2 replica, key ring + rate limit trên Redis), [JOURNEYS](JOURNEYS.md) (F03), [SECURITY_TESTS](SECURITY_TESTS.md) (F04), [LOAD](LOAD.md) (F05).

Người dùng chốt ngày 07/10/2026: **kind** (binary chính thức, kiểm SHA-256, vào `artifacts/tools/`, không commit; node image ghim digest) và **YAML + kustomize** (có sẵn trong kubectl, không Helm).

Acceptance (PLAN): **hai web replica, SQL/Redis có volume, probes đúng**, migration chạy bằng Job.

Requirement: chuẩn bị cho OPS-06 (F06–F08) và OPS-03 (F07). F06 không làm requirement nào Done một mình; ghi bằng chứng từng phần.

Ngoài phạm vi: CD từ GitHub Actions vào cluster (F07), rolling update/rollback có tải (F08), cluster thật/ingress controller/cert-manager, Helm.

## Công cụ

| Thành phần | Phiên bản |
|---|---|
| kind | v0.33.0 (`kind-windows-amd64` SHA-256 `4b22adaa…c9fc`, `kind-linux-amd64` `aee61515…8e0d`, đọc từ release GitHub 07/10/2026) |
| Node | `kindest/node:v1.37.0@sha256:a1ed56cf…0ae5` (image mặc định của kind v0.33.0) |
| kubectl | có sẵn (Docker Desktop v1.36.1 local; runner CI có sẵn) |

## Manifest (`deploy/k8s/base`, kustomize, namespace `ebaygroup5`)

| Đối tượng | Nội dung |
|---|---|
| `sqlserver` StatefulSet + headless Service | Image giống compose (ghim digest), PVC 10 Gi (`volumeClaimTemplates`), `fsGroup` 10001, giới hạn 3 Gi; **startup/readiness** = `sqlcmd SELECT 1`, **liveness** = TCP 1433 |
| `redis` StatefulSet + Service | Image giống compose, `requirepass` từ Secret, `appendonly yes` trên PVC 1 Gi; readiness `redis-cli ping`, liveness TCP |
| `migrate` Job | Image `ebaygroup5-migrate:<tag>`, chạy DbUp + cấp quyền login app (giống compose). Script đợi SQL sẵn sàng → xóa Job cũ → tạo Job → đợi `complete` rồi mới triển khai web. Chạy lại không đổi gì (DbUp idempotent) |
| `web` Deployment (**2 replica**) + headless Service | Image `ebaygroup5-web:<tag>`, cấu hình như `compose.app.yaml`; **startupProbe** `/health/live`, **livenessProbe** `/health/live`, **readinessProbe** `/health/ready` (SQL + Redis); non-root, không leo quyền, drop ALL capabilities; `topologySpreadConstraints`/anti-affinity mềm |
| `nginx` Deployment + Service NodePort | Dùng chung template F01; template thêm `${NGINX_RESOLVER}` và `${WEB_UPSTREAM}` (compose: `127.0.0.11` / `web:8080`; K8s: `kube-dns.kube-system.svc.cluster.local` / headless service web) → round-robin trên IP từng pod, giống compose; TLS từ Secret |
| NetworkPolicy | web chỉ nhận từ nginx; SQL chỉ từ web + migrate; Redis chỉ từ web |
| Secret | Tạo bởi script (mật khẩu ngẫu nhiên; TLS từ `New-LocalTlsCertificate.ps1`), **không có trong repo** |

Tag image: `<git short SHA>` (thêm `-dirty-<hex>` nếu cây làm việc có thay đổi); build từ `Dockerfile`, nạp vào node bằng `kind load docker-image` (`imagePullPolicy: Never`). Manifest dùng tag `local`, script thay bằng tag thật khi render.

Proxy/admin: web tin forwarded header chỉ từ dải pod (`ReverseProxy:KnownNetworks` = pod CIDR của kind) và NetworkPolicy chỉ cho nginx gọi web; Service nginx `externalTrafficPolicy: Local` giữ IP client; admin chỉ từ IP host như thấy trong cluster (script xác định, xem evidence).

## Scripts

- `scripts/Install-Kind.ps1`: tải kind theo OS, kiểm SHA-256 ghim, in đường dẫn.
- `scripts/Deploy-K8s.ps1` (dùng chung): build + load image, tạo/ cập nhật Secret, áp manifest theo thứ tự (SQL/Redis → đợi ready → migrate Job → web/nginx → đợi rollout).
- `scripts/Start-K8s.ps1`: cluster của người dùng `ebaygroup5` (https://localhost:9443, http 9080 — không đụng stack compose 8443/8080), secret lưu ở `secrets/k8s/` (ignore) để giữ dữ liệu qua các lần chạy; `-Stop` xóa cluster (hỏi lại trước khi xóa dữ liệu: dùng `-Stop -Confirm`).
- `scripts/Test-K8s.ps1`: cluster cô lập `ebaygroup5-k8s-<hex>`, cổng ngẫu nhiên, rồi kiểm:
  1. Smoke F01–F04 (`EbayGroup5.StackSmoke`) và Journeys J1–J7 qua NodePort HTTPS.
  2. 2 pod web Ready, cả hai nhận request (log nginx `upstream=` có 2 IP pod).
  3. Probes khai báo đúng (đọc spec); migrate Job `Complete`, chạy lại Job → 0 migration mới.
  4. **Volume:** xóa pod `sqlserver-0` → dữ liệu (đơn của journey) còn; xóa pod `redis-0` → cookie đăng nhập phát trước đó vẫn hợp lệ (key ring giữ trên PVC); xóa một pod web → cookie vẫn hợp lệ, Deployment tự đủ 2 replica.
  5. NetworkPolicy: pod thử không kết nối được SQL 1433.
  Pass → xóa cluster; fail → giữ lại, log vào `artifacts/k8s/<hex>/`.
- `Clear-TestArtifacts.ps1` liệt kê/xóa thêm cluster kind `ebaygroup5-k8s-<hex>`.
- CI: bước `Test-K8s.ps1` (kind trên runner).

## Điều chỉnh khi triển khai (07/10/2026)

- **Admin network:** IP host như thấy trong cluster là gateway của mạng Docker `kind` (đo được `172.19.0.1` trên Docker Desktop/Windows). `Deploy-K8s.ps1` khởi tạo bằng địa chỉ TEST-NET-1 `192.0.2.1` (admin đóng), gửi một request qua host port, đọc địa chỉ trong log nginx rồi ghi vào ConfigMap và restart web.
- **Thay pod không treo request:** lần chạy đầu, journey J1 treo 40 s vì nginx còn giữ IP của pod web cũ (DNS headless cache 10 s, `proxy_connect_timeout` mặc định 60 s). Sửa trong template dùng chung: `resolver … valid=5s`, `proxy_connect_timeout 2s`, `server … resolve max_fails=1 fail_timeout=5s` (để `proxy_next_upstream` mặc định thử replica khác); pod web có `preStop: sleep 10s` để nginx bỏ pod trước khi Kestrel dừng. Compose dùng cùng template, smoke/journeys compose vẫn pass. Đây cũng là nền cho F08 (rolling update không 5xx).
- **Probe HTTP** gửi `Host: localhost` (AllowedHosts); `runAsUser` số (1654 cho image .NET, 10001 SQL, 999 Redis) vì `runAsNonRoot` cần UID số.

## Chạy

- Cluster của người dùng: `pwsh -NoProfile -File scripts/Start-K8s.ps1` → https://localhost:9443 (CA `secrets/tls/ca.crt`; không đụng stack compose 8443). Chạy lại = deploy commit hiện tại (migration Job trước). `-Stop` dừng node (giữ dữ liệu), `-Delete` xóa cluster + dữ liệu (xác nhận bằng tên cluster, hoặc `-Force`).
- Kiểm thử cô lập: `pwsh -NoProfile -File scripts/Test-K8s.ps1` (~10–15 phút lần đầu vì tải node image và SQL Server trong node). Không chạy song song Test-CI.
- `Clear-TestArtifacts.ps1` liệt kê/xóa cluster `ebaygroup5-k8s-<12 hex>` còn sót; không đụng cluster `ebaygroup5`.
- Xem: `kubectl --context kind-ebaygroup5 -n ebaygroup5 get pods,pvc,jobs`.
