# Database baseline, migration và kiểm thử

Checkpoint A05. Đọc [contract DATABASE](../architecture/DATABASE.md) khi sửa schema/mapping. SQL nguồn giữ bytes/checksum; không chạy thẳng CREATE DATABASE CloneEbayDB trên server đang có dữ liệu. Runner dùng database `EbayGroup5`, tách nguồn database name khỏi DDL 17 bảng.

## Chuẩn bị và chạy lần đầu

Từ repo root bằng PowerShell 7, Docker Linux đang chạy:

```powershell
pwsh -NoProfile -File scripts/Initialize-LocalEnvironment.ps1
docker compose up -d --wait --wait-timeout 180
pwsh -NoProfile -File scripts/Initialize-Database.ps1
```

Script cuối dùng SDK local, migration connection riêng qua `Use-LocalInfrastructure.ps1 -Migration`, sinh `.env.database` nếu chưa có rồi chạy DbUp. File này ignored, chứa password của login `EbayGroup5App`; không overwrite/rotate khi chạy lại. File `.env` cũ giữ SA/Redis credentials. Giữ cả hai file riêng tư, không in chúng vào log.

Lần đầu: 2 scripts áp dụng, gồm baseline và role CRUD 17 bảng; lần sau: 0 scripts. Runner giữ application lock trong master theo tên database để các lần chạy đồng thời không trùng migration. Mọi pending script + journal được transaction hóa; CREATE DATABASE nằm ngoài transaction, nên lỗi lần đầu có thể để lại DB rỗng, không tự xóa.

DB có bảng mà chưa có journal bị từ chối. Không tự adopt/đánh dấu baseline hoặc xóa bảng để chạy lại. Đối chiếu metadata và lập task migration nhập dữ liệu riêng. Không chỉnh trực tiếp journal để né lỗi. Script đã phát hành không sửa; thêm file SQL có thứ tự mới trong `src/EbayGroup5.Database/Scripts/`.

## Chạy Web với account runtime

```powershell
. ./scripts/Use-LocalDotnet.ps1
. ./scripts/Use-LocalInfrastructure.ps1
dotnet restore --locked-mode
dotnet build --no-restore -c Release
dotnet run --project src/EbayGroup5.Web -c Release --no-build --no-launch-profile -- --urls http://127.0.0.1:5085
```

Mặc định chỉ export runtime DefaultConnection + Redis; MigrationConnection được xóa khỏi terminal để Web không thừa kế SA qua environment. Chỉ dùng `-Migration` khi chủ động chạy runner/test bằng tay. Web đăng ký EbayDbContext nhưng không tự migrate/seed, không log SQL parameter/secret. Liveness chưa kiểm tra DB; chưa có chức năng nghiệp vụ gọi DB qua HTTP.

App login chỉ có SELECT/INSERT/UPDATE/DELETE trên 17 bảng gốc qua role ebay_app; không db_owner, DDL hoặc quyền journal. Khi thêm bảng, cấp đúng quyền trong migration mới nếu cần. Không dùng entity trực tiếp làm request/response DTO ở task API sau. Nếu password file không khớp login đã có, setup báo lỗi, không tự reset password hay volume.

## Kiểm thử SQL thật

```powershell
pwsh -NoProfile -File scripts/Test-Database.ps1
pwsh -NoProfile -File scripts/Verify-Planning.ps1
```

8 test cases hiện tại: đối chiếu source/metadata/model, query đủ bảng và roundtrip; unique email kể cả NULL/FK/no cascade; quyền runtime; nâng cấp baseline đã có dữ liệu và mở rộng cột nullable; rerun; migration lỗi rollback schema/data/journal; runner đồng thời; từ chối database không có journal và 3 tên đích sai. Một số kiểm tra nằm cùng integration test. Không dùng InMemory provider thay SQL.

Test cần admin local để tạo database riêng `EbayGroup5_Test_<GUID>` và login riêng `EbayGroup5_TestApp_<GUID>`. Không tác động database EbayGroup5, không tự DROP database/login; giữ chúng để chẩn đoán. Mỗi lần chạy tạo thêm DB test, nên chỉ chạy lại khi thay đổi/failure cần xác minh. Dọn sau bằng quy trình riêng với danh sách đích đã kiểm tra; không dùng wildcard/prune. Log gần nhất có thể lưu trong `artifacts/`, không commit log thô.

Tests lấy baseline từ source độc lập, so sánh column/type/length/precision/scale/null/identity/collation, PK/unique/index và FK action/trust; bỏ tên constraint tự sinh. Baseline là subset để cho phép mở rộng. Khi thêm version cần cập nhật kỳ vọng số journal scripts và kiểm thử dữ liệu liên quan; không xóa assertion baseline.

## Giới hạn hiện tại

Chưa seed demo (A06), auth (B01), trạng thái enum hoặc constraint nghiệp vụ bổ sung. Cột legacy nullable/giới hạn decimal/datetime vẫn được giữ; business validation không được ép đổi schema gốc. Không có EF migrations hoặc reset database. A07 mới đưa checks vào CI; backup/HA/zero downtime chưa được nghiệm thu.
