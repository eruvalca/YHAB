---
applyTo: "src/YHAB/**"
---

Read `src/YHAB/AGENTS.md` before changing server, persistence, or Identity storage
code in this scope. It is the source of truth for feature boundaries, PostgreSQL
and migration rules, and readiness/liveness contracts. This file only routes you
there; do not add guidance here. Running migrations or the application also
requires `src/YHAB.AppHost/AGENTS.md`.
