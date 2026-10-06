# Handoff — D03 seller feedback và công thức uy tín

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `7b96a97`; nhánh `task/D03-seller-feedback`.
- Checkpoint: `git log --oneline --grep=D03`.
- Handoff D02 lưu ở [D02](D02.md).

## Hoàn tất

- Q-BIZ-003 **Answered**: Delivered + 60 ngày; comment tùy chọn, tối đa 500; áp dụng luật chờ 7 ngày cho Neutral/Negative; form gộp R1. Contract nằm ở [FEEDBACK](../architecture/FEEDBACK.md).
- DbUp `0008_SellerFeedback.sql`: bảng mới, chỉ mở rộng. Manifest có 9 assets. Bảng nguồn `Feedback` giữ nguyên, không dùng.
- `Application/Feedback/IFeedbackService.cs`:
  - `SellerReputation`: Score = P−N; % = P/(P+N), làm tròn 1 chữ số thập phân; trả null khi P+N = 0.
  - `LeaveFeedbackRequest`: dữ liệu của form gộp.
- `Infrastructure/Reputation/FeedbackService.cs`:
  - Eligibility theo `TimeProvider`; thời điểm giao lấy từ `Payment.paidAt`.
  - Gửi gộp feedback + review trong một transaction, dùng lại `ReviewService` internal `Validate`/`Submit`.
  - Bộ lọc `Visible()` dùng chung cho D05.
  - Đăng ký DI dạng `TimeProvider.System`.
- Web:
  - `/Reviews/Create` thành trang "Leave feedback".
  - Orders/Details: nút "Leave feedback" kèm trạng thái của từng phần.
  - Partial `_SellerReputation` trên Catalog/Details.
  - CSS cho các nút pill chọn sentiment.
- Tests: `FeedbackTests.cs` (16 case). `DatabaseTests`/`CheckoutTests` cập nhật số migration 7→8.

## Verification

- [Evidence](../evidence/D03.md):
  - Test-CI local: **177/177 .NET + 14/14 Node**, build 0 warnings.
  - Mutation bỏ luật chờ 7 ngày bị 2 test bắt.
  - Browser desktop + 320px.
- Trạng thái requirement:
  - G5-03 vẫn Done.
  - **G5-05 Pending**: còn trang uy tín và seller reply (D04).
  - G5-04 Pending D05; SUP-05 Pending.
- GitHub CI: kết quả ghi bên dưới sau khi push.
- `launchSettings.json` (untracked) không bị đụng. Artifacts nằm trong `artifacts/d02/preview`, `artifacts/d03`, đã ignore.

## Lưu ý cho task sau

- **D04:**
  - Trang hồ sơ seller, ví dụ `/Sellers/{id}/Feedback`: điểm, tỷ lệ, số lượng theo sentiment, danh sách feedback có phân trang.
  - Seller reply: chỉ seller nhận feedback được trả lời. Cần migration mới (cột reply hoặc bảng reply) và grant UPDATE/INSERT.
  - Link từ badge trên product. Profile chưa có mẫu UI (Q-UI-002 Partial): cần tìm ảnh mẫu và trình người dùng chốt trước khi làm UI.
- **D05:** thêm điều kiện ẩn vào `FeedbackService.Visible` và `ReviewService.Visible`; uy tín phải đổi khi nội dung bị ẩn.

## Tiếp theo / Jira

- D03 xong. Chờ người dùng chọn task tiếp, không tự làm D04/E/F.
- Ledger chờ gom đợt sync:
  - B05+B06 → KAN-11; C01–C05 → KAN-13..16; D01+D02 → KAN-17.
  - **D03 → KAN-18/TV3 Pham Van Cuong: hoàn tất local một phần, còn D04.**
- Chưa sync live; không tạo worklog/review/issue.
