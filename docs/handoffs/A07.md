# Handoff — A07 hoàn tất

Ngày 03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Commit nền e4371a5; nhánh task/A07-ci, fast-forward main sau kiểm tra. Tìm checkpoint `git log --oneline --grep=A07`.

## Đã xong

- Private repo [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5) đã tạo theo yêu cầu người dùng bằng gh (plugin không có create-repository), owner/visibility đã xác minh. Origin đã nối; main được push. Q-ENV-001 Answered.
- Workflow `.github/workflows/ci.yml`: main/task push, PR main, manual; Ubuntu 24.04, SDK global.json, pin action SHA, contents:read, TRX 7 ngày. Không deploy.
- Test-CI.ps1: docs/hash, locked restore/build, SQL container riêng GUID/random port/password, 12 tests; finally stop và khôi phục env. Không đọc .env hoặc dùng DB demo.
- Verify-Migrations + manifest khóa 4 SQL assets; không phải DB checksum ledger. Verify-Planning bỏ qua artifacts ignored, kiểm tra tracked và untracked docs của repo.

## Kiểm chứng

PASS [GitHub run 37086665404](https://github.com/Twiash/EbayGroup5/actions/runs/37086665404) tại e4371a5: job 2m8s, Ubuntu build 0 warning/error, 12/12 SQL tests (13s), docs/hash pass. Artifact sql-test-results ID 11261170975 upload success, retention 7 ngày.
PASS local CI 12 tests; actionlint 1.7.12; negative checksum/new-version fixture checks; docs validator và git diff --check.
Run có annotation upload-artifact v4 Node 20 được runner ép Node 24; upload success, theo dõi nâng action trong bảo trì. Không đổi code sau run, checkpoint tiếp theo cập nhật tài liệu nghiệm thu.
[EV-A07-01](../evidence/A07.md), [CI contract](../architecture/CI.md), [runbook](../runbooks/CI.md). A07 Done, CON-01 Verified A07; OPS-03 Pending vì CD F07 chưa triển khai.

## Tiếp theo: B01

Chỉ triển khai khi được yêu cầu: đọc card B01 và đúng dependency/contract, đặc biệt [DEMO_SEED](../architecture/DEMO_SEED.md). Demo password NULL, chưa đăng nhập được; phải từ chối NULL và thiết kế cấp credential riêng. Chưa auth/UI nghiệp vụ.
Không tự làm toàn backlog. UI U1/M1/R1/P1/D1/D2 đã chốt; chỉ nạp mẫu liên quan khi cần UI. Mapping TV1–TV5 và deadline còn chờ.

## Vận hành

SDK Windows: dot-source scripts/Use-LocalDotnet.ps1. Local CI: pwsh -NoProfile -File scripts/Test-CI.ps1; cần Docker Linux. Test container/DB local giữ lại để chẩn đoán, không DROP/prune/reset volume. SQL/Redis demo vẫn healthy.
Database EbayGroup5 đã seed A06-v1, giữ 17 bảng/constraints. Seed-Demo chạy riêng, transaction/lock/journal, không overwrite. App config: Use-LocalInfrastructure; initialize/seed theo runbook DATABASE. Handoff A06 lưu riêng; diễn biến CI local có trong evidence A07.
