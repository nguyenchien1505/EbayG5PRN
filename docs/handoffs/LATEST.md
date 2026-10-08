# Handoff — G02 báo cáo/demo/bàn giao (đang kiểm chứng)

08/10/2026. Baseline fe4a22b; branch task/G02-report-demo-handoff.
Handoff trước [G01](G01.md); G01 runtime409e05d/CI37689369159 success.

## Quyết định user

- G02 thực hiện đầy đủ, Jira sync thật để sau: OPS-04 vẫn Pending.
- Q-DEMO-001: đơn mới + thời gian rút ngắn; Q-TEAM-002 ngày nộp chưa xác định.
- Giữ seed/ngày lịch sử, không reset database/volume hoặc bypass quyền/thời hạn.

## Đã viết

- Contract [HANDOVER](../architecture/HANDOVER.md); bộ [REPORT](../delivery/REPORT.md),
  [SETUP](../delivery/SETUP.md), [DEMO](../delivery/DEMO.md), [HANDOVER](../delivery/HANDOVER.md).
- README cập nhật tiến độ; setup từ clone sạch, kịch bản5 thành viên/checklist/Jira batch.
- Start-K8s -Demo (30s/day): thêm credential riêng một lần, migration→seed→auth→web;
  DemoBusinessDaySeconds ConfigMap và Pod template stamp để đổi clock có hiệu lực.
- Test-AppStack -DemoClockSeconds10: journey mới INR/draft reload/worker escalation/admin;
  Test-Handover clone private GitHub đúng SHA, không copy local state/reference code.
- CI compose smoke kèm demo clock; giữ các gate trước, không giảm bảo mật/load thresholds.

## Verification / còn lại

- Parse scripts/build Journey0 warning/docs pass.
- Rehearsal đầu J1–J7 pass; J8 parse URL nhầm order thành case, đã sửa mở link case thật;
  rehearsal lần2 đang chạy trên stack riêng.
- Còn clean GitHub clone/Test-CI/demo, CI runtime và provision kind demo (idempotent).
- DOC-01 chưa Done tới khi có minh chứng; không claim thành viên đã hiểu/trình bày.
- Stage file cụ thể, skipci +dispatch runtime một lượt, FF main sau pass.
- Không ghi Jira live; user launchSettings untracked giữ nguyên.
