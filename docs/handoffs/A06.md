# Handoff — A06

Ngày 03/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Commit nền 10c830a; nhánh task/A06-demo-seed, fast-forward main sau kiểm tra. Tìm checkpoint: `git log --oneline --grep=A06`.

## Đã xong

- A06 seed chủ động bằng scripts/Seed-Demo.ps1, không seed trong Web/migration startup. Contract [DEMO_SEED](../architecture/DEMO_SEED.md).
- Migration 0003 thêm dbo.DemoSeedHistory; 17 bảng/constraint/bytes nguồn giữ nguyên. Runtime không được ghi seed journal.
- A06-v1: buyer/seller/admin, store/address, 2 categories, 3 products/inventory, 4 orders Pending/Shipped/Delivered/Cancelled, 8 items, COD payments, 2 shipping records. Dữ liệu English hư cấu; images NULL.
- Password NULL, chưa có auth và không tài khoản nào đăng nhập được. B01 phải từ chối NULL và thiết kế cấp credential an toàn; không dùng hash giả hoặc mật khẩu chung.
- Transaction + app lock; journal cùng transaction. Rerun bỏ qua cả dataset, giữ chỉnh sửa/xóa của người dùng. Email collision trước seed bị từ chối, không adopt user. Không tự repair/reset.
- Local EbayGroup5 đã migrate và seed; SchemaVersions có 3 entries, DemoSeedHistory có A06-v1.

## Kiểm tra

PASS Test-Database.ps1: 12 cases SQL thật (8 nền A05 + 4 seed): metadata/schema/model/permissions, populated upgrade, rollback, concurrency; seed totals/COD, giữ dữ liệu cũ/đã sửa, collision, rollback giữa batch/retry, target guard/uninitialized DB.
PASS Initialize-Database áp dụng 1 migration; Seed-Demo lần đầu created, lần hai skipped.
PASS locked restore, Release build 0 warning/error; validator docs/schema và git diff --check. [EV-A06-01](../evidence/A06.md).
CON-01 Verified A06; SUP-01–04 chỉ có fixture, vẫn Pending. Không chạy lại HTTP smoke vì Web không đổi.

## Tiếp theo

A07 CI build/test/migration: đọc card A07, dependency/evidence cần thiết và source scripts. GitHub URL vẫn thiếu, không đoán remote; tiếp tục phần local độc lập khi có yêu cầu.
B01 auth phải đọc DEMO_SEED; B05/B06 chốt state machine/validation và currency trước UI checkout. Nhãn seed chưa phải nghiệp vụ đã nghiệm thu.
Không tự làm backlog ngoài task được giao. Không cần đọc lại ProjectCode/gallery cho A07.

## Cách chạy và giới hạn

Docker SQL/Redis healthy. SDK: dot-source scripts/Use-LocalDotnet.ps1. Runtime config: dot-source scripts/Use-LocalInfrastructure.ps1; mặc định không export admin connection.
`pwsh -NoProfile -File scripts/Initialize-Database.ps1`, sau đó `pwsh -NoProfile -File scripts/Seed-Demo.ps1`. Xem [runbook DB](../runbooks/DATABASE.md).
Tests: `pwsh -NoProfile -File scripts/Test-Database.ps1`; tạo DB/login cô lập EbayGroup5_Test_<GUID>, giữ lại chẩn đoán, không DROP/prune/reset volume. Chỉ chạy lại khi có thay đổi/failure cần kiểm chứng.
Không sửa migration/seed v1 đã phát hành hoặc journal để ép chạy lại. Thay đổi dataset cần version mới và contract bảo toàn dữ liệu.
Chưa có migration checksum ledger, drift guard/adoption DB legacy, auth/UI nghiệp vụ hoặc CI. Liveness chưa là DB readiness.
GitHub URL, mapping TV1–TV5, deadline còn chờ; không phát sinh câu hỏi chặn A06. UI U1/M1/R1/P1/D1/D2 đã chốt. Handoff A05 đã lưu riêng.
