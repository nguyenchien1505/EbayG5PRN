# Chỉ mục minh chứng

| ID | Task/requirement | Bằng chứng | Giới hạn |
|---|---|---|---|
| EV-A00-01 | A00, CON-03 | Git local: `git log --oneline --grep=A00` | Chưa có remote/CI |
| EV-A01-01 | A01, CON-04, CON-05 | `pwsh -NoProfile -File scripts/Verify-Planning.ps1`; [kết quả checkpoint](A00-A01.md) | Chỉ kiểm tra tài liệu và SQL nguồn, không test ứng dụng |

Minh chứng khảo sát A02: [EV-A02-01](A02.md). Chỉ hoàn tất bản đồ tham khảo; UI-01 vẫn Pending.

Minh chứng scaffold A03: [EV-A03-01](A03.md). Restore/build và HTTP smoke pass; Docker/DB/nghiệp vụ còn Pending.

Minh chứng infrastructure A04: [EV-A04-01](A04.md). SQL/Redis healthy, auth và volume persistence pass; schema ứng dụng/nghiệp vụ còn Pending.

Minh chứng database A05: [EV-A05-01](A05.md). Baseline/EF/DbUp và 8 test SQL pass; CON-01 xác minh tại A05, CON-02 Done. Chức năng nghiệp vụ/OPS còn Pending.

Mọi chức năng và OPS còn Pending. Khi thêm minh chứng: ghi requirement ID, commit, môi trường, lệnh, pass/fail, artifact và giới hạn. Không đưa log tải lớn hay secret vào repo.

Minh chứng seed A06: [EV-A06-01](A06.md). 12 SQL cases pass, seed local created/rerun skipped; auth và SUP vẫn Pending.

Minh chứng CI A07: [EV-A07-01](A07.md). Local entry point/12 SQL tests/actionlint và GitHub Actions Ubuntu run pass, TRX đã upload; CD vẫn Pending.

Auth B01: [EV-B01-01](B01.md), 18 SQL/HTTP tests và browser journey pass, S1/L1 UI hoàn tất; SUP/UI toàn dự án Pending.

Review D01: [EV-D01-01](D01.md), 157 .NET +14 Node pass, review gắn OrderItem service/endpoint/SQL constraints/migration; G5-03 Pending tới UI D02.

Review UI D02: [EV-D02-01](D02.md), 161 .NET +14 Node, browser desktop/320px; G5-03 Done, G5-04 Pending D05.

Seller feedback D03: [EV-D03-01](D03.md), 177 .NET +14 Node, browser desktop/320px; G5-05 Pending D04.

Reputation page D04: [EV-D04-01](D04.md), 183 .NET +14 Node, browser desktop/320px; G5-05 Done.

Moderation D05: [EV-D05-01](D05.md), 190 .NET +14 Node, browser; G5-04 Done, SUP-05 phần review/feedback (dispute E05).
