---
description: Run one strict TDD cycle (red → green → refactor) for a single feature or fix.
---

Run ONE strict TDD cycle for: $ARGUMENTS

1. RED: add the check to SelfTest.cs first (stub the method under test so it compiles but returns a wrong value). Run `powershell -File scripts/test.ps1`. Confirm it FAILS before any implementation.
2. GREEN: write the minimum code to make it pass.
3. REFACTOR: clean up without changing behaviour. Re-run the suite.

Do not move on to the next feature until this cycle is green.
