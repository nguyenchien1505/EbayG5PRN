# Handoff — A04

Ngày 02/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5. Commit nền bf72446; nhánh task/A04-local-infra, fast-forward main sau kiểm tra. Tìm checkpoint: `git log --oneline --grep=A04`.

## Đã xong

- Compose project ebaygroup5-local với SQL Server 2022 CU27 Developer và Redis 7.4.11, pin tag+digest. Healthcheck, loopback ports, volumes riêng, Redis auth/AOF, hạn mức RAM/log.
- SQL đang ở 127.0.0.1:14335, Redis 127.0.0.1:16379, cả hai healthy và để chạy cho A05. Stack khác giữ nguyên.
- .env generated/ignored, setup không overwrite. Không in secrets. Dot-source `scripts/Use-LocalInfrastructure.ps1` để nạp connection strings vào terminal; .NET không tự đọc .env.
- P1 đã được người dùng chốt ngày 02/10/2026. U1/M1/R1/P1/D1/D2 đều dùng được theo scope, không hỏi lại. Gallery và card B06 đã cập nhật.

## Đọc tiếp tối thiểu

**A05**, task card + [contract infrastructure](../architecture/LOCAL_INFRA.md) + [runbook](../runbooks/LOCAL_INFRA.md), ADR-001, nguồn/schema/CON liên quan. [SOURCE_MAP](../architecture/SOURCE_MAP.md) chỉ dẫn files thực tế. Không nạp gallery/code cũ cho task DB.
A05 cần thiết kế DbUp baseline/migration append-only, EF mapping đủ 17 bảng và metadata tests. Database EbayGroup5 chưa tồn tại. SA hiện chỉ bootstrap local; bổ sung account app tối thiểu quyền trước runtime hoàn chỉnh.
SDK local 8.0.425: dot-source Use-LocalDotnet.ps1 ở mỗi terminal mới. App source vẫn scaffold A03; chưa gọi DB/Redis. Không đánh đồng Compose health với Web readiness.

## Kiểm tra và giới hạn

PASS Compose config quiet/up --wait, SQL SELECT 1, Redis authenticated PING + NOAUTH khi thiếu auth. Force-recreate giữ file marker SQL volume và key Redis AOF; file marker gỡ, key TTL 10 phút. Setup lần hai giữ nguyên .env; Git ignore đúng; TCP loopback và connection strings parse pass.
Runtime SQL 16.0.4295.3 Developer, Redis 7.4.11. [Minh chứng](../evidence/A04.md) ghi digest/lệnh/giới hạn. PASS validator: checksum SQL/17 bảng, 39 task/status và link trong 72 Markdown; Git whitespace pass.
Chưa chạy DB migration/metadata/SQL nghiệp vụ; CON-01/CON-02 và OPS còn Pending. Không build lại .NET vì không sửa source .NET.

## Vận hành an toàn

Dừng riêng stack bằng `docker compose stop` khi không dùng. Không down -v/prune/reset hoặc đổi SA password trong .env với volume cũ. Không log Compose config đã interpolate/inspect environment. Ports đổi trong .env nếu cần; endpoint nội bộ container phải dùng service name theo contract.

## Còn chờ

GitHub URL (A07), mapping TV1–TV5, deadline. UI mobile/profile/timeline/escalation sẽ tìm tiếp và trình khi đến task tương ứng; không chặn A05. Handoff A03 lưu riêng, không nạp lịch sử mặc định.
