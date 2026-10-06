# D01 — Product review gắn OrderItem

Contract D01 do agent soạn 06/10/2026 trên baseline main `2c58aac`, trước khi sửa source trong cùng phiên. Contract và code nằm chung commit `39c4f42`, nên không có commit riêng chứng minh thứ tự. Phạm vi D01: schema, service, endpoint ghi review. Ngoài phạm vi: UI và điểm trung bình (D02), seller feedback (D03–D04), ẩn/kiểm duyệt/audit (D05).

## Nguồn quyết định

**Đã duyệt** (MATRIX G5-03, PLAN §3.2, ADR-001; người dùng duyệt kế hoạch):

- Một product review cho mỗi `OrderItem`.
- Chỉ Buyer sở hữu đơn đã Delivered mới được review.
- Mua lại trong đơn khác được review tiếp.
- Rating ngoài 1–5 bị từ chối.
- Product review tách khỏi seller feedback; không suy sentiment từ số sao.

**Quyết định thiết kế của agent** (nằm trong scope đã duyệt, người dùng chưa xác nhận riêng):

- "Đã nhận" được hiểu là `OrderTable.status = Delivered`.
- Mã lỗi 400/403/404/409.
- Gửi trùng trả 409, không coi là thành công idempotent.
- Comment tối đa 2000 ký tự UTF-16, được trim, từ chối control char.
- Thứ tự lock và hình dạng schema bên dưới.

**Giả định đang mở — [Q-BIZ-001](../questions/INDEX.md):**

- Comment là tùy chọn.
- Chưa có thao tác sửa hoặc xóa review.

Server hiện chạy theo giả định này nhưng **chưa được người dùng chốt**. Nếu người dùng yêu cầu comment bắt buộc, phải thêm kiểm tra ở `ReviewService`; validation ở UI D02 không thay thế được kiểm tra server. Muốn chặn cả ở tầng SQL thì cần migration mới (0007 đã phát hành, không sửa).

## Quy tắc gửi

Service kiểm tra theo thứ tự cố định, trong một transaction:

1. Input hợp lệ.
2. Actor có role Buyer trong DB; sai role trả 403.
3. OrderItem tồn tại và đơn thuộc actor; thiếu hoặc của người khác đều trả 404, không lộ sự tồn tại.
4. `OrderTable.status = Delivered`; chưa nhận trả 409.
5. OrderItem có ProductId.
6. Chưa có review cho OrderItem đó; trùng trả 409.

Admin và Seller không có quyền vượt.

- **Nguồn "đã nhận":** status chỉ chuyển sang Delivered qua thao tác B06 `Receive` của đúng Buyer, kèm Payment Paid và Shipping Delivered trong cùng transaction (contract [ORDERS](ORDERS.md)). Vì vậy D01 không kiểm tra lại Payment/Shipping. Đơn legacy/seed có status Delivered được coi là đã nhận.
- **Input:** `OrderItemId`, `Rating` (số nguyên 1–5), `Comment` (theo Q-BIZ-001). Comment được trim; chuỗi rỗng hoặc chỉ có khoảng trắng lưu NULL; cho phép TAB/CR/LF. Reviewer, Product và thời điểm tạo lấy từ server và OrderItem; form không nhận các trường này.
- **Lỗi:** input sai → `Invalid` (400); sai role → `Forbidden` (403); thiếu/không thuộc actor → `NotFound` (404); chưa nhận, trùng, hoặc item không còn product → `Conflict` (409). Lần gửi thứ hai luôn trả 409 và không đổi review gốc.
- **Đồng thời:** transaction ReadCommitted, lock theo thứ tự của B06: User HOLDLOCK → OrderTable HOLDLOCK (shared; B06 dùng UPDLOCK) → OrderItem HOLDLOCK → kiểm tra Review bằng `UPDLOCK,HOLDLOCK` → INSERT. Unique index là lớp chặn cuối; lỗi 2601/2627 trả Conflict. Lỗi khác thì rollback và ném lại.
- **Eligibility:** `ListForOrderAsync(buyerId, orderId)` trả tối đa 50 dòng, mỗi dòng có `CanReview`, `Reviewed`, `Reason`; trả null (404) nếu không phải Buyer chủ đơn. Chỉ đọc.

## Review legacy

**Đã quyết định và triển khai ở D01:**

- Hàng `Review` có `OrderItemId IS NULL` (kể cả rating ngoài 1–5 hoặc NULL) được giữ nguyên: không backfill, không sửa, không xóa.
- Hàng legacy không phải review gắn đơn. Nó không đánh dấu `Reviewed` cho OrderItem nào và không chặn Buyer review OrderItem của mình, vì eligibility và kiểm tra trùng chỉ xét `OrderItemId`.

**Chờ D02 — [Q-BIZ-002](../questions/INDEX.md):** có hiển thị hàng legacy và có đưa vào điểm trung bình/số lượt không. D01 chưa quyết định điều này.

## Schema (DbUp 0007, chỉ mở rộng)

- `Review` thêm `OrderItemId int NULL`.
- `OrderItem` thêm `UNIQUE(id, productId)` để làm đích cho composite FK.
- `FK_Review_OrderItemProduct (OrderItemId, productId) → OrderItem(id, productId)`.
- `UX_Review_OrderItem`: unique, có filter `WHERE OrderItemId IS NOT NULL`.
- `CK_Review_OrderLink`: khi có OrderItemId thì productId/reviewerId/rating/createdAt NOT NULL, rating 1–5, comment NULL hoặc không trắng và ≤4000 byte. Hàng có OrderItemId NULL không bị ràng buộc.
- EF chỉ map `Review.OrderItemId` (FK một cột + unique index có filter). Composite FK và CHECK chỉ tồn tại trong SQL, vì EF không cho alternate key nullable. Quyền `ebay_app` trên Review đã có từ 0002.

## Endpoint

`POST /Reviews/Create`: form, antiforgery toàn cục, `Authorize(Policy="Buyer")`, no-store. Trường: `OrderItemId`, `Rating`, `Comment`. Thành công thì redirect `/Orders/Details/{orderId}`. Không có GET hay view; UI thuộc D02.

## Kiểm tra bắt buộc

- Chưa nhận, sai owner, sai role, gửi trùng, rating sai, comment sai.
- Mua lại; hai request đồng thời; rollback khi INSERT lỗi.
- Migration giữ dữ liệu legacy và metadata; ràng buộc SQL chặn ghi trực tiếp sai.
- HTTP: bỏ qua overposting, thiếu token trả 400, đúng mã theo role/owner.
