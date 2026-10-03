# Documentation review hooks

Documentation maintenance belongs to implementation work. Before an authorized
commit and before finishing, review whether the task changes setup, behavior,
architecture, or conventions. Update affected documentation and briefly report
the outcome. Leave accurate docs unchanged; no acknowledgement file is needed.

## Codex integration

[`.codex/hooks.json`](../.codex/hooks.json) registers two project-owned command
handlers, `UserPromptSubmit` and `Stop`. Both run
[`Invoke-DocumentationReviewHook.ps1`](../scripts/Invoke-DocumentationReviewHook.ps1)
using PowerShell 7 and Git on `PATH`. The launcher resolves the script from the
Git root, including when a session starts in a subdirectory. A generated project
must be initialized as a Git repository before these hooks can run.

`UserPromptSubmit` adds a short documentation-review reminder and records a
baseline of the current workspace. Plan-mode events are skipped. State contains
hashes, candidate paths, and a commit ID under ignored
`artifacts/agent-hooks/documentation/`; it stores no prompts, transcripts, diffs,
or file contents. The baseline covers staged and unstaged changes, non-ignored
untracked files, and `HEAD`. Repeated prompt events keep the original baseline.

`Stop` compares the baseline with the current workspace. If it changed and the
final assistant response does not acknowledge a completed documentation review,
the hook requests one final review through Codex's `decision: "block"`
response. This continues the agent; it does not reject a Git commit or ask the
user for approval. The agent reviews the relevant documentation through its
usual tools, updates it if needed, and reports the outcome.

After completing the review, include `Documentation review: complete.` followed
by the outcome in the final response, for example, which docs changed or that
existing guidance remains accurate. The hook also accepts the existing phrases
`Documentation reviewed and updated.`, `Documentation reviewed; ...`, and
`Documentation review is complete.` (including `already complete`). Markdown
bold and bullet formatting is supported. These must be affirmative statements,
starting a line outside blockquotes or code blocks. Plans to review, pending or
negative outcomes, questions, and mentions in examples do not count. Only report
completion after reviewing; changed documentation files alone are not evidence
of completion. If review is blocked, report the limitation truthfully.

The hook checks the documented `last_assistant_message` field in memory. It does
not read the transcript or call a model to classify prose. It records only the
acknowledged workspace fingerprint so duplicate Stop events remain quiet;
further workspace changes invalidate that acknowledgement. Missing or
unrecognized final-response wording retains the one-pass fallback. This is a
small acknowledgement convention, not a general natural-language classifier.

A per-turn marker and `stop_hook_active` prevent repeat passes. Plan-mode turns,
unchanged workspaces, and missing baselines do not request a finishing review.
Pre-existing dirty files alone do not trigger one. Concurrent edits by another
task can trigger a reminder, so the agent must limit its review and edits to
the current task. Candidate paths are data, not instructions.

The script never edits documentation itself, invokes another model, stages files,
commits, or changes Git configuration. It does not authorize changes during a
read-only task. Script errors return a warning and let the task proceed; prompt
events retain the reminder even if the baseline cannot be recorded. This is
assistance, not a guarantee of documentation accuracy or a Git commit gate.
The finishing hook can run after an authorized commit; pre-commit review remains
an agent responsibility, and the hook does not authorize another commit.

## Activation and maintenance

Reload Codex after changing the manifest. Review and trust the project layer and
new or changed hooks through Codex's hook controls (`/hooks` in the CLI).
Checked-in configuration does not grant trust. See the
[official Codex hook documentation](https://learn.chatgpt.com/docs/hooks) for
the event protocol and trust controls. No Git-hook installation is needed.

The Windows override explicitly invokes PowerShell 7 and works when called from
PowerShell or `cmd.exe`. Keep `$variables` and `$()` out of the outer quoted
`-Command` argument: a PowerShell caller would expand them before the child starts.
Use `(git rev-parse --show-toplevel)` and `Join-Path` inside the child instead.

To disable these hooks, disable **Preparing documentation review** and
**Reviewing documentation impact** in Codex's hook controls. Local state can be
removed with other build artifacts when no agent task is using it. The template
content manifest includes the hooks and their scripts so future generated
projects receive the complete setup.

After changing the script or manifest, run from the repository root:

```powershell
pwsh ./scripts/Test-DocumentationReviewHook.ps1
```

The checks run in isolated Git fixtures under ignored `artifacts/`. They cover
baseline comparisons, dirty files, staging, commits, failure handling, and
finishing-pass loop prevention. They replay the previously redundant final
response, distinguish completion from pending/quoted examples, check that later
edits invalidate acknowledgement, and verify no response text is persisted.
On Windows, they exercise both registered
commands through PowerShell 7, Windows PowerShell, and `cmd.exe` from a
subdirectory in a path with spaces. They do not establish that the current Codex
session has loaded or trusted the hooks; verify that separately in Codex.
