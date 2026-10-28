# Handoff — H07 (nghiệm thu UI lại, cập nhật bàn giao)

28/10/2026. Nhánh `task/H07-ui-acceptance` từ main `c64c009` (H06); commit theo `git log --oneline --grep=H07`. **Chưa push/merge** — chờ người dùng.
Handoff trước: [H06b](H06b.md).

## Đã xong

- Chụp lại 26 màn × desktop/mobile = 52 ảnh trong `docs/evidence/ui` (thay bộ G01), 0 tràn ngang, 0 vi phạm CSP.
- `Test-AppStack -Screens` tự bật demo clock 30 s/ngày; Journeys `--screens` tạo đơn mới qua UI (đơn seed 09/2026 đã hết cửa sổ return/INR); thêm S20–S25.
- [UI_ACCEPTANCE](../architecture/UI_ACCEPTANCE.md) có bảng H07 (G01 giữ làm lịch sử); REPORT §5, DEMO, HANDOVER cập nhật.

## Test

- `Test-AppStack -Screens` pass (52 file, 0 problem). Test-CI local pass 295/295 .NET + 20/20 Node, NuGet audit sạch, 0 warning.
- **Chưa chạy:** GitHub CI.

## Còn lại / đề xuất

- Ảnh sản phẩm trên form feedback, form return/INR, Returns and requests, chi tiết request (đang placeholder) — task riêng (DTO/service Reviews + Disputes).
- Messages theo M2 2026 (hai cột) — chưa làm.
- Q-TEAM-002 Open; Q-BIZ-007, Q-UI-012, Q-UI-010 Deferred; OPS-04 (Jira live) Pending.

## Bước tiếp theo

1. Người dùng cho push → GitHub CI → fast-forward main; H07 Done.
2. Người dùng deploy lại cluster demo `ebaygroup5` (đang chạy image G02) — agent không làm.

## Lưu ý vận hành

- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1`. Stage file cụ thể; `launchSettings.json` của người dùng. Không chạy Test-CI cùng Test-AppStack.
- Docker Desktop disk image ở `D:\WINDOWS\PRN3\DockerImage\DockerDesktopWSL`. Chỉ dọn bằng `scripts/Clear-TestArtifacts.ps1`.
