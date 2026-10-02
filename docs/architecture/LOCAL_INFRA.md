# Contract hạ tầng local — A04

Ngày 02/10/2026. Dependency A03 xác minh tại commit bf72446: MVC shell, SDK/lock/build/HTTP smoke đã pass. A04 chỉ tạo SQL Server/Redis local; chưa chạy baseline/migration/seed (A05/A06), chưa container hóa Web/Nginx (F01).

- Compose project `ebaygroup5-local`, network và named volumes riêng. Không dùng tên container cố định hay thao tác Docker toàn máy.
- SQL Server 2022 Developer, Redis 7.4; pin tag cụ thể và digest sau pull. SQL chỉ publish `127.0.0.1:14335`, Redis `127.0.0.1:16379` mặc định, cho phép đổi port qua .env. Container nội bộ dùng `sqlserver:1433` và `redis:6379`.
- `.env` ignored, khởi tạo password ngẫu nhiên bằng script, không in secret. `.env.example` chỉ mẫu; không commit credentials thật. Script không ghi đè .env có sẵn.
- SQL healthcheck chạy SELECT 1 với sqlcmd tools18, timeout và trust certificate chỉ local; Redis healthcheck PING có auth. Redis requirepass, chạy user redis, AOF; không coi Redis là nguồn lưu chat bền vững.
- SQL named volume `/var/opt/mssql`, Redis named volume `/data`. Restart/recreate phải giữ dữ liệu. `stop`/`down` không xóa volume; không chạy down -v/prune/reset.
- Web hiện chạy host. Script dot-source nạp .env vào biến `ConnectionStrings__DefaultConnection` và `ConnectionStrings__Redis` cho terminal đó; .NET không tự đọc .env. A05 mới dùng SQL; C03 mới dùng Redis. Chưa giả lập readiness Web bằng kiểm tra TCP.
- SQL SA chỉ bootstrap/local development. A05 cần account app tối thiểu quyền, không dùng SA cho runtime demo hoàn chỉnh. Database mục tiêu `EbayGroup5` chưa tồn tại trước A05. Không sửa schema nguồn.

Files: `compose.yaml`, `.env.example`, `scripts/Initialize-LocalEnvironment.ps1`, `scripts/Use-LocalInfrastructure.ps1`, `scripts/Verify-LocalInfrastructure.ps1`, `docs/runbooks/LOCAL_INFRA.md`.

Kiểm chứng: Compose config quiet, up --wait, health/auth, SELECT 1, Redis PING; marker riêng A04 kiểm tra qua force-recreate để chứng minh volumes, không tạo bảng nghiệp vụ. Không log config đã interpolate hoặc inspect env.

Nguồn: [Microsoft SQL container/tools18](https://learn.microsoft.com/en-us/sql/linux/install-upgrade/quickstart-install-docker?view=sql-server-ver17), [Redis official image](https://hub.docker.com/_/redis), [Compose startup health](https://docs.docker.com/compose/how-tos/startup-order/). Tra tag SQL trực tiếp registry MCR ngày triển khai.
