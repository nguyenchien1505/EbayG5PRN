# E03 — Trang request, timeline và danh sách buyer/seller

Contract do agent soạn ngày 06/10/2026 trên main `0230693`, trước khi sửa source. Dựa trên [DISPUTES](DISPUTES.md) (E01: luật, service), [DISPUTE_FORMS](DISPUTE_FORMS.md) (E02) và [ADR-002](../decisions/ADR-002-group5-fidelity.md). Requirement: **G5-07** (buyer quản lý request đã gửi, seller quản lý request đã nhận; danh sách/chi tiết/timeline đúng quyền và phân trang).

Không thuộc phạm vi E03:

- Job tự động: phản hồi tự động và Escalate/ExpireReturn khi quá hạn (E04). E03 chỉ hiển thị event `AutoNotice`/System nếu có.
- Giao diện admin phân xử và tin nhắn của admin (E05). Admin vào `/Disputes/*` nhận 403; service vẫn cho admin đọc như E01.
- Luật chuyển trạng thái: giữ nguyên `DisputeRules` của E01. Không thêm migration.

## Nguồn UI và nhãn

- **D2** (Community 09/2024, đã chốt), seller xử lý return:
  - Tiêu đề "You have a return request", câu tóm tắt và "Please respond by {date}…".
  - Khối "Info from the buyer": Reason, Comments.
  - "Select a reply" gồm các radio kèm mô tả → **Continue**.
  - Mô tả lấy theo D2: Accept the return ("Wait to get the item back before you refund the buyer."), Give a full refund ("You fully refund the buyer to close this request. The buyer keeps the item."), Offer a partial refund, Send the buyer a message ("You can reply to the buyer.").
- **H1** (Help 4042) và [Help 4041](https://www.ebay.com/help/buying/returns-refunds/return-item-refund?id=4041) (đọc ngày 06/10/2026):
  - Buyer: Purchases → More actions → **See request details**, rồi **Ask eBay to step in and help** → chọn lý do → **Confirm**.
  - Buyer đổi ý: See request details → **Close your request** kèm lý do.
- [Help 4702](https://www.ebay.com/help/selling/managing-returns-refunds/ask-ebay-step-help-sellers?id=4702) (đọc ngày 06/10/2026):
  - Seller Hub → Orders → tab **Returns** / **Requests and disputes** → **See details**.
  - Trên đơn đã bán, link đi tới request.

Không có ảnh thật của trang request details, nên phần sau là **thiết kế của clone**, cùng phong cách E1/D2:

- Bố cục trang.
- Câu chữ timeline.
- Nhãn trạng thái phía seller.
- Danh sách lý do khi Ask eBay to step in / Close your request.

**Lệch có chủ ý / giả định** — người dùng đã chấp nhận ngày 06/10/2026 (Q-UI-009): giữ nhãn clone tự đặt (sẽ chỉnh nếu người dùng xác nhận được eBay thật), tạm giữ luật offer của E01, đồng ý tên "Returns and requests".

- **Câu "Please respond by":** D2 ghi "…or we may approve it for you". Clone ghi "…or we may step in to help", vì quá hạn thì E04 chuyển eBay chứ không tự chấp nhận return (E01).
- **Offer a partial refund:** D2 ghi "You have one chance to offer an amount". Luật E01 cho seller offer lại sau khi buyer từ chối, nên clone dùng mô tả "Offer an amount. The buyer keeps the item." Đưa về đúng một lần thì phải sửa luật E01 bằng task riêng.
- **Tên trang danh sách:** eBay có "Returns and canceled orders" (buyer) và Seller Hub "Returns"/"Requests and disputes". Clone không có hủy đơn, nên đặt tên chung **"Returns and requests"**. Trang seller vẫn có hai tab Returns / Requests and disputes như Seller Hub.
- **Lý do Escalate và Close:** clone tự soạn, vì không tìm được danh sách chính thức (xem bảng dưới). Lý do không có cột riêng: nhãn lý do là dòng đầu của `Message` trong event, ghi chú của buyer (nếu có) nằm sau một dòng trống. Tổng độ dài vẫn ≤ 2000 theo luật E01.

| Lệnh | Mã lý do → nhãn |
|---|---|
| Escalate | `NoResponse` → "The seller hasn't responded" · `NoRefund` → "I haven't received a refund" · `Disagree` → "I don't agree with the seller's response" · `Other` → "Something else" |
| Close | `Resolved` → "My problem was resolved" · `ChangedMind` → "I changed my mind" · `Other` → "Something else" |

## Service (mở rộng `IDisputeService`, chỉ đọc)

| Phương thức | Hành vi |
|---|---|
| `ListAsync(actorId, DisputeListQuery(Kind?, State?, Page))` | Buyer: request có `raisedBy` = actor. Seller: request có `SellerId` = actor. Vai trò khác → `null`. Chỉ lấy dòng của module (`OrderItemId` NOT NULL). Sắp xếp `id DESC` (index `IX_Dispute_Buyer`/`IX_Dispute_Seller`). `PageSize` = 10. `State`: `Open` (chưa Closed) / `Closed` |
| `FindAsync(actorId, disputeId, timelinePage)` | Như E01, nhưng timeline được phân trang: mới nhất trước, `TimelinePageSize` = 20, kèm `EventTotal`/`EventPage`. Overload cũ của E01 trả trang 1 (trước đây là tối đa 200 event, cũ nhất trước). `Counterpart` = username của bên còn lại (buyer thấy seller, seller thấy buyer). Admin thấy cả hai, ở E05 |
| `RequestsForOrderAsync(actorId, orderId)` | Request của từng dòng hàng trong đơn mà actor là buyer hoặc seller (mới nhất trước); đơn không thuộc actor → rỗng. Dùng cho trang chi tiết đơn phía seller |

Một dòng danh sách (`DisputeListItem`) gồm: `Id`, `OrderId`, `OrderItemId`, `ItemTitle`, `Kind`, `Reason`, `Status`, `Outcome`, `CreatedAtUtc`, `DueAtUtc`, `LineTotal`, `OfferAmount`, `Counterpart`. Page/filter sai → 400 ở controller. Page vượt tổng → danh sách rỗng kèm tổng thật.

## Trang và route

Tất cả có `no-store`. POST dùng form token (`AutoValidateAntiforgeryToken` toàn cục). Hai form của E02 vẫn chỉ dành cho Buyer.

| Route | Quyền | Nội dung |
|---|---|---|
| `GET /Disputes?kind=&state=&page=` | Buyer, Seller | H1 "Returns and requests". Seller có tab All / Returns / Requests and disputes (`kind`). Cả hai vai trò có chip All / Open / Closed (`state`). Mỗi dòng: item, loại · lý do, Order #, giá dòng, ngày mở, bên còn lại, nhãn trạng thái theo người xem, link "See request details" (buyer) / "See details" (seller). Phân trang Previous/Next |
| `GET /Disputes/Details/{id}?activity=&reply=` | Buyer/Seller của request; người khác → 404 | Xem bên dưới. `activity` = trang timeline. `reply` = bước 2 của lệnh đã chọn |
| `POST /Disputes/Act` | Buyer, Seller | Field: `DisputeId`, `Action`, `Message`, `Amount`, `Tracking`, `Reason`. Vai trò và quyền luôn lấy từ server (`ActAsync`); `ItemReceived`/`ExpireReturn`/`Decide` từ client → 400 |

Trang chi tiết:

- **Đầu trang:**
  - Back to Returns and requests.
  - Tiêu đề theo người xem: buyer "Your return request" / "Your item not received request"; seller "You have a return request" / "You have an item not received request".
  - Dòng tóm tắt và câu hướng dẫn bước hiện tại, không lặp lại nhau.
    - Seller `Open` chưa có offer: câu tóm tắt kiểu D2 ("There's something wrong with the item your buyer's received, so they'd like to return it." / "Your buyer would like to return this item." với lý do đổi ý / "Your buyer hasn't received their item."), sau đó "Please respond by {due} or we may step in to help."
    - Buyer `Open`: nhãn trạng thái (có hạn), sau đó "We let the seller know. They can …" (tùy loại).
    - Các trạng thái khác: nhãn trạng thái theo người xem, kèm câu hướng dẫn.
  - Demo timing nếu bật.
- **Item:** tên, Order # (link tới đơn), giá dòng, bên còn lại.
- **Info from the buyer:** Reason, Comments (details), với INR thêm "The buyer would like: a refund / the item". Khi có: offer đang chờ, số tiền đã hoàn (mô phỏng), tracking hàng trả.
- **What happens from here:** 4 bước, đánh dấu bước hiện tại.
  - INR theo H1.
  - Return tóm tắt từ Help 4041: Start a return → Seller has 3 business days → Send the item back → Get your refund.
- **Select a reply** (bước 1, theo D2):
  - Radio cho các lệnh có trong `Actions` của người xem, trừ Escalate/Close, theo thứ tự cố định có mô tả → **Continue** (GET `?reply=`).
  - Buyer có thêm link riêng "Ask eBay to step in and help" (khi `Actions` có Escalate) và "Close your request".
  - Khi chưa escalate được: "You can ask eBay to step in after {due}".
  - Không có lệnh nào → không hiện khối này. Request đã đóng hiện "This request is closed."
- **Bước 2** (`reply` hợp lệ):
  - Form riêng của lệnh đó, với đúng các field cần: message bắt buộc với Message; tracking với AddTracking/MarkReturnShipped; amount với OfferPartial; lý do với Escalate/Close. Message tùy chọn với các lệnh còn lại.
  - Có câu xác nhận hậu quả, ví dụ "Refund US $X (simulated)". Nút xác nhận và Cancel (về bước 1).
  - `reply` không có trong `Actions` → 409, kèm "That option isn't available for this request right now." Giá trị không phải enum → 400.
- **Request activity** (timeline):
  - Mới nhất trước, 20 event/trang, link "Newer activity" / "Older activity".
  - Mỗi event: người làm ("You", "The buyer", "The seller", "eBay"), câu mô tả theo `Type`, message (HTML-encode, giữ xuống dòng), số tiền/tracking, thời điểm UTC.

Kết quả POST:

- **Success:** 302 về `Details/{id}` kèm notice theo lệnh, ví dụ "Your message was sent." / "You accepted the return." / "You asked eBay to step in. We usually respond within 48 hours."
- **Invalid:** 400, hiển thị lại bước 2 với dữ liệu đã nhập và lỗi.
- **Conflict:** 409, hiển thị lại trang với lỗi từ luật E01 (ví dụ gửi lại một lệnh đã thực hiện).
- **NotFound:** 404. **Forbidden:** 403.

Nhãn trạng thái theo người xem (buyer giữ nhãn E02):

| Status/Outcome | Seller thấy |
|---|---|
| Open | Respond by {due} (có offer: "Partial refund offered — waiting for the buyer") |
| ReturnAccepted | Return accepted — buyer ships by {due} |
| ReturnShipped | Item shipped back — refund by {due} |
| Escalated | eBay is reviewing this request |
| Refunded / PartialRefund / BuyerClosed / ItemReceived / SellerDeclined / ReturnNotShipped / DecidedForBuyer / DecidedForSeller | Closed — refunded / Closed — partial refund / Buyer closed this request / Closed — item received / Closed — you declined the return / Closed — return not shipped in time / Closed — decided in the buyer's favor / Closed — decided in your favor |

Với buyer, khi `Open` có offer thì nhãn là "The seller offered a partial refund".

## Điểm vào

- **Purchases → More actions → See request details:** trỏ tới `/Disputes/Details/{id}` của request mới nhất của dòng đó (thay link `#line-` của E02). Sau khi gửi form E02 vẫn redirect về trang chi tiết đơn như cũ.
- **Trang chi tiết đơn:**
  - Buyer: mỗi dòng trạng thái request thành link tới request.
  - Seller: thêm dòng trạng thái theo seller và link "See request details" cho từng request (`RequestsForOrderAsync`).
- **Menu tài khoản:** Buyer và Seller có "Returns and requests".

## Kiểm tra

- **Service (SQL thật):**
  - List đúng owner theo vai trò; người lạ, admin và vai trò khác không thấy; legacy bị loại.
  - Lọc theo kind/state; phân trang 10 (trang 2, trang vượt tổng); thứ tự.
  - Timeline phân trang 20, mới nhất trước, tổng đúng.
  - `Counterpart` đúng phía.
  - `RequestsForOrderAsync` chặn đơn không thuộc actor.
- **HTTP:**
  - Chưa đăng nhập → redirect; Admin → 403; người lạ → 404.
  - Act cần CSRF. Overposting (vai trò/trạng thái/seller) bị bỏ qua.
  - Act sai vai trò → 403/409. Invalid giữ dữ liệu đã nhập. Gửi lại → 409 và không ghi thêm.
  - Luồng Return đầy đủ qua HTTP: seller Accept → buyer Mark shipped → seller Refund. Luồng offer và luồng Escalate có lý do.
  - Danh sách buyer/seller và link điểm vào. Nội dung HTML được encode.
- **Browser:** desktop/390/320px — danh sách, chi tiết, Select a reply → Continue → Confirm.
