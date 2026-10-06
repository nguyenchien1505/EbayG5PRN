# D05 — Kiểm duyệt và audit review/feedback

Contract soạn 06/10/2026 trước khi sửa source, baseline main `249bce7`. Dependency:

- D04: runtime `83d6c25`, CI37428908236 pass.
- B02: [SECURITY](SECURITY.md). Admin chỉ dùng được từ địa chỉ trong `Security:AdminAllowedAddresses`; policy `AdminDemo`.

Phạm vi gồm: Report, hàng đợi Admin, ẩn mềm, audit, và việc tổng hợp đổi theo kết quả kiểm duyệt. Không gồm dispute admin (E05) và triển khai mạng thật (F).

## Nguồn quyết định

**Đã duyệt** (PLAN §3.2, SUP-05, G5-04):

- Kiểm duyệt dùng trạng thái ẩn và audit, không xóa bản ghi.
- Tổng hợp chỉ tính nội dung đang hiển thị.
- Chặn user thường; chỉ mạng demo được phép.

**Theo eBay, người dùng yêu cầu "tìm cách eBay xử lý rồi thực hiện" ngày 06/10/2026:**

- [Feedback policy](https://www.ebay.com/help/policies/feedback-policies/feedback-policies?id=4208): eBay "may remove the comment, ratings and/or images". Nội dung bị gỡ biến mất khỏi hồ sơ và không còn được tính; seller không reply được vào nội dung đã gỡ.
- [Product reviews](https://www.ebay.com/help/selling/listings/product-reviews?id=5186): người dùng bấm Report bên dưới review, chọn lý do, rồi eBay điều tra.
- Không hiển thị placeholder kiểu "Removed by eBay".

**Quyết định thiết kế của agent:**

- Có hai mức gỡ, tương ứng với "comment, ratings":
  - **Hide**: ẩn cả mục. Không hiển thị, không tính vào bất kỳ tổng hợp nào.
  - **RemoveComment**: chỉ ẩn comment (và reply đi kèm). Rating/sentiment vẫn hiển thị và vẫn được tính.
- **Restore** để sửa nhầm (có audit).
- **Dismiss** để đóng report không vi phạm.
- Lý do report gom từ các nhóm trong Feedback policy:
  - `Offensive`: thù ghét, bạo lực, tục tĩu.
  - `PersonalInfo`: thông tin cá nhân.
  - `Spam`: link hoặc giao dịch ngoài sàn, không liên quan.
  - `Other`.

## Report

- Ai được report: user có role Buyer hoặc Seller trong DB, và **không phải tác giả**.
  - Review: tác giả là `ReviewerId`.
  - Feedback: tác giả là `BuyerId`. Seller nhận feedback được report feedback về mình, giống "request removal" của eBay.
  - Admin không report mà dùng chức năng kiểm duyệt.
- Nội dung phải đang hiển thị: review gắn đơn chưa Hide, hoặc feedback chưa Hide. Nếu không, trả 404.
- Mỗi người chỉ report một nội dung một lần. Report lại → 409; chính tác giả → 403; sai role → 403.
- Input: `Reason` thuộc 4 giá trị trên; `Details` tùy chọn, tối đa 500 ký tự, trim, không có control char.
- Thông báo sau khi gửi: "Thanks — we'll review your report." Trang không hiện số lượng report và không cho biết ai đã report.

## Admin kiểm duyệt

- Admin là user có role Admin trong DB và đi qua middleware mạng. Service kiểm lại role cho từng thao tác. User thường nhận 403.
- **Hàng đợi** `/Admin/Moderation`: các nội dung có report `Open`, nhóm theo nội dung. Hiện số report, lý do, report mới nhất, trạng thái hiện tại. Sắp theo report mới nhất, tối đa 50 nội dung.
- **Chi tiết** `/Admin/ModerationItem?kind=review|feedback&id=`: nội dung đầy đủ, kể cả khi đã ẩn; danh sách report; lịch sử audit; các nút thao tác.
- **Thao tác** `POST /Admin/Moderate`:
  - Có các trường `kind`, `id`, `action`, `note` (tùy chọn, tối đa 500 ký tự).
  - Chạy trong một transaction ReadCommitted, lock nội dung trước bằng `UPDLOCK,HOLDLOCK`.
  - Mỗi thao tác luôn ghi **đúng một** dòng audit.

| Thao tác | Điều kiện, nếu không thỏa thì 409 | Kết quả |
|---|---|---|
| Hide | Đang chưa ẩn | Đặt `HiddenAtUtc`; các report Open chuyển sang Actioned |
| RemoveComment | Chưa ẩn, có comment, comment chưa bị gỡ | Đặt `CommentRemovedAtUtc`; report Open chuyển sang Actioned |
| Restore | Đang ẩn hoặc comment đã bị gỡ | Xóa cả hai mốc; report đã xử lý giữ nguyên |
| Dismiss | Có report Open | Report Open chuyển sang Dismissed; nội dung giữ nguyên |

## Ảnh hưởng tới phần hiển thị

**Review** (`ReviewService.Visible` thêm điều kiện `HiddenAtUtc IS NULL`):

- Trung bình, phân bố và số "product ratings" chỉ tính review chưa bị Hide.
- "Reviews" và danh sách chỉ gồm review có comment chưa bị gỡ.

**Seller feedback** (`FeedbackService.Visible` thêm điều kiện `HiddenAtUtc IS NULL`):

- Điểm và tỷ lệ chỉ tính feedback chưa bị Hide.
- Nếu comment đã bị gỡ, hồ sơ hiện sentiment nhưng không hiện comment lẫn reply.
- Không thể reply vào feedback đã ẩn (404) hoặc đã bị gỡ comment (409).

**Không đổi:** eligibility và kiểm tra trùng của D01/D03 vẫn tính cả nội dung đã ẩn, nên Buyer không thể gửi lại để lách kiểm duyệt.

## Schema — DbUp 0010 (chỉ mở rộng)

- `Review` và `SellerFeedback` thêm `HiddenAtUtc datetime2(7) NULL` và `CommentRemovedAtUtc datetime2(7) NULL`.
- **Review:**
  - Trigger `TR_Review_NoDelete` chặn DELETE với review gắn đơn (không xóa vật lý). Review legacy không bị ảnh hưởng.
  - Quyền của `ebay_app` trên Review giữ nguyên như 0002; không thu hẹp quyền baseline.
- **SellerFeedback:**
  - `GRANT UPDATE (HiddenAtUtc, CommentRemovedAtUtc)`.
  - Trigger của D04 vẫn chặn sửa nội dung gốc và reply.
- **`ContentReport`:**
  - Cột: `Id`, `ReviewId` (FK, NULL được), `SellerFeedbackId` (FK, NULL được), `ReporterId` (FK User), `Reason` (CHECK), `Details nvarchar(500)` (NULL được, không chỉ có khoảng trắng), `Status` (Open/Actioned/Dismissed), `CreatedAtUtc`, `ResolvedAtUtc`, `ResolvedById` (FK User).
  - CHECK chỉ đúng một trong hai đích (ReviewId hoặc SellerFeedbackId) được có giá trị.
  - CHECK: report Open thì không có `ResolvedAtUtc`/`ResolvedById`; report đã đóng thì phải có cả hai.
  - Unique có filter `(ReviewId, ReporterId)` và `(SellerFeedbackId, ReporterId)`.
  - Quyền: SELECT, INSERT, UPDATE (Status, ResolvedAtUtc, ResolvedById).
- **`ModerationAudit`:**
  - Cột: `Id`, `ReviewId` hoặc `SellerFeedbackId` (đúng một), `AdminId` (FK User), `Action` (CHECK), `Note nvarchar(500)` (NULL được), `CreatedAtUtc`.
  - Chỉ GRANT SELECT, INSERT.
  - Trigger `TR_ModerationAudit_AppendOnly` chặn UPDATE/DELETE với mọi login.
- EF khai báo `HasTrigger` cho Review, SellerFeedback và ModerationAudit.

## UI

- Nút "Report" (dạng link) dưới mỗi review trong danh sách review của product và dưới mỗi dòng trên hồ sơ feedback. Chỉ hiện cho Buyer/Seller đã đăng nhập và không phải tác giả.
- Trang `GET/POST /Reports/Create?kind=&id=`: tóm tắt nội dung, chọn lý do, ô chi tiết tùy chọn. Gửi xong quay lại trang nguồn kèm thông báo.
- Admin: menu My eBay có mục "Moderation"; trang hàng đợi và trang chi tiết; thao tác qua nút có CSRF.

## Kiểm tra bắt buộc

- Tổng hợp review và uy tín đổi đúng khi Hide/RemoveComment/Restore.
- Report: quyền, tác giả, trùng, input, nội dung đã ẩn.
- Admin: quyền theo role/mạng; điều kiện của từng thao tác; audit đúng một dòng; báo cáo chuyển trạng thái; rollback khi lỗi; thao tác đồng thời.
- Không xóa được ở tầng SQL; audit append-only; reply bị chặn.
- Migration giữ nguyên dữ liệu.
- HTTP: CSRF, 403/404/409; browser desktop và 320px.
