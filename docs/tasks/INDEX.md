# Backlog coding

Trạng thái ở đây là đầu mối theo dõi. Planned = cần kiểm tra dependency và chốt contract trước khi code; Ready = có thể bắt đầu task. Task chi tiết vẫn cần đọc source thực tế, không đoán symbol.

| ID | Công việc | Dependency | Status |
|---|---|---|---|
| [A00](A00.md) | Tạo repo mới và quy định làm việc | — | Done |
| [A01](A01.md) | Chuẩn hóa requirement và ma trận nghiệm thu | A00 | Done |
| [A02](A02.md) | Bản đồ code tham khảo và ghi nhận eBay | A01 | Done |
| [A03](A03.md) | Scaffold solution .NET 8 và khóa phiên bản | A00 | Done |
| [A04](A04.md) | Compose SQL Server/Redis và cấu hình local | A03 | Done |
| [A05](A05.md) | Baseline, migration runner, ánh xạ 17 bảng | A04 | Done |
| [A06](A06.md) | Seed dữ liệu demo lặp lại an toàn | A05 | Done |
| [A07](A07.md) | CI build/test và kiểm tra migration | A03–A06 | Done |
| [B01](B01.md) | Đăng ký/đăng nhập/đăng xuất | A06 | Done |
| [B02](B02.md) | Authorization, CSRF và xử lý lỗi | B01 | Done |
| [B03](B03.md) | Khung giao diện eBay và điều hướng | A03 | Planned |
| [B04](B04.md) | Catalog và product detail | A06, B03 | Planned |
| [B05](B05.md) | Buy Now, địa chỉ và COD mô phỏng | B02, B04 | Planned |
| [B06](B06.md) | Lịch sử mua/bán và giao/nhận | B05 | Planned |
| [C01](C01.md) | Schema và contract chat | A05, B02 | Planned |
| [C02](C02.md) | Service/API lưu và đọc chat | C01 | Planned |
| [C03](C03.md) | SignalR có xác thực và Redis backplane | C02 | Planned |
| [C04](C04.md) | Inbox, màn hình chat và trạng thái đọc | C03, B03 | Planned |
| [C05](C05.md) | Reconnect, tải bù và truy vết | C04 | Planned |
| [D01](D01.md) | Review gắn đơn hàng và điều kiện gửi | B06 | Planned |
| [D02](D02.md) | Giao diện review và điểm trung bình | D01, B04 | Planned |
| [D03](D03.md) | Seller feedback và công thức uy tín | D01 | Planned |
| [D04](D04.md) | Trang uy tín và seller reply | D03, B03 | Planned |
| [D05](D05.md) | Kiểm duyệt và audit review/feedback | D04, B02 | Planned |
| [E01](E01.md) | Schema và state machine dispute | B06 | Planned |
| [E02](E02.md) | Form khiếu nại và lưu nháp | E01, B03 | Planned |
| [E03](E03.md) | Buyer/seller dashboard và trao đổi | E02 | Planned |
| [E04](E04.md) | Phản hồi tự động và chuyển quá hạn | E03 | Planned |
| [E05](E05.md) | Admin xử lý và kết quả mô phỏng | E04, B02 | Planned |
| [F01](F01.md) | Docker image ứng dụng và Nginx HTTPS | C05, D05, E05 | Planned |
| [F02](F02.md) | Nhiều instance, key dùng chung, rate limit | F01 | Planned |
| [F03](F03.md) | Test tích hợp và hành trình trình duyệt | F02 | Planned |
| [F04](F04.md) | Kiểm thử bảo mật | F03 | Planned |
| [F05](F05.md) | JMeter và đo realtime | F03 | Planned |
| [F06](F06.md) | Kubernetes local và migration Job | F02 | Planned |
| [F07](F07.md) | CD GitHub Actions vào cluster local | A07, F06 | Planned |
| [F08](F08.md) | Rolling update và rollback có tải | F05, F07 | Planned |
| [G01](G01.md) | Rà ma trận yêu cầu và tinh chỉnh UI | F04, F08 | Planned |
| [G02](G02.md) | Báo cáo, hướng dẫn demo và bàn giao | G01 | Planned |
