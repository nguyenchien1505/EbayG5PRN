# SQL Server và Redis local

A04: Docker Desktop chạy Linux containers trên x64, Compose hỗ trợ `up --wait`. Web vẫn chạy trên host theo [LOCAL_SETUP](LOCAL_SETUP.md); Web image/Nginx thuộc F01. Dự kiến dành khoảng 3–4 GB RAM cho hai dịch vụ. Image SQL Developer chỉ dùng phát triển/học tập.

## Khởi động từ repo root

```powershell
pwsh -NoProfile -File scripts/Initialize-LocalEnvironment.ps1
. ./scripts/Use-LocalInfrastructure.ps1
docker compose config --quiet
docker compose up -d --wait --wait-timeout 180
pwsh -NoProfile -File scripts/Verify-LocalInfrastructure.ps1
docker compose ps
```

`.env` chứa mật khẩu sinh ngẫu nhiên và bị Git ignore; script setup chạy lại giữ nguyên file. Không dùng nguyên placeholder trong `.env.example`. Không gửi output `docker compose config` (không có `--quiet`) hoặc `docker inspect` đầy đủ vì có thể chứa secret. Docker engine có thể đọc environment; đây là credentials local, không dùng lại ở dịch vụ thật.

Hai cổng mặc định chỉ bind loopback: SQL `127.0.0.1,14335`, Redis `127.0.0.1:16379`. Đổi SQLSERVER_PORT/REDIS_PORT trong .env khi bị trùng rồi chạy lại lệnh up. Không dừng container khác hoặc đổi password để sửa lỗi port. File .env dùng đúng bốn key của mẫu, không dấu nháy; password do script tạo an toàn cho Compose, Redis config và connection string.

## Kết nối Web trên host

```powershell
. ./scripts/Use-LocalDotnet.ps1
. ./scripts/Use-LocalInfrastructure.ps1
$env:ASPNETCORE_ENVIRONMENT = 'Development'
dotnet run --project src/EbayGroup5.Web -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5085
```

Sau A05: chạy [Initialize-Database theo runbook](DATABASE.md) một lần trước khi nạp config runtime. Script mặc định đặt DefaultConnection với login EbayGroup5App và Redis, xóa MigrationConnection khỏi environment. Chỉ `-Migration` export SA cho runner/test. Không ghi appsettings hoặc in password. SQL local dùng Encrypt=True/TrustServerCertificate=True, không mang trust này sang production. Database EbayGroup5 và EF mapping đã có; Web không tự migrate. Không commit/chụp credentials.

Trong network Compose, endpoint lần lượt là `sqlserver:1433`, `redis:6379`; không dùng loopback host cho container Web tương lai. Liveness Web chưa phải readiness SQL/Redis.

## Vận hành và kiểm chứng

```powershell
# Dừng mà giữ container/volume; khởi động lại bằng up --wait ở trên.
docker compose stop
# Gỡ container/network nhưng giữ named volumes khi cần:
docker compose down
# Kiểm chứng có gián đoạn CHỈ hai container dự án, không xóa volume:
pwsh -NoProfile -File scripts/Verify-LocalInfrastructure.ps1 -CheckPersistence
```

Persistence check yêu cầu stack healthy trước khi chạy. Nó tạo file marker riêng trong SQL volume và key Redis TTL 10 phút, recreate hai container rồi đọc lại. Không tạo/sửa/xóa database hoặc schema. Đây là bằng chứng volume/AOF, chưa phải test bảo toàn dữ liệu nghiệp vụ SQL (A05).

Không dùng `down -v`, Docker prune hoặc xóa volume để sửa lỗi. Đổi password .env không tự đổi SA password trong SQL volume đã tồn tại; khôi phục giá trị đúng hoặc thay đổi mật khẩu theo quy trình SQL có quyền. Nếu healthcheck lỗi, xem `docker compose logs --tail 80 sqlserver redis` cục bộ, không đưa log chứa dữ liệu riêng lên Git. Kiểm tra RAM, port, đúng .env và SQL startup; không reset database.

Volumes được scope bởi project `ebaygroup5-local`: `sqlserver-data`, `redis-data`. SQL chứa toàn bộ `/var/opt/mssql`; Redis bật AOF trong `/data`. Đổi project name sẽ tạo bộ volume khác; phải xác minh volume trước khi thao tác. Khi nâng image, sao lưu dữ liệu trước, kiểm tra compatibility, review tag+digest trong compose và chạy lại health/persistence.

Thiết kế và nguồn: [contract LOCAL_INFRA](../architecture/LOCAL_INFRA.md). Không coi stack local single-instance là HA hoặc nghiệm thu OPS.

## C03 chat transport

Web hiện yêu cầu ConnectionStrings:Redis từ Use-LocalInfrastructure; schema0006 đã apply theo checkpoint C01, Web không tự migrate. Local HTTP cookie dùng Development như lệnh trên; Production cần HTTPS (F01). Default Chat:AllowedOrigins đúng http://127.0.0.1:5085/http://localhost:5085; nếu đổi port phải đặt lại allowlist explicit, không wildcard. Chat:RedisChannelPrefix default EbayGroup5.Chat.v1, cùng app/DB replicas dùng cùng prefix, DB/app khác phải khác prefix. Không ghi Redis password vào appsettings/output.

Hub `/hubs/chat` cookie Buyer/Seller, WebSocket-only/SkipNegotiation, exact Origin mandatory; no negotiate/SSE/query token. JoinConversation lấy owned ID từ REST inbox/open, group server-only. Receive/app frame32768 bytes; client sequence/read cursors giữ string. REST JSON dùng token `/api/v1/chat/antiforgery`/X-CSRF-TOKEN, cả REST/Hub publish sau SQL commit. Retry không event mới, Advanced=false không read event. Redis outage giữ receipt SQL, client cần REST catch-up; reliable outbox/reconnect/UI C04/C05 còn lại.

Kiểm tra `. ./scripts/Use-LocalDotnet.ps1; ./scripts/Test-CI.ps1`: tự tạo SQL+Redis pinned/password ngẫu nhiên cô lập, two Kestrel hosts/real WS,134 tests pass theo [C03 evidence](../evidence/C03.md). Test-Database yêu cầu SQL+Redis local healthy và chỉ tạo DB test riêng. Vòng đời container và DB test theo mục "Vòng đời Docker" bên dưới; không reset volume người dùng. DataProtection ephemeral shared provider chỉ dùng test; deployment persistent shared keys F02, TLS/proxy/perf chưa nghiệm thu. Liveness không phải readiness.

## Vòng đời Docker và dọn dữ liệu test (từ 06/10/2026)

**Lý do:** trước đây `Test-CI.ps1` chỉ `docker stop` container SQL/Redis tạm, và `Test-Database.ps1` giữ mọi database `EbayGroup5_Test_*`. Mỗi lần chạy Test-CI để lại một container chứa khoảng 230 database test. Các database này tích lũy trong `docker_data.vhdx`, làm file lên 114 GB và đầy ổ C:. Hậu quả là Docker không khởi động được và SQL báo "log … is not available". Người dùng đã purge dữ liệu Docker ngày 06/10/2026, sau đó môi trường được dựng lại theo các bước dưới đây.

**Quy tắc hiện hành:**

| Lệnh | Khi pass | Khi fail |
|---|---|---|
| `scripts/Test-CI.ps1` | Xóa hẳn (`docker rm -f -v`) hai container `ebaygroup5-ci-<hex>` và `…-redis` | Giữ lại (đã dừng) để chẩn đoán và in cảnh báo tên container. `-KeepContainers` cũng giữ lại kể cả khi pass |
| `scripts/Test-Database.ps1` | Xóa các database `EbayGroup5_Test_<32 hex>` trong SQL local | Giữ các database test để chẩn đoán |
| `scripts/Clear-TestArtifacts.ps1` | Mặc định **chỉ liệt kê**. `-Apply` mới xóa container `ebaygroup5-ci-*` và database `EbayGroup5_Test_<hex>`. `-IncludePreview` xóa thêm DB preview `EbayGroup5_Test_D02Visual…`. `-DatabasesOnly` bỏ qua container | — |

`Clear-TestArtifacts.ps1` chỉ nhận tên khớp đúng mẫu do dự án sinh ra. Script không bao giờ đụng database `EbayGroup5`, named volume (`sqlserver-data`, `redis-data`), image hay project Docker khác. Mật khẩu SA chỉ nằm trong environment của container, không đi qua command line của host.

**Thói quen:**

- Sau một lần Test-CI/Test-Database fail và đã chẩn đoán xong: chạy `pwsh -NoProfile -File scripts/Clear-TestArtifacts.ps1` để xem danh sách, rồi thêm `-Apply`.
- Không dùng `docker system prune`, `docker volume prune` hay `docker compose down -v` để dọn dẹp, vì có thể xóa dữ liệu dev hoặc của dự án khác.
- File `docker_data.vhdx` không tự thu nhỏ. Nếu ổ C: vẫn đầy sau khi dọn: thoát Docker Desktop, chạy `wsl --shutdown`, rồi compact vdisk. Sao lưu trước.
- Theo dõi dung lượng: `docker system df` và dung lượng trống của ổ C: (nên còn trên 20 GB khi chạy test SQL).

**Dựng lại môi trường local từ Docker rỗng** (ví dụ sau khi purge):

```powershell
pwsh -NoProfile -File scripts/Initialize-LocalEnvironment.ps1   # .env có sẵn được giữ nguyên
. ./scripts/Use-LocalInfrastructure.ps1
docker compose up -d --wait --wait-timeout 300
pwsh -NoProfile -File scripts/Verify-LocalInfrastructure.ps1
pwsh -NoProfile -File scripts/Initialize-Database.ps1          # tạo EbayGroup5 + toàn bộ DbUp migration
pwsh -NoProfile -File scripts/Seed-Demo.ps1                    # seed demo A06 (phải chạy trước bước kế)
pwsh -NoProfile -File scripts/Enable-DemoAuth.ps1              # mật khẩu demo lấy từ .env.auth
```

Dữ liệu tự tạo khi chạy app (tài khoản, đơn, chat, review, khiếu nại) chỉ nằm trong volume `sqlserver-data`. Mất volume là mất dữ liệu này. Schema, seed và secret local (`.env`, `.env.database`, `.env.auth`) đều nằm trong repo hoặc trên đĩa nên dựng lại được.

**Ghi nhận ngày 06/10/2026:** lần Test-CI đầu tiên ngay sau khi tải lại image có 7 test fail với lỗi "insufficient system memory in resource pool 'default'". Lần chạy lại pass 230/230 và không còn container sót. Nếu lỗi này lặp lại, cần xem lại `MSSQL_MEMORY_LIMIT_MB` của container CI (2048) và RAM của WSL (`.wslconfig`).
