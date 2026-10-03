# Database contract — A05

02/10/2026, dependency A04 xác minh healthy tại c5c2064. DbUp là nguồn migration duy nhất; EF chỉ mapping/query, không EnsureCreated/Migrate/EnsureDeleted.

- Giữ nguyên bytes source SQL và SHA256 đã ghi. Runner nhúng source gốc, xác minh hash rồi chỉ bỏ hai batch CREATE DATABASE/USE CloneEbayDB để chạy nguyên 17 CREATE TABLE trong database đích EbayGroup5. Không sửa kiểu/cột/PK/FK/unique. Khác tên database là cấu hình môi trường, không sửa schema.
- Mọi cột trừ id đều nullable theo source; datetime giữ datetime, decimal giữ precision/scale, FK NO ACTION. Email UNIQUE vẫn cho tối đa một NULL trên SQL Server; không thêm filtered index hoặc ép email NOT NULL. Validation nghiệp vụ bổ sung ở task sau.
- Domain POCO tại Core/Entities, fluent configuration và EbayDbContext tại Infrastructure/Persistence; không navigation suy đoán quan hệ 1-1 khi source không unique FK.
- Runner console riêng EbayGroup5.Database dùng MigrationConnection; Web không tự migrate. DbUp journal dbo.SchemaVersions, migration theo thứ tự và transaction. SQL application lock ngăn hai runner chạy đồng thời; database có bảng nhưng chưa có journal bị từ chối, không tự nhận làm baseline.
- 0001 tạo baseline từ source; 0002 chỉ thêm role ebay_app và quyền CRUD trên đúng 17 bảng. DbUp journal không được cấp cho app. Login app riêng, không DDL/db_owner/SA; password lấy từ environment, không nhúng SQL versioned/log. Script local lưu .env.database ignored và không overwrite.
- Migration SQL tương lai phải append-only theo ý nghĩa nghiệp vụ, không sửa script đã áp dụng. Không tự adopt database kỳ trước; cần task đối chiếu metadata riêng trước khi nhập dữ liệu.
- Test SQL thật trên database mới tên EbayGroup5_Test_<random>; tạo reference bằng DDL gốc độc lập, so sánh metadata normalized (bỏ tên constraint SQL tự sinh), model EF và query đủ bảng. Kiểm tra rerun với dữ liệu, unique/FK, rollback migration lỗi và quyền runtime. Giữ DB test để kiểm tra, không tự DROP/reset volume.

A06 thêm migration 0003 tạo dbo.DemoSeedHistory, không cấp quyền app; seed chạy riêng theo [DEMO_SEED](DEMO_SEED.md). Test bảo toàn baseline cần chạy lại sau mỗi migration mở rộng; so sánh baseline là subset để cho phép cột/bảng/index thêm mới.

Nguồn API: [DbUp usage](https://dbup.readthedocs.io/en/latest/usage/), [transactions](https://dbup.readthedocs.io/en/latest/more-info/transactions/), [EF property mapping](https://learn.microsoft.com/en-us/ef/core/modeling/entity-properties), [delete behavior](https://learn.microsoft.com/en-us/ef/core/saving/cascade-delete).
