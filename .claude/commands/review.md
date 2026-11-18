---
description: Fresh-context UI/UX review, then hardening review, of the current diff.
---

Review the current diff (`git diff` plus untracked files) with two fresh subagents, in order:
1. ui-reviewer — UI/UX, flows, accessibility, responsiveness. Give it fresh screenshots from
   `Tempo.exe --shot <dir>` (with TEMPO_DATA_DIR set and `--demo` data). Never the Settings page.
2. security-reviewer — hardening: file paths and imports (CSV, ICS, receipts), registry writes,
   URLs opened from data, the update check, anything written outside the data folder.

Both are read-only. Report each verdict (APPROVE or CHANGES REQUESTED) with findings. Do not ship on a CHANGES REQUESTED verdict.
