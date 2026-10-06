# D01 — Product review gắn OrderItem

Contract chốt trước code 06/10/2026, baseline main `2c58aac` (C05 docs). Phạm vi D01: schema, service, endpoint ghi review. Không UI/điểm trung bình (D02), không seller feedback (D03–D04), không ẩn/kiểm duyệt/audit (D05).

- **Một review cho một OrderItem.** Quyền review là `OrderItem` của đơn Buyer sở hữu, không phải Product. Mua lại sản phẩm trong đơn khác tạo OrderItem mới nên được review tiếp. Product review tách seller feedback; không suy sentiment từ số sao.
- **Điều kiện gửi** (service kiểm tra dưới transaction, thứ tự kiểm tra cố định): input hợp lệ → actor role DB là Buyer (sai role 403) → OrderItem tồn tại và đơn thuộc actor (thiếu/foreign đều 404, không lộ tồn tại) → `OrderTable.status = Delivered` (chưa nhận 409) → OrderItem có ProductId → chưa có review cho OrderItem (trùng 409). Admin và Seller không có bypass.
- **Nguồn "đã nhận":** `OrderTable.status = Delivered`. Trạng thái này chỉ đổi qua B06 `Receive` của đúng Buyer (kèm Payment Paid + Shipping Delivered atomic), nên D01 không lặp lại kiểm tra B06. Đơn legacy/seed Delivered được coi là đã nhận; không bịa dữ liệu cho đơn thiếu chi tiết.
- **Input:** `OrderItemId`, `Rating` 1–5 (số nguyên), `Comment` tùy chọn. Comment được trim; rỗng/chỉ khoảng trắng lưu NULL; tối đa 2000 ký tự UTF-16; từ chối control char trừ TAB/CR/LF. Reviewer, Product, thời điểm tạo lấy từ server/OrderItem; form không nhận reviewerId/productId/createdAt/status. Giả định: comment tùy chọn vì ma trận G5-03 chỉ yêu cầu "rating và comment"; D02 có thể siết ở UI nhưng không cần đổi schema.
- **Lỗi:** input sai → `Invalid` (400); sai role → `Forbidden` (403); thiếu/foreign → `NotFound` (404); chưa nhận, trùng, item thiếu product → `Conflict` (409). Trùng lượt **không** idempotent-success: lần gửi thứ hai luôn 409 và không đổi review gốc.
- **Đồng thời:** SQL transaction ReadCommitted, lock theo thứ tự B06: User HOLDLOCK → OrderTable HOLDLOCK → OrderItem HOLDLOCK → kiểm tra Review `UPDLOCK,HOLDLOCK` theo OrderItemId rồi INSERT. Unique index là lớp chặn cuối; vi phạm 2601/2627 trả Conflict. Lỗi khác rollback và ném lại, không để lại review dở.
- **Eligibility cho UI:** `ListForOrderAsync(buyerId, orderId)` trả từng dòng (≤50) với `CanReview`, `Reviewed`, `Reason`; trả null (404) nếu không phải Buyer chủ đơn. Chỉ đọc, dùng cho D02.

## Schema (DbUp 0007, chỉ mở rộng)

`Review` giữ nguyên cột gốc; thêm `OrderItemId int NULL`. `OrderItem` thêm `UNIQUE(id, productId)` để làm đích composite FK.

- `FK_Review_OrderItemProduct (OrderItemId, productId) → OrderItem(id, productId)`: review không thể trỏ Product khác sản phẩm của dòng đơn.
- `UX_Review_OrderItem` unique filtered `WHERE OrderItemId IS NOT NULL`: tối đa một review mỗi dòng đơn.
- `CK_Review_OrderLink`: khi có OrderItemId thì productId/reviewerId/rating/createdAt NOT NULL, rating 1–5, comment NULL hoặc không rỗng và ≤4000 byte. Hàng legacy không có OrderItemId không bị ràng buộc, không bị sửa hay xóa.
- Không backfill: không suy review legacy thuộc OrderItem nào. Hàng legacy vẫn tồn tại nhưng không phải review gắn đơn; D02 quyết định có hiển thị/tính trung bình hay không (ghi ở handoff).
- EF chỉ map `Review.OrderItemId` (FK đơn cột + unique filtered index); composite FK và CHECK chỉ tồn tại trong SQL (EF không cho alternate key nullable). Quyền `ebay_app` trên Review đã có từ 0002.

## Endpoint

`POST /Reviews/Create` (form, antiforgery toàn cục, `Authorize(Policy="Buyer")`, no-store): `OrderItemId`, `Rating`, `Comment`. Thành công redirect `/Orders/Details/{orderId}`. Không có GET/view: giao diện review thuộc D02.

## Kiểm tra bắt buộc

Chưa nhận/sai owner/sai role/trùng/rating sai/comment sai; mua lại; hai request đồng thời; rollback khi INSERT lỗi; migration giữ dữ liệu legacy và metadata; ràng buộc SQL chặn ghi trực tiếp sai; HTTP bỏ qua overposting, thiếu token 400, role/owner đúng mã.
