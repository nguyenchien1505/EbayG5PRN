# Chỉ mục minh chứng

| ID | Task/requirement | Bằng chứng | Giới hạn |
|---|---|---|---|
| EV-A00-01 | A00, CON-03 | Git local: `git log --oneline --grep=A00` | Chưa có remote/CI |
| EV-A01-01 | A01, CON-04, CON-05 | `pwsh -NoProfile -File scripts/Verify-Planning.ps1`; [kết quả checkpoint](A00-A01.md) | Chỉ kiểm tra tài liệu và SQL nguồn, không test ứng dụng |

Mọi chức năng và OPS còn Pending. Khi thêm minh chứng: ghi requirement ID, commit, môi trường, lệnh, pass/fail, artifact và giới hạn. Không đưa log tải lớn hay secret vào repo.
