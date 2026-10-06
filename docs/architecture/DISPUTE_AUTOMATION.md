# E04 — Phản hồi tự động và chuyển quá hạn

Contract do agent soạn ngày 06/10/2026 trên main `fb9521f` (cộng commit docs Q-UI-009), trước khi sửa source. Task này dựa trên:

- [DISPUTES](DISPUTES.md) (E01): lệnh System `Escalate`/`ExpireReturn`, `ActAsSystemAsync` idempotent, event `AutoNotice` có sẵn trong CHECK.
- [DISPUTE_DETAILS](DISPUTE_DETAILS.md) (E03): timeline hiển thị actor System là "eBay".
- PLAN §3.3: "Phản hồi tự động là xác nhận tiếp nhận, hướng dẫn bước tiếp theo và chuyển hàng đợi admin khi quá hạn; không tự phán quyết." và "Job quá hạn phải idempotent khi nhiều replica cùng chạy."

Requirement: **G5-08**, phần tiếp nhận, hướng dẫn và chuyển quá hạn. Admin phân xử có audit thuộc E05, nên G5-08 vẫn Pending tới E05.

Ngoài phạm vi:

- UI/hàng đợi admin và `Decide` (E05). E04 chỉ đưa request sang `Escalated`; E05 đọc từ đó.
- Email/push notification: clone không gửi ra ngoài, thông báo chỉ nằm trong timeline.
- Nhắc hạn giữa chừng (reminder trước khi quá hạn).
- Appeal.

## Hành vi

Không có quyết định tự động nào thay cho người dùng hoặc admin. Job chỉ chạy hai lệnh System đã duyệt ở E01 và ghi thông báo.

**1. Chuyển quá hạn** (lệnh System của E01, không đổi luật):

| Điều kiện (request của module, chưa Closed, `DueAtUtc` ≤ hiện tại) | Lệnh | Kết quả |
|---|---|---|
| `Open` và seller chưa phản hồi (`SellerRespondedAtUtc` NULL) | `Escalate` (System) | `Escalated`, hạn xem xét +48 giờ; vào hàng đợi admin (E05) |
| `ReturnAccepted` (buyer không gửi trả đúng hạn) | `ExpireReturn` (System) | Closed `ReturnNotShipped` |

Seller đã phản hồi nhưng chưa giải quyết, hoặc `ReturnShipped` quá hạn refund: không tự escalate (E01). Job chỉ ghi thông báo để buyer biết có thể Ask eBay to step in.

**2. Thông báo tự động** (`AutoNotice`, actor System):

- Mỗi request chỉ có một thông báo cho mỗi **khóa**.
- Thông báo chỉ ghi cho **trạng thái hiện tại**; không ghi bù thông báo của bước đã qua.
- Nội dung trung lập vì cả hai bên cùng xem timeline. Ngày giờ hiển thị theo cùng định dạng với E02/E03 (demo có giờ UTC).
- Câu chữ do clone soạn theo [Help 4042](https://www.ebay.com/help/buying/returns-refunds/get-help-item-hasnt-arrived?id=4042) và [Help 4041](https://www.ebay.com/help/buying/returns-refunds/return-item-refund?id=4041): seller 3 ngày làm việc, eBay xem xét khoảng 48 giờ.

| Khóa | Khi nào | Nội dung (tóm tắt) |
|---|---|---|
| `Received` | `Open` | Đã nhận request và báo cho seller; seller có đến {due} để (INR) gửi tracking/refund/tin nhắn hoặc (Return) accept/refund/offer; quá hạn chưa xong thì buyer có thể Ask eBay to step in |
| `ReturnAccepted` | `ReturnAccepted` | Buyer gửi trả kèm tracking trước {due}, nếu không request sẽ đóng |
| `ReturnShipped` | `ReturnShipped` | Hàng đang được gửi trả; seller có đến {due} để refund |
| `Escalated` | `Escalated` | eBay đang xem xét, thường phản hồi trong 48 giờ (trước {due}); hai bên vẫn nhắn được |
| `Due:Open` | `Open`, quá hạn, seller đã phản hồi | Hết thời gian phản hồi của seller; nếu chưa giải quyết, buyer có thể Ask eBay to step in |
| `Due:ReturnShipped` | `ReturnShipped`, quá hạn | Hết thời gian refund của seller; buyer có thể Ask eBay to step in |

Không có thông báo khi request đã Closed; event đóng của E01 đã đủ để giải thích.

## Idempotency và nhiều replica

- **DbUp `0013_DisputeNotice.sql`** (chỉ mở rộng):
  - `DisputeEvent.NoticeKey varchar(20) NULL`.
  - CHECK: `NoticeKey` NOT NULL ⇔ `Type = 'AutoNotice'`; khi có thì phải `ActorRole = 'System'`, có `Message` và khóa thuộc tập ở bảng trên.
  - `UX_DisputeEvent_Notice (DisputeId, NoticeKey) WHERE NoticeKey IS NOT NULL`.
  - Trigger append-only và grant của 0011 giữ nguyên.
- **Ghi thông báo** (`NotifyAsync(disputeId)`):
  - Trong một transaction, khóa dòng request bằng `UPDLOCK, HOLDLOCK` như các lệnh E01.
  - Tính các thông báo cần có cho trạng thái hiện tại; chỉ thêm khóa còn thiếu.
  - Unique index là chốt chặn cuối: vi phạm (2601/2627) → coi như replica khác đã ghi, không lỗi.
- **Chuyển quá hạn:** gọi `ActAsSystemAsync`, vốn khóa dòng và kiểm lại luật. Lệnh không còn áp dụng → `Conflict`, không ghi gì.
- **`RunOnceAsync`:** mỗi lần chạy xử lý tối đa `BatchSize` request (mặc định 100) cho mỗi bước:
  1. Chuyển quá hạn (theo `DueAtUtc`, index `IX_Dispute_Due`).
  2. Thông báo cho các request đang mở còn thiếu khóa của trạng thái hiện tại.
  - Phần còn lại để lần chạy sau.
  - Chạy lại bao nhiêu lần, hoặc nhiều replica chạy song song, cũng không tạo trùng.
- **Ghi ngay sau thao tác:** sau khi `Open` / `Act` thành công, controller gọi `NotifyAsync` cho request đó để buyer/seller thấy thông báo ngay. Lỗi ở bước này chỉ log cảnh báo, không làm hỏng thao tác của người dùng (trạng thái đã commit); job sẽ ghi lại sau. Trạng thái của request đóng vai trò outbox: không cần bảng outbox riêng.

**Ghi nhận khi triển khai (06/10/2026):**

- CHECK của 0013 phải ghi rõ `NoticeKey IS NOT NULL`, vì `NULL IN (...)` cho kết quả UNKNOWN và CHECK sẽ cho qua. Test constraint đã bắt được lỗi này trước khi phát hành.
- **Nâng cấp:** request đang mở từ trước E04 nhận thông báo của trạng thái hiện tại ở lần chạy đầu tiên. Thông báo này có thể nằm sau các tin nhắn đã có trong timeline. Đây là hành vi một lần khi nâng cấp.
- Trang chi tiết E03 khi `Escalated`: câu hướng dẫn đổi thành "We usually respond within 48 hours (by …). You can still message the {buyer|seller} here." để không lặp nhãn trạng thái; bỏ dòng trạng thái lặp lại ở khung "What happens from here".

## Job

- `DisputeAutomationWorker` (`BackgroundService` trong Web):
  - Chạy `RunOnceAsync` ngay khi khởi động, sau đó theo chu kỳ.
  - Mỗi lần chạy dùng một DI scope mới.
  - Lỗi được log (không kèm nội dung tin nhắn) và job tiếp tục ở chu kỳ sau.
  - Chỉ log số lượng: escalated / expired / notices.
- Cấu hình `Disputes:Automation`:
  - `Enabled` (mặc định true).
  - `IntervalSeconds` (mặc định 60, giới hạn 1–3600).
  - `BatchSize` (mặc định 100, giới hạn 1–1000).
- Khi chạy demo với `Disputes:DemoBusinessDaySeconds = 40` (3 ngày làm việc = 2 phút), nên đặt `IntervalSeconds` khoảng 10 để thấy việc chuyển quá hạn gần đúng thời điểm.
- WebApplicationFactory của test tắt worker (`Enabled=false`) để test không phụ thuộc thời gian. Một test riêng bật worker để kiểm tra vòng chạy thật.

## Kiểm tra

- **Pure:** thông báo cần có theo trạng thái/loại/quá hạn/seller đã phản hồi; định dạng ngày theo demo.
- **SQL/service** (đồng hồ giả để vượt hạn):
  - `Open` quá hạn mà seller chưa phản hồi → Escalated + thông báo `Escalated`.
  - Seller đã phản hồi → không escalate, có `Due:Open`.
  - `ReturnAccepted` quá hạn → ReturnNotShipped.
  - `ReturnShipped` quá hạn → `Due:ReturnShipped`.
  - Closed → không có gì.
  - Chưa tới hạn → chỉ có `Received`.
  - Bước đã qua không bị ghi bù. Batch tôn trọng `BatchSize`.
- **Idempotency:**
  - Chạy lại nhiều lần → không tăng event.
  - 4 instance chạy song song trên cùng DB → đúng 1 Escalated và 1 thông báo cho mỗi khóa.
- **Constraint/migration:** khóa trùng bị từ chối; khóa ngoài tập hoặc thiếu message bị từ chối; `AutoNotice` không có khóa bị từ chối; event thường có khóa bị từ chối; dữ liệu có sẵn không bị ảnh hưởng.
- **HTTP:**
  - Mở request qua form → trang request có thông báo `Received` ngay.
  - Seller Accept → có thông báo `ReturnAccepted`.
  - Worker bật trong host test tự escalate request quá hạn.
- **Browser:** timeline hiển thị thông báo của eBay.
