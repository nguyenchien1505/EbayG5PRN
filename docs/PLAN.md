# Kế hoạch thực hiện eBay Clone — Nhóm 5

- **Ngày chốt:** 02/10/2026.
- **Trạng thái:** Kế hoạch đã được người dùng đồng ý; chưa triển khai ứng dụng.
- **Công nghệ:** .NET 8, ASP.NET Core MVC/Razor, EF Core, SignalR, SQL Server.
- **Giao diện:** Tiếng Anh, bám phong cách và hành trình sử dụng eBay.
- **Tài liệu:** Tiếng Việt.
- **Jira:** [PRN_GROUP_5 — KAN](https://dattv.atlassian.net/plugins/servlet/project-config/KAN/details).
- **Nguyên tắc:** Viết mới có tham khảo; schema chỉ được thêm/mở rộng; task nhỏ và tài liệu đọc theo nhu cầu.

> Đây là bản kế hoạch chính trong repo. Trạng thái triển khai hiện hành ở docs/tasks/INDEX.md và docs/START_HERE.md; nội dung mô tả mốc lập kế hoạch bên dưới không thay thế trạng thái hiện hành.

## Mục lục

1. [Mục tiêu và căn cứ](#1-mục-tiêu-và-căn-cứ)
2. [Phạm vi và kiến trúc](#2-phạm-vi-và-kiến-trúc)
3. [Quy tắc nghiệp vụ](#3-quy-tắc-nghiệp-vụ)
4. [Bảo toàn database](#4-bảo-toàn-database)
5. [Backlog coding cho Codex](#5-backlog-coding-cho-codex)
6. [Git và môi trường](#6-git-và-môi-trường)
7. [CI/CD và zero downtime](#7-cicd-và-zero-downtime)
8. [Jira và phân công năm thành viên](#8-jira-và-phân-công-năm-thành-viên)
9. [Kiểm thử và nghiệm thu](#9-kiểm-thử-và-nghiệm-thu)
10. [Tài liệu và tiếp tục qua nhiều phiên](#10-tài-liệu-và-tiếp-tục-qua-nhiều-phiên)
11. [Xử lý thông tin chưa rõ và phối hợp với người dùng](#11-xử-lý-thông-tin-chưa-rõ-và-phối-hợp-với-người-dùng)
12. [Bắt đầu triển khai](#12-bắt-đầu-triển-khai)

## 1. Mục tiêu và căn cứ

Xây dựng dự án mới, hoàn thành đầy đủ chức năng nhóm 5 cùng các yêu cầu hạ tầng, kiểm thử và quản lý nhóm. Bổ sung chức năng phụ thuộc đủ để demo độc lập, không phải chờ code của nhóm khác.

Tách hai backlog:

- **Backlog triển khai cho Codex:** Task nhỏ, có đầu vào, dependency và tiêu chí hoàn thành rõ ràng.
- **Backlog Jira cho năm thành viên:** Phân công theo module, trách nhiệm kiểm tra, nghiệm thu và trình bày; liên kết task coding và minh chứng thực tế.

Jira sử dụng project **PRN_GROUP_5**, key **KAN**, tại **dattv.atlassian.net**. Tạm gọi thành viên là TV1–TV5; issue để `Unassigned` đến khi có ánh xạ tài khoản.

Chưa có ngày nộp. Chia kế hoạch theo mốc nghiệm thu và quan hệ phụ thuộc, không tự đặt deadline hoặc giờ làm giả.

### 1.1. Nguồn yêu cầu

- `Project Requiments/Ebay Clone Requiments.docx`: Yêu cầu chức năng, yêu cầu khác và lưu ý trải nghiệm eBay thực tế.
- `Project Requiments/clone_ebay_sqlserver_schema.sql`: Schema gốc bắt buộc bảo toàn.
- `ProjectCode/PRN232_Project_Group_5`: Bài nhóm 5 kỳ trước để tham khảo.
- Các dự án còn lại trong `ProjectCode`: Tham khảo có chọn lọc về kiến trúc, giao diện, nghiệp vụ, kiểm thử và triển khai.

Tên thư mục thực tế là **Project Requiments**. Không tự đổi tên thư mục nguồn.

### 1.2. Kết quả khảo sát đã có

- Đã đọc tài liệu yêu cầu, toàn bộ schema gốc, README và các phần code liên quan của nhóm 5 cũ; khảo sát thêm tài liệu, cấu hình hạ tầng và tổ chức dự án của các nhóm khác.
- Schema gốc có **17 bảng**.
- Code nhóm 5 cũ có logic xóa thành phần schema lúc khởi động, chat tin danh tính client gửi lên, lưu kết nối trong bộ nhớ tiến trình và kiểm tra điều kiện review chưa đầy đủ. Không dùng làm nền dự án mới.
- Một dự án tham khảo dùng PostgreSQL; không dùng để thay thế schema SQL Server được giao.
- Tại thời điểm khảo sát: máy có SDK .NET 10, Docker Linux đang chạy và `kubectl`; chưa có Kubernetes context. Cần chuẩn bị SDK .NET 8 và cluster local.
- Giữ .NET 8 theo lựa chọn của người dùng. Microsoft công bố kết thúc hỗ trợ ngày **10/11/2026**; ghi nhận trong tài liệu môi trường, không tự đổi framework. Nguồn: [Chính sách hỗ trợ .NET](https://dotnet.microsoft.com/en-us/platform/support/policy).

## 2. Phạm vi và kiến trúc

### 2.1. Phạm vi chức năng

| Phần | Mức triển khai |
|---|---|
| Chat buyer–seller | Realtime, lịch sử phân trang, chưa đọc/đã đọc, retry, reconnect, truy vết tin nhắn |
| Product review | Rating 1–5 và bình luận sau khi nhận hàng; trung bình và số lượt |
| Seller feedback | Positive/Neutral/Negative, bình luận, trang uy tín và phản hồi của seller |
| Dispute | Tạo khiếu nại có lý do, lưu nháp, buyer/seller quản lý, trao đổi, chuyển admin, xử lý và lịch sử |
| Tài khoản phụ thuộc | Đăng ký buyer, đăng nhập/đăng xuất, phân quyền; seed seller và admin |
| Sản phẩm phụ thuộc | Danh sách, tìm kiếm/lọc/phân trang, chi tiết, thông tin seller; dữ liệu sản phẩm seed |
| Đơn hàng phụ thuộc | Buy Now, địa chỉ nhận hàng, COD mô phỏng, lịch sử mua/bán, chuyển trạng thái giao/nhận |
| Admin phụ thuộc | Hàng đợi khiếu nại, chi tiết vụ việc, phân xử, kiểm duyệt review/feedback |
| Hạ tầng | Docker, Nginx, nhiều instance, Redis, rate limiting, GitHub Actions, Kubernetes local |
| Minh chứng | Jira, kiểm thử nghiệp vụ/bảo mật/tải, rolling update, báo cáo và kịch bản demo |

Không triển khai đầy đủ đấu giá, coupon, giỏ hàng, quản trị toàn sàn, thanh toán thật hoặc vận chuyển thật. **Các bảng tương ứng vẫn được giữ nguyên.**

### 2.2. Kiến trúc ứng dụng

Chọn **modular monolith**: Một ứng dụng web chứa MVC, REST API và SignalR, tách các lớp Core/Application, Infrastructure và Web. Không tách microservices cho bài này.

- MVC dùng Bootstrap 5, CSS riêng và JavaScript theo module.
- Web, API và Hub cùng origin; cookie xác thực `HttpOnly`, `Secure`, chống CSRF cho thao tác ghi.
- Hash password bằng thư viện chuẩn .NET; danh tính lấy từ principal phía server.
- Các module gọi application service, không gọi vòng qua HTTP giữa các phần của cùng ứng dụng.
- Dùng DTO riêng, không trả trực tiếp EF entity.
- Adapter cho thanh toán/giao hàng mô phỏng và thông báo admin, để thay thế được khi cần tích hợp.
- SQL Server lưu dữ liệu nghiệp vụ; Redis phục vụ SignalR backplane và bộ đếm rate limit dùng chung.
- Khóa Data Protection lưu bền vững và dùng chung giữa replica để không mất phiên đăng nhập khi đổi instance.

### 2.3. Interface công khai

- REST API dưới `/api/v1`: Các nhóm `catalog`, `orders`, `conversations`, `reviews`, `seller-feedback`, `disputes`, `admin/disputes`.
- SignalR Hub tại `/hubs/chat`.
- Lỗi dùng `ProblemDetails` kèm `traceId`.
- Phân trang có giới hạn; OpenAPI cho REST.
- Task contract của từng module phải chốt request/response, event và mã lỗi trước task triển khai liên quan.

## 3. Quy tắc nghiệp vụ

### 3.1. Chat

- Buyer có thể liên hệ seller từ trang sản phẩm, không cần mua trước.
- Mỗi cuộc hội thoại có hai thành viên; đọc lịch sử và gửi tin phải kiểm tra tư cách thành viên.
- Không nhận `senderId` làm căn cứ xác thực.
- Tin nhắn ghi SQL thành công trước khi xác nhận đã gửi.
- Client gửi `clientMessageId`; retry không tạo bản ghi trùng.
- Khi reconnect, tải bù từ SQL theo cursor; Redis không phải kho lưu tin nhắn.
- Lịch sử dùng cursor, mặc định 50 tin/lần, tối đa 100.
- Log chứa message ID, thời gian UTC, trace ID và kết quả; không log nội dung chat hoặc token.
- Realtime dùng WebSocket và `SkipNegotiation`, reconnect tự động; REST hỗ trợ tải bù khi mất kết nối.
- Cấu hình chỉ WebSocket và bỏ negotiation tránh yêu cầu sticky session của SignalR khi scale nhiều server. Nếu sau này đổi transport phải đánh giá lại session affinity. Nguồn: [Hướng dẫn SignalR](https://learn.microsoft.com/en-us/aspnet/core/signalr/scale).

### 3.2. Review và uy tín

- Một product review cho mỗi `OrderItem`; chỉ buyer sở hữu đơn đã `Delivered` mới được đánh giá.
- Mua lại sản phẩm trong đơn khác được đánh giá tiếp; rating ngoài 1–5 bị từ chối.
- Tách product review khỏi seller feedback. eBay có đánh giá seller Positive/Neutral/Negative; không suy diễn sentiment này từ số sao sản phẩm. Nguồn: [Seller ratings của eBay](https://www.ebay.com/help/buying/resolving-issues-sellers/seller-ratings?id=4023).
- Seller feedback gắn `OrderItem`, tối đa một lượt; seller được trả lời feedback về mình.
- Công thức bản clone: **Điểm uy tín = Positive − Negative**; **Tỷ lệ tích cực = Positive / (Positive + Negative) × 100%**. Không có mẫu thì hiển thị “No feedback yet”.
- Dùng toàn bộ feedback hợp lệ của bản clone; ghi rõ đây là quy tắc giản lược, không tuyên bố sao chép toàn bộ thuật toán eBay.
- Tổng hợp chỉ tính nội dung đang hiển thị. Kiểm duyệt dùng trạng thái ẩn và audit, không xóa bản ghi.

### 3.3. Đơn hàng và khiếu nại

- Demo tạo đơn một seller, kiểm tra tồn kho và tính tiền phía server; snapshot seller/giá để nghiệp vụ sau mua ổn định.
- Trạng thái đơn chính: `Pending → Shipped → Delivered` (Pending là tên persisted cho bước Placed, chốt B05/B06); seller chỉ thao tác đơn snapshot của mình, Buyer xác nhận đã nhận. COD mô phỏng ghi Paid cùng transaction Delivered, không thu tiền thật.
- Khiếu nại chọn dòng hàng cụ thể nhưng giữ liên kết `Dispute.orderId` gốc, tránh nhập nhằng nếu gặp đơn nhiều seller.
- Lý do: Chưa nhận hàng, khác mô tả, hư hỏng và khác. “Chưa nhận hàng” không bị buộc phải có trạng thái `Delivered`.
- Luồng khiếu nại: `Open → SellerResponded → Resolved` hoặc `Escalated → Resolved/Rejected`; buyer có thể đóng vụ việc của mình.
- Mỗi dòng hàng chỉ có một khiếu nại đang mở; kiểm tra cả tại service và database.
- Lưu nháp trên server, debounce và lưu trước chuyển trang; khôi phục sau reload. Xóa nháp sau khi gửi thành công.
- Phản hồi tự động là xác nhận tiếp nhận, hướng dẫn bước tiếp theo và chuyển hàng đợi admin khi quá hạn; không tự phán quyết.
- Mặc định cho seller ba ngày làm việc, thứ Hai–thứ Sáu, chưa tính ngày lễ. Cấu hình demo rút xuống hai phút và hiển thị rõ.
- Tham khảo hành trình eBay cho seller phản hồi trước khi nền tảng can thiệp: [eBay step in](https://www.ebay.com/help/selling/managing-returns-refunds/ask-ebay-step-help-sellers?id=4702). Không trình bày các quy tắc giản lược của demo như chính sách đầy đủ của eBay.
- Job quá hạn phải idempotent khi nhiều replica cùng chạy.
- Phân xử có lịch sử actor, thời gian và kết quả; hoàn tiền chỉ mô phỏng.

## 4. Bảo toàn database

1. Sao chép schema được giao vào baseline bất biến, lưu checksum và đối chiếu với bản nguồn.
2. Giữ đủ bảng, cột, kiểu dữ liệu, PK, FK, unique constraint hiện hữu. Không đổi tên, thu hẹp kiểu hay bỏ constraint để chạy demo.
3. EF Core ánh xạ toàn bộ schema gốc; dùng **SQL migration có phiên bản qua DbUp** làm nguồn thay đổi schema duy nhất.
4. Chỉ bổ sung bảng/cột/index/constraint phù hợp. Cột mới trên dữ liệu cũ phải nullable hoặc có bước backfill an toàn.
5. Nhóm mở rộng: Conversation/read state/idempotency; liên kết review–order item; seller feedback chi tiết; dispute draft/history/reply; notification/outbox; audit; khóa Data Protection.
6. Migration chạy bằng tiến trình/job riêng trước rollout; không chạy tranh chấp khi mỗi web replica khởi động.
7. Không rollback database bằng `DROP`; rollback ứng dụng dùng image cũ tương thích schema đã mở rộng.
8. Test metadata trên SQL Server thật xác nhận baseline còn nguyên sau mọi migration, kể cả database đã có dữ liệu.

Không dùng `EnsureDeleted`, tái tạo DB hoặc bỏ khóa ngoại để vượt qua lỗi nghiệp vụ. Reset dữ liệu demo phải là thao tác riêng có phạm vi rõ ràng, không nằm trong startup.

## 5. Backlog coding cho Codex

Mỗi mã bên dưới tương ứng **một task card độc lập** có: Mục tiêu, dependency, tài liệu cần đọc, vùng code liên quan, contract, acceptance criteria và lệnh kiểm tra.

Mặc định làm **một task mỗi phiên**. Task vượt khoảng 3–6 file nghiệp vụ hoặc gồm nhiều thay đổi độc lập phải tách thành task con trước khi code. Đây không phải giới hạn cứng cho scaffold/migration.

Trạng thái chi tiết nằm trong [task index](tasks/INDEX.md); bảng dưới đây mô tả phạm vi, không cập nhật trạng thái trùng lặp ở nhiều nơi.

| ID | Công việc | Phụ thuộc | Điều kiện hoàn thành chính |
|---|---|---|---|
| A00 | Tạo repo mới và quy định làm việc | — | Repo riêng, ignore đúng, giữ nguyên hai thư mục nguồn |
| A01 | Chuẩn hóa requirement và ma trận nghiệm thu | A00 | Mỗi yêu cầu nhóm 5 và yêu cầu khác có ID, test/minh chứng |
| A02 | Bản đồ code tham khảo và ghi nhận eBay | A01 | Có nguồn, điểm học hỏi, điểm loại bỏ, khác biệt chủ động |
| A03 | Scaffold solution .NET 8 và khóa phiên bản | A00 | Restore/build chạy được, có global.json và package lock |
| A04 | Compose SQL Server/Redis và cấu hình local | A03 | Healthcheck, volume, .env.example, hướng dẫn khởi động |
| A05 | Baseline, migration runner, ánh xạ 17 bảng | A04 | DB mới đúng schema, có test bảo toàn |
| A06 | Seed dữ liệu demo lặp lại an toàn | A05 | Buyer/seller/admin, sản phẩm, đơn nhiều trạng thái |
| A07 | CI build/test và kiểm tra migration | A03–A06 | Pipeline đầu tiên chạy được; chưa deploy |
| B01 | Đăng ký/đăng nhập/đăng xuất | A06 | Password hash, cookie, lỗi đăng nhập an toàn |
| B02 | Authorization, CSRF và xử lý lỗi | B01 | Phân quyền object-level; admin chỉ từ mạng demo cho phép |
| B03 | Khung giao diện eBay và điều hướng | A03 | Header, search, My eBay, responsive, empty/error states |
| B04 | Catalog và product detail | A06, B03 | Tìm/lọc/phân trang; điểm vào contact seller |
| B05 | Buy Now, địa chỉ và COD mô phỏng | B02, B04 | Tạo đơn thật trong DB; không tin giá client |
| B06 | Lịch sử mua/bán và giao/nhận | B05 | Hai vai trò chạy được luồng đến Delivered |
| C01 | Schema và contract chat | A05, B02 | Membership, cursor, idempotency được cố định |
| C02 | Service/API lưu và đọc chat | C01 | Không đọc chéo, không trùng khi retry |
| C03 | SignalR có xác thực và Redis backplane | C02 | Hai user khác instance nhận được tin |
| C04 | Inbox, màn hình chat và trạng thái đọc | C03, B03 | Loading, gửi lỗi, unread, phân trang hoạt động |
| C05 | Reconnect, tải bù và truy vết | C04 | Mất kết nối không mất tin đã xác nhận |
| D01 | Review gắn đơn hàng và điều kiện gửi | B06 | Chặn sai owner/chưa nhận/trùng lượt |
| D02 | Giao diện review và điểm trung bình | D01, B04 | Tổng hợp đúng, danh sách và phân trang |
| D03 | Seller feedback và công thức uy tín | D01 | Sentiment tách biệt; công thức có unit test |
| D04 | Trang uy tín và seller reply | D03, B03 | Chỉ seller liên quan được phản hồi |
| D05 | Kiểm duyệt và audit review/feedback | D04, B02 | Ẩn nội dung cập nhật đúng tổng hợp |
| E01 | Schema và state machine dispute | B06 | Reason, actor, transition, active uniqueness rõ ràng |
| E02 | Form khiếu nại và lưu nháp | E01, B03 | Reload khôi phục dữ liệu; gửi lại không tạo trùng |
| E03 | Buyer/seller dashboard và trao đổi | E02 | Danh sách đúng quyền, timeline có phân trang |
| E04 | Phản hồi tự động và chuyển quá hạn | E03 | Outbox/job chạy nhiều lần không nhân đôi |
| E05 | Admin xử lý và kết quả mô phỏng | E04, B02 | Phân xử, thông báo, audit hoàn chỉnh |
| F01 | Docker image ứng dụng và Nginx HTTPS | C05, D05, E05 | Chạy đầy đủ qua một địa chỉ HTTPS |
| F02 | Nhiều instance, key dùng chung, rate limit | F01 | Đổi instance không mất login; Hub cũng bị giới hạn |
| F03 | Test tích hợp và hành trình trình duyệt | F02 | Luồng chính và quyền truy cập vượt qua kiểm thử |
| F04 | Kiểm thử bảo mật | F03 | Kết quả thực cho IDOR, XSS, CSRF, auth, secret scan |
| F05 | JMeter và đo realtime | F03 | Dữ liệu tải, cấu hình máy, p95, báo cáo lỗi |
| F06 | Kubernetes local và migration Job | F02 | Hai web replica, SQL/Redis có volume, probes đúng |
| F07 | CD GitHub Actions vào cluster local | A07, F06 | Image theo commit SHA, deploy, smoke test |
| F08 | Rolling update và rollback có tải | F05, F07 | Minh chứng HTTP liên tục và chat phục hồi |
| G01 | Rà ma trận yêu cầu và tinh chỉnh UI | F04, F08 | Mọi yêu cầu có trạng thái và bằng chứng |
| G02 | Báo cáo, hướng dẫn demo và bàn giao | G01 | Clone/setup từ đầu, kịch bản trình bày đầy đủ |

Các mốc nghiệm thu:

- [ ] **M1 — Nền móng:** A00–A07.
- [ ] **M2 — Demo thương mại điện tử tối thiểu:** B01–B06.
- [ ] **M3 — Đủ nghiệp vụ nhóm 5:** C01–E05.
- [ ] **M4 — Đủ vận hành và kiểm thử:** F01–F08.
- [ ] **M5 — Sẵn sàng nộp:** G01–G02.

Test nghiệp vụ được viết cùng từng task. F03 là kiểm tra xuyên suốt hệ thống, không phải đợi tới cuối mới bắt đầu test.

## 6. Git và môi trường

- Tạo repo tại `D:\WINDOWS\PRN3\EbayGroup5`; không khởi tạo Git bao trùm mã tham khảo.
- Giữ `Project Requiments` và `ProjectCode` làm nguồn chỉ đọc; không đưa toàn bộ code kỳ trước vào repo mới.
- `main` luôn build được; nhánh ngắn theo task, ví dụ `task/C02-chat-api`.
- Tự commit khi hoàn thành thay đổi nhất quán và kiểm tra phù hợp đã pass. Ví dụ: `feat(chat): persist messages with retry protection [C02]`.
- Stage file cụ thể, kiểm tra diff trước commit; không commit secret, .env, log, build output hoặc thay đổi không liên quan.
- Không giả danh năm thành viên khi commit; không force-push hoặc reset làm mất công việc.
- Dùng GitHub repo private cho CI/CD; URL repo là đầu vào cấu hình khi triển khai, không tự đoán remote.
- Compose có chế độ phát triển nhẹ và demo nhiều replica; named volumes giữ dữ liệu. Reset/xóa volume là thao tác riêng.
- SQL Server 2022 Developer cho học tập; pin phiên bản image/package, không dựa vào latest.
- Kubernetes local dùng Docker Desktop cluster; manifest riêng cho Nginx, web, SQL Server, Redis và migration Job.

## 7. CI/CD và zero downtime

- CI: Restore locked dependencies → build → unit/integration test → kiểm tra migration và secret → build image.
- CD: GitHub Actions dùng runner local, chỉ nhận workflow từ nhánh tin cậy; pull request không chạy deploy.
- Image gắn commit SHA; migration mở rộng chạy trước khi thay web.
- Web có tối thiểu hai replica, readiness/startup/liveness riêng và graceful shutdown; `maxUnavailable: 0`, `maxSurge: 1`. Nguồn: [Kubernetes Deployments](https://kubernetes.io/docs/concepts/workloads/controllers/deployment/).
- Rollback đổi về image trước, không xóa schema mới.
- Nghiệm thu zero downtime cho **cập nhật ứng dụng khi SQL/Redis hoạt động bình thường**. WebSocket có thể reconnect khi pod dừng; phải phục hồi và không mất tin đã xác nhận.

## 8. Jira và phân công năm thành viên

Tạo năm Epic theo module, mỗi Epic có issue phân tích/contract, triển khai, kiểm thử và bàn giao. Issue triển khai ánh xạ tới nhiều task Codex nhỏ; không đưa toàn bộ task coding thành danh sách Jira quá vụn.

| Thành viên | Epic phụ trách | Task Codex liên quan | Minh chứng kiểm tra/trình bày |
|---|---|---|---|
| TV1 | Nền tảng, database, tài khoản, đơn hàng | A01, A05–A06, B01–B06 | ERD, bảo toàn schema, luồng mua/nhận hàng |
| TV2 | Chat và realtime | C01–C05 | Hai tài khoản chat, reconnect, log, nhiều instance |
| TV3 | Review và uy tín seller | D01–D05 | Điều kiện đánh giá, công thức, kiểm duyệt |
| TV4 | Khiếu nại và admin | E01–E05 | Khôi phục nháp, trao đổi, quá hạn, phân xử |
| TV5 | DevOps, kiểm thử hệ thống, báo cáo | A00, A02–A04, A07, F01–G02 | CI/CD, Docker/K8s, tải, bảo mật, rolling update |

TV1–TV4 phối hợp TV5 cung cấp minh chứng và phần trình bày module của mình. A02 được TV5 tổng hợp cùng người phụ trách module liên quan.

- Mỗi issue có acceptance criteria, dependency, vai trò phụ trách, task IDs và liên kết commit/test/report.
- Kiểm tra issue hiện có trong KAN trước khi tạo để tránh trùng.
- Workflow dự kiến: `To Do → In Progress → Review → Done`, ánh xạ vào trạng thái thực tế của project.
- Không đánh dấu Done chỉ vì code đã viết; phải có nghiệm thu.
- Ghi nhận thực tế Codex hỗ trợ triển khai; không tạo worklog, ngày hoàn thành hoặc review chưa xảy ra.
- Khi chưa có tài khoản từng thành viên: Assignee để Unassigned, vai trò TV1–TV5 ghi trong nội dung/nhãn phù hợp.

## 9. Kiểm thử và nghiệm thu

| Nhóm | Tình huống bắt buộc |
|---|---|
| Database | DB mới; nâng cấp DB có dữ liệu; migration lần hai; nguyên 17 bảng và constraint gốc |
| Phân quyền | Không đọc đơn/chat/dispute người khác; seller không sửa dữ liệu seller khác; user thường không vào admin |
| Chat | Giả mạo sender; retry; mất mạng; reconnect; hai instance; Redis gián đoạn; lịch sử lớn |
| Review | Chưa nhận hàng; sai owner; rating sai; gửi đồng thời; mua lại; ẩn nội dung |
| Dispute | Reload nháp; gửi hai lần; sai transition; seller sai quyền; job chạy đồng thời |
| Bảo mật | Stored XSS, CSRF, IDOR, brute force, request quá lớn, lỗi lộ dữ liệu, secret trong repo |
| Vận hành | Restart giữ dữ liệu; login qua nhiều replica; deploy; rollback |
| Giao diện | Desktop/mobile; empty/loading/error states; bàn phím; toàn bộ hành trình demo |

### 9.1. Ngưỡng nghiệm thu của dự án

Các số dưới đây là **mục tiêu do dự án đặt**, không phải số liệu bắt buộc nguyên văn từ đề:

- Dữ liệu tải riêng: 100.000 tin nhắn, 10.000 review, 1.000 dispute.
- JMeter: 50 user đồng thời trong 10 phút; p95 API chính dưới 1 giây, lỗi ngoài dự kiến dưới 1%.
- Realtime: 20 cặp chat đồng thời; p95 từ gửi đến nhận dưới 1 giây trên local.
- Rolling update: Không có HTTP 5xx trong bài đo; chat phục hồi trong 10 giây và đủ tin đã ACK.
- Stress test tăng dần tới điểm nghẽn, ghi nhận giới hạn thực tế.
- JMeter đo HTTP/nghiệp vụ; client SignalR riêng đo realtime. Không suy ra chất lượng chat từ HTTP test.

Lưu cấu hình máy, commit, seed, thời lượng và kết quả mỗi lần đo. Nếu không đạt, tạo task sửa; không tự hạ ngưỡng để ghi báo cáo đạt.

## 10. Tài liệu và tiếp tục qua nhiều phiên

### 10.1. Bộ tài liệu

Các đường dẫn dưới đây tính từ repo `EbayGroup5` dự kiến tạo tại A00.

| Tài liệu | Vai trò |
|---|---|
| AGENTS.md | Quy tắc bắt buộc; đầu/cuối phiên; database, Git, phạm vi đọc, thông tin chưa rõ |
| docs/START_HERE.md | Điểm vào ngắn: Trạng thái, task hiện tại, task tiếp theo, đường dẫn cần đọc |
| docs/requirements/ | Yêu cầu chuẩn hóa, ID, nguồn, ma trận requirement → task → test → minh chứng |
| docs/architecture/ | Module, database, auth, triển khai; tách theo chủ đề |
| docs/contracts/ | REST/SignalR, trạng thái nghiệp vụ, mã lỗi, công thức |
| docs/tasks/ | Một file mỗi task; index chỉ có ID, trạng thái, dependency, đường dẫn |
| docs/decisions/ | ADR ngắn: Quyết định, lý do, ảnh hưởng |
| docs/handoffs/ | Bàn giao từng phiên; bản LATEST ngắn |
| docs/runbooks/ | Setup, migration, test, backup/restore, deploy, rollback |
| docs/evidence/ | Chỉ mục minh chứng, commit, kết quả; file tải lớn ở CI artifact |
| docs/team/ | Phân công, ánh xạ task–Jira, hướng dẫn thuyết trình |
| docs/questions/ | Chỉ mục câu hỏi còn mở và file chi tiết theo module khi cần |

File kế hoạch này là bản đồ tổng thể; **không bắt buộc đọc lại toàn bộ trong mỗi phiên**. Không dùng một file khổng lồ làm trí nhớ toàn dự án.

Mã nguồn phản ánh hành vi hiện tại; requirement và ADR phản ánh hành vi mong muốn. Khi lệch nhau phải ghi nhận, không sửa tài liệu để hợp thức hóa lỗi code. Yêu cầu trực tiếp mới của người dùng phải được cập nhật vào tài liệu liên quan để phiên sau không dùng quyết định cũ.

### 10.2. Đầu phiên

1. Đọc AGENTS.md, START_HERE và handoff mới nhất.
2. Kiểm tra branch, working tree và commit gần nhất.
3. Đọc đúng task card, contract và ADR được card tham chiếu.
4. Tìm kiếm có phạm vi để tìm symbol/file liên quan; không nạp cả repo hoặc thư mục tham khảo.
5. Xác nhận dependency bằng code/test/commit, không chỉ tin dòng trạng thái.
6. Kiểm tra câu hỏi mở liên quan; nếu có mâu thuẫn ảnh hưởng contract/nghiệp vụ, xử lý theo mục 11.

Ngân sách đọc ban đầu mục tiêu khoảng **4.000–6.000 token tài liệu**, chưa tính source cần thiết. START_HERE khoảng một trang; handoff mới nhất tối đa khoảng 100 dòng. Đây là quy tắc tiết kiệm context, không phải lý do bỏ qua file cần kiểm tra.

### 10.3. Cuối phiên

- Chạy kiểm tra phù hợp; phân biệt pass, fail và chưa chạy.
- Cập nhật task card, ma trận nghiệm thu, contract/ADR nếu thay đổi.
- Ghi handoff: Branch, commit nền, file đã đổi, việc xong/còn lại, test, lỗi hiện có, câu hỏi còn chờ, bước tiếp theo và tập file tối thiểu cần đọc.
- Commit phần hoàn tất và đã kiểm tra; nếu đang dở, ghi working tree cụ thể.
- Giữ handoff lịch sử để tra cứu nhưng không tự nạp tất cả ở phiên mới.
- Jira chỉ chứa tóm tắt và liên kết, không sao chép toàn bộ tài liệu kỹ thuật.
- Khi task có quá nhiều hướng điều tra hoặc đổi contract lớn, dừng ở điểm an toàn, tách task con và bàn giao. Không tiếp tục dựa vào trí nhớ hội thoại khi thiếu căn cứ.

### 10.4. Mẫu yêu cầu cho phiên mới

> Đọc AGENTS.md, docs/START_HERE.md và handoff mới nhất. Thực hiện task C02 theo task card. Chỉ đọc các tài liệu và source liên quan; kiểm tra dependency trước khi sửa. Nếu có thông tin nghiệp vụ hoặc UI eBay quan trọng chưa tự xác minh được, báo rõ để tôi cung cấp; tiếp tục phần độc lập trong lúc chờ. Chạy kiểm thử phù hợp, cập nhật trạng thái và handoff, tự commit phần hoàn tất. Không triển khai task kế tiếp ngoài phạm vi đã yêu cầu.

## 11. Xử lý thông tin chưa rõ và phối hợp với người dùng

**Bổ sung được người dùng yêu cầu:** Nếu có phần chưa rõ, ví dụ luồng nghiệp vụ hoạt động thực tế hoặc UI frontend eBay mà Codex không tự lấy/xác minh được, Codex phải báo để người dùng cung cấp.

### 11.1. Trình tự xử lý

1. Kiểm tra requirement, quyết định đã chốt, contract và code liên quan trước.
2. Khi cần hành vi eBay thực tế, tra tài liệu chính thức hoặc quan sát trang truy cập được bằng công cụ phù hợp. Không coi code kỳ trước là bằng chứng eBay đang hoạt động như vậy.
3. Nếu vẫn thiếu thông tin quan trọng, báo cụ thể phần thiếu và đề nghị đúng loại dữ liệu cần bổ sung.
4. Tiếp tục các công việc độc lập; giữ phần phụ thuộc ở trạng thái chờ làm rõ. Không âm thầm bịa UI hoặc nghiệp vụ rồi coi là đã xác minh.
5. Khi người dùng trả lời, ghi vào contract/ADR hoặc tài liệu tham chiếu của module, liên kết task liên quan và đóng câu hỏi.

### 11.2. Các tình huống cần báo

- Trang eBay cần tài khoản, giao dịch thật, vai trò seller hoặc không truy cập được bằng công cụ hiện có.
- Không quan sát được trạng thái UI như đơn chưa nhận, dispute đang mở, phản hồi seller hoặc kết quả admin.
- Tài liệu yêu cầu và hành vi eBay có khác biệt ảnh hưởng điều kiện chấm điểm, phân quyền, database hoặc kết quả nghiệp vụ.
- Thiếu thiết kế mobile, nội dung modal, validation hoặc thứ tự thao tác cần thiết để tái tạo màn hình đúng.
- Thông tin giảng viên, deadline, GitHub remote hoặc ánh xạ tài khoản thành viên không có trong tài liệu.

### 11.3. Nội dung một yêu cầu làm rõ

- **Task/màn hình:** Phần nào đang cần thông tin.
- **Đã kiểm tra:** Nguồn đã đọc/quan sát và kết quả.
- **Chưa rõ:** Câu hỏi cụ thể, tránh hỏi chung chung.
- **Người dùng có thể cung cấp:** Link, ảnh chụp toàn màn hình/trạng thái liên quan, video thao tác ngắn hoặc mô tả từng bước với kết quả mong đợi.
- **Ảnh hưởng:** Contract/nghiệp vụ/UI nào phụ thuộc; phần nào vẫn làm tiếp được.
- **Đề xuất tạm thời, nếu có:** Nêu rõ là giả định cần xác nhận, không phải hành vi eBay đã kiểm chứng.

Ví dụ: “Task E02 cần bố cục form mở khiếu nại sau khi chọn đơn. Tôi đã đọc eBay Help nhưng không vào được màn hình cần tài khoản có đơn hàng. Bạn có thể gửi ảnh bước chọn lý do và bước nhập nội dung, hoặc video ngắn? Trong lúc chờ tôi tiếp tục service lưu nháp; phần bố cục form chưa đánh dấu hoàn tất.”

### 11.4. Lưu lại để không hỏi lặp

- Mỗi câu hỏi có ID như `Q-UI-001`, module, task, nguồn đã kiểm tra, tác động và trạng thái `Open/Answered/Deferred`.
- Câu trả lời kèm ngày, nguồn và đường dẫn ảnh/video nếu có; không để chỉ tồn tại trong hội thoại.
- Task card chỉ dẫn tới câu hỏi liên quan; không nạp toàn bộ nhật ký câu hỏi.
- Thiếu thông tin ảnh hưởng bảo mật, schema, contract hoặc nghiệp vụ chốt thì phải chờ làm rõ trước khi triển khai phần phụ thuộc.
- Chi tiết thẩm mỹ nhỏ, có thể sửa dễ dàng, được dùng mặc định nhất quán với thiết kế đã chốt và ghi nhận; không hỏi lại từng quyết định nhỏ.
- Hết thời gian chờ không được coi là người dùng đã xác nhận một giả định.

## 12. Bắt đầu triển khai

Bước đầu là **A00–A01**:

1. Tạo repo mới, đưa bản kế hoạch này vào quản lý phiên bản.
2. Tạo AGENTS.md, START_HERE, task index và quy tắc bàn giao; đưa mục 11 vào quy định bắt buộc cho agent.
3. Chuẩn hóa requirement và ma trận nghiệm thu, ghi rõ chức năng đầy đủ, chức năng phụ thuộc và ngoài phạm vi.
4. Chuẩn bị task cards theo kế hoạch; chỉ nạp card đang làm trong từng phiên.
5. Sau đó mới scaffold, triển khai nghiệp vụ và đồng bộ backlog báo cáo lên Jira.

Việc tạo file kế hoạch này không đồng nghĩa A00/A01 đã hoàn thành, không đồng nghĩa đã khởi tạo Git, tạo issue Jira hoặc triển khai ứng dụng.
