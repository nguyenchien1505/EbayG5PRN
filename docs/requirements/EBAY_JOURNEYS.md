# Hành trình eBay và đối chiếu bản clone

Khảo sát 02/10/2026, eBay.com tiếng Anh. Nguồn chính thức xác minh mô tả nghiệp vụ; không đồng nghĩa đã thao tác trên tài khoản có giao dịch. UI public đã quan sát một phần; màn hình nghiệp vụ chưa xác minh trực quan. Không có giao dịch, tin nhắn hoặc tài khoản thật được tạo.

## Trạng thái bằng chứng

- **DOC:** Đã đọc nội dung eBay Help chính thức tại các link dưới.
- **UI-PARTIAL:** Đã quan sát trực tiếp phần UI được nêu, không suy rộng tới các trang khác.
- **UNVERIFIED:** Chưa quan sát màn hình/trạng thái thật.
- **CLONE:** Quyết định của dự án đã duyệt; có thể khác eBay và không được trình bày như chính sách thật.

## Chat

| Hành trình | Bằng chứng và giới hạn | Bản clone / task |
|---|---|---|
| Trước mua: trang listing → Contact seller | DOC: Có seller không nhận câu hỏi trước mua; có luồng chọn chủ đề/FAQ trước form. [Contacting a seller](https://www.ebay.com/help/buying/working-sellers/contact-seller?id=4021) | CLONE: Buyer đã đăng nhập mở chat trực tiếp với seller từ product detail; chưa có opt-out/FAQ. B04, C01–C04 |
| Sau mua: Purchase history → Contact seller | DOC: Liên hệ seller từ item đã mua. Cùng nguồn trên | CLONE: Nối từ lịch sử đơn tới cùng hội thoại buyer–seller. B06, C04 |
| My eBay → Messages → conversation | DOC: eBay mô tả gom tin theo buyer/seller thành thread. [Messages](https://www.ebay.com/help/account/messages/messages?id=4194). UI-PARTIAL: Thấy mục Messages trong menu; UNVERIFIED: inbox bị chặn | CLONE: Hai thành viên, lịch sử SQL, SignalR, cursor/retry/ACK. Không suy từ Help rằng eBay dùng SignalR hoặc cùng cơ chế ACK. C01–C05 |

Không thêm ảnh đính kèm, offers, custom folders, email copy hoặc eBay.ai trong v1 chỉ vì trang Messages có mô tả. Phản hồi tự động của dispute trong đề không đồng nghĩa tính năng Time Away/AI của Messages.

## Review và feedback

DOC: Từ purchase history, người mua có thể để feedback nếu chưa gửi; seller feedback có sentiment, và item đủ điều kiện có phần sao. Feedback profile hiển thị kết quả. Nguồn còn mô tả giới hạn thời gian/tuổi tài khoản, feedback tự động và cách tính điểm có ngoại lệ. [Leaving feedback](https://www.ebay.com/help/buying/feedback/feedback?id=4007).

| Bước | Bản clone đã chốt | Task / bằng chứng còn thiếu |
|---|---|---|
| Chọn dòng hàng đã nhận | Owner + Delivered, mỗi OrderItem một review; lần mua khác được đánh giá lại | B06, D01; UI form/order actions UNVERIFIED, Q-UI-002 |
| Chấm sao + viết nhận xét sản phẩm | Review 1–5 tách dữ liệu seller feedback; không coi đây là hai trang riêng thật trên eBay | D01–D02; DOC xác minh ý nghĩa, layout chưa xác minh |
| Gửi seller feedback | Positive/Neutral/Negative và comment, gắn OrderItem | D03; công thức Positive−Negative và tỷ lệ đã chốt là CLONE |
| Xem uy tín và phản hồi | Hiển thị summary/filter/comment; seller reply đúng quyền; admin có thể ẩn với audit | D04–D05; giao diện profile và moderation chưa quan sát |

CLONE không có feedback tự động, cửa sổ 60/90 ngày, tuổi tài khoản tối thiểu hay điều chỉnh điểm theo tuần. Không dùng các quy tắc chưa thuộc scope làm điều kiện mới. Chưa có bằng chứng rằng màn hình review và seller feedback phải tách thành hai page; task UI phải tham chiếu Q-UI-002 trước khi chốt layout.

## Khiếu nại

| Hành trình thật theo Help | Bản clone / giới hạn |
|---|---|
| Chưa nhận hàng: Purchases → More actions → I didn't receive it → chọn mong muốn/nhập chi tiết → gửi. Chỉ báo sau ngày dự kiến giao. [Item not received](https://www.ebay.com/help/returns-refunds/item-not-delivered/item-not-received?id=4042) | CLONE gom thành reason trong Dispute; không buộc Delivered. E01 phải chốt điều kiện thời gian và reason theo kế hoạch, không lén áp toàn bộ chính sách eBay |
| Hàng sai/hỏng: mở return từ Purchases; seller có thời gian phản hồi. [Return an item](https://www.ebay.com/help/buying/returns-refunds/return-item-refund?id=4041) | CLONE vẫn dùng Dispute theo dòng hàng và timeline; chưa triển khai nhãn trả hàng/vận chuyển ngược/refund thật. Giữ bảng ReturnRequest nhưng không giả vờ đã có full return workflow |
| Chưa giải quyết: request/return details → Ask eBay to step in → lý do → Confirm. Help quy định sau hơn ba ngày làm việc. [Ask eBay to step in](https://www.ebay.com/help/buying/resolving-problems-seller/resolve-problem-item-not-received?id=4701) | CLONE có chuyển admin tự động khi quá hạn và cấu hình demo hai phút theo kế hoạch. Đó là lựa chọn demo, không phải bằng chứng eBay tự chuyển đúng cách này |

Phần giao diện form, timeline buyer/seller và nút escalation đều UNVERIFIED (Q-UI-003). Help chỉ mô tả kết quả xử lý; không cung cấp giao diện nội bộ admin. Admin dashboard sẽ là UI riêng của bài, không yêu cầu người dùng tìm quyền admin eBay.

## Các quy tắc đã có và điều cần chốt ở task thiết kế

- Giữ state machine tổng thể trong PLAN; E01 phải định nghĩa tên trạng thái đóng bởi buyer, actor/transition, eligibility từng reason và thời điểm job xử lý. A02 không tự đổi schema/state.
- Không biến mốc ba ngày làm việc thành ba ngày lịch; không kế thừa vòng quét 24 giờ và status số của code cũ.
- C01 chốt membership và idempotency trước code; D01 chốt điều kiện review từ đơn; E01 chốt constraint một case mở mỗi dòng hàng.
- Các khác biệt CLONE ở trên là phạm vi đã chốt hoặc phần ngoài scope, không phải đề xuất tự mở rộng thêm tính năng.

## Theo dõi và tài liệu liên quan

[Câu hỏi UI](../questions/UI-REFERENCES.md) chặn việc tuyên bố tái tạo chính xác màn hình private, không chặn A03–A07 hoặc backend tuân theo yêu cầu đã duyệt. [UI_REFERENCE](../architecture/UI_REFERENCE.md) ghi màn hình, desktop/mobile và mức quan sát. UI-01 và các requirement chức năng vẫn Pending đến khi triển khai/kiểm thử.
