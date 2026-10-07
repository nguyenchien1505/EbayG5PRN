# F07 — CD thủ công từ GitHub Actions vào kind local

Contract 08/10/2026, baseline main `4cefffb` (lịch sử sau khi xoá co-author Claude).
Dependency: A07/F06 đã có CI thật, Dockerfile web/migrate và Deploy-K8s.
Người dùng chốt runner **tạm thời trên máy hiện tại**, một job, không cài service.

## Luồng và quyền

- Workflow `CD local`, chỉ `workflow_dispatch`, chỉ `refs/heads/main` và actor là owner repo.
- Job build trên GitHub-hosted Ubuntu: yêu cầu CI `ci.yml` success trên **đúng SHA**,
  build image web/migrate gắn full SHA + OCI revision, xuất Docker archive và manifest SHA-256/image ID.
- Artifact thuộc cùng run, retention 1 ngày; không registry/public package, không chứa secrets.
- Job deploy Windows self-hosted: label riêng mỗi lần chạy, tải đúng artifact cùng run,
  checkout đúng SHA; kiểm revision/checksum/image IDs trước khi `kind load` và deploy.
- Token workflow chỉ `contents: read`, `actions: read`. Không PR/push tự deploy;
  concurrency riêng cho cluster, không huỷ deployment đang chạy.
- Runner official Windows x64 ghim version/checksum; đăng ký `--ephemeral`,
  `--no-default-labels`, custom label ngẫu nhiên; một job rồi GitHub tự gỡ đăng ký.
  Không service, startup entry hoặc mở port inbound. Bootstrap dùng phiên gh đã đăng nhập
  để lấy registration token ngắn hạn; token truyền bằng biến môi trường của runner,
  không in token/secret, không ghi token vào script/command line/repo.
- State nằm ngoài checkout Actions: `EBAY_CD_STATE_ROOT` trỏ tới thư mục secrets local;
  SQL/Redis password và TLS của cluster `ebaygroup5` giữ nguyên qua các lần deploy.
  Không upload state/runner credentials/diagnostic logs lên GitHub.

## Triển khai

- `Build-CdBundle.ps1`: build/export hai image từ tracked tree sạch và ExpectedSha.
- `Deploy-K8s.ps1`: bổ sung chế độ image đã build; các caller F06 vẫn build như cũ.
  Khởi động node kind đã dừng nếu có; không xoá cluster/PVC/database.
- `Invoke-CdDeployment.ps1`: xác minh artifact + image, dùng secrets/TLS có sẵn,
  gọi Deploy-K8s, kiểm job migration complete, image đúng SHA, 2 replica ready,
  HTTP redirect và HTTPS ready/catalog qua CA thật. Smoke không ghi dữ liệu demo.
- `Start-CdRunner.ps1`: cài runner đã kiểm checksum vào artifacts riêng từng lần,
  nối toolchain local vào PATH của process, đăng ký runner ephemeral rồi chạy một job.
  Khi kết thúc/fail, gỡ registration còn sót bằng API và xoá credential config của
  **đúng runner lần này**; không dọn runner/cluster khác.

## Kiểm chứng và ranh giới

- Local: syntax/docs/checksum + test artifact sai checksum/SHA bị từ chối trước load,
  artifact thật build/import/deploy trên cluster test cô lập nếu phù hợp.
- CI trên runtime SHA + một run CD thật build ở GitHub và deploy trên máy người dùng;
  lưu URL run, SHA image, health/replicas/migration và runner deregistration trong evidence.
- OPS-03 chỉ Done khi có run CD thật success; OPS-06/PERF-01 vẫn Pending F08.
- Không thay app, nghiệp vụ, schema/migration hoặc sync Jira. Không đo zero downtime ở F07.
- Máy Windows vẫn là host được tin cậy; ephemeral là vòng đời đăng ký một job,
  không phải máy ảo cô lập. Chỉ owner chạy workflow đã review từ main.

Nguồn chính thức: [ephemeral runner](https://docs.github.com/en/actions/reference/runners/self-hosted-runners#ephemeral-runners-for-autoscaling),
[workflow_dispatch](https://docs.github.com/en/actions/reference/workflows-and-actions/events-that-trigger-workflows#workflow_dispatch),
[runner registration API](https://docs.github.com/en/rest/actions/self-hosted-runners#create-a-registration-token-for-a-repository).
