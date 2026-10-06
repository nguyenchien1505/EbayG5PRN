# Handoff — D01 review gắn OrderItem

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.

- Baseline main `2c58aac`; runtime `6ba273a` trên `task/D01-review-order-link`, fast-forward vào main sau khi [CI37418785742](https://github.com/Twiash/EbayGroup5/actions/runs/37418785742) pass.
- Checkpoint: `git log --oneline --grep=D01`. Handoff C05 lưu ở [C05](C05.md).

## Hoàn tất

- [REVIEWS](../architecture/REVIEWS.md): contract một review mỗi OrderItem, gồm quy tắc gửi, mã lỗi, thứ tự lock, schema và endpoint. Mục "Nguồn quyết định" tách phần đã duyệt, phần thiết kế của agent và giả định đang mở.
- DbUp `0007_ReviewOrderLink.sql`, chỉ mở rộng, không backfill:
  - `Review.OrderItemId` (NULL được).
  - `OrderItem UNIQUE(id, productId)`.
  - Composite FK `(OrderItemId, productId)`.
  - `UX_Review_OrderItem`, `CK_Review_OrderLink`.
  - Manifest checksum có 0007.
- Code:
  - Application: `Application/Reviews/IReviewService.cs`.
  - Service: `Infrastructure/Reviews/ReviewService.cs` (`ListForOrderAsync` + `SubmitAsync` trong transaction).
  - EF map `Review.OrderItemId`, đăng ký DI.
  - Web: `ReviewsController`, `ReviewForms` → `POST /Reviews/Create`. Chưa có GET hay view.
- Tests: `ReviewTests.cs` (17 case). `DatabaseTests` và `CheckoutTests` cập nhật số migration 6→7.

## Verification

- [Evidence](../evidence/D01.md):
  - Test-CI local và GitHub CI cùng pass: **157/157 .NET + 14/14 Node**, build 0 warning, 0 error.
  - Mutation bỏ guard Delivered → 3 test fail; khôi phục file thì pass lại.
- Dependency B06:
  - Runtime `c2cf9e5`, [CI37400491344](https://github.com/Twiash/EbayGroup5/actions/runs/37400491344) pass đúng hash.
  - `c2cf9e5` là ancestor của `2c58aac`. Từ `c2cf9e5` đến `2c58aac` không đổi source Orders hay entity OrderTable/OrderItem.
  - D01 không sửa file nào của B06.
  - `OrderTests` 19/19 pass trong CI D01.
  - Trước khi code chỉ đọc contract/source B06, không chạy riêng test B06. Phần kiểm tra ancestor/drift làm sau, lúc rà tài liệu.
- Requirement: G5-03 **Pending** (thiếu UI D02); G5-04 Pending D02/D05; SUP-05 Pending D05/E05; UI-01 Pending.
- `launchSettings.json` (untracked) không bị đụng.

## Đã duyệt / thiết kế agent / còn mở

- **Đã duyệt** (MATRIX G5-03, PLAN §3.2):
  - Một review mỗi OrderItem.
  - Chỉ Buyer chủ đơn đã Delivered.
  - Mua lại được review.
  - Rating ngoài 1–5 bị từ chối.
  - Review tách khỏi seller feedback.
- **Thiết kế của agent** (người dùng chưa xác nhận riêng):
  - "Đã nhận" = `OrderTable.status=Delivered`; không kiểm tra lại Payment/Shipping vì B06 bảo đảm.
  - Gửi trùng luôn trả 409.
  - Comment tối đa 2000 ký tự UTF-16, được trim, từ chối control char.
- **Q-BIZ-001 Open:** server đang cho comment tùy chọn và chưa có sửa/xóa review. Đây là giả định, **chưa được chốt**.
  - Nếu chốt comment bắt buộc: thêm kiểm tra ở `ReviewService` và test. UI D02 không thay được kiểm tra server. Muốn chặn ở SQL thì cần migration mới; không sửa 0007.
  - Không đổi hành vi server khi chưa có câu trả lời.
- **Review legacy:**
  - **Đã quyết định ở D01:** hàng `OrderItemId IS NULL` giữ nguyên, không backfill. Hàng này không đánh dấu `Reviewed` và không chặn Buyer review OrderItem.
  - **Q-BIZ-002 Open, chờ D02:** có hiển thị và có tính vào trung bình/số lượt không. Đề xuất (chưa chốt): chỉ tính review có `OrderItemId`. Phải chốt trong contract D02 trước khi code G5-04.
- D02 dùng `IReviewService.ListForOrderAsync` cho Orders/Details. Mẫu R1 đã chốt; profile chưa có mẫu (Q-UI-002 Partial).

## Tiếp theo / Jira

- D01 xong. Chờ người dùng chọn task tiếp, không tự làm D02/D03/E/F.
  - Làm D02: cần trả lời Q-BIZ-001 và Q-BIZ-002 trước khi chốt contract.
  - Hoặc làm E01.
- Jira, ledger chờ gom đợt (giữ nguyên các task trước):
  - B05+B06 → KAN-11/TV1.
  - C01 → KAN-13; C02 → KAN-14; C03+C04 → KAN-15; C05 → KAN-16 (TV2).
  - **D01 → KAN-17/TV3 Pham Van Cuong: hoàn tất local một phần, còn D02; KAN-17 chưa Done.**
  - Chưa sync live; không tạo worklog/review/issue.
