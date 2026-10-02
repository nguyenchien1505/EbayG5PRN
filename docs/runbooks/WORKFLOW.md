# Quy trình làm việc local

Chạy từ repo root, PowerShell 7. Các lệnh này chỉ dành cho Git/tài liệu; setup ứng dụng bổ sung ở A03–A04.

```powershell
git status --short
git log -5 --oneline
git switch -c task/A02-reference-audit
pwsh -NoProfile -File scripts/Verify-Planning.ps1
git diff --check
```

Sau khi kiểm tra thay đổi, stage các file cụ thể rồi commit có ID task. Không dùng lệnh xóa/reset dữ liệu làm bước dọn thông thường. Không merge khi main có thay đổi chưa giải quyết.

Ở giai đoạn agent làm tuần tự, nhánh task hoàn tất được fast-forward vào main sau kiểm tra. Giữ lịch sử thật; remote/push chỉ cấu hình khi có URL đích.

Kiểm tra tài liệu không thay thế dotnet test hoặc kiểm thử SQL. Sau scaffold, task card phải có lệnh kiểm tra ứng dụng cụ thể và cập nhật runbook.
