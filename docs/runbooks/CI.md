# CI build/test — A07

Workflow: `.github/workflows/ci.yml`, contract [CI](../architecture/CI.md). Push main/task branches, pull request vào main hoặc workflow_dispatch. Chưa deploy; Actions token chỉ contents:read.

## Chạy local cùng entry point

PowerShell 7, Docker Linux đang chạy; SDK theo global.json trên PATH. Trên Windows hiện tại:

```powershell
. ./scripts/Use-LocalDotnet.ps1
pwsh -NoProfile -File scripts/Test-CI.ps1
```

Script kiểm tra docs/hash migration, locked restore, Release build rồi tạo SQL container riêng tên ebaygroup5-ci-GUID với cổng loopback ngẫu nhiên và password sinh tạm. Không đọc .env, không dùng SQL/Redis demo. 12 tests tạo database cô lập và ghi `artifacts/ci/test-results/database.trx`. Container được dừng trong finally, không xóa database/volume; local giữ container đã dừng để chẩn đoán. Không dump docker inspect toàn bộ vì environment chứa credential tạm. GitHub runner tự thu hồi sau job.

CI upload riêng TRX (7 ngày); không upload secrets hoặc database. Khi cần dọn container local, xác định đúng tên cụ thể qua thông báo lần chạy và theo quy trình riêng; không wildcard/prune.

## Thay đổi migration

Không sửa SQL nguồn hoặc script đã phát hành. Thêm version mới vào Scripts/Seed và append SHA256 vào `scripts/migration-checksums.json`; update kỳ vọng journal/test dữ liệu. `pwsh -NoProfile -File scripts/Verify-Migrations.ps1` phát hiện sửa bytes, thiếu hoặc thêm file chưa đăng ký. Manifest là review guard trong repo, không phải checksum ledger trong database hoặc bảo vệ khỏi người sửa đồng thời manifest và SQL.

## Kết nối GitHub

03/10/2026: người dùng yêu cầu tự tạo private repo bằng plugin GitHub. Plugin xác nhận tài khoản Twiash nhưng không có capability tạo repository. `gh auth status` báo chưa đăng nhập; trình duyệt cũng ở trang login. Cần người dùng hoàn tất `gh auth login --hostname github.com --git-protocol https --web` (không gửi token vào chat). Sau đó tạo private EbayGroup5, xác minh owner/visibility, thêm remote theo URL trả về, push checkpoint và kiểm tra Actions run thật.

Chỉ khi run GitHub thành công mới đánh dấu acceptance A07 pipeline đầu tiên. Local pass không chứng minh runner Ubuntu hoặc artifact upload đã chạy. OPS-03 còn cần CD ở F07.
