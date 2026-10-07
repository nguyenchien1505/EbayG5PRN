# Handoff — F06: Kubernetes local và migration Job

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `695aef9`. Branch `task/F06-k8s-local`; checkpoint `git log --oneline --grep=F06`. Runtime `5936f01`, fast-forward vào main sau khi [CI37656323271](https://github.com/Twiash/EbayGroup5/actions/runs/37656323271) pass (Test-K8s chạy trên runner).
- Handoff trước: [F05](F05.md) (gồm bổ sung Q-PERF-001: sửa deadlock ghi đơn đồng thời, DbUp 0014).
- Người dùng chốt 07/10/2026: kind (binary chính thức + SHA-256, node image ghim digest) và YAML + kustomize.

## Hoàn tất

- Contract: [K8S](../architecture/K8S.md). **Không có migration**; không đổi code app.
- `deploy/k8s/base`: SQL/Redis StatefulSet + PVC, web Deployment 2 replica (startup/liveness `/health/live`, readiness `/health/ready`, `Host: localhost`, non-root, read-only rootfs, preStop 10 s), nginx NodePort (`externalTrafficPolicy: Local`), NetworkPolicy; `deploy/k8s/jobs/migrate.yaml` (migrate + `--seed-demo`/`--enable-demo-auth`).
- Scripts: `Install-Kind.ps1`, `Deploy-K8s.ps1` (build + `kind load`, Secret qua stdin, ConfigMap, SQL/Redis → migrate Job → web/nginx, đo IP host cho admin), `Start-K8s.ps1` (cluster `ebaygroup5`, https://localhost:9443, `-Stop`/`-Delete`), `Test-K8s.ps1` (cluster cô lập); `Clear-TestArtifacts.ps1` dọn cluster test; CI thêm `Test-K8s.ps1` (timeout job 75 phút).
- Template nginx dùng chung: `${NGINX_RESOLVER}`/`${WEB_UPSTREAM}`, DNS `valid=5s`, `proxy_connect_timeout 2s`, `max_fails=1 fail_timeout=5s` (lỗi thật: request treo 40 s khi pod web bị thay).

## Verification

- [Evidence](../evidence/F06.md). `Test-K8s.ps1` PASS: journeys J1–J7, 2 replica cùng nhận request, probes, PVC Bound, migration 14 → 0 khi chạy lại, dữ liệu + phiên đăng nhập giữ qua xóa pod SQL/Redis/web, NetworkPolicy chặn SQL/web/Redis, smoke F01–F04.
- `Test-AppStack.ps1` (compose) vẫn PASS sau khi đổi template nginx.
- `Start-K8s.ps1`: deploy → Ready; `-Stop` → start lại → Ready, 0 migration mới. Cluster `ebaygroup5` đang **dừng** (giữ dữ liệu); `secrets/k8s/secrets.json` (ignore) giữ mật khẩu của cluster này.
- **Test-CI local: 263/263 .NET + 20/20 Node**, build 0 warning.
- CI: lần đầu kẹt `queued` phía GitHub; smoke XFF flaky ở ranh giới cửa sổ (sửa: thử tới 25 lần); pod SQL Pending trên runner 2 vCPU do CPU request (giảm request CPU, giữ memory). Sau đó [CI37656323271](https://github.com/Twiash/EbayGroup5/actions/runs/37656323271) success.

## Requirement

- Không requirement nào Done riêng. OPS-06 Pending (F07 CD, F08 rolling update/rollback có tải); OPS-03 Pending (F07).

## Lưu ý

- Image tag theo commit (`<sha>` hoặc `<sha>-dirty-<hex>` khi cây có thay đổi); tag `-dirty-` trên host có thể xóa sau khi load vào node.
- Một node kind, local-path storage; TLS ở nginx trong cluster.
- DB dev `EbayGroup5` (compose) chưa chạy 0014; `Start-AppStack.ps1` sẽ áp dụng khi chạy lại.

## Cho task sau

- **F07:** build image theo commit SHA trong GitHub Actions và deploy vào cluster local qua runner tin cậy (self-hosted trên máy người dùng cần người dùng quyết định); dùng lại `Deploy-K8s.ps1`.
- **F08:** `kubectl rollout` dưới tải bằng `Invoke-LoadTest.ps1`/`ChatLoad` trên cluster; maxUnavailable 0 + preStop đã có.

## Tiếp theo / Jira

- F01+F02+F06 → **KAN-26 / TV5 Hải Lê: hoàn tất local một phần** (còn F07). Chưa sync live; không tạo worklog/review/issue.
