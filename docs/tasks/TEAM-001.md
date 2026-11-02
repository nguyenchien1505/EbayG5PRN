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

## Cập nhật 02/11/2026 — chỉnh sửa của người dùng và điểm năng suất

- Người dùng tự sửa trong Excel: bỏ cột Jira cũ, ~Dòng code, Bằng chứng (Task đã làm), bỏ ~Dòng/% (Tổng quan) và sheet Ghi chú.
  Giữ nguyên các chỉnh sửa này; bổ sung sửa bằng Excel (COM) để không mất định dạng.
- Task đã làm thêm cột **Độ khó (1–3)** (dropdown), điền sẵn theo số dòng đã đo: <400 → 1, 400–900 → 2, >900 → 3; H06 để trống (task gộp).
- Tổng quan thêm Độ khó (tổng), Hoàn thành việc còn lại (theo ưu tiên Cao 3/TB 2/Thấp 1, không tính Deferred) và
  **Điểm năng suất (/10)** = 10 × (50% độ khó/max + 20% số task/max + 30% hoàn thành). Trọng số ở ô D15:D20, có ô kiểm tra tổng = 100%.
  Không xếp hạng/xếp loại (người dùng chọn chỉ điểm).
- [x] Excel tính lại: Hải 7,0; Đạt 5,5; Huy 5,6; Nguyên 5,8; Cương 2,1 (chưa việc còn lại nào Done).
  Thử trên bản sao: R13 của Cương → Done cho 75% và 4,4 điểm, đúng công thức.

## Cập nhật 02/11/2026 — chia lại theo mức đóng góp

- Yêu cầu: đóng góp (số task + độ khó) đồng đều, Cương thấp nhất nhưng không quá thấp. Tổng quan thêm **Mức đóng góp**
  = (TS khối lượng × độ khó/tổng + TS số task × số task/tổng) / (tổng hai trọng số), dùng chung trọng số với điểm năng suất.
- Giả định "không quá thấp": Cương ≈ 70% mức trung bình của bốn người còn lại (sửa tay nếu nhóm muốn khác).
- Kết quả (task/độ khó/đóng góp): Hải 11/20/21,4%; Đạt 11/19/20,6%; Huy 10/20/20,9%; Nguyên 11/21/22,2%; Cương 9/13/14,9%.
  Danh sách task theo người ở [OWNERSHIP](../team/OWNERSHIP.md). Ghi chú lệch Jira và mô tả R09–R13 cập nhật theo phân công mới.
- [x] Excel tính lại: 52 task đủ, mỗi task đúng một người; tổng đóng góp 100%; điểm năng suất Hải 6,8, Đạt 6,5, Huy 6,6, Nguyên 7,0, Cương 4,7.

## Cập nhật 02/11/2026 — bốn người chênh ≤ 0,3 điểm

- Yêu cầu: điểm đóng góp của Hải, Đạt, Huy, Nguyên cách nhau ≤ 0,3; Cương vẫn thấp nhất nhưng không quá thấp.
- Chia để bốn người cùng 11 task/độ khó 20 → Mức đóng góp 21,4% và điểm năng suất 7,0 bằng nhau (chênh 0 theo cả hai cách hiểu "điểm").
  Cương 8 task/độ khó 13 → 14,4%, điểm 4,7 (~67% mức bốn người). Đổi so với lần trước: D04 Huy→Đạt, B03 Đạt→Huy,
  H00 Cương→Huy, D05 Nguyên→Cương, D06 Cương→Nguyên.
- Điểm năng suất còn 30% theo việc còn lại hoàn thành, nên sẽ lệch khi nhóm cập nhật trạng thái — đó là phần phản ánh tiến độ thật.
- [x] Excel tính lại: 52 task, mỗi task một người; đóng góp tổng 100%.

## Cập nhật 02/11/2026 — không cào bằng, bốn người lệch ≤ 0,3

- Người dùng không muốn chia bằng nhau; yêu cầu điểm bốn người lệch khoảng 0,3 trở xuống, Cương vẫn thấp nhất nhưng không quá thấp.
- Task/độ khó/đóng góp/điểm: Nguyên 10/21/21,6%/6,7; Đạt 11/20/21,4%/6,6; Hải 12/19/21,2%/6,5; Huy 10/20/20,9%/6,4
  (chênh 0,24 trước làm tròn); Cương 9/13/14,9%/4,6 (~70% mức bốn người). Đổi so với lần trước: G01, G02 Nguyên→Hải;
  F04 Hải→Nguyên; B03, H00 Huy→Cương; H04 Cương→Huy.
- [x] Excel tính lại: 52 task, mỗi task một người; đóng góp tổng 100%.

## Cập nhật 02/11/2026 — dữ liệu thời gian

- Người dùng yêu cầu ngày Bắt đầu/Hạn chót/Ngày xong, dự án từ 08/09/2026. Git chỉ có commit từ 02/10/2026, nên người dùng chọn:
  Bắt đầu/Hạn chót = **kế hoạch dựng lại**; Ngày xong = commit cuối có `[ID]` (thực tế). Việc còn lại để trống (Q-TEAM-002).
- Bắt đầu: forward pass theo dependency trong INDEX, độ khó = số ngày dẫn trước, co giãn vào 08/09–01/10 (A–G) và 08/10–24/10 (H);
  task phải bắt đầu sau dependency, mỗi người nhận tối đa 1 task mới/ngày, không muộn hơn commit thật đầu tiên. OPS: bắt đầu = commit đầu.
- Hạn chót theo mốc đợt: A 03/10, B 06/10, C–E 08/10, F–G 10/10, H00–H04 16/10, H05–H07 30/10, OPS = bắt đầu + 2 ngày.
- Kết quả: 51/52 đúng hạn, A07 trễ 3 ngày (sửa CI tới 06/10); Ngày xong trễ tô đỏ bằng conditional formatting.
- Người dùng tự sửa trước đó: xóa phần lớn Ghi chú ở Task đã làm và R01, R02, R07 ở Việc còn lại — giữ nguyên.
- [x] So sánh trước/sau: chỉ cột I–K và dòng giải thích Tổng quan A25 thay đổi; không có task bắt đầu sau ngày xong.
