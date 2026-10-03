# Handoff — A05

Ngày 02/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Commit nền c5c2064; nhánh task/A05-database, fast-forward main sau kiểm tra. Tìm checkpoint: `git log --oneline --grep=A05`.

## Đã xong

- Database EbayGroup5: 17 bảng gốc nguyên schema và dbo.SchemaVersions của DbUp, 2 journal entries. Source checksum bất biến. Không có seed demo.
- Core/Entities 17 POCO; Infrastructure/Persistence/EbayDbContext mapping exact nullable/type/identity/FK. Web đăng ký context, không migrate/seed trong startup.
- Console EbayGroup5.Database: source hash/preamble guard, target allowlist, application lock, transaction+journal; từ chối DB có bảng chưa journal. Không tự adopt/reset.
- 0002 chỉ thêm role CRUD 17 bảng. Runtime login EbayGroup5App không DDL/journal; .env.database ignored/generated, không overwrite. SA chỉ export qua Use-LocalInfrastructure -Migration; default nạp runtime/Redis và bỏ admin env.
- Scripts Initialize-Database/Test-Database và [runbook](../runbooks/DATABASE.md), [contract](../architecture/DATABASE.md), [source map](../architecture/SOURCE_MAP.md) đã cập nhật.

## Kiểm tra

PASS 8 xUnit cases SQL thật: metadata nguồn độc lập và EF queries/roundtrip; unique email/NULL/FK/no cascade; runtime CRUD nhưng cấm DDL/journal; populated baseline upgrade + nullable addition; rerun; transaction rollback schema/data/journal; concurrency; nonempty unjournaled DB/tên đích sai bị từ chối.
PASS Initialize lần đầu 2, lần sau 0; runtime login verified. Locked restore và Release build 0 warning/error, Web home/liveness pass; web đã dừng, SQL/Redis để healthy. Config mặc định không export admin credentials.
Xem [EV-A05-01](../evidence/A05.md). PASS validator docs/schema: source hash/17 bảng, 39 task/status và link trong 76 Markdown; Git whitespace pass. CON-01 Verified A05 phải test lại mỗi migration; CON-02 Done; nghiệp vụ/OPS/CI còn Pending.

## Tiếp theo: A06

Seed dữ liệu demo lặp lại an toàn. Đọc card A06, DATABASE contract/runbook, entity/context khi cần và đúng SUP/requirement liên quan. Không cần đọc lại ProjectCode/gallery.
Cần quyết định seed identity/business states và password hashes phù hợp task auth trước khi ghi; không bịa tài khoản đăng nhập hoạt động khi auth chưa có. Không sửa schema nguồn, không thêm enum/constraint làm hẹp nullable legacy. Seed không overwrite dữ liệu người dùng hoặc xóa DB.

## Cách chạy và giới hạn

Từ repo root: docker compose up -d --wait; pwsh -NoProfile -File scripts/Initialize-Database.ps1. SDK dot-source Use-LocalDotnet.ps1, runtime config dot-source Use-LocalInfrastructure.ps1; tests chạy Test-Database.ps1.
Test tạo riêng EbayGroup5_Test_<GUID>/login TestApp, giữ lại để chẩn đoán; không tự DROP/prune. Các DB từ lần test lỗi/đã pass vẫn tồn tại. Không chạy test thêm chỉ để lặp kết quả khi không có thay đổi.
Migration scripts đã áp dụng không sửa; thêm version mới và cập nhật kỳ vọng journal/tests. Baseline original CloneEbayDB chỉ tên trong source; runner dùng EbayGroup5 sau khi bỏ đúng hai batch CREATE DATABASE/USE đã xác minh.
Chưa có migration checksum ledger, drift guard runtime hoặc adoption DB legacy; bảo toàn hiện được kiểm chứng bằng tests và immutable source. A07 đưa test vào CI. Không coi health live là DB readiness.

## Còn chờ

GitHub URL (A07), mapping TV1–TV5, deadline. UI U1/M1/R1/P1/D1/D2 đã chốt; mobile/profile/timeline sẽ tìm và trình khi đến task UI. Không chặn A06. Handoff A04 lưu riêng, không đọc lịch sử mặc định.
