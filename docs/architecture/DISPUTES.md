# E01 — Khiếu nại (request) và state machine

Contract do agent soạn ngày 06/10/2026 trên main `aaec085`, trước khi sửa source.

Phạm vi E01: schema, state machine, service nghiệp vụ (mở request, chuyển trạng thái, timeline), nối với B05/B06. Ngoài phạm vi:

- Form và nháp (E02).
- Danh sách, chi tiết và UI trao đổi (E03).
- Job phản hồi tự động và quá hạn (E04).
- UI admin phân xử (E05).

## Nguồn quyết định

**Đã duyệt trước đó:** MATRIX G5-06…G5-08 và G5-N03; PLAN §3.3 (theo dòng hàng, một request đang mở mỗi dòng, seller có 3 ngày làm việc, hoàn tiền mô phỏng); [ADR-002](../decisions/ADR-002-group5-fidelity.md) (nhóm 5 giống eBay).

**Nguồn eBay (đọc ngày 06/10/2026):**

- [Item not received — Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042):
  - Báo khi "estimated delivery date passes" và trong 30 ngày.
  - Chọn "I'd like a refund" / "I'd still like the item".
  - "Seller has 3 business days".
  - Ask eBay to step in sau 3 ngày làm việc, tối đa 21 ngày làm việc kể từ khi mở; eBay "usually within 48 hours".
  - Request đã đóng không mở lại được.
- [Return an item — Help 4041](https://www.ebay.com/help/buying/returns-refunds/return-item-refund?id=4041):
  - Seller có 3 ngày làm việc; có thể accept, partial refund, refund ngay hoặc nhắn tin.
  - Money Back Guarantee áp dụng cho hàng lỗi hoặc khác mô tả, kể cả khi listing không nhận trả.
  - Sau khi accept, buyer gửi trả có tracking; refund sau khi seller nhận lại hàng.
  - Buyer escalate khi seller không phản hồi hoặc không refund đúng hạn.
- Danh sách lý do return lấy từ E1m (Community 03/2026), xem [UI_CANDIDATES](UI_CANDIDATES.md#rà-soát-06102026--khiếu-nại-q-ui-003).

**Người dùng chốt ngày 06/10/2026 (Q-BIZ-006):**

1. Ngày giao dự kiến tính lúc checkout và lưu lại; đơn cũ được backfill từ `orderDate`.
2. Return có bước gửi trả: Accept → buyer gửi trả kèm tracking demo → seller refund.
3. Mọi listing coi như có "30-day returns". Seller được Decline lý do đổi ý, không được Decline lý do lỗi hàng.

**Quyết định thiết kế của agent** (trong phạm vi đã duyệt):

- Tên trạng thái, outcome và event bên dưới.
- Thời hạn gửi trả 5 ngày làm việc; seller có 3 ngày làm việc để refund sau khi buyer gửi trả (clone không có tracking hàng trả thật).
- Return áp dụng cho cả dòng hàng (không chọn số lượng).
- Ngày làm việc là thứ Hai–thứ Sáu theo lịch UTC, không tính ngày lễ (PLAN).
- PLAN §3.3 đề xuất 4 lý do (chưa nhận, khác mô tả, hư hỏng, khác) và trạng thái `SellerResponded/Resolved/Rejected`. Hai điểm này **được thay** bằng danh sách lý do và luồng của eBay theo ADR-002.

## Thời gian

- `Disputes:EstimatedDeliveryBusinessDays` (mặc định 5): ngày giao dự kiến = thời điểm đặt + N ngày làm việc.
  - Lưu ở `OrderTable.EstimatedDeliveryUtc` lúc checkout. Mark as shipped ghi cùng giá trị vào `ShippingInfo.estimatedArrival` (cột có sẵn).
  - Migration backfill đơn cũ có `orderDate` theo 5 ngày làm việc: đặt từ thứ Hai–thứ Sáu thì +7 ngày, thứ Bảy +6, Chủ nhật +5. Đơn không có ngày đặt giữ NULL và không mở INR được.
- `Disputes:DemoBusinessDaySeconds` (mặc định 0 = thời gian thật).
  - Khi > 0, mỗi "ngày làm việc" bằng số giây này. Ví dụ đặt 40 thì 3 ngày làm việc = 2 phút như PLAN; ngày giao dự kiến 5 ngày = 200 giây.
  - Cửa sổ 30 ngày lịch và mốc 48 giờ không rút ngắn.
  - UI (E02/E03) phải ghi rõ khi đang chạy thời gian demo.
- Mọi hạn được tính tại thời điểm sự kiện và lưu thành mốc tuyệt đối (`DueAtUtc`). Đổi cấu hình không làm thay đổi request đã mở.

## Điều kiện mở (Buyer sở hữu đơn, theo từng dòng hàng)

| Kind | Điều kiện | Nội dung |
|---|---|---|
| `ItemNotReceived` | Đơn `Pending`/`Shipped`, có seller; `EstimatedDeliveryUtc` < hiện tại ≤ `EstimatedDeliveryUtc` + 30 ngày | `Preference` = `Refund` hoặc `Item` (bắt buộc); thông tin thêm tùy chọn, tối đa 2000 ký tự |
| `Return` | Đơn `Delivered` nhất quán (đúng một Payment `Paid` có `paidAt`); hiện tại ≤ `paidAt` + 30 ngày | Reason bắt buộc (10 lý do); details bắt buộc, 1–2000 ký tự |

Điều kiện chung:

- Dòng hàng thuộc đơn.
- Dòng đó không có request nào đang mở (bất kể loại).
- Chưa từng có request **cùng loại** cho dòng đó, vì đóng rồi không mở lại được.
- Đơn `Delivered` không mở INR được: ở clone, Delivered nghĩa là buyer đã tự xác nhận nhận hàng (khác eBay, nơi Delivered đến từ tracking).

Lý do Return:

- **Đổi ý (seller được Decline):** `OrderedWrongItem`, `ChangedMind`, `NoLongerNeeded`, `BetterPrice`, `DoesntFit`.
- **Lỗi hàng (seller không được Decline):** `Damaged`, `Defective`, `MissingParts`, `NotAsDescribed`, `WrongItem`.

Lý do của INR luôn là `ItemNotReceived`.

## Trạng thái và hạn

| Status | Đang chờ | `DueAtUtc` |
|---|---|---|
| `Open` | Seller phản hồi/giải quyết | Mở + 3 ngày làm việc |
| `ReturnAccepted` (chỉ Return) | Buyer gửi trả | Accept + 5 ngày làm việc |
| `ReturnShipped` (chỉ Return) | Seller nhận lại và refund | Gửi trả + 3 ngày làm việc |
| `Escalated` | Admin (eBay step in) | Escalate + 48 giờ (mục tiêu hiển thị, không tự xử) |
| `Closed` | — | NULL; có `Outcome` + `ClosedAtUtc` |

Outcome:

- `Refunded`, `PartialRefund`: hoàn tiền **mô phỏng**, không đổi Payment.
- `BuyerClosed`.
- `ItemReceived`: buyer xác nhận đã nhận hàng ở B06.
- `SellerDeclined`.
- `ReturnNotShipped`.
- `DecidedForBuyer`: hoàn tiền mô phỏng toàn dòng.
- `DecidedForSeller`.

## Chuyển trạng thái

Mỗi lệnh ghi đúng một event (timeline, append-only). Lệnh ngoài bảng → `Conflict`; sai vai trò hoặc không thuộc request → `Forbidden`/`NotFound`.

| Lệnh | Ai | Từ | Kết quả |
|---|---|---|---|
| `Message` | Buyer, Seller; Admin khi `Escalated` | Mọi trạng thái trừ Closed | Event, không đổi trạng thái |
| `AddTracking` | Seller | INR, `Open` | Event có tracking (1–100 ký tự) |
| `Refund` | Seller | INR `Open`; Return `Open` (buyer giữ hàng) hoặc `ReturnShipped`; cả hai loại khi `Escalated` (seller tự hoàn tiền trong lúc chờ phân xử) | Closed `Refunded`, `RefundAmount` = số lượng × đơn giá của dòng |
| `AcceptReturn` | Seller | Return `Open` | `ReturnAccepted`, xóa offer đang chờ |
| `OfferPartial` | Seller | Return `Open`, chưa có offer chờ | Lưu `OfferAmount` (0 < amount < tổng dòng, 2 chữ số thập phân) |
| `AcceptOffer` | Buyer | Return `Open` có offer | Closed `PartialRefund`, `RefundAmount` = offer |
| `DeclineOffer` | Buyer | Return `Open` có offer | Xóa offer |
| `Decline` | Seller | Return `Open`, lý do đổi ý | Closed `SellerDeclined` |
| `MarkReturnShipped` | Buyer | `ReturnAccepted` | `ReturnShipped`, lưu tracking (1–100 ký tự) |
| `Escalate` | Buyer | `Open` hoặc `ReturnShipped`, khi hiện tại ≥ `DueAtUtc`; với INR còn phải ≤ ngày mở + 21 ngày làm việc | `Escalated` |
| `Escalate` | System (E04) | `Open`, quá `DueAtUtc`, seller chưa phản hồi | `Escalated` |
| `Close` | Buyer | Mọi trạng thái trừ Closed | Closed `BuyerClosed` |
| `ExpireReturn` | System (E04) | `ReturnAccepted`, quá `DueAtUtc` | Closed `ReturnNotShipped` |
| `ItemReceived` | Buyer, qua B06 Confirm received | INR chưa Closed của đơn | Closed `ItemReceived`, cùng transaction với Delivered |
| `Decide` | Admin (E05) | `Escalated` | Closed `DecidedForBuyer` (refund mô phỏng toàn dòng) hoặc `DecidedForSeller` |

Ghi chú:

- `SellerRespondedAtUtc` lưu lần đầu seller thực hiện bất kỳ lệnh nào; E04 dùng để biết seller đã phản hồi chưa.
- Message, tracking, note dài 1–2000 ký tự, được trim, không có ký tự điều khiển (trừ xuống dòng/tab).
- Appeal sau phân xử (eBay cho 30 ngày) **không thuộc phạm vi**.

## Schema — DbUp `0011_Dispute.sql` (chỉ mở rộng)

- `OrderTable.EstimatedDeliveryUtc datetime2 NULL` và backfill như trên.
- `OrderItem`: thêm `UQ_OrderItem_Id_Order (id, orderId)` để làm FK ghép.
- `Dispute` (bảng gốc) giữ nguyên `orderId`, `raisedBy` (buyer), `description` (details), `status`, `resolution` (để E05 ghi note phân xử). Thêm:
  - `OrderItemId` (FK ghép với `orderId`), `SellerId` (snapshot từ đơn), `Kind`, `Reason`, `Preference`.
  - `CreatedAtUtc`, `DueAtUtc`, `SellerRespondedAtUtc`, `EscalatedAtUtc`, `ClosedAtUtc`, `Outcome`.
  - `OfferAmount`, `RefundAmount`, `ReturnTracking`.
- Dòng legacy (`OrderItemId IS NULL`) không bị CHECK mới ràng buộc và không hiện trong module.
- CHECK cho dòng của module:
  - Đủ trường bắt buộc.
  - Kind, Reason, Preference khớp nhau.
  - Status thuộc tập trên; Closed ⇔ `ClosedAtUtc` và `Outcome` đều có.
  - Escalated ⇒ `EscalatedAtUtc`.
  - Số tiền dương.
  - Return bắt buộc có details.
- `UX_Dispute_ActiveLine (OrderItemId) WHERE OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL`: mỗi dòng chỉ một request đang mở.
- `UX_Dispute_LineKind (OrderItemId, Kind) WHERE OrderItemId IS NOT NULL`: không mở lại cùng loại.
- Index cho danh sách buyer/seller và hàng đợi theo hạn (E03/E04).
- `TR_Dispute_NoDelete`: chặn xóa dòng của module.
- `DisputeEvent`:
  - Cột: Id, DisputeId, ActorId, ActorRole (`Buyer|Seller|Admin|System`, System ⇔ ActorId NULL), Type, Message, Amount, Tracking, CreatedAtUtc.
  - Append-only bằng trigger.
  - Grant SELECT, INSERT cho `ebay_app`.

## Service

`IDisputeService`:

- `EligibilityAsync(buyerId, orderItemId)`: với từng Kind, trả CanOpen, lý do không được và cửa sổ thời gian.
- `OpenAsync(buyerId, request)`.
- `ActAsync(actorId, disputeId, command)`: vai trò lấy từ DB (RaisedBy / SellerId / role Admin); không bao giờ lấy từ client.
- `ActAsSystemAsync(disputeId, action)`: dành cho E04.
- `FindAsync(actorId, disputeId)`: trả chi tiết + timeline cho buyer/seller của request hoặc admin.

Chi tiết thực thi:

- Mọi ghi diễn ra trong transaction, khóa dòng request bằng `UPDLOCK, HOLDLOCK`.
- Khi mở request, khóa đơn trước. Unique index là chốt chặn cuối: vi phạm → `Conflict` kèm id request đang có.
- Luật chuyển trạng thái là hàm thuần `DisputeRules`, unit-test được, kèm `AvailableActions` cho UI. Service chỉ nạp snapshot, gọi rules và ghi kết quả.
- Mã kết quả: `Success`, `Invalid`, `NotFound`, `Forbidden`, `Conflict`, giống các module trước.

## Kiểm tra

- Bảng luật được test đầy đủ: mỗi lệnh × vai trò × trạng thái; `Decide` thành công khi và chỉ khi lệnh có trong `AvailableActions`.
- Ngày làm việc: cuối tuần, chế độ demo, backfill SQL khớp hàm C#.
- Điều kiện mở: owner, trạng thái, cửa sổ thời gian, request trùng/đang mở, mở lại cùng loại, hai lần mở song song.
- Constraint, trigger và grant bằng SQL trực tiếp; migration từ dữ liệu có sẵn.
- Tích hợp B06: Confirm received đóng INR; Ship ghi estimatedArrival.
