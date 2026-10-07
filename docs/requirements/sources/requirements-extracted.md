## Nhóm 1: Người mua (Buyer)

### Yêu cầu tổng hợp:
- Đăng ký tài khoản người dùng (email, password, xác nhận email)
- Đăng nhập/đăng xuất, cập nhật thông tin cá nhân
- Xem danh sách sản phẩm (phân trang, lọc theo danh mục, giá, tên...)
- Xem chi tiết sản phẩm (ảnh, mô tả, người bán, đánh giá...)
- Thêm sản phẩm vào giỏ hàng (Cart local & server)
- Thực hiện đặt hàng (tạo đơn hàng mới)
- Chọn địa chỉ giao hàng (Address - nhiều địa chỉ, chọn mặc định)
- Thanh toán (giả lập qua PayPal, COD)
- Xem lịch sử đơn hàng: trạng thái, chi tiết từng đơn hàng
- Gửi yêu cầu hoàn trả đơn hàng
- Gửi đánh giá sản phẩm (Review: số sao, bình luận)
- Sử dụng mã giảm giá (Coupon)
- Xem thông báo hệ thống: đơn hàng, khuyến mãi, phản hồi

✅ Bảo mật thông tin cá nhân và giao dịch (hash password, JWT, CSRF, SSL)

🚀 Tốc độ tải trang tìm kiếm và chi tiết sản phẩm phải dưới 1s

📱 Giao diện phải responsive, dễ thao tác trên mobile

⚙️ Hệ thống phải có thể mở rộng khi có nhiều user truy cập cùng lúc

🐞 Gỡ lỗi dễ qua log chi tiết và phân biệt lỗi client/server


## Nhóm 2: Người bán (Seller)

### Yêu cầu tổng hợp:
- Đăng nhập và chuyển đổi sang chế độ bán hàng
- Cập nhật hồ sơ cửa hàng (Store name, banner, mô tả)
- Đăng bán sản phẩm (form nhập dữ liệu, upload ảnh)
- Quản lý danh sách sản phẩm (chỉnh sửa, tạm ẩn, xoá)
- Quản lý tồn kho (Inventory), số lượng sản phẩm còn lại
- Tạo mã giảm giá, voucher theo sản phẩm cụ thể
- Xác nhận đơn hàng, in phiếu vận chuyển (giả lập)
- Cập nhật trạng thái đơn hàng (giao hàng thành công/thất bại)
- Xem đánh giá sản phẩm do người mua đánh giá
- Trả lời phản hồi hoặc gửi phản hồi hệ thống (Feedback)
- Báo cáo doanh số, số đơn hàng theo tuần/tháng
- Quản lý các khiếu nại liên quan đến đơn hàng đã bán

✅ Chỉ seller đã xác minh mới được đăng sản phẩm

🛡 Hạn chế spam sản phẩm (rate limit API + xác thực reCaptcha nếu cần)

📊 Dashboard phải tải nhanh kể cả khi seller có hàng trăm sản phẩm

📈 Cho phép mở rộng tính năng tạo store riêng theo thời gian

🧪 Log chi tiết lỗi khi đăng/bán hàng để hỗ trợ nhanh support


## Nhóm 3: Quản trị viên (Admin)

### Yêu cầu tổng hợp:
- Giao diện dashboard tổng quan: số lượng user, sản phẩm, đơn hàng
- Danh sách người dùng: duyệt tài khoản mới, khoá/mở khoá
- Danh sách sản phẩm: xoá hoặc ẩn các sản phẩm vi phạm
- Quản lý đơn hàng toàn hệ thống
- Thống kê hệ thống theo ngày, tháng, quý (doanh thu, đơn hàng, người dùng mới)
- Giám sát hệ thống đánh giá – phản hồi (Review + Feedback)
- Quản lý danh sách khiếu nại (Dispute): duyệt, điều phối, phân xử
- Xét duyệt yêu cầu hoàn trả đơn hàng
- Gửi email cảnh báo hệ thống hoặc thông báo toàn bộ người dùng

🔐 Admin panel chỉ truy cập từ IP nội bộ hoặc xác minh 2FA

📉 Phân quyền rõ ràng cho từng loại admin (monitor, support...)

📈 Có khả năng mở rộng dashboard để theo dõi nhiều loại thống kê hơn

💾 Dữ liệu nhạy cảm phải được log riêng (kèm mã hóa khi lưu)

🧩 Giao diện dễ sử dụng cho quản trị viên không chuyên kỹ thuật


## Nhóm 4: Hệ thống thanh toán & giao hàng (System Integration)

### Yêu cầu tổng hợp:
- Module thanh toán mô phỏng (PayPal, COD)
- Tính phí giao hàng dựa theo khu vực (giả lập logic đơn giản)
- Kết nối API vận chuyển (giả lập): tạo mã vận đơn, cập nhật trạng thái giao hàng
- Gửi email xác nhận thanh toán thành công
- Gửi email khi trạng thái đơn hàng thay đổi (giao hàng thành công, thất bại)
- Tự động huỷ đơn hàng quá thời gian chờ thanh toán
- Tính tổng tiền đơn hàng: giá sản phẩm, số lượng, phí vận chuyển, mã giảm giá

🔐 Kết nối các API thanh toán giả lập phải được kiểm tra auth token & secured key

⚡ Tốc độ xác nhận thanh toán không quá 2 giây

🔁 Hệ thống retry nếu kết nối với API vận chuyển thất bại

🧱 Module thanh toán và vận chuyển phải có thể plug-in dễ dàng (microservice/hook)

🐞 Log chi tiết transaction ID và lỗi giao tiếp giữa các module


## Nhóm 5: Chat, Đánh giá & Khiếu nại (Interaction Layer)

### Yêu cầu tổng hợp:
- Giao diện chat giữa người mua – người bán (socket.io hoặc WebSocket)
- Lưu lịch sử chat (message store theo thời gian, người gửi)
- Gửi đánh giá sau mỗi đơn hàng đã nhận (rating + comment)
- Hiển thị trung bình đánh giá trên mỗi sản phẩm
- Hệ thống điểm uy tín người bán (tính theo feedback tích cực)
- Giao diện gửi khiếu nại đơn hàng (nội dung, chọn lý do)
- Giao diện quản lý khiếu nại đã gửi (buyer) hoặc đã nhận (seller)
- Kết nối hệ thống phản hồi tự động với hệ thống admin

🔒 Tin nhắn giữa người dùng phải được bảo mật (WebSocket qua SSL, mã hóa khi cần)

💬 Giao diện chat phải mượt, không giật lag, hỗ trợ realtime

⚙️ Hệ thống xử lý khiếu nại cần đảm bảo không mất dữ liệu khi reload

📈 Mở rộng khả năng lưu trữ lịch sử chat, phản hồi nhanh khi số lượng lớn

🧪 Gỡ lỗi phải hỗ trợ debug từng tin nhắn qua thời gian/log ID


## Các yêu cầu khác (25% số điểm)

- Đáp ứng được khi lượng người dùng tăng lên: load balance, Nginx
- Hạn chế số lượt request của người trong khoảng thời gian: rate limiting
- CI&CD: Jenkins, GitHub Action
- Quản lý team: Jira
- Kiểm thử về tải, an ninh mạng: JMeter
- Đảm bảo Zero Downtime khi hệ thống cập nhật: K8s


## LƯU Ý

Đây chỉ là những yêu cầu gợi ý tối thiểu cần đạt được (làm đủ được 80% điểm). Từng nhóm cần trải nghiệm hệ thống thực sự và làm đúng theo hệ thống đã có.