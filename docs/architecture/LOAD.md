# F05 — Kiểm thử tải (JMeter) và đo realtime

Contract do agent soạn ngày 07/10/2026 trên main `1fe0a94`, trước khi sửa source.

Dựa trên PLAN §9.1 (ngưỡng do dự án đặt), [SCALE_OUT](SCALE_OUT.md) (2 replica, rate limit/key ring dùng chung), [JOURNEYS](JOURNEYS.md) (stack cô lập), [CHAT](CHAT.md) (cursor/paging, Redis backplane).

Người dùng chốt ngày 07/10/2026:

- **JMeter bản chính thức Apache** (5.6.3, zip tải từ Apache, kiểm SHA-512 ghim trong repo, giải nén vào `artifacts/tools/` — bị ignore, không commit), chạy bằng Java có sẵn (local Java 26; CI dùng Java của runner).
- **Bài đo đầy đủ chạy local**, CI chạy **bản ngắn** để giữ script luôn chạy được.

Requirement:

| ID | F05 cung cấp | Done khi |
|---|---|---|
| OPS-01 | Nhiều WebSocket qua Nginx, khẳng định phân bố trên ≥ 2 replica và có cặp chat mà hai đầu nằm **khác replica** vẫn nhận tin đủ | CI + local pass |
| G5-N02 | p95 gửi → nhận của 20 cặp chat đồng thời (client SignalR riêng), không mất/trùng tin | Full local đạt p95 < 1 s |
| G5-N04 | Dataset 100.000 tin / 10.000 review / 1.000 dispute; lịch sử chat phân trang trên hội thoại 20.000 tin, inbox, danh sách review/dispute trong JMeter | Full local đạt p95 < 1 s |
| PERF-01 | 50 user JMeter × 10 phút, p95 API chính < 1 s, lỗi ngoài dự kiến < 1 %; stress tăng dần tới điểm nghẽn | Full local đạt; giới hạn thực tế ghi lại |
| OPS-05 | Script chạy lại được + báo cáo tải (F04 đã có phần an ninh) | Có báo cáo + CI chạy bản ngắn |

Nếu không đạt ngưỡng: ghi số thật, tạo việc sửa; **không hạ ngưỡng** (PLAN §9.1).

## Thành phần

| Phần | Nội dung |
|---|---|
| `scripts/Test-Load.ps1 -Profile Short\|Full\|Stress` | Dựng stack cô lập riêng (như `Test-AppStack.ps1`: project `ebaygroup5-loadstack-<hex>`, mật khẩu/port/subnet/chứng chỉ tạm, 2 replica, Nginx HTTPS), seed tải, chạy JMeter và client realtime **đồng thời**, đọc log, tính số liệu, gate theo profile, ghi báo cáo vào `artifacts/load/<hex>/`. Pass → xóa project; fail → giữ lại |
| `scripts/Install-JMeter.ps1` | Tải `apache-jmeter-5.6.3.zip` (dlcdn, dự phòng archive.apache.org), kiểm SHA-512 ghim trong script, giải nén vào `artifacts/tools/apache-jmeter-5.6.3`. Đã có thì dùng lại |
| `compose.load.yaml` | Override **chỉ cho stack tải**: `RateLimits__auth__PermitLimit` cao (50 user đăng nhập từ cùng một IP host); các giới hạn chat giữ mặc định. Stack thường/CI smoke không dùng file này |
| `tests/EbayGroup5.Load` | Console .NET 8. `seed`: tạo user tải (mật khẩu ngẫu nhiên mỗi lần chạy, truyền qua biến môi trường), sản phẩm, đơn đã giao/review/dispute qua **service thật** (checkout, order, review, dispute) và tin nhắn bằng SQL theo lô đúng mọi ràng buộc 0006; xuất CSV credential/hội thoại vào thư mục artifacts. `chat`: N cặp buyer–seller, mỗi người một kết nối SignalR WSS qua Nginx, gửi luân phiên mỗi ~1 s, đo độ trễ gửi → nhận bằng cùng một đồng hồ (Stopwatch trong một tiến trình), báo p50/p95/p99/max, số tin thiếu/trùng |
| `tests/load/ebaygroup5.jmx` | Kế hoạch JMeter: mỗi thread đăng nhập một lần (token antiforgery trích từ trang), rồi lặp: catalog, chi tiết sản phẩm, trang review, hồ sơ seller, Purchases, Disputes, inbox API, lịch sử chat (trang đầu + một trang cũ hơn theo cursor), gửi tin REST (X-CSRF-TOKEN). Think time 0,5–1,5 s. Tham số `-J`: host/port/threads/duration/ramp/CSV |
| App | `ChatHubFilter.OnConnectedAsync` log Information `Chat connection opened user={UserId}` (không nội dung) → script ánh xạ user → replica từ log từng container |

## Dữ liệu tải (Full; Short dùng 1/10)

- 50 buyer + 20 seller tải, mỗi seller 10 sản phẩm (tồn kho lớn), 1 địa chỉ/buyer.
- 11.000 đơn một dòng hàng (service checkout → ship → receive): 10.000 có review (rating 1–5, comment) + seller feedback; 1.000 có return request (service), 900 được buyer đóng, 100 để mở.
- 1.000 hội thoại (mỗi buyer × mỗi seller); 100.000 tin: hội thoại "hot" (buyer 1 × seller 1) 20.000 tin, phần còn lại chia đều. Sequence liên tục, LastSequence/read cursor khớp, TraceId/hash hợp lệ.
- Cặp realtime i ↔ i (i = 1..20) dùng hội thoại sẵn có; JMeter thread k dùng hội thoại (buyer k, seller k mod 20).

## Gate

| | Short (CI) | Full (local) | Stress (local) |
|---|---|---|---|
| JMeter | 10 user × 60 s | 50 user × 600 s | 50 → 100 → 200 → 400 → 800 → 1600 user, mỗi bậc 120 s (thread > 50 dùng chung 50 tài khoản; 429 do giới hạn theo user được tách riêng) |
| Realtime | 10 cặp × 60 s | 20 cặp × 600 s | 20 cặp mỗi bậc |
| Lỗi JMeter < 1 % | gate | gate | ghi bậc vượt |
| p95 mỗi nhãn < 1 s | chỉ báo cáo (runner dao động) | **gate** | ghi bậc vượt |
| Realtime: 0 thiếu/trùng | gate | gate | ghi |
| Realtime p95 < 1 s | chỉ báo cáo | **gate** | ghi |
| WS trên ≥ 2 replica + ≥ 1 cặp khác replica | gate | gate | — |

Báo cáo ghi: commit, profile, cấu hình máy (CPU, nhân, RAM, OS, Docker CPU/RAM), Java/JMeter, số bản ghi dataset, thời lượng, p50/p95/p99/max và lỗi theo nhãn, kết quả realtime, phân bố replica. HTML dashboard của JMeter nằm trong artifacts (không commit).

Ngoài phạm vi: K8s/rolling update (F06–F08), thay đổi luật nghiệp vụ hay ngưỡng để đạt số đo, tối ưu hiệu năng (nếu không đạt sẽ thành việc riêng).

## Chạy

- CI (mỗi push): `./scripts/Test-AppStack.ps1 -Load Short` sau smoke/journeys; JMeter được cache theo checksum; báo cáo `report.json`, `chat-*.json`, `seed-deadlocks.txt` upload thành artifact `load-report`.
- Local đầy đủ: `pwsh -NoProfile -File scripts/Test-AppStack.ps1 -Load Full` (seed ~20 phút + đo 10 phút); stress: `-Load Stress`. Không chạy song song với Test-CI (tranh RAM SQL).
- Báo cáo nằm ở `artifacts/load/<utc>-<profile>/` (ignore): `report.json` (máy, dataset, p50/p95/p99/max theo nhãn, realtime, replica), `dashboard-<threads>/index.html` (HTML của JMeter). Stack pass thì bị xóa, báo cáo giữ lại; fail thì giữ cả stack (`Clear-TestArtifacts.ps1 -Apply` để dọn).
- File credential tải (`jmeter-users.csv`, mật khẩu ngẫu nhiên mỗi lần chạy) chỉ nằm trong thư mục stack tạm, không vào báo cáo.

## Phát hiện: deadlock khi nhiều đơn được ghi đồng thời (Q-PERF-001)

Lần seed Full đầu tiên (8 luồng ghi song song, mỗi luồng một buyer) dừng ở đơn ~6.000 vì bước ship/receive bị deadlock 10 lần liên tiếp. `system_health` của SQL ghi **5.141 deadlock**. Tài nguyên và câu lệnh liên quan:

| Số lần | Tài nguyên | Khóa | Câu lệnh |
|---|---|---|---|
| 4.190 | `Dispute` PK | RangeS-U | `SELECT * FROM dbo.Dispute WITH (UPDLOCK,HOLDLOCK) WHERE orderId=@p0 AND OrderItemId IS NOT NULL AND ClosedAtUtc IS NULL AND Kind='ItemNotReceived'` (Receive đóng INR — E01). Không có index theo `orderId` → quét cả bảng dưới HOLDLOCK |
| 6.570 | `Payment` PK | RangeS-S | `SELECT * FROM dbo.Payment WITH (HOLDLOCK) WHERE orderId=@p0` (OrderService B06) + `UPDATE OrderTable/Payment` khi Receive. Có `IX_Payment_Order`, nhưng khóa phạm vi nằm trên PK — nhiều khả năng plan quét PK (chưa xác minh plan; giả thuyết: plan cache từ khi bảng còn nhỏ) |
| 2.540 | `ShippingInfo` `IX_ShippingInfo_Order` | RangeS-S | `SELECT * FROM dbo.ShippingInfo WITH (HOLDLOCK) WHERE orderId=@p0` rồi `INSERT ShippingInfo` khi Ship: khóa khoảng trống của key chưa tồn tại, chặn insert của đơn kế bên |

Hệ quả cho người dùng thật: nhiều seller giao hàng hoặc nhiều buyer xác nhận nhận hàng **cùng lúc** sẽ có request bị chọn làm nạn nhân deadlock (HTTP 500), và thời gian mỗi đơn tăng dần theo kích thước bảng (seed: ~60 s / 1.000 đơn lúc đầu, ~110 s / 1.000 đơn ở mốc 6.000).

**Đã sửa** sau khi người dùng chốt (mục "Sửa Q-PERF-001" bên dưới). Ghi chú gốc: F05 không sửa (ngoài phạm vi: thay đổi B06/E01 + migration index). Seed mặc định ghi tuần tự (`EBAY_LOAD_WRITERS=1`, có thể đặt lại để tái hiện). Kịch bản JMeter không ghi đơn nên số đo PERF-01 không bị ảnh hưởng, nhưng **luồng ghi đơn chưa được kiểm tải**. Đề xuất task sửa: index `Dispute(orderId)` (DbUp 0014, chỉ thêm), truy vấn Payment/ShippingInfo seek theo index (hoặc khóa theo đơn đã UPDLOCK thay vì HOLDLOCK theo khoảng), kèm test đồng thời. Chờ người dùng quyết định.

### Sửa Q-PERF-001 (người dùng chốt 07/10/2026)

Làm như phần bổ sung F05 (PLAN cố định 40 task), branch `task/F05-order-deadlocks`, contract soạn trước khi sửa source.

- **Tái hiện trước khi sửa:** `OrderConcurrencyTests` — 8 worker, mỗi worker checkout → ship → receive 25 đơn **của riêng nó** (không retry). Trên code cũ: **85 deadlock / 200 đơn**.
- **Nguyên tắc khóa:** mọi thao tác ghi lên dòng con của một đơn (OrderItem, Payment, ShippingInfo, dispute INR của đơn) đều đã giữ `UPDLOCK` trên dòng `OrderTable` của đơn đó (checkout tạo mới trong cùng transaction; Ship/Receive và mở dispute khóa đơn trước). Vì vậy khóa phạm vi `HOLDLOCK` theo `orderId` trên bảng con là thừa và chính nó gây gap-lock/deadlock giữa các đơn khác nhau.
- **Thay đổi:**
  - `OrderService.Load`: đọc OrderItem/Payment/ShippingInfo **không** `HOLDLOCK` (đơn đã `UPDLOCK,HOLDLOCK` trong `ActAsync`; `FindAsync` chỉ đọc).
  - `DisputeService.ItemReceivedAsync`: `UPDLOCK` (giữ tới commit) + `FORCESEEK` trên index mới, bỏ `HOLDLOCK`.
  - **DbUp 0014** (chỉ thêm): `IX_Dispute_Order ON dbo.Dispute(orderId, id)`.
  - Các đọc `HOLDLOCK` theo khóa của bảng con ở Feedback/Review (Payment theo orderId) thêm `FORCESEEK` để không quét cả bảng dưới khóa phạm vi.
- **Kiểm tra:** `OrderConcurrencyTests` 0 deadlock; toàn bộ test B06/D/E giữ nguyên (luật trạng thái không đổi); seed tải mặc định ghi **8 luồng** trở lại và `Invoke-LoadTest.ps1` **gate 0 deadlock** của SQL Server trong lúc seed (CI Short giữ hồi quy).
