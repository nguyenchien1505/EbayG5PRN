# Kiến trúc

Các quyết định gốc ở [ADR-001](../decisions/ADR-001-foundation.md). Chưa có source tree runtime ở checkpoint A00–A01.

- A03 tạo solution modular monolith, các lớp Core/Application, Infrastructure, Web và test tương ứng; ghi SOURCE_MAP thực tế sau khi tạo.
- A05 ghi mapping đầy đủ 17 bảng và chiến lược migration/baseline.
- F01–F02 ghi sơ đồ Nginx, web replicas, SQL, Redis và trust boundary.
- F06–F08 ghi cluster local, probes, migration Job và rollout/rollback.

Tài liệu kiến trúc phải phản ánh code đã tồn tại; phần dự kiến đánh dấu rõ. Không tạo cây thư mục/symbol giả rồi dẫn agent tìm theo.
