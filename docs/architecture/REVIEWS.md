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

**Người dùng chốt 06/10/2026 — [Q-BIZ-001/002](../questions/INDEX.md)** (sau khi đối chiếu eBay Help/Community):

- Rating 1–5 bắt buộc, comment tùy chọn (eBay tách "product ratings" và "reviews").
- Buyer không tự sửa/xóa review; vi phạm xử lý bằng Report + Admin ẩn mềm có audit ở D05.
- Chỉ review gắn đơn được hiển thị/tính; không thêm tiêu đề review (giữ giản lược, không migration 0008).

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
- **Input:** `OrderItemId`, `Rating` (số nguyên 1–5, bắt buộc), `Comment` (tùy chọn, Q-BIZ-001). Comment được trim; chuỗi rỗng hoặc chỉ có khoảng trắng lưu NULL; cho phép TAB/CR/LF. Reviewer, Product và thời điểm tạo lấy từ server và OrderItem; form không nhận các trường này.
- **Lỗi:** input sai → `Invalid` (400); sai role → `Forbidden` (403); thiếu/không thuộc actor → `NotFound` (404); chưa nhận, trùng, hoặc item không còn product → `Conflict` (409). Lần gửi thứ hai luôn trả 409 và không đổi review gốc.
- **Đồng thời:** transaction ReadCommitted, lock theo thứ tự của B06: User HOLDLOCK → OrderTable HOLDLOCK (shared; B06 dùng UPDLOCK) → OrderItem HOLDLOCK → kiểm tra Review bằng `UPDLOCK,HOLDLOCK` → INSERT. Unique index là lớp chặn cuối; lỗi 2601/2627 trả Conflict. Lỗi khác thì rollback và ném lại.
- **Eligibility:** `ListForOrderAsync(buyerId, orderId)` trả tối đa 50 dòng, mỗi dòng có `CanReview`, `Reviewed`, `Reason`; trả null (404) nếu không phải Buyer chủ đơn. Chỉ đọc.

## Review legacy

**Đã quyết định và triển khai ở D01:**

- Hàng `Review` có `OrderItemId IS NULL` (kể cả rating ngoài 1–5 hoặc NULL) được giữ nguyên: không backfill, không sửa, không xóa.
- Hàng legacy không phải review gắn đơn. Nó không đánh dấu `Reviewed` cho OrderItem nào và không chặn Buyer review OrderItem của mình, vì eligibility và kiểm tra trùng chỉ xét `OrderItemId`.

**Chốt 06/10/2026 — [Q-BIZ-002](../questions/INDEX.md):** hàng legacy không hiển thị và không tính vào trung bình/số lượt; D02 chỉ đọc review có `OrderItemId IS NOT NULL`.

## Schema (DbUp 0007, chỉ mở rộng)

- `Review` thêm `OrderItemId int NULL`.
- `OrderItem` thêm `UNIQUE(id, productId)` để làm đích cho composite FK.
- `FK_Review_OrderItemProduct (OrderItemId, productId) → OrderItem(id, productId)`.
- `UX_Review_OrderItem`: unique, có filter `WHERE OrderItemId IS NOT NULL`.
- `CK_Review_OrderLink`: khi có OrderItemId thì productId/reviewerId/rating/createdAt NOT NULL, rating 1–5, comment NULL hoặc không trắng và ≤4000 byte. Hàng có OrderItemId NULL không bị ràng buộc.
- EF chỉ map `Review.OrderItemId` (FK một cột + unique index có filter). Composite FK và CHECK chỉ tồn tại trong SQL, vì EF không cho alternate key nullable. Quyền `ebay_app` trên Review đã có từ 0002.

## Endpoint

`POST /Reviews/Create`: form, antiforgery toàn cục, `Authorize(Policy="Buyer")`, no-store. Trường: `OrderItemId`, `Rating`, `Comment`. Thành công thì redirect `/Orders/Details/{orderId}`. D02 bổ sung GET form và view lỗi (xem dưới).

## Kiểm tra bắt buộc

- Chưa nhận, sai owner, sai role, gửi trùng, rating sai, comment sai.
- Mua lại; hai request đồng thời; rollback khi INSERT lỗi.
- Migration giữ dữ liệu legacy và metadata; ràng buộc SQL chặn ghi trực tiếp sai.
- HTTP: bỏ qua overposting, thiếu token trả 400, đúng mã theo role/owner.

## D02 — Giao diện review và điểm trung bình

Contract soạn trước khi sửa source 06/10/2026, baseline main `ae10f9d`. Dependency: D01 runtime `6ba273a` có CI37418785742 pass; B04 catalog contract [CATALOG](CATALOG.md). Phạm vi: form review của Buyer, tổng hợp và danh sách review trên sản phẩm. Ngoài phạm vi: seller feedback/uy tín (D03–D04), Report/ẩn/audit (D05), review trên lưới catalog.

**Nguồn hiển thị:** chỉ review gắn đơn (`OrderItemId IS NOT NULL`; CHECK bảo đảm rating 1–5, có reviewer/createdAt), product thuộc catalog B04. Toàn bộ query nằm trong một hàm lọc chung của `ReviewService` để D05 thêm điều kiện ẩn ở một chỗ.

**Tổng hợp (G5-04):**

- `Ratings` = số review gắn đơn của product; `Reviews` = số review trong đó có comment (giống eBay "N product ratings · M reviews").
- `Average` = tổng rating / `Ratings`, làm tròn 1 chữ số thập phân (MidpointRounding.AwayFromZero). `Ratings = 0` thì `Average = null` và hiển thị "No ratings yet"; không chia cho 0.
- Phân bố số lượt theo 5→1 sao; tính từ một query GROUP BY rating trên SQL.

**Trang sản phẩm `/Catalog/Details/{id}`:** dưới tiêu đề hiện sao + điểm + "N product ratings" (link tới mục review). Mục "Ratings and reviews": điểm lớn, thanh phân bố, tối đa 3 review mới nhất có comment, link "See all M reviews".

**Trang tất cả review `/Catalog/Reviews/{id}?page=&sort=`:**

- Chỉ review có comment, 10 dòng/trang. `sort` ∈ `recent` (mặc định, id giảm dần), `highest` (rating giảm, id giảm), `lowest` (rating tăng, id giảm); thứ tự ổn định.
- Page nguyên dương; sort ngoài danh sách hoặc page sai trả 400; page vượt trả danh sách rỗng, không tự reset. Product không thuộc catalog trả 404 (giống B04).
- Mỗi review: tên người viết (username; NULL hiển thị "eBay user"), sao, nhãn "Verified purchase", ngày UTC "MMM dd, yyyy", comment. Razor encode toàn bộ; comment giữ xuống dòng bằng CSS, không render HTML.
- Trang public, không cần đăng nhập, không lộ email/order/OrderItem ID.

**Form review (R1, Buyer):**

- Orders/Details của Buyer: mỗi dòng đủ điều kiện có nút "Leave a review"; dòng đã review hiện "You rated this item n out of 5"; dòng chưa nhận hiện lý do. Seller không thấy phần này.
- `GET /Reviews/Create?orderItemId=`: Buyer chủ đơn (khác chủ/thiếu 404, sai role 403). Trang "Leave a review" theo R1: thẻ item, "How would you rate this item?" (5 radio sao, bắt buộc), "Tell us more about this item (optional)" (textarea, bộ đếm 0/2000). Dòng không đủ điều kiện hiện lý do, không hiện form.
- `POST /Reviews/Create` giữ quy tắc D01. Lỗi input render lại form với giá trị đã nhập và thông báo (400); chưa nhận/trùng render lý do (409); không thuộc actor 404; thiếu context hợp lệ thì trả mã lỗi không kèm form. Thành công redirect Orders/Details kèm thông báo TempData "Thanks — your review was posted."
- Validation HTML chỉ hỗ trợ người dùng; server vẫn là nơi quyết định.

**Kiểm tra bắt buộc:** tổng hợp/làm tròn/phân bố đúng, legacy và product khác bị loại, không có review → null; phân trang không trùng/thiếu, sort, page/sort sai 400, product ngoài catalog 404; HTML encode; Orders/Details hiện đúng nút/trạng thái theo eligibility; GET/POST form theo owner/role, render lại khi lỗi; browser desktop + mobile 320px.
