# F03 — Test tích hợp xuyên suốt và hành trình trình duyệt

Contract do agent soạn ngày 07/10/2026 trên main `15e72bc`, trước khi sửa source.

Task này dựa trên:

- [DEPLOYMENT](DEPLOYMENT.md) (F01): stack HTTPS và smoke.
- [SCALE_OUT](SCALE_OUT.md) (F02): 2 replica, key ring và rate limit dùng chung.
- Contract của từng module B–E.
- PLAN §5: test nghiệp vụ đã viết theo từng task; F03 kiểm **xuyên suốt hệ thống**.

Acceptance của task: **luồng chính và quyền truy cập vượt qua kiểm thử.**

Người dùng chốt ngày 07/10/2026: dùng **Playwright .NET** (NuGet `Microsoft.Playwright`) điều khiển **Chrome có sẵn** trên máy và trên runner GitHub (`channel: chrome`), không tải thêm trình duyệt.

Requirement được bổ sung bằng chứng:

- **G5-N05** (C05, F03): message ID/UTC/trace liên kết được log và DB, không lộ nội dung.
- **OPS-01**: chat xuyên instance qua stack thật. Phần đo tải thuộc F05.
- **G5-N01**: chạy trên WSS thật; kiểm thử bảo mật thuộc F04.

Ngoài phạm vi: kiểm thử bảo mật chuyên sâu (F04), đo tải/latency (F05), K8s (F06).

## Thành phần

| Phần | Nội dung |
|---|---|
| `tests/EbayGroup5.Journeys` | Console .NET 8 + Playwright, chạy Chrome headless trên stack của `Test-AppStack.ps1` (2 replica, Nginx HTTPS). Chrome chỉ chấp nhận đúng khóa công khai của chứng chỉ server tạm (`--ignore-certificate-errors-spki-list=<SPKI SHA-256>`), không dùng "ignore mọi lỗi HTTPS". Mỗi vai trò có browser context riêng. Khi fail thì chụp ảnh màn hình vào `artifacts/stack/<hex>/` (bị ignore, không commit) |
| `scripts/Test-AppStack.ps1` | Thêm sau smoke F01/F02: seed demo (`--seed-demo`) và bật credential demo bằng mật khẩu ngẫu nhiên (`--enable-demo-auth`) qua image `migrate`; chạy Journeys; kiểm log correlation (bên dưới) |
| `tests/EbayGroup5.Database.Tests/AccessMatrixTests.cs` | Ma trận quyền: route × vai trò (khách, Buyer, Seller, Admin) × đối tượng của người khác, kiểm trên một host |
| Nginx `traceparent` | `proxy_set_header traceparent "00-$request_id-<16 hex đầu của request_id>-01"` và bỏ `tracestate`. Request trong app nhận **Activity TraceId = request id của Nginx**. Header do client gửi bị ghi đè |
| Log container | `Logging__Console__FormatterName=json` cho web: log có cấu trúc, tìm được theo trường |

## Hành trình trình duyệt (dữ liệu: seed demo A06)

| # | Vai trò | Luồng | Khẳng định |
|---|---|---|---|
| J1 | Buyer | Đăng nhập → catalog → sản phẩm → Buy It Now → chọn địa chỉ → Place order | Có trang xác nhận; Purchases có đơn mới |
| J2 | Buyer + Seller (2 context song song) | Buyer bấm **Message** trên seller card, gửi tin; Seller mở **Messages**, thấy tin **realtime**, trả lời; Buyer thấy trả lời realtime trong panel | WSS qua Nginx; hai context có thể nằm trên hai replica khác nhau (Redis backplane). Nội dung tin chỉ dùng chuỗi đánh dấu ngẫu nhiên |
| J3 | Seller → Buyer | Seller: Sales → đơn J1 → Mark as shipped. Buyer: đơn → Confirm received → Leave feedback (5 sao + comment, seller feedback Positive) | Trang sản phẩm có review đã xác minh; hồ sơ seller có feedback |
| J4 | Buyer + Seller | Buyer: Return this item → lý do Item damaged → details → Confirm return. Seller: Returns and requests → See details → Accept the return → Continue → Accept return. Buyer: I shipped the item + tracking. Seller: Refund | Trạng thái cuối "Closed — refunded"; timeline có thông báo tự động của eBay |
| J5 | Seller + Admin | Seller báo cáo review của J3 (Report). Admin: Moderation → nội dung → Hide | Review biến mất khỏi trang sản phẩm công khai |
| J6 | Mọi vai trò | Khách mở trang cần đăng nhập → về Login. Buyer mở `/Admin/Moderation` → 403. Seller mở `/Orders/Purchases` → 403. Buyer mở request của người khác → 404 | Trang lỗi có thông điệp, không lộ chi tiết |

**Lỗi tìm được bằng journey:** Nginx gửi `Host $host` (bỏ port), nên redirect tuyệt đối tới Login trỏ sang `https://localhost` (cổng 443). Đã sửa bằng `Host $http_host` (giữ port; AllowedHosts vẫn lọc theo tên); smoke kiểm thêm origin của redirect.

Escalate → Admin Decide cần vượt hạn 3 ngày làm việc. Phần này đã được kiểm tra ở E04/E05 bằng HTTP và đồng hồ giả, nên không lặp lại trên trình duyệt.

## Ma trận quyền (HTTP, một host)

Kiểm trong `AccessMatrixTests`. Các cột: khách / buyer chủ / buyer khác / seller chủ / seller khác / admin. "Login" nghĩa là chuyển về `/Account/Login`.

| Route | Khách | Buyer chủ | Buyer khác | Seller chủ | Seller khác | Admin |
|---|---|---|---|---|---|---|
| `/Orders/Purchases` | Login | 200 | 200 | 403 | 403 | 403 |
| `/Orders/Sales` | Login | 403 | 403 | 200 | 200 | 403 |
| `/Orders/Details/{đơn}` | Login | 200 | 404 | 200 | 404 | 403 |
| `/Messages` | Login | 200 | 200 | 200 | 200 | 403 |
| `/api/v1/chat/conversations` | 401 (không redirect) | 200 | 200 | 200 | 200 | 403 |
| `/Disputes` | Login | 200 | 200 | 200 | 200 | 403 |
| `/Disputes/Details/{request}` | Login | 200 | 404 | 200 | 404 | 403 |
| `/Disputes/NotReceived?orderItemId=` (đã có request) | Login | 409 | 404 | 403 | 403 | 403 |
| `/Admin/Moderation`, `/Admin/Disputes`, `/Admin/Dispute/{id}` | Login | 403 | 403 | 403 | 403 | 200 |
| `/Checkout?productId=` | Login | 200 | 200 | 403 | 403 | 403 |
| `/Reviews/Create?orderItemId=` (đơn chưa giao) | Login | 200, chỉ có lý do, không có form (D02) | 404 | 403 | 403 | 403 |
| `/Account/Me` | Login | 200 | 200 | 200 | 200 | 200 |
| `/Catalog/Details/{id}`, `/Sellers/Feedback/{id}` | 200 | 200 | 200 | 200 | 200 | 200 |

Bản nháp đầu của bảng ghi `/Reviews/Create` (đơn chưa giao) là 409. Contract D02 lại quy định "dòng không đủ điều kiện hiện lý do, không hiện form", nên bảng đã được sửa theo D02 và test kiểm thêm rằng không có form.

## Log correlation (G5-N05), kiểm trên stack thật

Sau J2, script đọc DB (sqlcmd trong container SQL, mật khẩu không đi qua command line của host) và log:

- Lấy `Message.Id`, `Message.TraceId` của tin J2.
- Log Nginx có `rid=<TraceId>` cho request đã mang tin, tức `GET /hubs/chat` (WebSocket) hoặc REST.
- Log web (JSON) có `message=<Id>` và `trace=<TraceId>`.
- **Chuỗi nội dung đánh dấu của J2 không xuất hiện** trong bất kỳ log nào (Nginx, cả hai replica web).

## Chạy

- `pwsh -NoProfile -File scripts/Test-AppStack.ps1` (CI cũng chạy): smoke F01/F02 + Journeys + correlation.
- Cần Chrome đã cài sẵn. Nếu không có Chrome, script báo lỗi rõ ràng, không tự tải.
