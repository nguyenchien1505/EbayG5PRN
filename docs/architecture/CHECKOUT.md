# B05 — Buy Now / COD contract

Chốt kỹ thuật 06/10/2026, dependency B02/B04 verified tại 06bb5f3. UI Q-UI-006 Answered: user chốt C1 và bố cục đề xuất; xem [UI_CANDIDATES](UI_CANDIDATES.md).

- Một fixed-price item mỗi đơn, quantity 1–99. Buyer từ authenticated principal; service kiểm tra lại role Buyer trong DB. Không bind buyer/seller/price/total/payment status từ client. USD demo; total = giá DB × quantity, không thêm phí/thuế/giao hàng thật.
- Địa chỉ chỉ của Buyer; thêm mới với fullName/phone/street/city/state/country đúng độ dài schema (state optional). Không edit/delete trong B05 nên địa chỉ đã gắn đơn không đổi. Legacy địa chỉ thiếu phone vẫn chọn được nếu name/street/city/country hợp lệ. Mặc định chỉ dùng để ưu tiên hiển thị, không tự sửa seed.
- Quote lấy giá server; POST có ExpectedUnitPrice chỉ để phát hiện giá thay đổi. Giá khác trả 409 để Buyer review lại, không tạo đơn. Decimal total phải vừa decimal(10,2).
- Transaction SQL Server ReadCommitted: khóa Buyer UPDLOCK/HOLDLOCK, kiểm tra replay, khóa Product rồi Seller/Address và các Inventory của product UPDLOCK/HOLDLOCK. Tồn kho cộng các quantity dương bằng long, trừ theo inventory ID tăng dần; NULL/âm không có sẵn. Product phải còn hợp lệ theo CATALOG. Khóa product chung giữa các Buyer ngăn oversell; khóa Buyer tuần tự hóa yêu cầu trùng. Mọi write có cancellation và rollback transaction khi lỗi/thiếu stock/giá đổi.
- Tạo OrderTable Pending, OrderItem snapshot UnitPrice, Payment COD/Pending/PaidAt NULL và trừ stock atomic. Không ShippingInfo, không chuyển trạng thái B06.
- B06 bổ sung OrderTable.SellerId snapshot ngay khi tạo đơn từ seller đã khóa; [ORDERS](ORDERS.md) quản lý history và Pending→Shipped→Delivered/COD Paid mô phỏng, không trừ inventory lần nữa.
- CheckoutKey UUID không rỗng, scope Buyer. Additive nullable CheckoutKey/CheckoutFingerprint trên OrderTable + unique filtered (buyerId,CheckoutKey), không thay 17 bảng gốc. Fingerprint SHA256 của product/address/quantity/expected price; replay cùng payload trả đơn cũ dù giá/tồn hiện tại đổi, key cùng Buyer khác payload trả 409. Không coi disable button là chống trùng. Buyer khác dùng cùng UUID độc lập. Rollback không tiêu thụ key.
- GET confirmation chỉ Buyer owner; foreign/missing cùng 404. GET checkout không write/reserve stock. Unsafe MVC có global antiforgery. Không nhận method/status từ client; không thanh toán hay vận chuyển thật.

Files triển khai theo scope đã ghi trước code: Application/Checkout DTO/interface; Infrastructure/Checkout service; Core OrderTable + EF mapping; DbUp 0004 + checksum; Web CheckoutController/forms/views và nối Details sau duyệt UI; SQL/HTTP CheckoutTests; task/evidence/handoff/matrix.

UI đã triển khai: Ship to chọn địa chỉ owned, Add address màn riêng, Review item/quantity, Pay with COD demo, Order summary/Place order. Desktop hai cột, mobile một cột; tổng trên UI chỉ preview, POST tính lại server. Giá đổi/stock conflict trả 409 và hiển thị quote mới để review; địa chỉ/quantity invalid trả 400. Add address giữ quantity và chọn địa chỉ mới. Confirmation owner-only không thay thế purchase history B06.

## Quyết định H06a (checkout theo C2)

28/10/2026, [ADR-003](../decisions/ADR-003-ui-fidelity-references.md), mẫu C2 (live 27/10/2026, thay C1) trong [references](../references/ui/INDEX.md). Chỉ đổi giao diện; POST, field, validation, 400/409, replay và COD của B05 giữ nguyên.

- **Shell checkout:** header tối giản (logo + "Checkout"), không search/danh mục; footer chỉ dòng legal. Không có "Give us feedback" (không có tính năng).
- **Cột trái** (các khối ngăn bằng đường kẻ, như C2): **Pay with** một ô "Cash on delivery" chọn sẵn — clone chỉ có COD, không hiện PayPal/card/Google Pay. **Ship to** radio các địa chỉ của Buyer + "Add a new address". **Review order** seller (avatar chữ cái, tên, "x% positive feedback" khi có theo H02), ảnh đầu sản phẩm (H05), tiêu đề link item, giá, ô Quantity viền kiểu C2 (vẫn input số 1–99), "Returns accepted", **Delivery** một lựa chọn chọn sẵn "Est. delivery: <ngày>" = `DisputeTiming.EstimatedDelivery(now)` + "Standard Shipping" + "Free" (như H05).
- **Order Summary** (nền xám, sticky desktop): Item (n), Shipping Free, Order total, nút **"Confirm and pay"** (thay "Place order"); disabled khi chưa có địa chỉ hoặc hết hàng, dòng dưới nút ghi lý do như C2 ("Add a shipping address" / "This item is out of stock"), ngược lại "Cash on delivery. No money is collected."; eBay Money Back Guarantee dạng text như H05.
- **Bỏ** (không có dữ liệu/tính năng, ADR-003 §3): Gift cards, coupon, Donate to charity, Add note for seller, quy đổi tiền tệ, lựa chọn giao hàng thứ hai, Options/Condition.
- **Add address:** trang riêng cùng shell (không inline, không cần JS); trường B05 giữ nguyên (Country or region, Full name, Street address, City, State optional, Phone) dạng ô nhãn nổi như C2; nút "Add" + "Cancel" (về checkout). Không First/Last, Street 2, ZIP, "Save as primary" vì bảng Address không có cột tương ứng.
- **Confirmation:** không có ảnh mẫu (không đặt được đơn thật, Q-UI-011) → giữ nội dung B05 ("Your order is placed"), chỉ đổi kiểu theo shell checkout.
- **Mobile:** một cột, Order Summary sau Review order.
