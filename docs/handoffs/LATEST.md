# Handoff — D05 kiểm duyệt và audit review/feedback

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `249bce7`; runtime `316adac` trên `task/D05-moderation`, fast-forward vào main sau khi [CI37431358143](https://github.com/Twiash/EbayGroup5/actions/runs/37431358143) pass.
- Checkpoint: `git log --oneline --grep=D05`.
- Handoff D04 lưu ở [D04](D04.md).

## Hoàn tất

- Q-BIZ-005: làm theo eBay. Nội dung bị gỡ biến mất, không có placeholder. Hai mức gỡ: Hide (ẩn cả mục) và RemoveComment (giữ rating). Thêm Restore/Dismiss có audit. Contract: [MODERATION](../architecture/MODERATION.md).
- DbUp `0010_Moderation.sql`:
  - Review và SellerFeedback thêm `HiddenAtUtc` và `CommentRemovedAtUtc`.
  - Bảng mới `ContentReport` và `ModerationAudit`.
  - Trigger `TR_Review_NoDelete` và `TR_ModerationAudit_AppendOnly`.
  - Grant theo từng cột. Manifest có 11 assets.
- Application/Infrastructure:
  - `IModerationService` / `ModerationService`: report, queue, detail, act trong một transaction; lock nội dung bằng UPDLOCK; mỗi thao tác ghi đúng một dòng audit.
  - `ReviewService.Visible` và `FeedbackService.Visible` lọc nội dung đã ẩn.
  - Hồ sơ feedback ẩn comment và reply đã bị gỡ; reply bị chặn khi nội dung đã ẩn hoặc đã gỡ comment.
- Web:
  - `ReportsController` (Buyer/Seller) và `AdminController` (`AdminDemo`).
  - Views `Reports/Create`, `Admin/Moderation`, `Admin/ModerationItem`.
  - Link Report trong `_ReviewList` và `Sellers/Feedback`.
  - `TempData["SiteNotice"]` hiển thị trong layout; menu "Moderation" cho Admin.
- Tests:
  - `ModerationTests.cs` (7 case).
  - Thêm `SqlFixture.MigrateBefore` dùng chung cho các test migration.
  - Số migration 9→10.

## Verification

- [Evidence](../evidence/D05.md):
  - Test-CI local: **190/190 .NET + 14/14 Node**.
  - Mutation bỏ lọc ẩn bị bắt.
  - Browser: report → moderator Hide → tổng hợp public đổi 14→13 ratings, 4.0→4.2; kiểm tra thêm ở 320px.
- Trạng thái requirement:
  - **G5-04 Done**; G5-03 và G5-05 Done.
  - SUP-05 Pending: phần dispute thuộc E05.
- **[CI37431358143](https://github.com/Twiash/EbayGroup5/actions/runs/37431358143) exact runtime 316adac success**: job 4m25s, 190 .NET + 14 Node, build 0 warnings, TRX upload. Docs-only [skip ci] sau CI.
- `launchSettings.json` (untracked) không bị đụng.

## Lưu ý

- Dùng Admin demo: seed `admin.a06` chưa có mật khẩu. Chạy `scripts/Enable-DemoAuth.ps1` (B01), rồi truy cập từ loopback.
- **E05:** dùng lại mẫu `AdminDemo` + kiểm tra role DB + audit append-only của `ModerationService`. Chưa có bảng audit dùng chung cho dispute.
- Nhóm D đã xong toàn bộ (D01–D05). Có thể làm KAN-20 (nghiệm thu reputation) khi gom đợt.

## Tiếp theo / Jira

- D05 xong. Chờ người dùng chọn task tiếp, không tự làm E/F.
- Ledger chờ gom đợt sync:
  - B05+B06 → KAN-11; C01–C05 → KAN-13..16; D01+D02 → KAN-17; D03+D04 → KAN-18.
  - **D05 → KAN-19/TV3 Pham Van Cuong: hoàn tất local.**
  - KAN-20 chưa làm.
- Chưa sync live; không tạo worklog/review/issue.
