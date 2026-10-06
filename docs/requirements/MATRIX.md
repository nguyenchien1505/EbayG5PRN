# Ma trận yêu cầu và nghiệm thu

Status `Pending` nghĩa là chưa triển khai/chưa nghiệm thu. Task tài liệu Done không làm chức năng tương ứng Done. Bằng chứng ghi ở `docs/evidence/INDEX.md` theo requirement ID và commit.

## Nhóm 5 — Yêu cầu chức năng gốc

| ID | Yêu cầu chuẩn hóa | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| G5-01 | Giao diện chat buyer–seller qua WebSocket | C01–C04 | Hai tài khoản trao đổi realtime; user thứ ba không truy cập | Done |
| G5-02 | Lưu lịch sử chat theo thời gian/người gửi | C01–C02, C05 | Restart/reconnect đọc lại đúng thứ tự, có phân trang | Done |
| G5-03 | Rating và comment sau mỗi đơn đã nhận | D01–D02 | Chỉ owner đơn Delivered, một review mỗi OrderItem; mua lại được review | Pending |
| G5-04 | Trung bình đánh giá trên sản phẩm | D02, D05 | Trung bình/số lượt đúng, đổi theo kiểm duyệt, không chia cho zero | Pending |
| G5-05 | Điểm uy tín seller từ feedback tích cực | D03–D04 | Sentiment riêng, công thức đã chốt, seller reply đúng quyền | Pending |
| G5-06 | Gửi khiếu nại có nội dung và lý do | E01–E02 | Đúng dòng hàng/owner; không tạo trùng khi gửi lại | Pending |
| G5-07 | Buyer quản lý đã gửi, seller quản lý đã nhận | E03 | Danh sách/chi tiết/timeline đúng quyền và phân trang | Pending |
| G5-08 | Phản hồi tự động kết nối hệ thống admin | E04–E05 | Tiếp nhận, hướng dẫn, chuyển quá hạn, admin phân xử có audit | Pending |

## Nhóm 5 — Yêu cầu phi chức năng gốc

| ID | Yêu cầu chuẩn hóa | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| G5-N01 | Bảo mật tin nhắn, WebSocket qua SSL, mã hóa khi cần | B02, C01–C03, F01, F04 | HTTPS/WSS, kiểm tra membership; không log nội dung/secret; không tuyên bố có E2EE | Pending |
| G5-N02 | Chat mượt và realtime | C04–C05, F05 | UI không treo; có pending/error/reconnect; đo latency theo PERF-01 | Pending |
| G5-N03 | Khiếu nại không mất dữ liệu khi reload | E02 | Nháp server và khôi phục; gửi thành công mới xóa nháp | Pending |
| G5-N04 | Mở rộng lưu trữ lịch sử, phản hồi nhanh với dữ liệu lớn | C01–C05, F02, F05 | Cursor/index, nhiều instance, đo dataset lớn | Pending |
| G5-N05 | Debug tin nhắn theo thời gian/log ID | C05, F03 | Message ID/UTC/trace ID liên kết được log và DB không lộ nội dung | Pending |

## Yêu cầu khác — Nhóm 25% trong đề

| ID | Yêu cầu chuẩn hóa | Task | Minh chứng nghiệm thu | Status |
|---|---|---|---|---|
| OPS-01 | Tăng user: load balancing, Nginx | F01–F02, F05 | Nginx phân tải ít nhất hai app instance; chat xuyên instance | Pending |
| OPS-02 | Rate limiting theo khoảng thời gian | F02, F04 | HTTP trả 429 theo policy; Hub giới hạn invocation; counter dùng chung | Pending |
| OPS-03 | CI và CD bằng GitHub Actions | A07, F07 | Log CI thật, image SHA, deploy local qua runner tin cậy, smoke test | Pending |
| OPS-04 | Quản lý nhóm trên Jira | A01, G02 | Backlog KAN, vai trò, task/code mapping, trạng thái có bằng chứng | Pending |
| OPS-05 | Kiểm thử tải và an ninh, JMeter | F04–F05 | Script chạy lại được; báo cáo tải, bảo mật, giới hạn thực tế | Pending |
| OPS-06 | Zero downtime khi cập nhật bằng K8s | F06–F08 | Rolling update/rollback dưới tải; HTTP không 5xx, chat phục hồi | Pending |

## Luồng phụ thuộc được người dùng duyệt

| ID | Phạm vi | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| SUP-01 | Buyer đăng ký/đăng nhập/đăng xuất; seller/admin seed | A06, B01–B02 | Password hash, cookie, CSRF, phân quyền, lỗi an toàn | Done |
| SUP-02 | Catalog/detail/search/filter/pagination | B03–B04 | UI tiếng Anh, seller/contact link, dữ liệu seed | Done |
| SUP-03 | Buy Now/address/COD/inventory | B05 | Tiền server, transaction nhất quán, không bán quá tồn kho | Done |
| SUP-04 | Lịch sử mua/bán, giao và nhận hàng | B06 | Owner/seller đúng quyền, trạng thái hợp lệ tới Delivered | Done |
| SUP-05 | Admin dispute và kiểm duyệt review/feedback | B02, D05, E05 | Chặn user thường; mạng demo cho phép; audit và không xóa vật lý | Pending |

## Ràng buộc và quyết định của dự án

| ID | Quy tắc | Task | Nghiệm thu | Status |
|---|---|---|---|---|
| CON-01 | Chỉ mở rộng schema gốc, giữ 17 bảng và constraint | A05, A07, mọi task DB | Checksum nguồn + metadata baseline sau migration DB rỗng/có dữ liệu; EV-A05-01, EV-A06-01, EV-A07-01 | Verified A07 |
| CON-02 | .NET 8 MVC/Razor, SQL Server, viết mới | A03–A05 | Build đúng framework; SQL/EF/DbUp chạy thật; EV-A05-01 | Done |
| CON-03 | Git riêng, commit theo checkpoint | A00 và xuyên suốt | Git local, ignore, commit thật; không secret/giả tác giả | Established |
| CON-04 | Task nhỏ, đọc theo nhu cầu, bàn giao | A00–A01 và xuyên suốt | AGENTS, START_HERE, task cards, handoff, kiểm tra docs | Established |
| CON-05 | Báo thông tin UI/nghiệp vụ không tự lấy được | A02 và xuyên suốt | Câu hỏi cụ thể, nguồn đã kiểm tra, câu trả lời lưu bền vững | Established |
| UI-01 | Bám hành trình eBay thực tế, UI tiếng Anh responsive | A02, B03, C04, D02–D04, E02–E03, G01 | Có nguồn/ảnh tham chiếu truy cập được, khác biệt ghi rõ, desktop/mobile | Pending |
| PERF-01 | Ngưỡng tải/latency nội bộ | F05, F08 | Theo mục tiêu bên dưới, ghi cấu hình máy và dữ liệu đo | Pending |
| DOC-01 | Báo cáo, demo và bàn giao | G01–G02 | Mỗi yêu cầu có bằng chứng; setup từ clone sạch; mỗi thành viên giải thích module | Pending |

`Established` chỉ áp dụng quy tắc làm việc đã được thiết lập, không khẳng định đã tuân thủ trong các task tương lai. CON-01 Verified A07: [metadata/upgrade và seed tests trên GitHub CI](../evidence/A07.md) đã pass cho baseline hiện tại; bắt buộc chạy lại với mỗi migration, không suy ra tương lai đã pass.

## Mục tiêu PERF-01 và giới hạn

- Dataset riêng: 100.000 message, 10.000 review, 1.000 dispute.
- HTTP JMeter: 50 user/10 phút, p95 API chính < 1s, lỗi ngoài dự kiến < 1%.
- Client SignalR: 20 cặp, p95 gửi→nhận < 1s trên local.
- Rolling update: 0 HTTP 5xx trong bài đo; chat reconnect ≤ 10s, đủ tin đã ACK.
- Stress test tăng dần để xác định điểm nghẽn; không tự đổi ngưỡng nếu fail.
- Zero downtime chỉ kiểm tra cập nhật web khi SQL/Redis hoạt động bình thường; không tuyên bố cluster local hoặc DB đơn instance là HA hoàn chỉnh.

## Ngoài phạm vi chức năng nhưng giữ schema

Không full auction, coupon, cart, seller product CRUD, quản trị toàn sàn, payment/shipping thật, dashboard doanh thu, email marketing. Không xóa bảng tương ứng. Notification trong app và phản hồi tự động phục vụ nhóm 5; email thật không phải điều kiện demo đã chọn.

## Đối chiếu nguồn

G5/G5-N/OPS xuất phát từ [nguồn giảng viên](sources/requirements-extracted.md); SUP/CON/PERF là lựa chọn người dùng và quy tắc trong [kế hoạch](../PLAN.md). UI-01 thực hiện lưu ý trải nghiệm hệ thống thực tế của đề. Không coi mọi quy tắc giản lược trong kế hoạch là chính sách eBay thật.

Ghi nhận A02: [EBAY_JOURNEYS](EBAY_JOURNEYS.md) và [UI_REFERENCE](../architecture/UI_REFERENCE.md) là bằng chứng khảo sát một phần. UI-01 vẫn Pending, Q-UI-001–003 chưa có câu trả lời.

SUP-01: evidence [B01](../evidence/B01.md) và [B02](../evidence/B02.md); quyền trên order/chat/dispute kiểm chứng ở module tương ứng, SUP-05 còn Pending.

SUP-02: [B04 evidence](../evidence/B04.md), catalog/detail MVC/API trên seed; Contact seller là điểm vào, gửi tin nhắn kiểm chứng ở C04.

SUP-03: [B05 evidence](../evidence/B05.md), 51 SQL/HTTP tests pass; principal Buyer, owned address, server total, atomic COD/stock và replay/concurrency/rollback. B06/SUP-04 kiểm chứng tại evidence B06.

SUP-04: [B06 evidence](../evidence/B06.md): snapshot owner Buyer/Seller history, transactional Ship→Receive→Delivered/COD Paid; 70 SQL/HTTP tests và browser desktop/mobile. Không real payment/delivery. UI-01 xuyên suốt vẫn Pending.

C01: [schema/contract evidence](../evidence/C01.md), 91 SQL/HTTP/schema tests pass; two-slot membership/FK/index/sequence/wire và legacy preservation. G5-01/02, G5-N01/02/04/05 vẫn Pending vì service/realtime/UI/reconnect/TLS/dataset lớn chưa kiểm chứng; task schema Done không làm requirement chat Done.

C02: [service/REST evidence](../evidence/C02.md),116 tests pass (25 new+91 regression), SQL principal/membership/context/retry/rollback/paging/read và HTTP401/403/404/CSRF/no-store/body. G5-01/02 và G5-N01/02/04/05 vẫn Pending: service đã có, realtime UI/transport/restart reconnect/TLS/multi-instance/dataset lớn và operational trace còn C03–C05/F.

C03: [WS/Redis evidence](../evidence/C03.md),134 tests (18 new+116 regression), hai Kestrel apps/DI roots/cổng độc lập trong một process, real WebSocket+Redis cross-host send/read/REST publish. Origin/cookie/expiry/object/stale recipient/rotation/payload/replay guards, Redis unavailable durable SQL verified. G5-01/02 và G5-N01/02/04/05 vẫn Pending: UI/reconnect/restart/TLS/persistent shared keys/deployed replicas/perf/trace còn C04–C05/F; không suy ra end-to-end acceptance từ transport tests.

C04: [UI evidence](../evidence/C04.md),137 .NET +8 Node tests, actual two-account loopback WS browser exchange/read/unread/error/retained UUID retry/paging and third-party404. **G5-01 Done**. G5-02/N01/N02/N04/N05 vẫn Pending C05/F cho restart/reconnect/TLS/trace/perf/scale; UI-01 toàn dự án chưa Done. Mobile responsive adaptation390/320 verified, không claim ảnh eBay mobile.

C05: [recovery evidence](../evidence/C05.md),140 .NET +14 Node, fresh Kestrel host restart/rejoin/paged SQL history/read cursors/idempotent pending/metadata trace, actual browser restart1050-message catch-up/no Refresh/lost event/lost ack one row. **G5-02 Done**. G5-N01/N02/N04/N05 Pending F01–F05 cho deployed TLS/keys/scale/latency/dataset/log collection; UI-01 project-wide Pending. Không durable browser draft/outbox claim.
