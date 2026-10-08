# G02 — Báo cáo, demo và bàn giao

08/10/2026. Baseline `fe4a22b`, G01 runtime `409e05d`/CI37689369159 success.
User giao hoàn tất G02, **hoãn đồng bộ Jira thật**. OPS-04 vẫn Pending;
G02 có thể Done trong phạm vi đã duyệt, không tuyên bố Jira đã cập nhật.

## Deliverables

- `docs/delivery/REPORT.md`: báo cáo tiếng Việt, phạm vi nhóm 5, kiến trúc/schema,
  nghiệp vụ, bảo mật, hiệu năng, ảnh UI, truy vết, giới hạn và trạng thái thật.
- `docs/delivery/SETUP.md`: từ clone sạch tới Docker/K8s, prerequisites,
  migration/seed/auth/TLS và dừng an toàn; không cần hai thư mục code nguồn cũ.
- `docs/delivery/DEMO.md`: script cho 5 người, luồng mới không phụ thuộc hạn seed,
  dữ liệu riêng mỗi buổi, thời gian chờ, kết quả mong đợi, phương án khi lỗi.
- `docs/delivery/HANDOVER.md`: source map cập nhật, giải thích module cho từng người,
  kiểm tra cá nhân trước trình bày, quản lý secrets/backup và batch Jira để sau.
- README/START_HERE/task/evidence/matrix/handoff dẫn đến tài liệu bàn giao cuối.

## Demo

- Đề xuất Q-DEMO-001: giữ seed A06-v1 bất biến/idempotent, demo bằng **đơn mới**;
  option demo rút ngắn một ngày làm việc theo DisputeTiming hiện có.
- Không sửa ngày lịch sử hoặc seed SQL, không bỏ điều kiện owner/Delivered/thời hạn.
- Nếu chốt bật demo: bootstrap kind chỉ chạy chủ động, thêm credential riêng một lần
  vào secrets local; migration → seed → enable auth → web. Normal mode =0.
- User chốt 08/10/2026: đơn mới + clock rút ngắn, ngày nộp chưa xác định.
  `Start-K8s -Demo` mặc định30s/day, clock0 khi không có Demo; Pod template stamp
  clock để cấu hình mới có hiệu lực cả khi image tag không đổi.
- Không in password lên tool/log, không stage .env/secrets hoặc credential CSV.
  User tự mở file credential local khi trình bày. Không lấy credential người dùng làm test.

## Verification

- Build/test các thay đổi cấu hình, rerun provisioning giữ password cũ;
  test demo mới buyer/seller/admin qua browser trên stack cô lập (không can thiệp DB bằng SQL).
- Clone repo sạch vào thư mục test riêng: locked restore/build + Test-AppStack,
  TLS/WSS/journeys. Không copy .env/secrets/build output/code tham khảo từ checkout cũ.
- Repo kiểm docs, SQL checksum, migration manifest, secret scan; GitHub CI trên runtime SHA.
- Bằng chứng ghi lệnh/commit/container/CI/artifact và pass/fail thực, không tạo
  lịch sử con người đã trình bày/hiểu module hoặc Jira transitions chưa xảy ra.
- Ngày nộp chưa xác định ghi rõ; không chặn hoàn thành tài liệu kỹ thuật.
