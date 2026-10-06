# Handoff — D04 trang uy tín và seller reply

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `baa8a8a`; nhánh `task/D04-reputation-reply`.
- Checkpoint: `git log --oneline --grep=D04`.
- Handoff D03 lưu ở [D03](D03.md).

## Hoàn tất

- Đầu vào đã chốt:
  - Q-UI-007: F1 giản lược, theo trang eBay Feedback profile thật xem ngày 06/10/2026.
  - Q-BIZ-004: tỷ lệ 12 tháng, điểm tích luỹ, reply một lần, không sửa.
  - Contract: [FEEDBACK](../architecture/FEEDBACK.md#chi-tiết-triển-khai-d04).
- DbUp `0009_SellerFeedbackReply.sql`: thêm `ReplyText` và `RepliedAtUtc` kèm CHECK; trigger `TR_SellerFeedback_Immutable`; `GRANT UPDATE` cho hai cột reply. Manifest có 10 assets. EF khai báo `HasTrigger`.
- Application:
  - `FeedbackCounts` chứa công thức của D03.
  - `SellerReputation` gồm Lifetime/Month1/Month6/Month12 (dùng `AddMonths` theo TimeProvider).
  - Thêm `SellerFeedbackPage`, `FeedbackEntry`, `ReplyResult`.
- `FeedbackService` thêm:
  - `ReputationAsync` theo các mốc thời gian, chạy bằng một query group.
  - `ProfileAsync`: 25 dòng/trang, lọc theo sentiment, che tên buyer, recency.
  - `ReplyAsync`: một câu UPDATE có điều kiện, atomic.
- Web:
  - `SellersController`: `GET /Sellers/Feedback/{id}` (public) và `POST /Sellers/Reply` (policy Seller).
  - Views `Sellers/Feedback`, `InvalidFeedbackFilters`.
  - Badge `_SellerReputation` hiện tỷ lệ 12 tháng và link hồ sơ trên product.
  - Menu My eBay: "Feedback profile" cho Seller. Thêm CSS.
- Tests: `SellerProfileTests.cs` (6 case lớn). Test D03 cập nhật theo kiểu `FeedbackCounts`. Test migration loại thêm 0009. Số migration 8→9.

## Verification

- [Evidence](../evidence/D04.md):
  - Test-CI local: **183/183 .NET + 14/14 Node**.
  - Mutation bỏ kiểm tra chủ sở hữu khi reply → 2 test bắt được.
  - Browser desktop + 320px, có xem reply thật.
- Trạng thái requirement:
  - **G5-05 Done.** G5-03 Done.
  - G5-04 Pending D05; SUP-05 Pending D05/E05; UI-01 Pending.
- GitHub CI: ghi bên dưới sau khi push.
- `launchSettings.json` (untracked) không bị đụng.

## Lưu ý cho D05

- Thêm trạng thái ẩn cho Review và SellerFeedback bằng migration mới. Trigger D04 không chặn các cột mới.
- Lọc ẩn trong `ReviewService.Visible` và `FeedbackService.Visible`; summary và uy tín phải đổi theo.
- Report + Admin ẩn mềm có audit; không xóa vật lý (SUP-05). Admin chỉ dùng được trong mạng demo (`AdminNetwork`).
- Feedback/reply đã ẩn không được hiển thị. Cần quyết định có hiện placeholder kiểu "Removed by eBay" hay không; nếu chưa rõ thì trình người dùng chọn.

## Tiếp theo / Jira

- D04 xong. Chờ người dùng chọn task tiếp, không tự làm D05/E/F.
- Ledger chờ gom đợt sync:
  - B05+B06 → KAN-11; C01–C05 → KAN-13..16; D01+D02 → KAN-17.
  - **D03+D04 → KAN-18/TV3 Pham Van Cuong: hoàn tất local.**
  - KAN-19 (D05) và KAN-20 chưa làm.
- Chưa sync live; không tạo worklog/review/issue.
