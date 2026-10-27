# D03 — Seller feedback và công thức uy tín

Contract soạn 06/10/2026 trước khi sửa source, baseline main `7b96a97`. Dependency: D01 (contract [REVIEWS](REVIEWS.md), runtime `6ba273a`, CI37418785742 pass) và D02 (runtime `d290a60`, CI37423139287 pass). Phạm vi D03: lưu seller feedback, áp dụng công thức, gộp form "Leave feedback" theo R1, hiển thị huy hiệu uy tín cạnh tên seller. Ngoài phạm vi: trang hồ sơ uy tín và seller reply (D04), Report/ẩn/audit (D05).

## Nguồn quyết định

**Đã duyệt trong PLAN §3.2:**

- Seller feedback gắn với `OrderItem`, tối đa một lượt mỗi dòng.
- Mức đánh giá Positive/Neutral/Negative, tách khỏi số sao của review sản phẩm; không suy ra mức này từ số sao.
- **Điểm uy tín = Positive − Negative.**
- **Tỷ lệ tích cực = Positive / (Positive + Negative) × 100%.**
- Chưa có feedback nào thì hiển thị "No feedback yet".
- Dùng toàn bộ feedback hợp lệ, không giới hạn 12 tháng. Đây là quy tắc giản lược, không tuyên bố sao chép đúng thuật toán của eBay.
- Chỉ tính nội dung đang hiển thị (D05 sẽ thêm trạng thái ẩn).

**Người dùng chốt 06/10/2026 ([Q-BIZ-003](../questions/INDEX.md)), sau khi đối chiếu eBay Help và R1:**

- Chỉ để lại feedback khi đơn đã Delivered và **trong vòng 60 ngày kể từ khi giao**.
- Comment tùy chọn, tối đa 500 ký tự (R1 hiển thị "0 / 500").
- **Trong 7 ngày đầu sau khi giao chỉ được chọn Positive.** Neutral/Negative chỉ mở từ ngày thứ 7 trở đi, đúng câu nhắc trong R1.
- Một form gộp "Leave feedback": phần trải nghiệm với seller ở trên, phần đánh giá sản phẩm của D02 ở dưới.

**Quyết định thiết kế của agent** (nằm trong scope đã duyệt):

- **Thời điểm giao** = `Payment.paidAt`. B06 ghi trường này cùng transaction chuyển sang Delivered (COD Paid). Đơn phải có đúng một Payment ở trạng thái Paid và có paidAt; thiếu dữ liệu thì không được để feedback.
- Mốc 7 ngày và 60 ngày tính bằng thời gian UTC tuyệt đối theo `TimeProvider`: Neutral/Negative cho phép khi `now ≥ deliveredAt + 7 ngày`; mọi feedback chỉ nhận khi `now ≤ deliveredAt + 60 ngày`.
- Seller nhận feedback là **snapshot `OrderTable.SellerId`** (B06), không phải seller hiện tại của listing. Đơn legacy có SellerId NULL thì không để feedback được.
- Tỷ lệ tích cực làm tròn 1 chữ số thập phân (MidpointRounding.AwayFromZero). Nếu Positive + Negative = 0 thì tỷ lệ = null; khi đó chỉ hiện điểm, hoặc "No feedback yet" nếu tổng bằng 0.
- Không ghi vào bảng nguồn `Feedback` (sellerId/averageRating/totalReviews/positiveRate): bảng này giữ nguyên, không đọc, không ghi. Số liệu uy tín được tính trực tiếp từ bảng mới, để tránh có hai nguồn dữ liệu lệch nhau khi D05 ẩn nội dung.

## Quy tắc gửi

Trong một transaction ReadCommitted, thứ tự lock là User → OrderTable → OrderItem → Payment → SellerFeedback (`UPDLOCK,HOLDLOCK`) → INSERT. Thứ tự kiểm tra:

1. Input hợp lệ.
2. Actor có role Buyer; sai role trả 403.
3. OrderItem thuộc đơn của actor; thiếu hoặc không thuộc actor trả 404.
4. Status = Delivered.
5. SellerId snapshot tồn tại.
6. Có thời điểm giao.
7. Còn trong hạn 60 ngày.
8. Nếu chọn Neutral/Negative thì đã qua 7 ngày.
9. Chưa có feedback cho OrderItem này.

Các bước 4–9 vi phạm đều trả 409. Gửi trùng không coi là thành công idempotent. Lỗi 2601/2627 trả Conflict.

Payment chỉ bị lock khi đơn đã Delivered. Lúc đó B06 không còn thao tác Receive nào đang chạy, nên không có vòng chờ lock với B06.

**Form gộp `POST /Reviews/Create`:**

- Các trường: `Sentiment`, `SellerComment` (tối đa 500), `Rating`, `Comment` (tối đa 2000).
- Phải có ít nhất một phần; không có gì thì trả 400.
- Có comment sản phẩm mà không chọn sao thì trả 400.
- Hai phần chạy trong **một transaction**: phần nào lỗi thì rollback cả hai, rồi render lại form với lý do.
- Phần nào đã gửi trước đó thì chỉ hiện trạng thái. Gửi lại phần đó trả 409.
- Quy tắc của D01/D02 cho review giữ nguyên.

## Schema (DbUp 0008, chỉ mở rộng)

Bảng mới `dbo.SellerFeedback`:

| Cột | Kiểu và ràng buộc |
|---|---|
| `Id` | int identity, PK |
| `OrderItemId` | int NOT NULL, FK OrderItem, UNIQUE |
| `BuyerId` | int NOT NULL, FK User |
| `SellerId` | int NOT NULL, FK User |
| `Sentiment` | varchar(8), CHECK ∈ Positive/Neutral/Negative |
| `Comment` | nvarchar(500) NULL, CHECK không chỉ có khoảng trắng |
| `CreatedAtUtc` | datetime2(7), default SYSUTCDATETIME() |

- CHECK `BuyerId <> SellerId`.
- Index `(SellerId, Id DESC)` include Sentiment, phục vụ tổng hợp và D04.
- `GRANT SELECT, INSERT` cho `ebay_app`. D04/D05 sẽ cấp thêm quyền khi cần reply hoặc ẩn.
- Không backfill. 17 bảng nguồn, các cột gốc và seed đều giữ nguyên.

## Hiển thị trong D03

**Form "Leave feedback"** (R1, `GET /Reviews/Create?orderItemId=`):

- Phần "How was your experience?" có ba lựa chọn dạng pill. Trong 7 ngày đầu, Neutral/Negative bị vô hiệu kèm câu nhắc "You need to wait 7 days before you can leave neutral or negative feedback for this seller" và ngày được mở.
- Ô comment cho seller có bộ đếm 0/500.
- Bên dưới là phần review sản phẩm của D02.
- Phần nào không đủ điều kiện thì hiện lý do thay cho form.

**Orders/Details:**

- Nút "Leave feedback" hiện khi còn ít nhất một phần có thể gửi.
- Trạng thái "You left Positive feedback for the seller." và "You rated this item n out of 5."

**Catalog/Details:** cạnh tên seller hiện `(score)` và "x% positive feedback", hoặc "No feedback yet". Trang hồ sơ chi tiết thuộc D04.

## Kiểm tra bắt buộc

- Unit test công thức, gồm các trường hợp 0/0/0, chỉ có Neutral, điểm âm và làm tròn.
- Eligibility: chưa giao, không thuộc actor, sai role, SellerId NULL, không có thời điểm giao, quá 60 ngày, Neutral/Negative trước 7 ngày, gửi trùng, comment/sentiment không hợp lệ.
- Gửi gộp phải rollback cả hai phần khi một phần lỗi; gửi đồng thời; uy tín tính theo seller snapshot.
- Migration giữ nguyên dữ liệu và metadata; bảng `Feedback` nguồn không đổi.
- HTTP: CSRF, role/owner, overposting; browser desktop và 320px.

## Quyết định D04

Người dùng chốt 06/10/2026 ([Q-BIZ-004, Q-UI-007](../questions/INDEX.md)). Phần này viết trước khi triển khai; D04 đã triển khai theo đúng nội dung này.

- **Tỷ lệ tích cực = P/(P+N) của các feedback có `CreatedAtUtc` trong 12 tháng gần nhất** (`CreatedAtUtc > now.AddMonths(-12)`, theo `TimeProvider`; 1 và 6 tháng tính tương tự với AddMonths). Quyết định này thay quy tắc "toàn bộ feedback" của PLAN §3.2 đối với tỷ lệ.
- **Điểm uy tín (P−N) vẫn tính toàn thời gian**, giống feedback score tích luỹ của eBay. Đây là cách agent hiểu câu trả lời và được ghi rõ để có thể đổi lại nếu người dùng muốn.
- Badge D03 trên product đổi sang tỷ lệ 12 tháng trong D04 (đã triển khai).
- Không có P hoặc N trong 12 tháng thì tỷ lệ = null. Không có feedback nào thì hiển thị "No feedback yet".
- **Seller reply:** chỉ seller nhận feedback (`SellerFeedback.SellerId` = actor có role Seller) mới được reply; mỗi feedback reply một lần, tối đa 500 ký tự, không sửa/rút. Cần migration mới cho reply và quyền tương ứng.
- **Trang hồ sơ:** theo F1 giản lược trong [UI_CANDIDATES](UI_CANDIDATES.md#f1--seller-feedback-profile).

### Chi tiết triển khai D04

Phần này soạn trước khi sửa source, trên baseline main `baa8a8a`. Dependency D03 runtime `f8189df` có CI37425953453 pass và là ancestor của baseline.

**Schema — DbUp 0009 (chỉ mở rộng):**

- `SellerFeedback` thêm `ReplyText nvarchar(500) NULL` và `RepliedAtUtc datetime2(7) NULL`. CHECK: hai cột cùng NULL, hoặc cùng có giá trị và reply không chỉ toàn khoảng trắng.
- Trigger `TR_SellerFeedback_Immutable` chặn mọi UPDATE làm đổi các cột gốc (item, buyer, seller, sentiment, comment, created), và chặn sửa reply khi reply đã có. Áp dụng cho mọi login. D05 thêm cột ẩn thì không bị trigger chặn.
- `ebay_app` chỉ được `GRANT UPDATE (ReplyText, RepliedAtUtc)`, vẫn không có DELETE.

**Gửi reply:**

- Một câu lệnh `UPDATE … WHERE Id=@id AND SellerId=@actor AND ReplyText IS NULL`, atomic.
- Kết quả: actor không có role Seller → 403; feedback thuộc seller khác hoặc không tồn tại → 404; đã có reply → 409; reply rỗng/>500 ký tự/có control char → 400.
- Gửi trùng không coi là thành công idempotent.

**Uy tín:**

- `FeedbackCounts(P, Neu, Neg)` chứa công thức của D03.
- `SellerReputation(Lifetime, Month1, Month6, Month12)`: `Score` lấy từ Lifetime, `PositivePercent` lấy từ Month12.
- Badge trên product hiện `(score)` và "x% positive feedback (12 months)". Nếu Month12 không có P hoặc N thì hiện "No feedback in the last 12 months"; không có feedback nào thì "No feedback yet". Badge có link sang trang hồ sơ.

**Trang hồ sơ `/Sellers/Feedback/{sellerId}?sentiment=&page=`** (public, no-store):

- sellerId phải là user có role Seller; nếu không thì 404.
- `sentiment` nhận `positive`/`neutral`/`negative`, để trống là tất cả; page phải nguyên dương; sai thì 400. Page vượt quá thì trả danh sách rỗng.
- 25 dòng/trang, sắp theo Id giảm dần.
- Mỗi dòng hiện sentiment, comment, tên item (Product, NULL thì hiện "Item unavailable"), buyer che tên dạng ký tự chữ/số đầu + `***` + ký tự chữ/số cuối (bỏ khoảng trắng và dấu; ≤2 ký tự thì chỉ giữ ký tự đầu; không có thì hiện "Buyer"), đơn giá của dòng đơn, "Verified purchase", thời điểm tương đối (Past month / Past 6 months / Past year / More than a year ago), và reply kèm ngày nếu có.
- Không hiện email, order ID hay buyer ID.
- Seller chủ hồ sơ thấy form Reply (tối đa 500 ký tự) cho từng dòng chưa có reply. Gửi xong redirect về trang kèm thông báo; nếu lỗi thì render lại trang 1 kèm thông báo lỗi và đúng status.
- Menu My eBay của Seller có thêm mục "Feedback profile".

**Cập nhật D05:** `Visible()` loại feedback đã Hide khỏi uy tín và hồ sơ; RemoveComment ẩn comment và reply nhưng giữ sentiment; không reply vào nội dung đã Hide (404) hoặc đã gỡ comment (409). Xem [MODERATION](MODERATION.md).

## Quyết định H01 (08/10/2026, giống eBay)

Soạn trước khi sửa source, baseline main `5a8d3b9`. Nguồn: [ADR-003](../decisions/ADR-003-ui-fidelity-references.md), mẫu R1/F1/F2 trong [references](../references/ui/INDEX.md), chú thích trên hồ sơ eBay live, trang Community/hướng dẫn seller (ghi ở evidence H01). Thay các quy tắc tương ứng của D03/D04 ở trên.

**Điểm feedback (toàn thời gian):**

- Một buyer chỉ làm điểm của seller thay đổi tối đa +1 (Positive) và −1 (Negative) cho mỗi **tuần giao dịch**. Tuần = thứ Hai–Chủ nhật theo giờ Pacific (`Pacific Standard Time`, có DST), xác định từ `OrderTable.orderDate` (UTC) của dòng đơn; thiếu orderDate thì dùng `CreatedAtUtc` của feedback.
- Score = số cặp (buyer, tuần) có Positive − số cặp (buyer, tuần) có Negative. Neutral không đổi điểm.
- Tuần tính trong SQL (`AT TIME ZONE`, không phụ thuộc tzdata của container) **một lần khi ghi** bằng trigger `TR_SellerFeedback_TransactionWeek` và lưu ở cột `TransactionWeek` (0015, có backfill); đọc chỉ đếm `DISTINCT` theo index `IX_SellerFeedback_Reputation`, không join đơn hàng. Lần đo CI đầu (tính tuần lúc đọc) làm catalog p95 lên 1,9 s nên đã đổi trước khi lên main.

**% tích cực (12 tháng):** P'/(P'+N') với P', N' là số cặp (buyer, tuần) Positive/Negative của feedback có `CreatedAtUtc` trong 12 tháng; không có P' hoặc N' thì null. Bảng Feedback ratings 1/6/12 tháng vẫn đếm từng feedback (như eBay: 432/8/3 nhưng 99,5%). Cột All time giữ cho đến H02.

**Sao theo điểm:** 10 vàng, 50 xanh dương, 100 ngọc lam, 500 tím, 1.000 đỏ, 5.000 xanh lá, 10.000 sao băng vàng, 25.000 sao băng ngọc lam, 50.000 sao băng tím, 100.000 sao băng đỏ, 500.000 sao băng xanh lá, 1.000.000 sao băng bạc. Nguồn là hướng dẫn seller bên thứ ba (Help chính thức không mở được), khớp màu quan sát trên F2 (13 vàng, 133/216 ngọc lam, 543/829 tím, 1.031–4.024 đỏ). Điểm < 10 không có sao.

**Form "Leave feedback" theo R1** (một form cho một dòng đơn):

1. How was your experience? Positive/Neutral/Negative (quy tắc 7 ngày, 60 ngày giữ nguyên).
2. How would you rate this item? 1–5 sao.
3. Tell us more about this item: **một ô comment tối đa 500 ký tự** ("Feedback length"). Comment lưu vào feedback seller khi chọn sentiment và vào review sản phẩm khi chọn sao; phần nào đã gửi trước đó thì chỉ ghi phần còn lại. Comment mà không chọn sentiment hoặc sao → 400.
4. Did this item arrive on time? (Optional) Yes/No.
5. Rate more details about your purchase (Optional): Item description, Shipping time, Seller communication, mỗi dòng 1–5 sao.
6. Mục 4–5 chỉ hiện và chỉ được gửi cùng phần seller (sentiment); gửi khi không có sentiment hoặc giá trị ngoài miền → 400. "Add up to 5 photos" không làm (Q-UI-012 Deferred 28/10/2026, xem [câu hỏi](../questions/INDEX.md)).

**Detailed seller ratings (DSR):**

- Accurate description = trung bình Item description; Shipping speed = Shipping time; Communication = Seller communication; mỗi dòng kèm số lượt, chỉ tính feedback hiển thị có `CreatedAtUtc` trong 12 tháng, làm tròn 1 chữ số.
- Reasonable shipping cost: clone không thu phí vận chuyển, nên theo quy tắc eBay "free shipping → tự động 5 sao" mỗi feedback trong 12 tháng tính một lượt 5 sao.
- Chỉ hiển thị khi tổng số DSR do buyer chấm (ba dòng đầu, đếm từng giá trị) ≥ ngưỡng `Feedback:DetailedRatingsMinimum` (mặc định 10, như câu "at least 10 detailed seller ratings" trên F1); demo có thể hạ ngưỡng bằng cấu hình. Hiển thị chi tiết thuộc H02; H01 cung cấp dữ liệu.

**Schema — DbUp 0015 (chỉ mở rộng):** `SellerFeedback` thêm `ArrivedOnTime bit NULL`, `DescriptionRating`, `ShippingTimeRating`, `CommunicationRating tinyint NULL` (CHECK 1–5). Trigger `TR_SellerFeedback_Immutable` mở rộng để các cột mới cũng bất biến. Quyền INSERT hiện có đã bao gồm cột mới. Không backfill (feedback cũ không có DSR).

## Quyết định H02 (09/10/2026, hồ sơ và khối người bán giống eBay)

Soạn trước khi sửa source, baseline main sau H01. Mẫu: F2 (classic desktop), F2m (mobile), SA và IT-4 (trang sản phẩm), IT-1 (seller card) trong [references](../references/ui/INDEX.md). Thay bố cục "F1 giản lược" của D04 theo [ADR-003](../decisions/ADR-003-ui-fidelity-references.md). Quy tắc tính điểm giữ H01.

**Dữ liệu mới (DbUp 0016, chỉ mở rộng):** `dbo.[User]` thêm `JoinedAtUtc datetime2(7) NULL DEFAULT SYSUTCDATETIME()`. Không backfill user cũ (không có nguồn ngày thật) → "Member since"/"Joined" chỉ hiện khi có giá trị. Đăng ký và seed mới tự có ngày. Thêm index `IX_Product_Seller` và `IX_Message_SellerTurns` (lọc) vì khối người bán nằm trên mọi trang sản phẩm có trong bài tải F05.

**Số liệu chỉ lấy từ dữ liệu thật (ADR-003 §3):**

- Items sold = tổng `quantity` của các dòng đơn có `OrderTable.SellerId` = seller; hiển thị gọn như eBay (`2.0K items sold`); 0 thì bỏ.
- "Usually responds within 24 hours": các lượt buyer nhắn trong 90 ngày (tin buyer đầu tiên sau tin seller hoặc đầu hội thoại) có seller trả lời; trung vị thời gian trả lời ≤ 24 giờ thì hiện câu này, ngược lại hoặc không có dữ liệu thì bỏ.
- Popular categories = danh mục của listing seller, nhiều nhất trước, tối đa 7; "See all" → danh sách hàng của seller.
- Điểm buyer cạnh tên che (`b***r (0)`): điểm feedback nhận với vai trò buyer (clone chưa có feedback cho buyer → 0, không bịa).
- Không hiện: Top-rated seller, revised feedback, eBay automated feedback (Q-BIZ-007 Deferred: không tạo Positive tự động, điểm chỉ từ feedback thật), ảnh feedback (Q-UI-012 Deferred), quốc gia đăng ký.

**Trang hồ sơ `/Sellers/Feedback/{id}` (F2):**

- Breadcrumb Home › Feedback profile; tiêu đề "Feedback profile".
- Thẻ seller: avatar chữ đầu, tên, `(điểm ★)` ⓘ, "Positive Feedback (last 12 months): x%" ⓘ, "Member since: Mon-DD-YY" nếu có. Hộp "Member Quick Links": Contact member (mở chat trên listing mới nhất còn bán, ẩn nếu không có), View items for sale.
- "Feedback ratings" 1/6/12 tháng (bỏ cột All time của D04 để giống eBay); mỗi số là link lọc theo mức + kỳ.
- "Detailed seller ratings — Average for the last 12 months": 4 dòng sao + (số lượt); dưới ngưỡng H01 hiện "This information will be available when this member receives at least {n} detailed seller ratings."
- Tab: All received Feedback / Received as buyer / Received as seller / Left for others (`tab=all|buyer|seller|left`). Clone chỉ có feedback seller nhận → tab buyer/left hiện trạng thái rỗng thật.
- Ô "Search Feedback … with an item title or ID", Period (All / Past month / Past 6 months / Past year), Sort By (Most relevant = có comment trước rồi mới nhất — xấp xỉ của clone; Most recent), lọc sentiment từ link bảng.
- Danh sách FEEDBACK (icon, comment đậm, tên item + `(#id)` link sản phẩm, reply) / FROM (`Buyer: x***y (0)`, giá, Verified purchase) / WHEN; Report và form Reply giữ như D04/D05.
- Phân trang số trang + Previous/Next, "Page x of y", Items per page 25/50/100. Tham số sai → 400 như D04.
- Mobile ≤ 700 px theo F2m: header gọn (avatar, tên, % positive), tiêu đề "Seller feedback", nút Sort/Filter (details), thẻ feedback một cột, "N results"; ẩn bảng ratings/DSR như eBay mweb.

**Trang sản phẩm (SA/IT-4, IT-1):**

- Seller card cạnh giá: tên `(điểm ★)` link hồ sơ, "x% positive · Seller's other items · Contact seller", nút Message giữ hành vi C04.
- Khối "About this seller": avatar, tên, "x% positive feedback · N items sold", Joined Mon YYYY, Usually responds…, Visit store / Message seller / Save seller, Detailed seller ratings dạng thanh + số (khi đủ ngưỡng), Popular categories.
- Khối "Seller feedback (N)": dropdown All ratings/Positive/Neutral/Negative (`fb=` GET, neo `#seller-feedback`), 8 thẻ mới nhất kiểu mweb, "See all feedback".
- Visit store / Seller's other items / View items for sale → `/Catalog?seller={id}` (lọc listing theo seller, tên store lấy từ bảng nguồn `Store` nếu có). Save seller → trang chung "This feature isn't available in this demo" (`/Help/Unavailable/save-seller`, danh sách khóa cố định, khóa lạ 404).

**Kiểm duyệt sau H01/H02:** comment R1 được ghi vào cả feedback seller và review sản phẩm, nhưng hai bản ghi vẫn kiểm duyệt riêng như D05: ẩn review không ẩn feedback (hành trình J5 kiểm khu Reviews), admin xử lý feedback qua báo cáo riêng.
