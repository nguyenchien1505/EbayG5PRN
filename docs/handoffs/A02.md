# Handoff — A02

Ngày 02/10/2026. Repo: D:/WINDOWS/PRN3/EbayGroup5.
Commit nền: afe134c. Nhánh làm việc: task/A02-reference-audit; checkpoint fast-forward vào main sau kiểm tra. Tìm commit bằng `git log --oneline --grep=A02`.

## Đã xong

- REFERENCE_MAP: đường dẫn/symbol code nhóm 5 và nguồn bổ sung; điểm nên học và lỗi không kế thừa.
- EBAY_JOURNEYS: chat trước/sau mua, review/feedback, chưa nhận hàng/return/escalation; phân biệt DOC, UI-PARTIAL, UNVERIFIED và CLONE.
- UI_REFERENCE: quan sát home/menu, không xác nhận private UI hoặc mobile.
- Q-UI-001–003 ghi yêu cầu inbox, form feedback và case buyer/seller. Không có câu trả lời/ảnh từ người dùng ở checkpoint này.
- Task/index A02 Done; UI-01 và chức năng runtime vẫn Pending. Không đổi stack/scope/contract nghiệp vụ đã chốt.

## Giới hạn khảo sát

Messages bị eBay chặn bot. Không vượt chặn, không tạo giao dịch/tin nhắn/khiếu nại thật. Screenshot chỉ xem trong công cụ, không lưu file artifact. Không coi Help là bằng chứng pixel UI.
Code cũ chỉ đọc tĩnh; không chạy appsettings hoặc nhập secret vào tài liệu.

## Kiểm tra

PASS: validator xác minh SQL checksum/17 bảng, 39 task/index, requirement IDs và link trong 63 file Markdown. Git whitespace check không báo lỗi. Kiểm tra 19 đường dẫn tham khảo: đều tồn tại.
Không có dotnet test/build hoặc SQL runtime test vì chưa scaffold.

## Tiếp theo

**A03**, đọc task card A03 và các đầu vào tối thiểu của card. Không cần nạp lại toàn bộ tài liệu A02 để scaffold.
Bổ sung .NET 8 song song SDK hiện có, pin phiên bản đã xác minh, solution/layers và smoke web theo card. Không làm A04/A05 trong cùng phiên nếu chỉ được yêu cầu một task.

## Thông tin còn chờ

GitHub remote, tài khoản TV1–TV5, deadline và Q-UI-001–003. Không chặn A03.
Khi làm C04/D02/E02 cần đọc câu hỏi UI tương ứng và câu trả lời nếu có; không coi im lặng là duyệt layout tự nghĩ ra.

Handoff A00–A01 đã lưu riêng, không cần đọc mặc định.
