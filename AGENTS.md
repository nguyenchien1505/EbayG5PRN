# Quy định cho agent

## Bắt đầu

1. Làm việc tại repo này, không sửa hai thư mục nguồn bên ngoài.
2. Đọc `docs/START_HERE.md`, `docs/handoffs/LATEST.md` và đúng task card trong `docs/tasks/`.
3. Kiểm tra `git status --short`, branch và `git log -5 --oneline` trước khi sửa.
4. Đọc các contract/ADR và source được task chỉ dẫn. Xác nhận dependency bằng bằng chứng, không chỉ trạng thái.
5. Không đọc lại toàn bộ PLAN, toàn bộ repo, mọi handoff hay mọi task. Dùng `rg` với phạm vi cụ thể.

## Phạm vi và nguồn sự thật

- Mặc định một task mỗi phiên; A00–A01 là cặp khởi tạo đã được yêu cầu. Không tự làm tất cả backlog từ một lệnh tiếp tục không cụ thể.
- Nếu người dùng yêu cầu nhiều task, hoàn thành phạm vi đó nhưng giữ checkpoint/handoff theo từng task.
- Yêu cầu trực tiếp của người dùng có ưu tiên; cập nhật quyết định mới vào tài liệu để phiên sau không dùng quyết định cũ.
- `requirements/MATRIX.md` là yêu cầu nghiệm thu; ADR/contract là quyết định thiết kế; code/test là hành vi thực tế. Khi mâu thuẫn phải ghi nhận và giải quyết.
- Code kỳ trước là tài liệu tham khảo, không phải chuẩn nghiệp vụ hay bảo mật.
- Task `Planned` chưa sẵn sàng code: xác minh dependency và chốt contract tại task thiết kế liên quan trước.
- Không tự spawn subagent nếu người dùng chưa yêu cầu; không cần đọc mọi skill, chỉ dùng skill liên quan.

## Database và công nghệ

- .NET 8 + MVC/Razor + EF Core + SQL Server; modular monolith. Không tự đổi stack.
- Bản schema nguồn `docs/requirements/sources/clone_ebay_sqlserver_schema.sql` bất biến, kể cả bytes/checksum.
- Giữ 17 bảng, mọi cột/kiểu/PK/FK/unique hiện hữu. Chỉ thêm/mở rộng, không rename/drop/thu hẹp hoặc gỡ constraint để demo.
- SQL migration có phiên bản qua DbUp là cơ chế thay đổi schema; EF Core chỉ mapping/query, không tạo nguồn migration thứ hai.
- Không dùng EnsureDeleted, reset volume hay tái tạo database trong startup/test nhắm vào DB người dùng.
- Test DB dùng database cô lập; migration có dữ liệu cần backfill an toàn và kiểm tra metadata. Không rollback schema bằng DROP.
- Danh tính từ principal server; kiểm tra quyền trên từng object. Không log secret, token hay nội dung chat.

## Khi thiếu thông tin

- Kiểm tra tài liệu/repo và nguồn eBay chính thức hoặc UI truy cập được trước khi hỏi.
- Nếu không tự xác minh được nghiệp vụ hoặc UI quan trọng: báo task/màn hình, nguồn đã kiểm tra, câu hỏi cụ thể, dữ liệu người dùng cần cung cấp và phần bị ảnh hưởng.
- Người dùng có thể gửi link, screenshot, video ngắn hoặc mô tả bước/kết quả; không cần hỏi lại quyền đề nghị bổ sung thông tin.
- Ghi câu hỏi vào `docs/questions/INDEX.md`; tạo file chi tiết chỉ khi cần. Câu trả lời phải vào contract/ADR và task, không chỉ nằm trong chat.
- Tiếp tục phần độc lập. Không bịa giao diện/nghiệp vụ hoặc coi hết thời gian chờ là xác nhận.
- Có thể chọn chi tiết thẩm mỹ nhỏ nhất quán với thiết kế đã chốt; ghi giả định, không hỏi từng chi tiết nhỏ.

## Git và kiểm tra

- Nhánh ngắn `task/<ID>-<slug>`; main chứa checkpoint nhất quán đã kiểm tra. Không force-push/reset mất thay đổi.
- Tự commit phần hoàn tất sau kiểm tra phù hợp; stage file cụ thể, xem diff trước commit.
- Message có task ID. Dùng Git identity đang cấu hình; không giả danh năm thành viên.
- Không commit .env, secret, key, log, build output, ảnh dữ liệu cá nhân hoặc code tham khảo hàng loạt.
- Không sửa thay đổi không liên quan của người dùng. Ghi rõ pass/fail/chưa chạy; không tuyên bố đã test nếu chưa chạy.
- Không tự tạo remote hoặc đẩy code đến repo đoán được. Thiếu URL GitHub chỉ chặn phần remote/CI.
- Không tự tạo worklog, lịch sử review hoặc trạng thái Jira không phản ánh thực tế.

## Kết thúc và context

- Chia task khi có nhiều thay đổi độc lập hoặc thường vượt 3–6 file nghiệp vụ (không áp dụng cứng với scaffold/migration).
- Cập nhật task card và INDEX; ma trận yêu cầu chỉ Done khi có kiểm chứng thực tế.
- Cập nhật contract/ADR nếu thay đổi; không sao chép nội dung dài giữa các tài liệu.
- Ghi handoff trong `docs/handoffs/LATEST.md`: branch, checkpoint, file liên quan, đã xong, còn lại, test, câu hỏi, bước tiếp theo.
- Handoff tối đa khoảng 100 dòng; START_HERE khoảng một trang. Mục tiêu đọc ban đầu 4.000–6.000 token tài liệu, không bỏ qua source cần thiết để đạt con số.
- Trước khi thay handoff, lưu bản cũ vào lịch sử nếu chứa thông tin cần giữ; không nạp lịch sử mặc định.
- Không ghi hash của commit chứa chính tài liệu đó; dùng commit nền hoặc chỉ dẫn `git log` theo task ID.
