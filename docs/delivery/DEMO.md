# Kịch bản demo nhóm 5 (khoảng20–25 phút)

Ngày demo: **chưa xác định**. Chuẩn bị theo [SETUP](SETUP.md), kind `-Demo`
với **30s/ngày làm việc**. Luồng UI vẫn theo rule thật; thời gian demo phải nói rõ.
Refund/COD/ship/receive là mô phỏng, không chuyển tiền hoặc giao hàng thật.

## Trước buổi trình bày

- Chạy lại `Start-K8s.ps1 -Demo` trên checkpoint đã kiểm, web2 Ready.
- Mở3 profile: Buyer, Seller, Admin; thử login trước, không chiếu file secrets.
- Tạo trước2 đơn **mới trong buổi này** bằng Wireless Headphones/Buy It Now/COD.
  Ghi order IDs để không thao tác nhầm đơn seed cũ.
- Đơn A để ship/receive/review/return. Đơn B **không Confirm received** để demo INR.
- Đơn B sau150s đủ ngày giao dự kiến (5×30s); tạo đầu buổi để không phải đợi khi tới phần TV4.
- Chuẩn bị3 URL: /Orders/Purchases, /Messages, /Disputes; admin /Admin/Disputes.
- Mở [ảnh nghiệm thu](../evidence/ui/) (bộ H07, 28/10/2026) và report/CI làm dự phòng khi mạng tới GitHub có vấn đề;
  luôn nói rõ ảnh/run cũ là minh chứng, không giả kết quả live.

## Trình tự và người phụ trách

| Mốc | Người | Thao tác | Kết quả giải thích |
|---|---|---|---|
| 0–4 phút | TV1 Dat | Catalog →detail →Buy It Now →địa chỉ →Confirm and pay; tạo A/B | Giá/tồn kho tính server, transaction, owner,3 vai trò; 17 bảng nguồn giữ nguyên |
| 4–8 phút | TV2 Huy | Buyer Message trên detail, Seller Messages trả lời; reload history | WSS cookie+membership, lưu SQL, UUID idempotent, sequence/cursor/read; Redis chỉ event |
| 8–12 phút | TV3 Cuong | Seller ship A, Buyer Confirm received →Leave feedback Positive+5 sao; mở rating và seller profile | Điều kiện Delivered, rating vs sentiment, score/%12tháng, seller reply một lần |
| 12–16 phút | TV4 Nguyên | Return A lý do Item damaged →Next →Details; đợi Draft saved/reload; gửi | Nháp server, owner/state machine; seller Accept return →buyer tracking →seller Refund |
| 16–20 phút | TV4+TV5 | INR B →nội dung+Refund →Send request; seller không phản hồi; mở admin khi quá hạn | Có automatic notice; sau90s (3×30s) +worker tối đa60s tự Escalated; admin quyết định có audit |
| 20–25 phút | TV5 Hải | 2 replica, readiness, CI/CD/load/rolling reports; admin moderation nếu còn thời gian | HTTPS/WSS,shared keys/rate limit,0 HTTP5xx,ACK đủ; Jira sync để sau |

Để demo admin sớm, gửi INR B ngay sau khi qua150s rồi tiếp tục review/return A
trong lúc chờ. Không trả lời INR B bằng seller trong bài **auto-escalation** vì rule
chuyển tự động chỉ áp dụng khi chưa phản hồi. Seller vẫn xem được request trong dashboard.
Buyer/seller mở **cùng case URL** sau admin decision, thấy kết quả và timeline.

Return A: seller có90s phản hồi; sau AcceptReturn buyer có150s để gửi tracking;
sau ReturnShipped seller có90s để refund. Chuẩn bị lời trình bày tránh hết hạn giữa bước.
Không bật clock cực ngắn cho demo người thật. Clock rút ngắn không thay window30 ngày,
feedback60 ngày/chờ7 ngày hoặc admin review48 giờ; phần dùng AddBusinessDays mới rút ngắn.

## Nội dung nhập mẫu

- Chat: “Does this include the charging cable?” / “Yes, it comes in the box.”
- Review: “Arrived in good condition.”, rating5, Positive.
- Return: Item damaged, “The headband cracked in transit.”, tracking “DEMO-RETURN-01”.
- INR: “The delivery date has passed and the parcel has not arrived.”
- Admin: Decide for buyer, “Demo evidence reviewed; refund is simulated.”

Mỗi lần rehearsal dùng đơn mới để không bị review/dispute một lần hoặc trạng thái Closed.
Gửi hai lần do mạng dùng UUID/idempotency, không tạo hai review/case giống nhau.

## Điều cần chỉ ra tại mỗi module

| Module | Quyền và tình huống kiểm |
|---|---|
| Commerce | Buyer không đọc đơn người khác, seller chỉ ship đơn mình; không chỉnh giá qua request |
| Chat | Admin/user thứ ba không được tham gia; reload SQL history; retry không nhân tin |
| Review | Chưa Delivered không được review; report/admin Hide đổi aggregate; reply không sửa lần hai |
| Dispute | Draft giữ sau reload; action sai state bị từ chối; timeline có actor/time/audit |
| Ops | 2 replica, login qua replicas, CD imageSHA, rollout undo giữ DB14 migration |

Bảo mật/latency nên mở **evidence đã chạy**, không tạo SQL outage/rollout trên cluster demo
trong thời gian trình bày nếu chưa chuẩn bị. [REPORT](REPORT.md) dẫn từng test/CI.

## Rehearsal tự động

`Test-AppStack.ps1 -DemoClockSeconds 10` dùng stack/data/password/certificate cô lập;
chạy các journey mua/chat/review/return/moderation/quyền và thêm INR từ đơn mới,
draft reload, worker quá hạn, admin quyết định. Auto test dùng10s/day và worker2s;
cluster demo cho người dùng30s/day, worker60s, nên thời gian chờ khác nhau.

## Khi trình bày không đạt bước

Dừng ở bước lỗi, ghi order/case/time/status, kiểm readiness/auth/clock thay vì bấm liên tục.
Không đổi ngày SQL/owner/role hoặc reset dữ liệu để làm demo pass.
Nếu401/429, login đúng profile/đợi Retry-After. Nếu mất kết nối, đợi reconnect/catch-up;
draft/pending chat chỉ memory nên không reload trước Sent. Nêu giới hạn trung thực.
Chuyển tới evidence/run đã kiểm chứng nếu cần, ghi rõ chưa xác minh live bước bị lỗi.
