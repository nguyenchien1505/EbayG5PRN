# Handoff — D01 review gắn OrderItem

06/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5; baseline main `2c58aac`, nhánh `task/D01-review-order-link` (commit local, **chưa push, chưa merge main**). Checkpoint `git log --oneline --grep=D01`. C05 handoff đã lưu ở [C05](C05.md).

## Hoàn tất

- [REVIEWS](../architecture/REVIEWS.md) chốt contract trước code: một review/OrderItem, điều kiện gửi, lỗi 400/403/404/409, lock order, schema và endpoint.
- DbUp `0007_ReviewOrderLink.sql`: `Review.OrderItemId` NULL, `OrderItem UNIQUE(id,productId)`, composite FK `(OrderItemId,productId)`, unique filtered `UX_Review_OrderItem`, `CK_Review_OrderLink`. Chỉ mở rộng, không backfill, hàng Review legacy giữ nguyên. Manifest `scripts/migration-checksums.json` thêm 0007.
- `Application/Reviews/IReviewService.cs`, `Infrastructure/Reviews/ReviewService.cs` (`ListForOrderAsync` eligibility cho D02, `SubmitAsync` transactional), EF map `Review.OrderItemId`, DI trong `WebApplicationHost`.
- `Web/Controllers/ReviewsController.cs` + `Models/ReviewForms.cs`: `POST /Reviews/Create` (Buyer policy, CSRF, no-store). Không GET/view.
- Tests: `ReviewTests.cs` (17 cases); `DatabaseTests`/`CheckoutTests` cập nhật số migration.

## Verification

- [Evidence](../evidence/D01.md): Test-CI pass, **157/157 .NET + 14/14 Node**, build 0 warnings/errors, Verify-Planning/Verify-Migrations pass. Mutation bỏ guard Delivered làm 3 test fail rồi khôi phục.
- G5-03 vẫn **Pending** (cần UI D02); G5-04 Pending D02/D05; SUP-05 Pending D05/E05. UI-01 project-wide Pending.
- Chưa chạy GitHub Actions cho D01 (chưa push). Không thay đổi launchSettings.json chưa track của người dùng.

## Quyết định/giả định cần D02 biết

- `Comment` tùy chọn (rating bắt buộc), tối đa 2000 UTF-16 units; D02 có thể bắt buộc ở UI mà không đổi schema. Hỏi lại người dùng nếu muốn bắt buộc ở server.
- Điều kiện "đã nhận" là `OrderTable.status=Delivered`; không lặp kiểm tra Payment/Shipping của B06.
- Trùng lượt luôn 409, không idempotent-success; không sửa/xóa review (nếu cần sửa, đó là scope mới).
- Review legacy (OrderItemId NULL, rating bất kỳ kể cả 9/NULL) tồn tại trong schema nguồn: D02 phải chỉ tính review có `OrderItemId IS NOT NULL` cho trung bình/số lượt, hoặc ghi quyết định khác.
- D02 dùng `IReviewService.ListForOrderAsync` để vẽ nút/biểu mẫu trên Orders/Details; mẫu UI R1 đã chốt (Q-UI-002 phần review form).

## Tiếp theo / Jira

- D01 only complete; chờ task người dùng chọn, không tự D02/D03/E/F. Gợi ý: D02 (UI + trung bình) hoặc E01.
- Cần người dùng quyết: push `task/D01-review-order-link` lên origin để chạy GitHub CI rồi merge main fast-forward (như các task trước), hay giữ local.
- Jira pending batch: B05+B06/KAN-11/TV1; C01/KAN-13, C02/KAN-14, C03+C04/KAN-15, C05/KAN-16/TV2; **D01 thuộc KAN-17/TV3 Pham Van Cuong, hoàn tất local một phần (KAN-17 còn D02)**. Chưa sync live, không worklog/review/duplicate.
