# Handoff — TEAM-001 (Excel phân công 5 thành viên)

02/11/2026. Nhánh `task/TEAM-001-team-tracker` từ main `b1cfd71` (OPS-002); xem `git log --oneline --grep=TEAM-001`.
Handoff trước: [OPS-002](OPS-002.md) (deploy lại cluster demo, lưu ý vận hành secrets/SDK/Docker).

## Đã xong

- [TEAM_TASKS.xlsx](../team/TEAM_TASKS.xlsx): Tổng quan (công thức theo người), Task đã làm (52 task), Việc còn lại (R01–R13,
  X01–X03 Deferred); Tổng quan có Điểm năng suất (/10) và bảng trọng số sửa được. Task card [TEAM-001](../tasks/TEAM-001.md).
- Phân công (chia lại theo mức đóng góp) ở [OWNERSHIP](../team/OWNERSHIP.md): bốn người ~21% mỗi người, Cương ~15%.
- Không đổi code, Jira, ngày lịch sử.

## Test

- exceljs đọc lại: 52 ID khớp INDEX, 14/13/10/10/5 task, link bằng chứng tồn tại. Excel (COM, chỉ đọc) tính công thức đúng.
- `scripts/Verify-Planning.ps1` pass. Không chạy test ứng dụng (không có thay đổi code).

## Còn lại / đề xuất

- R01 (OPS-04): đồng bộ Jira KAN theo phân công mới — chỉ khi người dùng yêu cầu.
- R02 / Q-TEAM-002: ngày nộp; khi có ngày thì điền cột Hạn chót trong file.
- R03–R05 là việc code tồn đọng H07: mỗi việc cần task card riêng trước khi sửa.
- File Excel giờ do nhóm sửa trực tiếp (người dùng đã bỏ cột Jira/Dòng/Bằng chứng và sheet Ghi chú); agent sau sửa bằng Excel, không sinh lại.

## Lưu ý vận hành

- Giữ nguyên lưu ý ở [OPS-002](OPS-002.md): chạy `Start-K8s` từ checkout có `secrets/`, SDK cục bộ, chỉ dọn bằng `Clear-TestArtifacts.ps1`.
