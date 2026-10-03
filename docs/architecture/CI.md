# CI contract — A07

03/10/2026; nền a5e4129. A03–A06 xác minh qua commits, global.json/lock files, scripts và 12 SQL tests. Git remote: [Twiash/EbayGroup5](https://github.com/Twiash/EbayGroup5), private, tạo theo yêu cầu người dùng 03/10/2026 bằng gh sau khi người dùng đăng nhập. Q-ENV-001 đã giải quyết.

- GitHub Actions push main/task branches, pull_request main và manual dispatch; Ubuntu 24.04 hosted runner, không self-hosted/pull_request_target/deploy. Token chỉ contents:read; checkout không giữ credential, actions pin commit SHA.
- SDK lấy global.json; locked restore, Release build, documentation/source integrity và SQL tests bằng cùng scripts/Test-CI.ps1. Không phụ thuộc script SDK Windows hoặc .env local ở CI.
- Mỗi lần chạy tạo SQL Developer container tên GUID, image cùng digest A04, random loopback port, password random trong process environment, không mount volume/repo. Chỉ dừng đúng container do lần chạy tạo, không DROP database/reset volume. Local giữ container đã dừng để chẩn đoán; hosted runner bị thu hồi sau job.
- Migration và seed tests dùng EbayGroup5_Test_GUID như A05/A06, không chạm EbayGroup5. Health wait có timeout và test chỉ chạy khi SQL ready. Không cần Redis vì chưa có test Redis nghiệp vụ.
- Manifest hash migration/seed đã phát hành, schema nguồn vẫn guard độc lập. Khi thêm version append manifest và update test kỳ vọng; không sửa hash cũ để hợp thức hóa sửa migration.
- Artifact chỉ TRX 7 ngày, không upload .env/docker logs/database. Test failures có thể chứa dữ liệu fixture nên chỉ dùng dữ liệu hư cấu. Không export credential vào GITHUB_ENV/artifact; restore process environment trong finally.
- Files: .github/workflows/ci.yml, scripts/Test-CI.ps1, scripts/Verify-Migrations.ps1 và manifest; docs/task/evidence/handoff. Không thay schema/runtime nghiệp vụ.

Nguồn: [workflow syntax](https://docs.github.com/en/actions/reference/workflows-and-actions/workflow-syntax), [setup-dotnet](https://github.com/actions/setup-dotnet), [checkout](https://github.com/actions/checkout). Tag v5.0.1 của hai action và upload-artifact v4.6.2 được đối chiếu SHA qua git ls-remote khi triển khai.
