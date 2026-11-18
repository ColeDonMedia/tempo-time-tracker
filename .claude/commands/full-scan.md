---
description: Run the full verification sweep — self-test suite, Release build with warnings as errors, single-file publish — before shipping.
---

Run the full pre-ship verification sweep:

    powershell -File scripts/test.ps1 -Full

It does, in order:
1. Debug build + full self-test suite (SelfTest.cs, including the translation-table checks).
2. Release build with warnings as errors (this is our typecheck + lint).
3. Single-file publish of Tempo.exe.

Report each step's real output. Ship only when ALL steps are green. If anything fails, fix it and re-run the full sweep.
