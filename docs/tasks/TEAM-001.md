# TEAM-001 — File Excel phân công công việc 5 thành viên

Status: Done

## Mục tiêu và phạm vi

Theo yêu cầu người dùng 02/11/2026: một file Excel quản lý công việc cho Hải, Nguyên, Cương, Huy, Đạt gồm phân công
trách nhiệm 52 task đã Done (để báo cáo/bảo vệ) và danh sách việc còn lại. Cương nhận ít việc nhất nhưng vẫn có phần code;
khối lượng còn lại chia gần đều cho bốn người. Không sửa code, Jira hay ngày lịch sử.

## Dependency và đầu vào tối thiểu

- [INDEX](INDEX.md), [JIRA_MAPPING](../team/JIRA_MAPPING.md), [OWNERSHIP](../team/OWNERSHIP.md), tồn đọng [H07](../handoffs/H07.md),
  Q-TEAM-002/Q-UI-010/Q-UI-012/Q-BIZ-007 trong [câu hỏi](../questions/INDEX.md).

## Thay đổi và quyết định

- `docs/team/TEAM_TASKS.xlsx`: sheet Tổng quan (công thức COUNTIF/SUMIF theo người), Task đã làm (52 dòng),
  Việc còn lại (R01–R13 + X01–X03 Deferred), Ghi chú. Dropdown Phụ trách/Trạng thái/Ưu tiên/Loại; cột ngày để trống (Q-TEAM-002).
- Phân công mới (người dùng duyệt kế hoạch 02/11/2026), ghi tại [OWNERSHIP](../team/OWNERSHIP.md). Khối lượng ước lượng
  = dòng thêm+xóa ngoài `docs/` trong commit có `[ID]`: Hải 14 task ~7.150, Đạt 13 ~6.570, Huy 10 ~6.980,
  Nguyên 10 ~6.650, Cương 5 ~1.160 (D01–D02 là phần code).
- R02 (hỏi ngày nộp) giao Đạt thay vì Cương như bản kế hoạch, để Cương giữ ít việc còn lại nhất.
- File sinh bằng script Node/exceljs ngoài repo; sửa trực tiếp trong Excel từ nay (file là nguồn theo dõi của nhóm).

## Acceptance và kiểm tra

- [x] Đọc lại bằng exceljs: 4 sheet, 52 ID khớp INDEX đúng một lần, số task theo người 14/13/10/10/5, mọi link bằng chứng tồn tại.
- [x] Mở bằng Excel (COM, chỉ đọc) và tính lại: Tổng quan ra 52 task, 28.503 dòng, % Cương 4,1%, việc còn lại 13 (+3 Deferred), 0 chưa phân công.
- [x] `scripts/Verify-Planning.ps1` pass.

## Kết quả

Xem `git log --oneline --grep=TEAM-001`. Jira chưa đồng bộ theo phân công mới (R01 / OPS-04 Pending).
