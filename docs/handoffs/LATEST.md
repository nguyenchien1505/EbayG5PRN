# Handoff — E05: admin xử lý request và kết quả mô phỏng

07/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `029674b`. Runtime trên `task/E05-dispute-admin`; checkpoint `git log --oneline --grep=E05`. GitHub CI: xem mục Verification.
- Handoff trước: [E04](E04.md). **Nhóm E (khiếu nại) đã xong E01–E05.**

## Hoàn tất

- Contract: [DISPUTE_ADMIN](../architecture/DISPUTE_ADMIN.md). **Không có migration** (vẫn 0001–0013); luật E01 giữ nguyên.
- **Application/Infrastructure:**
  - `QueueAsync(admin, DisputeQueueQuery(State, Page))`:
    - Waiting = `Escalated`, sắp theo hạn 48 giờ.
    - Closed = đã từng escalate và đã đóng, sắp theo thời điểm đóng.
    - 20 dòng/trang.
  - `DisputeEventView.ActorName`: chỉ có khi người xem là Admin.
  - **Siết `ViewerRole`:** Admin phải có credential (như D05). Áp dụng cho `FindAsync`/`ActAsync`.
- **Web:**
  - `AdminController` thêm `Disputes`, `Dispute/{id}`, `DisputeAct` (chỉ nhận Decide/Message).
  - `Models/AdminDisputes.cs`; Views `Admin/Disputes`, `Admin/Dispute`.
  - `DisputeText.Status` có nhãn cho admin. `DisputeRequestText.Verb`/`AuditActor` tách khỏi `Event`.
  - Menu admin có thêm "Request reviews". CSS `.dispute-overdue`, `.dispute-summary-plain`.
- **Tests:**
  - `DisputeAdminTests`: 3 test (hàng đợi/quyền, quyết định song song/audit, HTTP đầu cuối).
  - `DisputeTests`/`DisputeDetailsTests` có wrapper `Setup` cấp credential cho seed admin.
- [SECURITY](../architecture/SECURITY.md) có thêm dòng E05.

## Verification

- [Evidence](../evidence/E05.md):
  - Test tập trung 46/46.
  - Hai admin song song → chỉ 1 quyết định.
  - Mutation 2/2 bị bắt.
  - Browser: admin Decide, buyer thấy kết quả; desktop/390/320px.
- **Test-CI local pass: 242/242 .NET + 20/20 Node**, build 0 warnings, container CI tự xóa.
- GitHub CI cho runtime E05: ghi bằng commit docs-only `[skip ci]` sau khi pass.
- Fixture preview (bị ignore) có moderator `d02admin@example.invalid`. Request #3 của preview đã được quyết cho buyer.
- Không đụng `launchSettings.json`.

## Trạng thái requirement

- **G5-01…G5-08, G5-N03, SUP-05: Done.**
- Còn Pending, đều thuộc nhóm F: G5-N01 (TLS/WSS), G5-N02/N04 (đo hiệu năng), G5-N05 (log/trace), UI-01 project-wide (G01).

## Tiếp theo

- Nhóm F theo [task index](../tasks/INDEX.md), bắt đầu từ F01 (Docker image + Nginx HTTPS; dependency C05, D05, E05 đã đủ).
- Không tự chuyển sang F khi người dùng chưa giao.
- E01–E05 → KAN-21…KAN-24 / TV4 Nguyên Nguyễn: hoàn tất local. Chưa sync Jira live; không tạo worklog/review/issue.
