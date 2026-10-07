# Ma trận yêu cầu và nghiệm thu

Status `Pending` nghĩa là chưa triển khai/chưa nghiệm thu. Task tài liệu Done không làm chức năng tương ứng Done. Bằng chứng ghi ở `docs/evidence/INDEX.md` theo requirement ID và commit.

## Nhóm 5 — Yêu cầu chức năng gốc

| ID | Yêu cầu chuẩn hóa | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| G5-01 | Giao diện chat buyer–seller qua WebSocket | C01–C04 | Hai tài khoản trao đổi realtime; user thứ ba không truy cập | Done |
| G5-02 | Lưu lịch sử chat theo thời gian/người gửi | C01–C02, C05 | Restart/reconnect đọc lại đúng thứ tự, có phân trang | Done |
| G5-03 | Rating và comment sau mỗi đơn đã nhận | D01–D02 | Chỉ owner đơn Delivered, một review mỗi OrderItem; mua lại được review | Done |
| G5-04 | Trung bình đánh giá trên sản phẩm | D02, D05 | Trung bình/số lượt đúng, đổi theo kiểm duyệt, không chia cho zero | Done |
| G5-05 | Điểm uy tín seller từ feedback tích cực | D03–D04 | Sentiment riêng, công thức đã chốt, seller reply đúng quyền | Done |
| G5-06 | Gửi khiếu nại có nội dung và lý do | E01–E02 | Đúng dòng hàng/owner; không tạo trùng khi gửi lại | Done |
| G5-07 | Buyer quản lý đã gửi, seller quản lý đã nhận | E03 | Danh sách/chi tiết/timeline đúng quyền và phân trang | Done |
| G5-08 | Phản hồi tự động kết nối hệ thống admin | E04–E05 | Tiếp nhận, hướng dẫn, chuyển quá hạn, admin phân xử có audit | Done |

## Nhóm 5 — Yêu cầu phi chức năng gốc

| ID | Yêu cầu chuẩn hóa | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| G5-N01 | Bảo mật tin nhắn, WebSocket qua SSL, mã hóa khi cần | B02, C01–C03, F01, F04 | HTTPS/WSS, kiểm tra membership; không log nội dung/secret; không tuyên bố có E2EE | Done |
| G5-N02 | Chat mượt và realtime | C04–C05, F05 | UI không treo; có pending/error/reconnect; đo latency theo PERF-01 | Done |
| G5-N03 | Khiếu nại không mất dữ liệu khi reload | E02 | Nháp server và khôi phục; gửi thành công mới xóa nháp | Done |
| G5-N04 | Mở rộng lưu trữ lịch sử, phản hồi nhanh với dữ liệu lớn | C01–C05, F02, F05 | Cursor/index, nhiều instance, đo dataset lớn | Done |
| G5-N05 | Debug tin nhắn theo thời gian/log ID | C05, F03 | Message ID/UTC/trace ID liên kết được log và DB không lộ nội dung | Done |

## Yêu cầu khác — Nhóm 25% trong đề

| ID | Yêu cầu chuẩn hóa | Task | Minh chứng nghiệm thu | Status |
|---|---|---|---|---|
| OPS-01 | Tăng user: load balancing, Nginx | F01–F02, F05 | Nginx phân tải ít nhất hai app instance; chat xuyên instance | Done |
| OPS-02 | Rate limiting theo khoảng thời gian | F02, F04 | HTTP trả 429 theo policy; Hub giới hạn invocation; counter dùng chung | Done |
| OPS-03 | CI và CD bằng GitHub Actions | A07, F07 | Log CI thật, image SHA, deploy local qua runner tin cậy, smoke test | Done |
| OPS-04 | Quản lý nhóm trên Jira | A01, G02 | Backlog KAN, vai trò, task/code mapping, trạng thái có bằng chứng | Pending |
| OPS-05 | Kiểm thử tải và an ninh, JMeter | F04–F05 | Script chạy lại được; báo cáo tải, bảo mật, giới hạn thực tế | Done |
| OPS-06 | Zero downtime khi cập nhật bằng K8s | F06–F08 | Rolling update/rollback dưới tải; HTTP không 5xx, chat phục hồi | Done |

## Luồng phụ thuộc được người dùng duyệt

| ID | Phạm vi | Task | Nghiệm thu tối thiểu | Status |
|---|---|---|---|---|
| SUP-01 | Buyer đăng ký/đăng nhập/đăng xuất; seller/admin seed | A06, B01–B02 | Password hash, cookie, CSRF, phân quyền, lỗi an toàn | Done |
| SUP-02 | Catalog/detail/search/filter/pagination | B03–B04 | UI tiếng Anh, seller/contact link, dữ liệu seed | Done |
| SUP-03 | Buy Now/address/COD/inventory | B05 | Tiền server, transaction nhất quán, không bán quá tồn kho | Done |
| SUP-04 | Lịch sử mua/bán, giao và nhận hàng | B06 | Owner/seller đúng quyền, trạng thái hợp lệ tới Delivered | Done |
| SUP-05 | Admin dispute và kiểm duyệt review/feedback | B02, D05, E05 | Chặn user thường; mạng demo cho phép; audit và không xóa vật lý | Done |

## Ràng buộc và quyết định của dự án

| ID | Quy tắc | Task | Nghiệm thu | Status |
|---|---|---|---|---|
| CON-01 | Chỉ mở rộng schema gốc, giữ 17 bảng và constraint | A05, A07, mọi task DB | Checksum nguồn + metadata baseline sau migration DB rỗng/có dữ liệu; EV-A05-01, EV-A06-01, EV-A07-01 | Verified A07 |
| CON-02 | .NET 8 MVC/Razor, SQL Server, viết mới | A03–A05 | Build đúng framework; SQL/EF/DbUp chạy thật; EV-A05-01 | Done |
| CON-03 | Git riêng, commit theo checkpoint | A00 và xuyên suốt | Git local, ignore, commit thật; không secret/giả tác giả | Established |
| CON-04 | Task nhỏ, đọc theo nhu cầu, bàn giao | A00–A01 và xuyên suốt | AGENTS, START_HERE, task cards, handoff, kiểm tra docs | Established |
| CON-05 | Báo thông tin UI/nghiệp vụ không tự lấy được | A02 và xuyên suốt | Câu hỏi cụ thể, nguồn đã kiểm tra, câu trả lời lưu bền vững | Established |
| UI-01 | Bám hành trình eBay thực tế, UI tiếng Anh responsive | A02, B03, C04, D02–D04, E02–E03, G01 | Có nguồn/ảnh tham chiếu truy cập được, khác biệt ghi rõ, desktop/mobile | Pending |
| PERF-01 | Ngưỡng tải/latency nội bộ | F05, F08 | Theo mục tiêu bên dưới, ghi cấu hình máy và dữ liệu đo | Done |
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

D01: [review-order link evidence](../evidence/D01.md), 157 .NET +14 Node tests (17 new), Buyer-owner/Delivered-only/one-per-OrderItem/repurchase/concurrency/rollback/SQL constraints/legacy-preserving migration/HTTP CSRF-role-overposting verified; mutation check caught. **G5-03 vẫn Pending**: chưa có UI gửi/hiển thị review (D02). G5-04 Pending D02/D05; SUP-05 Pending D05/E05. Không claim browser acceptance.

D02: [review UI evidence](../evidence/D02.md), 161 .NET +14 Node, form R1/Orders eligibility/public summary-distribution-list/paging/sort/encode/legacy exclusion và browser desktop+320px. **G5-03 Done** (D01 rule + D02 UI). G5-04 vẫn Pending: trung bình/số lượt/không chia 0 đã kiểm chứng, phần "đổi theo kiểm duyệt" chờ D05. G5-05 Pending D03–D04; SUP-05 Pending D05/E05; UI-01 project-wide Pending.

D03: [seller feedback evidence](../evidence/D03.md), 177 .NET +14 Node; sentiment tách review, công thức Score=P−N / P/(P+N) unit-tested, 60 ngày/chờ 7 ngày, gửi gộp atomic, seller snapshot, constraint/permission/migration, HTTP và browser. **G5-05 vẫn Pending**: trang uy tín và seller reply đúng quyền thuộc D04. G5-04 Pending D05; SUP-05 Pending D05/E05.

D04: [reputation page/reply evidence](../evidence/D04.md), 183 .NET +14 Node; điểm P−N tích luỹ và % 12 tháng (Q-BIZ-004 thay quy tắc "toàn bộ" của PLAN cho tỷ lệ), cửa sổ 1/6/12 tháng, hồ sơ F1 public che tên buyer, reply chỉ seller nhận feedback, một lần, bất biến ở service+trigger+grant; HTTP và browser. **G5-05 Done** (D03 sentiment/công thức + D04 trang/reply). G5-04 Pending D05; SUP-05 Pending D05/E05; UI-01 project-wide Pending.

D05: [moderation evidence](../evidence/D05.md), 190 .NET +14 Node; Report/queue/Hide/RemoveComment/Restore/Dismiss, audit append-only, không xóa vật lý (trigger), tổng hợp review/uy tín đổi theo kiểm duyệt; Admin role + AdminNetwork, user thường 403. **G5-04 Done** (D02 tổng hợp + D05 kiểm duyệt). SUP-05 vẫn Pending: phần review/feedback đạt, phần admin dispute thuộc E05. UI-01 project-wide Pending.

D06: [entry point evidence](../evidence/D06.md), 203 .NET +14 Node; điểm vào nhóm 5 theo eBay (ADR-002): nút Message trong seller card, sao/% positive trên card catalog (batch, cùng quy tắc với trang chi tiết), Leave feedback + Ready for feedback và More actions → Contact seller trên Purchases, thu cột thư mục Messages. Không đổi luật nghiệp vụ; G5-01/03/04/05 giữ Done; G5-06 Pending E01–E02. UI-01 project-wide Pending.

E01: [dispute schema/state machine evidence](../evidence/E01.md), 225 .NET +14 Node; DbUp 0011 (ngày giao dự kiến, Dispute mở rộng, DisputeEvent append-only), rules thuần kiểm 7.728 tổ hợp, service open/act/system/find, INR đóng khi Confirm received, constraint/trigger/grant/migration/mutation. **G5-06 vẫn Pending** (form E02), G5-07 Pending E03, G5-08 Pending E04–E05, G5-N03 Pending E02, SUP-05 Pending E05.

E02: [dispute form/draft evidence](../evidence/E02.md); DbUp 0012 nháp server, form INR/Return theo eBay, autosave + khôi phục khi reload + xóa khi gửi thành công, gửi lại không tạo trùng, đúng dòng hàng/owner. **G5-06 Done** (E01 rule + E02 form), **G5-N03 Done**. G5-07 Pending E03, G5-08 Pending E04–E05, SUP-05 Pending E05.

E03: [request list/details evidence](../evidence/E03.md), 234 .NET +20 Node; danh sách buyer đã gửi / seller đã nhận (lọc, phân trang 10), trang request D2/H1 với trả lời, Ask eBay to step in và Close có lý do, timeline mới nhất trước phân trang 20; quyền theo owner (người lạ 404, admin 403 chờ E05), CSRF, overposting, gửi lại không ghi thêm, encode; browser desktop/390/320px. **G5-07 Done**. G5-08 Pending E04–E05, SUP-05 Pending E05; UI-01 project-wide Pending.

E04: [automation evidence](../evidence/E04.md); DbUp 0013 khóa thông báo duy nhất, job trên mỗi replica chạy System Escalate/ExpireReturn và ghi thông báo tiếp nhận/hướng dẫn/quá hạn/eBay xem xét theo trạng thái hiện tại; chạy lại và 4 replica song song không nhân đôi; HTTP ghi ngay sau thao tác, worker thật escalate request quá hạn; browser. **G5-08 vẫn Pending**: tiếp nhận/hướng dẫn/chuyển quá hạn đã đạt, admin phân xử có audit thuộc E05. SUP-05 Pending E05.

E05: [admin decision evidence](../evidence/E05.md); hàng đợi admin theo hạn 48 giờ, trang review có audit trail đủ actor/thời điểm/kết quả, Decide buyer/seller kèm note và hoàn tiền mô phỏng (Payment không đổi), Message hai bên; Admin cần credential (như D05), Buyer/Seller 403, ngoài mạng demo 403, CSRF, hai admin song song chỉ một quyết định, request không xóa/không mở lại; buyer/seller thấy kết quả, không thấy tên admin; browser. **G5-08 Done** (E04 tự động + E05 phân xử), **SUP-05 Done** (D05 kiểm duyệt + E05 dispute). UI-01 project-wide Pending.

F01: [app image/Nginx HTTPS evidence](../evidence/F01.md); image web/migrate pin digest + locked restore, Nginx TLS/WSS/308, forwarded headers chỉ từ proxy đã khai báo (rỗng = tắt), readiness SQL+Redis, stack thật qua HTTPS 12/12 (TLS chain, cookie Secure, WSS handshake, Origin, XFF giả bị bỏ qua) chạy cả trong GitHub CI. **G5-N01 vẫn Pending** (HTTPS/WSS đạt; kiểm thử bảo mật F04), **OPS-01 vẫn Pending** (Nginx đạt; hai instance F02, tải F05).

F02: [scale-out evidence](../evidence/F02.md); 2 replica web sau Nginx, key ring DataProtection dùng chung trên Redis (đổi instance giữ login, đối chứng key ring khác thì mất), rate limit dùng chung trên Redis cho auth/chat REST/mọi invocation Hub (429 + Retry-After; Hub `429:` giữ kết nối), dự phòng theo instance khi Redis gián đoạn; stack thật 2 upstream, XFF giả 429 nhờ bộ đếm chung. **OPS-02 Done** (F04 sẽ thử vượt giới hạn). **OPS-01 vẫn Pending**: Nginx + 2 instance đạt, chat xuyên instance đạt ở test hai host C03; đo qua stack dưới tải thuộc F05. G5-N04 Pending F05.

F03: [system journeys evidence](../evidence/F03.md); Playwright trên stack thật (HTTPS, 2 replica): mua hàng, chat realtime hai chiều qua WSS, giao/nhận + feedback, return đến refund, report + admin ẩn, quyền truy cập; ma trận quyền 16 route × 6 vai trò. Hai lỗi thật đã sửa: redirect mất port, 403 body rỗng. **G5-N05 Done**: request id của Nginx = trace của app = `Message.TraceId`, không có nội dung chat trong log Nginx/web. G5-N01 Pending F04; **OPS-01 vẫn Pending**: CI lần này giữ cả hai WebSocket chat trên một replica, nên chat xuyên instance qua stack chưa được chứng minh (chỉ có test hai host C03); giao cho F05.

F04: [security evidence](../evidence/F04.md); IDOR 29 lượt (dữ liệu không đổi), CSRF đủ 18 POST, stored XSS 10 trường × 27 trang + trình duyệt dưới CSP thật (0 vi phạm), header CSP/DENY/nosniff, TLS 1.2/1.3 only, `ws://` bị từ chối, body > 1 MiB 413, secret scan toàn lịch sử + audit NuGet; mutation 5/5. **G5-N01 Done** (HTTPS/WSS + membership + CSRF + không log nội dung, không E2EE). OPS-05 Pending F05 (JMeter). Rủi ro còn lại: logout không thu hồi cookie (Q-SEC-001), key ring không mã hóa khi lưu (F06).

F05: [load evidence](../evidence/F05.md); JMeter 5.6.3 + client SignalR riêng trên stack thật (Nginx HTTPS, 2 replica), dataset 100.000 tin / 10.000 review / 1.000 dispute (hội thoại 20.000 tin). Full local: 50 user × 10 phút 0 % lỗi, p95 ≤ 39 ms (lịch sử chat 7 ms); 20 cặp chat p95 20,4 ms, 11.753/11.753 tin; WS trên 2 replica, 13/20 cặp khác replica nhận đủ. Stress: tới 800 user p95 < 100 ms, 1600 user p95 inbox 1,3 s (giới hạn thực tế). **OPS-01, OPS-05, G5-N02, G5-N04 Done.** PERF-01 vẫn Pending: phần tải/latency đạt, phần rolling update thuộc F08. Phát hiện deadlock ghi đơn đồng thời (Q-PERF-001) đã sửa: DbUp 0014 + bỏ khóa phạm vi bảng con, `OrderConcurrencyTests` 85 → 0 deadlock, seed 11.000 đơn 8 luồng 0 deadlock.

F06: [Kubernetes evidence](../evidence/F06.md); kind cluster local: web 2 replica + startup/liveness/readiness probes, SQL/Redis StatefulSet có PVC (dữ liệu và phiên đăng nhập giữ qua restart pod), migration Job (14 → 0 khi chạy lại), NetworkPolicy, journeys J1–J7 + smoke trên cluster. OPS-06 vẫn Pending (CD F07, rolling update/rollback có tải F08); OPS-03 Pending F07.

F07: [CD evidence](../evidence/F07.md); CI37668281294 và CD37669952216 success trên runtime `f0b65b0`:
GitHub-hosted build image full SHA, artifact checksum/digest mapping/revision/platform,
Windows runner ephemeral deploy vào kind, migration 0 mới, 2 web Ready, TLS CA/hostname + read-only smoke,
runner deregister/không service. **OPS-03 Done** (CI A07 + CD F07). OPS-06/PERF-01 vẫn Pending F08.

F08: [rolling evidence](../evidence/F08.md), Full local 50 HTTP user +20 cặp WSS ×600s,
dataset 100k tin/10k review/1k dispute: 152.130 request 0 error/5xx/network, 12.891 ACK đủ/một dòng SQL,
80 recovery max 2,51s, max route p95 241,48ms/chat p95 27,98ms. CI37678243173 attempt 2 success
trên ad0ce27, rolling Short 4.635 request/1.188 ACK, 0 thiếu/trùng, recovery max 1,38s.
Revision/image/pod UID xác nhận update và undo thật; 14 migration giữ nguyên.
**OPS-06 Done** (F06 K8s +F07 CD +F08 transition), **PERF-01 Done** (F05 Full/Stress +F08).
Giới hạn: single-node kind, image tương thích/cùng binary nghiệp vụ, không chứng minh DB/node failover.
