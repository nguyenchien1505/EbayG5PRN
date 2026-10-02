# Thông tin UI cần người dùng bổ sung

**Cập nhật 02/10/2026:** người dùng yêu cầu tự tìm ảnh Internet và trình để chốt. Đã nhận U1 và chốt M1/R1/D1/D2; xem [gallery](../architecture/UI_CANDIDATES.md) và trạng thái mới ở [INDEX](INDEX.md). P1 chờ duyệt. Không hỏi lại các mẫu đã chốt. Các mục Open bên dưới ghi tình trạng tại A02; phần còn thiếu hiện tại là mobile, profile và timeline/escalation, tìm tiếp ở task tương ứng.

Mở ngày 02/10/2026 tại A02. Đã đọc eBay Help, quan sát home/menu; Messages bị chặn “Pardon Our Interruption”. Chưa quan sát form sau mua bằng tài khoản có giao dịch. Không cần cung cấp password, cookie hay tạo giao dịch/khiếu nại thật để phục vụ khảo sát.

| ID | Task | Thông tin cần | Tác động | Status |
|---|---|---|---|---|
| Q-UI-001 | C04 | Nếu sẵn có: ảnh hoặc video inbox Messages và một cuộc hội thoại, thấy danh sách, item context, composer và trạng thái gửi; mobile nếu có | Chờ để chốt layout chat giống eBay; C01–C03/backend tiếp tục được | Open |
| Q-UI-002 | B06, D02–D04 | Nếu sẵn có: dòng hàng đã nhận trong Purchase History và form Leave feedback/rating, thể hiện star/sentiment/comment cùng hoặc khác bước | Chờ chốt hành trình màn hình review; điều kiện owner/Delivered trong kế hoạch giữ nguyên | Open |
| Q-UI-003 | E02–E03 | Nếu sẵn có: form báo chưa nhận/sai mô tả, bước chi tiết và trang case có phản hồi buyer/seller; video ngắn cũng được | Chờ độ giống UI private; state/service theo requirement vẫn có thể thiết kế | Open |

Chỉ cần gửi nhóm ảnh đang có, có thể che tên, địa chỉ, order ID và nội dung riêng tư. Không yêu cầu giao diện admin eBay. Khi chưa có nguồn, không lấy code kỳ trước làm bằng chứng giao diện thật.

Nếu người dùng không có các màn hình, cần ghi câu trả lời và xin lựa chọn thiết kế giản lược theo Help trước khi coi phần private UI là đã chốt. Không coi im lặng là chấp thuận. A03 scaffold và các task hạ tầng không bị chặn.

## Tiếp nhận câu trả lời

Ghi ngày và nguồn, chỉ lưu tài liệu người dùng cho phép vào repo sau khi bỏ dữ liệu riêng tư; cập nhật UI_REFERENCE và task liên quan. Không nạp tất cả ảnh vào mỗi phiên, chỉ ảnh cần cho màn hình đang làm.

Chưa có câu trả lời hoặc ảnh người dùng ở thời điểm hoàn thành A02.
