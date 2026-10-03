# Handoff — A07 (chờ GitHub login)

Ngày 03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Nền a5e4129; nhánh task/A07-ci. Tìm checkpoint `git log --oneline --grep=A07`.

## Đã triển khai

- Workflow `.github/workflows/ci.yml`: push main/task branches, PR main, manual dispatch; Ubuntu 24.04, SDK global.json, actions pin SHA, token contents:read, artifact TRX 7 ngày. Không deploy.
- `scripts/Test-CI.ps1`: docs/hash, locked restore/build, SQL container riêng GUID/random port/password, 12 tests, finally stop container và khôi phục env. Không dùng .env hoặc DB demo, không xóa/reset volume.
- `scripts/Verify-Migrations.ps1` + migration-checksums.json khóa 4 SQL assets đã phát hành; thêm version phải append manifest. Không phải DB checksum ledger.
- [CI contract](../architecture/CI.md), [runbook](../runbooks/CI.md), [EV-A07-01](../evidence/A07.md).

## Kiểm chứng

PASS local Test-CI: 12/12 SQL tests, locked restore/build 0 warning/error, docs/hash. PASS actionlint 1.7.12. Negative hash/new-version fixture checks pass. Container test đã dừng và giữ dữ liệu; SQL/Redis demo healthy.
Lần đầu gặp LASTEXITCODE chưa gán, đã sửa child pwsh invocation và chạy lại thành công.
GitHub Actions thật/Ubuntu/artifact upload CHƯA CHẠY. A07 InProgress, OPS-03 Pending.

## Bước tiếp theo cần hoàn tất A07

Người dùng đã yêu cầu tự tạo private repo bằng plugin GitHub; không hỏi lại URL repo. Plugin GitHub connected login Twiash nhưng tools không có create repository. GitHub CLI có cài nhưng chưa login; Edge cũng hiện trang login.
Đã nhờ người dùng chạy `gh auth login --hostname github.com --git-protocol https --web` và báo xong; không nhận token/mật khẩu qua chat. Khi đã login, xác minh account, tạo private EbayGroup5 (không overwrite repo có sẵn), xác minh URL/visibility, thêm remote, push checkpoint được kiểm tra, theo dõi/fix Actions đến pass rồi cập nhật evidence/task/INDEX/START_HERE và commit checkpoint.
Không tự đoán remote hoặc tuyên bố CI pass từ local. Không làm B01/all backlog trong task này.

## Nền A06 giữ nguyên

Database local đã seed A06-v1, giữ 17 bảng nguồn và constraints; 0003 chỉ thêm DemoSeedHistory. Seed-Demo chạy riêng, transaction/lock/journal, rerun không overwrite. Password demo NULL; chưa auth/UI. B01 phải từ chối NULL và thiết kế credential riêng.
SDK Windows: dot-source scripts/Use-LocalDotnet.ps1; config app: Use-LocalInfrastructure.ps1; DB initialize/seed theo runbook DATABASE. CI entry point cần Docker Linux và SDK trên PATH.
Test DB/container được giữ để chẩn đoán; không DROP/prune/reset. Các thay đổi migration vẫn cần test metadata nguồn. UI U1/M1/R1/P1/D1/D2 đã chốt. TV1–TV5 và deadline còn chờ. Handoff A06 đã lưu riêng.
