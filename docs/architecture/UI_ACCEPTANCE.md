# G01 — Nghiệm thu UI (UI-01) và rà ma trận yêu cầu

Contract do agent soạn ngày 08/10/2026 trên main `013f7f4`, trước khi sửa source. Người dùng chốt phương án **P1** sau bản rà soát cùng ngày: nghiệm thu UI + truy vết, không đổi nghiệp vụ; lỗi hiển thị nhỏ (CSS/markup) sửa trong G01, chênh lệch lớn tách task riêng.

## Quy tắc phân loại (người dùng nhắc lại 08/10/2026, theo [ADR-002](../decisions/ADR-002-group5-fidelity.md))

- **Thuộc requirement (nhóm 5 + điểm vào/hiển thị của nhóm 5 trên trang khác):** làm **giống eBay thật nhất có thể**. Mọi khác biệt phải có lý do được duyệt (contract/question) hoặc thành việc sửa.
- **Không thuộc requirement nhưng cần để demo (nhóm 1–4):** giản lược được, với điều kiện **không làm hỏng hay sai** tính năng nhóm 5.
- Mỗi màn hình được xếp một trong: **Khớp** · **Khác biệt đã duyệt** (dẫn chứng) · **Sửa trong G01** (nhỏ) · **Task sửa** (lớn, ghi phạm vi).

## Phương pháp

1. Stack cô lập (`Test-AppStack.ps1 -Screens`): Nginx HTTPS, 2 replica, seed demo A06 + credential ngẫu nhiên. Journeys chạy chế độ `--screens`: tạo dữ liệu "sạch" qua UI (chat buyer ↔ seller, feedback + review cho một dòng hàng, return cho dòng khác, item-not-received cho đơn đã gửi) rồi chụp.
2. Mỗi màn chụp **desktop 1280×900** và **mobile 390×844** (full page), JPEG nén; kiểm tra tràn ngang (`scrollWidth ≤ clientWidth`) và vi phạm CSP trên từng màn.
3. So từng ảnh với nguồn đã chốt trong [UI_CANDIDATES](UI_CANDIDATES.md)/[UI_REVIEW](UI_REVIEW.md); ghi kết quả vào bảng dưới. Ảnh commit ở `docs/evidence/ui/` (dữ liệu demo hư cấu, không có dữ liệu cá nhân).
4. Không dùng/ghi cluster `ebaygroup5` của người dùng (seed demo cho cluster thuộc G02).

## Màn hình và nguồn tham chiếu

| # | Màn (vai trò) | Requirement | Nguồn eBay đã chốt |
|---|---|---|---|
| S01 | Catalog: sao + số rating, seller % positive trên card (buyer) | G5-04, G5-05 (điểm vào); SUP-02 giản lược | UI_REVIEW live 06/10/2026 (A2) |
| S02 | Trang sản phẩm: seller card + nút Message, tổng hợp review | G5-01, G5-04, G5-05 | UI_REVIEW live (A1), D02 |
| S03 | Panel chat mở trên trang sản phẩm | G5-01 | U1 |
| S04 | Trang tất cả review của sản phẩm | G5-03, G5-04 | D02/REVIEWS |
| S05 | Hồ sơ feedback seller (công khai) | G5-05 | F1 live |
| S06 | Purchases: chip, Leave feedback, More actions | G5-03, G5-06 (điểm vào); SUP-04 giản lược | P1 + live (A3/A4) |
| S07 | Chi tiết đơn: Contact seller / Leave feedback / Return / I didn't receive it / See request details | G5-01/03/06/07 (điểm vào) | P2 (01/2025) |
| S08 | Form Leave feedback (seller + item) | G5-03, G5-05 | R1 |
| S09 | Return bước 1 (lý do) | G5-06 | E1 (11/2022) + E1m (03/2026) |
| S10 | Return bước 2 (chi tiết, nháp) | G5-06, G5-N03 | E1 |
| S11 | Item not received | G5-06 | E0 + Help 4042 |
| S12 | Returns and requests (buyer) | G5-07 | DISPUTE_DETAILS |
| S13 | Chi tiết request (buyer), timeline | G5-07, G5-08 | H1 (Help 4042) |
| S14 | Messages (buyer): inbox + thread | G5-01, G5-02 | M1 + live 2026 |
| S15 | Messages (seller) | G5-01 | M1 |
| S16 | Chi tiết request (seller): lựa chọn trả lời | G5-07 | D2 (09/2024) |
| S17 | Hồ sơ feedback (seller xem, ô Reply) | G5-05 | F1 |
| S18 | Admin: hàng đợi kiểm duyệt | SUP-05 (giản lược, không có mẫu eBay công khai) | — |
| S19 | Admin: hàng đợi khiếu nại | G5-08 / SUP-05 | — |

## Ngoài phạm vi G01

Đổi luật nghiệp vụ, migration, tính năng mới (ví dụ badge số tin chưa đọc trên header — phương án P2 không được chọn; ghi là khác biệt). Báo cáo/demo/Jira live thuộc G02.

## Kết quả (08/10/2026)

Ảnh: [docs/evidence/ui/](../evidence/ui/) — `S<nn>-<màn>-desktop.jpg` / `-mobile.jpg`, 38 ảnh (19 màn × 2), tạo bằng `pwsh -NoProfile -File scripts/Test-AppStack.ps1 -Screens <thư mục>`. Lần chụp cuối: **0 màn tràn ngang, 0 vi phạm CSP**.

| # | Phân loại | Ghi chú |
|---|---|---|
| S01 Catalog | Khớp (điểm vào) | Card: sao + (số rating), "Seller 100% positive (n)". Lưới thay vì list là giản lược nhóm 1 đã duyệt ([UI_REVIEW](UI_REVIEW.md)); ảnh sản phẩm là placeholder (nhóm 2 không có upload) |
| S02 Trang sản phẩm | Khớp | Sao + "5.0 · 1 product rating" dưới tiêu đề; seller card (n) · % positive (12 tháng) · **Message**; tổng hợp + phân bố + review gần nhất. Chỉ có Buy It Now (nhóm 1 giản lược) |
| S03 Panel chat | Khớp U1 · **sửa G01** | Bỏ nhãn "Connected" thường trực (eBay không hiện trạng thái kết nối); trạng thái chỉ hiện khi có sự cố (Connecting/Reconnecting/Offline/paused). Nút ↻ giữ lại (khôi phục C04/C05, khác biệt đã duyệt) |
| S04 Ratings and reviews | Khớp D02 | Sao/thanh phân bố hiển thị đúng sau đổi CSP F04 (class `pct-N`) |
| S05 Hồ sơ feedback | Khớp F1 · **sửa G01** | Bỏ dòng "Clone rule: score = …" (eBay không hiện công thức; quy tắc đã theo eBay ở Q-BIZ-004) |
| S06 Purchases | Khớp P1/live | Chip All/Ready for feedback/…; Leave feedback; More actions. Card đơn nhiều dòng hiện tiêu đề dòng đầu + "n order line(s)" (giản lược) |
| S07 Chi tiết đơn | Khớp P2 | Contact seller · Leave feedback · Return this item · I didn't receive it · See request details theo điều kiện |
| S08 Leave feedback | Khớp R1 | Sentiment + comment seller; sao + review sản phẩm |
| S09 Return bước 1 | Khớp E1/E1m | 10 lý do hiện hành |
| S10 Return bước 2 | Khớp E1 · **sửa G01** | Không có "Add photos" (khác biệt đã duyệt, [DISPUTE_FORMS](DISPUTE_FORMS.md)); bỏ khoảng trống dưới form, trạng thái nháp nằm cùng dòng ghi chú |
| S11 Item not received | Khớp E0/Help 4042 · **sửa G01** | Như S10; xem phát hiện dữ liệu seed bên dưới |
| S12 Returns and requests | Khớp (clone theo [DISPUTE_DETAILS](DISPUTE_DETAILS.md)) | Chip All/Open/Closed, hạn phản hồi |
| S13 Request (buyer) | Khác biệt đã duyệt | Thiết kế clone theo câu chữ H1 (không có ảnh thật; Q-UI-009) |
| S14/S15 Messages | Khớp M1 + live 2026 | Icon thu/mở thư mục (A5); nhãn "Read" và nút ↻ là quyết định C04/C05 (khác biệt đã duyệt) |
| S16 Request (seller) | Khớp D2 | Accept the return / full refund / partial refund / message |
| S17 Hồ sơ feedback (seller) | Khớp F1 | Reply một lần, Report |
| S18/S19 Admin | Khác biệt đã duyệt | Không có mẫu eBay công khai; giao diện admin giản lược (nhóm 3), nghiệp vụ theo MODERATION/DISPUTE_ADMIN |

### Lỗi dữ liệu demo tìm được và đã sửa

- **Đơn seed không có ngày giao dự kiến** → "I didn't receive it" không bao giờ mở được trên dữ liệu demo (G5-06). DbUp 0011 chỉ backfill đơn có sẵn lúc migrate; DB mới seed sau migration. `DemoSeedRunner` gán `EstimatedDeliveryUtc` cho đơn A06 khi seed (công thức 0011; `v1.sql` bất biến không đổi). Chỉ áp dụng cho lần seed đầu.

### Khác biệt với eBay còn lại (ghi nhận, không làm trong G01)

- Header không có số tin chưa đọc / chuông thông báo (phương án P2 không được chọn).
- Không đính kèm ảnh trong chat, review và khiếu nại (cần kho file + kiểm duyệt; đã duyệt cho khiếu nại ở E02, áp dụng tương tự cho chat U1 và review R1).
- Nút ↻ thủ công và nhãn "Read" trong Messages (C04/C05).

### Phát hiện cho G02 (cần người dùng chốt: Q-DEMO-001)

Dữ liệu seed gắn ngày cố định 20–25/09/2026: cửa sổ return của đơn delivered hết **25/10/2026**, INR của đơn shipped hết **28/10/2026**, feedback hết **24/11/2026**. Đơn mới tạo khi demo cần qua ngày giao dự kiến (5 ngày làm việc) mới báo INR, trừ khi bật `Disputes:DemoBusinessDaySeconds` (hiện compose/K8s chưa bật).
