# Handoff — OPS-002 (deploy lại cluster demo)

28/10/2026. Nhánh `task/OPS-002-k8s-secrets-guard` từ main `72631b2` (H07); xem `git log --oneline --grep=OPS-002`.
Handoff trước: [H07](H07.md) (đợt H00–H07 Done, phần còn lại/đề xuất).

## Đã xong

- Cluster demo `ebaygroup5` chạy `ebaygroup5-web:72631b2` (main sau H07): 2 pod web Ready, journal 16 migration,
  seed `A06-v1/H05-v1`, HTTPS https://localhost:9443 trả 200, ảnh sản phẩm H05 hiển thị. Dữ liệu/volume giữ nguyên.
- Sự cố khi deploy: chạy từ worktree không có `secrets/` làm script sinh mật khẩu/CA mới và Job migrate thất bại; đã khôi phục
  bằng `secrets/` gốc của checkout chính. `Start-K8s.ps1` nay dừng nếu cluster đã có mà thiếu secrets — [OPS-002](../tasks/OPS-002.md).

## Test

- Deploy lại pass (log `Start-K8s -Demo`), kiểm HTTPS/WSS/pod/journal như task card. Guard: thử thiếu `secrets/` → dừng đúng.
- CI 40695913974 đỏ J4 (form Return: `.button` ghi đè `[hidden]`, race với JS) → sửa `site.css`; Test-AppStack local pass;
  GitHub CI [40699588765](https://github.com/Twiash/EbayGroup5/actions/runs/40699588765) trên `ed3dbe7` success attempt 1. Cluster demo deploy lại từ main sau sửa.

## Còn lại / đề xuất

- Từ H07: ảnh sản phẩm trên form feedback, return/INR, Returns and requests, chi tiết request (placeholder); Messages theo M2 2026.
- Q-TEAM-002 Open; Q-BIZ-007, Q-UI-012, Q-UI-010 Deferred; OPS-04 (Jira live) Pending.

## Lưu ý vận hành

- Chạy `Start-K8s` từ checkout chính `D:\WINDOWS\PRN3\EbayGroup5` (có `secrets/`); worktree phải chép `secrets/` từ đó.
- SDK 8 ở `%LOCALAPPDATA%\EbayGroup5\dotnet`: dot-source `scripts/Use-LocalDotnet.ps1`. Stage file cụ thể; `launchSettings.json` của người dùng. Không chạy Test-CI cùng Test-AppStack.
- Docker Desktop disk image ở `D:\WINDOWS\PRN3\DockerImage\DockerDesktopWSL`. Chỉ dọn bằng `scripts/Clear-TestArtifacts.ps1`.
