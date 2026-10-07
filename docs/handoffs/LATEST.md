# Handoff — F07: CD GitHub Actions vào kind local (đang kiểm thử)

08/10/2026. Baseline main `4cefffb`; branch `task/F07-cd-local`.
Handoff trước: [F06](F06.md); runtime SHA trong lịch sử cũ dùng ánh xạ backup ngoài repo.

## Quyết định

- Q-ENV-002: người dùng chọn runner tạm thời trên máy hiện tại, một job CD, không service.
- Contract [CD](../architecture/CD.md), runbook [CD](../runbooks/CD.md).
- Chỉ workflow_dispatch owner/main; CI success trên đúng SHA trước build hosted.
- Web/migrate image full SHA + OCI revision, Docker archive có SHA-256/image ID;
  runner Windows nhận artifact cùng run, xác minh trước load và deploy bằng Deploy-K8s.
- State/TLS vẫn ở secrets local ngoài checkout Actions; không thay schema/app/dữ liệu.

## Đã làm

- Workflow CD; Build-CdBundle, Invoke-CdDeployment, Start-CdRunner và smoke Node chỉ đọc.
- Deploy-K8s hỗ trợ image có sẵn, start node đã dừng, migration trước cập nhật web/nginx.
- Artifact validation + 5 rejection cases pass; PowerShell/Node syntax, docs, YAML guards pass.
- Test-CI local pass 263/263 .NET +20/20 Node; build 0 warning; chưa có CI/CD thật trên runtime F07.
- Local prebuilt deploy + smoke pass; image ID stamp đảm bảo rebuild cùng SHA thay pod, probe thực tế pass.
- CI đầu/7cf2185 pass. CD đầu build pass nhưng import ID khác backend bị chặn;
  runner đã deregister. Digest mapping classic/containerd và test archive mới pass,
  deploy bằng artifact hosted thật trên local pass. Đang CI bản sửa và CD lại.

## Còn lại

- Kiểm tra diff và commit runtime [F07].
- Push branch với [skip ci], dispatch đúng một CI kiểm chứng; pass mới FF main.
- Start-CdRunner dispatch một CD thật, kiểm image/2 replica/migration/TLS và deregistration.
- Ghi [evidence F07](../evidence/F07.md), cập nhật acceptance và OPS-03 khi có run success.
- OPS-06/PERF-01 còn F08; không tự làm F08 hoặc sync Jira.

## Giữ nguyên

- File chưa theo dõi `src/EbayGroup5.Web/Properties/launchSettings.json` của người dùng.
- Git history backup: D:/WINDOWS/PRN3/EbayGroup5-backups/20261008-remove-claude.
