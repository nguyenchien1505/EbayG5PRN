# Handoff — OPS-001 (Q-OPS-001 lỗi mạng rolling)

27/10/2026. Branch `task/OPS-001-rolling-drain` từ `task/H05-item-page` `49f250a` (đã gồm Q-SEC-002, chưa gộp main);
xem `git log --oneline --grep=OPS-001`. Handoff trước [H05](H05.md) (H05, Q-SEC-002, Q-UI-011).

## Đã xong

- Q-OPS-001 Answered: drain không lỗi (nginx bỏ pod cũ ≤1 s, không retry/connect pod chết). Inbox chạy ~62 round trip SQL
  tuần tự; trên runner CI 2 vCPU bão hoà, đuôi inbox vượt timeout 5 s của probe khi thay pod. Chi tiết, số đo, giới hạn:
  [ROLLING §Q-OPS-001](../architecture/ROLLING.md#q-ops-001--lỗi-mạng-chập-chờn-khi-thay-pod).
- Sửa `ChatService.InboxAsync` thành một câu SQL (DTO/quy tắc giữ nguyên; CHAT.md cập nhật). Gate/timeout/retry không đổi.
- `RollingLoad` report thêm `networkErrorSamples` (Timeout vs lỗi kết nối) — runbook ROLLING cập nhật.
- Task card [OPS-001](../tasks/OPS-001.md), INDEX task/câu hỏi.

## Test

- Test-CI local: 295/295 .NET + Node + NuGet audit pass (hai lần, lần hai có assertion inbox = summary).
- Rolling Short local, node kind giới hạn `docker update --cpus 2` (chỉ để mô phỏng runner, không commit): 2 lần liên tiếp PASS,
  0 lỗi mạng/5xx. Trước sửa local cũng pass (không tái hiện được lỗi gốc) — bằng chứng cuối là GitHub CI attempt 1.
- GitHub CI [40538934712](https://github.com/Twiash/EbayGroup5/actions/runs/40538934712) trên `8fdded1`: success attempt 1, rolling 0 lỗi mạng/5xx;
  p95 inbox rollback vẫn 1,8 s (margin tới 5 s chưa lớn) — một lần xanh chưa loại hẳn flake.

## Bước tiếp theo

1. Nhánh đã push. Nếu run sau còn `status 0`, đọc `networkErrorSamples` trong artifact `rolling-report` (loại lỗi + UTC)
   trước khi đổi gì khác.
2. Gộp theo thứ tự: `task/H05-item-page` → `task/OPS-001-rolling-drain` (nhánh này chứa H05) → `task/Q-UI-011-references`.
3. Còn mở: Q-UI-012, Q-TEAM-002; H06 chốt contract theo UI_CANDIDATES §Q-UI-011; H07 sau cùng. Cluster `ebaygroup5` vẫn chạy G02.

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1` trước khi build.
- Stage file cụ thể; `src/EbayGroup5.Web/Properties/launchSettings.json` là file của người dùng. Không chạy Test-CI cùng Test-AppStack/rolling.
- eBay chặn Chrome headless: không vượt chặn; ảnh mẫu chụp bằng trình duyệt in-app hoặc người dùng gửi, che thông tin cá nhân.
