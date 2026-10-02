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
dotnet run --project src/EbayGroup5.Web -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5085
```

Script chỉ đặt `ConnectionStrings__DefaultConnection` và `ConnectionStrings__Redis` trong terminal hiện tại, không ghi appsettings hay in password. Database `EbayGroup5` và EF mapping chưa tồn tại trước A05; Web A03 chưa dùng connection strings. SQL connection local dùng Encrypt=True/TrustServerCertificate=True, không mang cấu hình trust này sang production. SA phục vụ bootstrap; tài khoản ứng dụng giới hạn quyền sẽ bổ sung ở A05. SSMS kết nối master bằng sa/password trong .env khi cần kiểm tra trước A05; không commit/chụp password.

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
