# Chỉ mục minh chứng

| ID | Task/requirement | Bằng chứng | Giới hạn |
|---|---|---|---|
| EV-A00-01 | A00, CON-03 | Git local: `git log --oneline --grep=A00` | Chưa có remote/CI |
| EV-A01-01 | A01, CON-04, CON-05 | `pwsh -NoProfile -File scripts/Verify-Planning.ps1`; [kết quả checkpoint](A00-A01.md) | Chỉ kiểm tra tài liệu và SQL nguồn, không test ứng dụng |

Minh chứng khảo sát A02: [EV-A02-01](A02.md). Chỉ hoàn tất bản đồ tham khảo; UI-01 vẫn Pending.

Minh chứng scaffold A03: [EV-A03-01](A03.md). Restore/build và HTTP smoke pass; Docker/DB/nghiệp vụ còn Pending.

Mọi chức năng và OPS còn Pending. Khi thêm minh chứng: ghi requirement ID, commit, môi trường, lệnh, pass/fail, artifact và giới hạn. Không đưa log tải lớn hay secret vào repo.
