# Handoff — G02 báo cáo, demo và bàn giao

08/10/2026. Baseline main `fe4a22b`; branch `task/G02-report-demo-handoff`.
Runtime `e635866`; checkpoint `git log --oneline --grep=G02`. Handoff trước [G01](G01.md).

## Quyết định user

- Hoàn tất G02, **Jira sync thật để sau** → OPS-04 Pending.
- Q-DEMO-001: đơn mới +clock rút ngắn; Q-TEAM-002 ngày nộp chưa xác định.
- Không sửa ngày seed, không reset dữ liệu/volume; rehearsal con người không được giả ghi nhận.

## Hoàn tất

- [REPORT](../delivery/REPORT.md), [SETUP](../delivery/SETUP.md), [DEMO](../delivery/DEMO.md),
  [HANDOVER](../delivery/HANDOVER.md) + [contract](../architecture/HANDOVER.md).
- Báo cáo phạm vi/architecture/schema/security/UI/load/CD/rolling, ảnh UI và evidence;
  kịch bản5 người, source/câu hỏi luyện tập, setup từ clone và Jira batch để sau.
- Start-K8s -Demo:30s/business day, migration→seed→auth→web; tạo credential riêng một lần.
- Deploy-K8s default clock0; ConfigMap+Pod template stamp đảm bảo đổi clock thay pod;
  CD thường dùng clock thật. Không migration/đổi app business/seed SQL.
- Test-AppStack -DemoClockSeconds10 thêm INR mới/draft/worker/admin;
  Test-Handover thực sự clone GitHub đúng SHA, không copy state/reference projects.
- Test setup chọn subnet không overlap khi retained stacks tồn tại; không prune mạng khác.

## Verification

- Clean GitHub clone `e635866`: locked restore/build0 warning; **263 .NET +20 Node**,
  artifact/archive/checksum/secret/audit, browser J1–J7 +G02, TLS/WSS/2replica/trace PASS.
- Clone report: artifacts/handover/clean-3bc10979e28c4a0583d02ee7f687e3ed/report.json.
- **[CI37709417510](https://github.com/Twiash/EbayGroup5/actions/runs/37709417510) success** trên e635866:
  263 .NET+20Node; compose+G02 demo/load/K8s(default clock0)/rolling pass.
  Receipt demo: order6/case2, draftReload/workerEscalation/adminDecision=true.
  Rolling:5.169 HTTP0 5xx,1.238 ACK đủ,40 reconnect,max1.049,44ms.
- Kind -Demo provisioning +rerun PASS:14 migration,3product,3credentialed demo user;
  rerun seed skipped/auth0 mới, mọi password/TLS checksum giữ nguyên;2web/PVC Ready/Bound.
- Harness sửa URL order→case, selector Decision note, admin outcome label; failures ghi ở evidence.
  Không thay app/rule để làm test pass. Test stacks của phiên đã được dọn.

## Status / còn lại

- **40/40 task kỹ thuật Done; G02/DOC-01 Done trong phạm vi bàn giao**.
- **OPS-04 Pending**, Jira live hoãn theo user; KAN-28 phần kỹ thuật local complete,
  không giả transition/worklog/review. Xem HANDOVER cho lần sync sau.
- Mỗi người tự rehearsal/giải thích theo checklist, không ghi nhận đã thực hiện khi chưa xác nhận.
- Q-TEAM-002 ngày nộp chưa xác định; Q-UI-010 unread badge Deferred.

## Runtime

- Cluster `ebaygroup5` đang Ready: **https://localhost:9443**, web image e635866,
  **demo30s/day**. Có catalog/3tài khoản, không thêm đơn/case test vào cluster này.
- Private credential keys demo-buyer/seller/admin-password tại secrets/k8s/secrets.json;
  không in/share/commit. Backup trước bổ sung: secrets/k8s/secrets.pre-g02.json (ignore).
- Start-K8s -Stop giữ dữ liệu; bật lại `-Demo` cho buổi trình bày; không Delete/reset/prune.
- Web/default CD dùng clock0; chuyển clock chỉ dùng đơn mới, không sửa hạn case đã có.
- User untracked src/EbayGroup5.Web/Properties/launchSettings.json giữ nguyên.
