# Handoff — A00–A01

Ngày: 02/10/2026. Repo: D:/WINDOWS/PRN3/EbayGroup5.
Nhánh checkpoint: main. Commit: dùng `git log --oneline --grep=A00` (không lưu hash tự tham chiếu).

## Đã xong

- Git riêng, ignore/line endings; PLAN bản chính trong repo, file ngoài repo là link chuyển tiếp.
- AGENTS, START_HERE, ADR nền tảng, workflow và quy tắc hỏi người dùng khi thiếu dữ liệu UI/nghiệp vụ.
- SQL nguồn nguyên bytes/checksum; trích Word vào requirements/sources. Không sửa tài liệu gốc hoặc code cũ.
- Ma trận 8 chức năng + 5 phi chức năng nhóm 5, 6 OPS, 5 dependency và ràng buộc dự án.
- 39 task cards; A00/A01 Done, A02/A03 Ready, phần còn lại Planned. A02/A03 có hướng dẫn cụ thể; card Planned sẽ được cụ thể hóa theo source/contract từ dependency.
- Phân công TV1–TV5; chưa tạo issue Jira hoặc remote GitHub.

## Kiểm tra

Lệnh và kết quả ở [minh chứng](../evidence/A00-A01.md). Validator chỉ kiểm tra tài liệu, checksum và bảng trong SQL nguồn; không phải kiểm thử database runtime.
Chưa chạy dotnet build/test: chưa có solution. Chưa cài .NET 8, tạo Docker stack hoặc K8s.

## Tiếp theo

Mặc định A02: [task card](../tasks/A02.md). Đọc SOURCES, ADR-001 và question index theo card, rồi chỉ khảo sát code/màn hình liên quan.
A03 cũng đủ dependency nếu người dùng chọn scaffold trước. Không tự mở toàn bộ ProjectCode.

## Thông tin chưa có

Q-ENV-001 GitHub URL, Q-TEAM-001 tài khoản thành viên, Q-TEAM-002 ngày nộp. Không chặn A02/A03.
Chưa có câu hỏi UI cụ thể; A02 cần quan sát trước khi hỏi screenshot/video.

## Lưu ý

- CWD chat có thể vẫn là D:/WINDOWS/PRN3; phải đặt workdir repo khi dùng Git/build.
- Tài liệu được cấu trúc để không đọc lại toàn bộ PLAN mỗi phiên.
- Task tiếp theo phải kiểm tra working tree/commit trước khi sửa.
