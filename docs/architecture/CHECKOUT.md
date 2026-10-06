# B05 — Buy Now / COD contract

Chốt kỹ thuật 06/10/2026, dependency B02/B04 verified tại 06bb5f3. UI Q-UI-006 Answered: user chốt C1 và bố cục đề xuất; xem [UI_CANDIDATES](UI_CANDIDATES.md).

- Một fixed-price item mỗi đơn, quantity 1–99. Buyer từ authenticated principal; service kiểm tra lại role Buyer trong DB. Không bind buyer/seller/price/total/payment status từ client. USD demo; total = giá DB × quantity, không thêm phí/thuế/giao hàng thật.
- Địa chỉ chỉ của Buyer; thêm mới với fullName/phone/street/city/state/country đúng độ dài schema (state optional). Không edit/delete trong B05 nên địa chỉ đã gắn đơn không đổi. Legacy địa chỉ thiếu phone vẫn chọn được nếu name/street/city/country hợp lệ. Mặc định chỉ dùng để ưu tiên hiển thị, không tự sửa seed.
- Quote lấy giá server; POST có ExpectedUnitPrice chỉ để phát hiện giá thay đổi. Giá khác trả 409 để Buyer review lại, không tạo đơn. Decimal total phải vừa decimal(10,2).
- Transaction SQL Server ReadCommitted: khóa Buyer UPDLOCK/HOLDLOCK, kiểm tra replay, khóa Product rồi Seller/Address và các Inventory của product UPDLOCK/HOLDLOCK. Tồn kho cộng các quantity dương bằng long, trừ theo inventory ID tăng dần; NULL/âm không có sẵn. Product phải còn hợp lệ theo CATALOG. Khóa product chung giữa các Buyer ngăn oversell; khóa Buyer tuần tự hóa yêu cầu trùng. Mọi write có cancellation và rollback transaction khi lỗi/thiếu stock/giá đổi.
- Tạo OrderTable Pending, OrderItem snapshot UnitPrice, Payment COD/Pending/PaidAt NULL và trừ stock atomic. Không ShippingInfo, không chuyển trạng thái B06.
- CheckoutKey UUID không rỗng, scope Buyer. Additive nullable CheckoutKey/CheckoutFingerprint trên OrderTable + unique filtered (buyerId,CheckoutKey), không thay 17 bảng gốc. Fingerprint SHA256 của product/address/quantity/expected price; replay cùng payload trả đơn cũ dù giá/tồn hiện tại đổi, key cùng Buyer khác payload trả 409. Không coi disable button là chống trùng. Buyer khác dùng cùng UUID độc lập. Rollback không tiêu thụ key.
- GET confirmation chỉ Buyer owner; foreign/missing cùng 404. GET checkout không write/reserve stock. Unsafe MVC có global antiforgery. Không nhận method/status từ client; không thanh toán hay vận chuyển thật.

Files triển khai theo scope đã ghi trước code: Application/Checkout DTO/interface; Infrastructure/Checkout service; Core OrderTable + EF mapping; DbUp 0004 + checksum; Web CheckoutController/forms/views và nối Details sau duyệt UI; SQL/HTTP CheckoutTests; task/evidence/handoff/matrix.

UI đã triển khai: Ship to chọn địa chỉ owned, Add address màn riêng, Review item/quantity, Pay with COD demo, Order summary/Place order. Desktop hai cột, mobile một cột; tổng trên UI chỉ preview, POST tính lại server. Giá đổi/stock conflict trả 409 và hiển thị quote mới để review; địa chỉ/quantity invalid trả 400. Add address giữ quantity và chọn địa chỉ mới. Confirmation owner-only không thay thế purchase history B06.
