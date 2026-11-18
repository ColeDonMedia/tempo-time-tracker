# Project
Tempo: Windows time tracker + invoicing. .NET 8 WPF (code-built UI, no XAML pages, no NuGet packages), JSON storage, own PDF writer and QR encoder. MIT, GitHub: ColeyD12345/tempo-time-tracker.

# Commands
- Test (build + self-test): powershell -File scripts/test.ps1
- Full pre-ship sweep: powershell -File scripts/test.ps1 -Full   (Release build with warnings as errors + single-file publish)
- Run with sample data: set TEMPO_DATA_DIR to a scratch folder, then Tempo.exe --demo
- Screenshots: Tempo.exe --shot <dir> --theme Dark|Light --lang en  (needs TEMPO_DATA_DIR)
- Sample invoice: Tempo.exe --sample-pdf out.pdf [--template Classic|Minimal|Bold] [--payments] [--quote]
- The test suite is SelfTest.cs (PASS/FAIL lines). Every new rule goes there, written UI-free.

# Workflow: STRICT TDD, always in this order
1. RED: write a failing self-test first (stub the method so it compiles and returns a wrong value). Run it and confirm it FAILS.
2. GREEN: write the minimum code to make it pass.
3. REFACTOR: clean up without changing behaviour, then re-run.
- Never write logic without a failing test first. UI-only code (layout, styling) is the exception: verify it with --shot screenshots.
- Never delete or weaken a test to make it pass.
- After every feature, run the FULL suite.
- Before shipping, run the full sweep (scripts/test.ps1 -Full). It must be all green.

# Code rules
- UI text: English keys, wrapped in Loc.T / Loc.F, with a 9-language row in Strings.*.cs (en|es|fr|de|pt|it|ja|zh|ko). The self-test checks the tables.
- Store times in UTC and display them in Settings.TimeZone. Write the data file atomically (State.Save).
- Deletes go through TrashBin with an undo toast, never a hard delete.
- Dev-only flags only work when TEMPO_DATA_DIR is set.

# Reviews (fresh subagents, read-only)
- After each feature, ui-reviewer checks the UI/UX against the --shot screenshots.
- Before shipping, security-reviewer does a hardening review (file paths, imports, registry, URLs and the update check).
- Never accept "done". Accept a passing test run, a diff or a log.

# Safety
- Never publish screenshots of the Settings page (they show local paths).
- Never kill processes by name. Stop only your own PID or the exe in this build folder.
- Commit as ColeyD12345 (noreply email) with the Co-Authored-By trailer. Push only when asked.

# Context hygiene
- /clear between unrelated tasks. /compact when a session gets long.
- Push noisy research into the explorer subagent and keep the main context clean.
