# Handoff — F07: CD GitHub Actions vào kind local

08/10/2026. Repo D:/WINDOWS/PRN3/EbayGroup5.
Baseline main `4cefffb`; branch `task/F07-cd-local`; checkpoint `git log --oneline --grep=F07`.
Runtime cuối **f0b65b0** đã FF main sau CI success; handoff trước [F06](F06.md).

## Hoàn tất

- User chọn runner Windows tạm thời trên máy này, một job, không service (Q-ENV-002).
- [CD contract](../architecture/CD.md), [runbook](../runbooks/CD.md), [evidence](../evidence/F07.md).
- `.github/workflows/cd.yml`: chỉ workflow_dispatch owner/main; CI success đúng SHA;
  build hosted Ubuntu → archive web/migrate full SHA/OCI revision → deploy Windows label riêng.
- `Build-CdBundle.ps1`, `Get-CdArchiveIdentities.ps1`, `Invoke-CdDeployment.ps1`,
  `Start-CdRunner.ps1`, `cd-smoke.mjs`; CI có artifact/archive rejection tests.
- Deploy-K8s: image preloaded, start node đã dừng, migration trước web/nginx;
  stamp loaded image ID vào Pod template để rebuild cùng SHA vẫn thay pod.
- State/TLS ở secrets local ngoài checkout Actions; không đổi app/schema/migration.

## Verification

- Local Test-CI: **263/263 .NET +20/20 Node**, build 0 warning; artifact/archive cases pass.
- Local deploy artifact thật + TLS smoke; probe khác image ID/cùng SHA thay cả hai pod pass.
- **[CI37668281294](https://github.com/Twiash/EbayGroup5/actions/runs/37668281294) success** trên f0b65b0:
  263 .NET +20 Node +artifact/archive, compose journeys/smoke, load Short, Test-K8s pass.
- **[CD37669952216](https://github.com/Twiash/EbayGroup5/actions/runs/37669952216) success**:
  hosted build/import đúng SHA, checksum/digest/revision/Linux amd64,
  migration Complete (0 mới), 2 web Ready, HTTPS CA/hostname/headers/catalog + HTTP308 pass.
- Runner ID22 deregistered=true, 0 runner trên GitHub, không Listener/Worker còn chạy,
  không service. Credential config của ID21/22 được xóa; state/log runner không upload.
- Lỗi thật đã sửa: Docker classic `.Id` = config digest, containerd `.Id` = manifest digest;
  ánh xạ cả hai theo exact tag trong archive đã kiểm checksum. CD đầu bị chặn trước deploy,
  không đổi backend Docker hoặc bỏ identity check. Xem evidence.

## Requirement / Jira

- **F07 Done, OPS-03 Done**; OPS-06/PERF-01 còn F08.
- **KAN-26 / TV5 Hải Lê: F01+F02+F06+F07 hoàn tất local**; chưa sync Jira live.
- Không tự tạo issue/worklog/review. Chỉ CI các checkpoint code cần kiểm chứng;
  push `[skip ci]` tránh CI trùng, CD chỉ thủ công.

## Runtime / bước tiếp theo

- Cluster người dùng `ebaygroup5` đang **Ready**, https://localhost:9443;
  web image `ebaygroup5-web:f0b65b020cdf09b2d9d55c44265e2773d5178794` (2 replica).
- Secrets/PVC giữ nguyên. `Start-K8s.ps1 -Stop` giữ dữ liệu; không Delete/reset/prune.
- F08: đọc F05/LOAD, F07/CD, K8S; rollout/rollback dưới tải, HTTP liên tục và chat phục hồi.
- Khi deploy lại từ main mới: CI success trên đúng SHA trước `Start-CdRunner.ps1`;
  docs-only checkpoint có `[skip ci]` cần dispatch CI nếu muốn deploy chính checkpoint đó.
- File `src/EbayGroup5.Web/Properties/launchSettings.json` của user vẫn untracked, không sửa.
- Backup lịch sử trước xoá co-author: D:/WINDOWS/PRN3/EbayGroup5-backups/20261008-remove-claude.
