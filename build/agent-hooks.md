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
untracked files, and `HEAD`. Repeated Codex prompt events keep the original
baseline; each Copilot prompt event refreshes it for a new turn. See
[GitHub Copilot integration](#github-copilot-integration).

The reminder also directs instruction changes to the
[instruction scope policy](../AGENTS.md#instruction-scope): repository-wide rules
and short routing instructions belong at the root, subtree-specific rules belong
in applicable nested `AGENTS.md` files, and detailed reusable workflows belong in
repository-owned `.agents/skills`. The agent discovers and reads nested guidance
for the area it is working in and reviews changed instructions for scope,
discoverability, and duplication. Existing feature and workflow documentation
remains the source for those details; instructions should reference it.
The root [task routing table](../AGENTS.md#read-the-guidance-for-the-task) also
requires relevant guides when a change affects their contracts from another
directory, or an operation such as running tests touches no files in that scope.

This is guidance for the agent's review, not automatic file-access injection or
a semantic placement validator. The hook does not read every nested instruction
file into context or decide whether a rule applies repository-wide. It retains
the same one-pass review behavior and does not require reorganizing unrelated
instructions. Mechanical checks can enforce explicit conventions, but choosing
the appropriate scope still requires review.

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

The hook reads the final assistant response in memory only. Codex supplies the
documented `last_assistant_message`; Copilot supplies a transcript path, so the
shared script reads the newest assistant message from that session log. It never
calls a model to classify prose and records only the acknowledged workspace
fingerprint so duplicate finishing events remain quiet; further workspace changes
invalidate that acknowledgement. Missing or unrecognized final-response wording
retains the one-pass fallback. This is a small acknowledgement convention, not a
general natural-language classifier.

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

## GitHub Copilot integration

[`.github/hooks/documentation-review.json`](../.github/hooks/documentation-review.json)
registers the same script for the GitHub Copilot app and CLI under their native
event names, `userPromptSubmitted` and `agentStop`. Copilot loads repository hooks
from `.github/hooks/*.json` after folder trust is confirmed and does not read
`.codex/hooks.json`, so the Codex manifest is not reused.

[`Invoke-DocumentationReviewHook.ps1`](../scripts/Invoke-DocumentationReviewHook.ps1)
serves both protocols and identifies the caller from the payload. Codex sends
`hook_event_name` with `session_id` and `turn_id`; Copilot omits an event name and
sends only the fields for the event, using `cwd`, `sessionId`, and
`stop_hook_active`. The script therefore recognizes a prompt event by `prompt` and
a finishing event by `stopReason`/`transcriptPath`. The remaining differences:

- A prompt event returns `additionalContext` to Copilot and
  `hookSpecificOutput.additionalContext` to Codex. Copilot does not document the
  prompt-event `additionalContext`, although the installed app and CLI honor it;
  the protocol checks in `Test-DocumentationReviewHook.ps1` cover it so a
  regression fails validation instead of passing silently.
- A finishing event returns `decision: "block"` with a `reason` to both. Copilot
  replays that reason as a new user prompt, so the script records the hash of its
  own reason and keeps the original turn baseline rather than comparing the
  workspace against itself.
- Copilot sends no final assistant message and provides `transcriptPath` instead,
  so the script reads the newest `assistant.message` from that session log in
  memory. It still stores only hashes, never response text.
- Copilot has no turn id, so its state is scoped to the session.
  `stop_hook_active` remains the re-entrancy guard against a continuation loop,
  and the runtime independently caps consecutive blocks.

## Instruction discovery

Verified against the installed GitHub Copilot app and CLI with
`copilot instruction list --json`:

- Repository instructions load from `.github/copilot-instructions.md` and the root
  `AGENTS.md`.
- Nested `AGENTS.md` files load for the session working directory and the
  directories between it and the repository root, plus directories on the path of a
  file the agent is working on. A session rooted at the repository root receives
  the root file alone, so no sibling subtree is loaded automatically.
- `.github/instructions/**/*.instructions.md` files load independently of the
  working directory. Their `applyTo` glob restricts them to matching files, while a
  file without `applyTo` is still loaded as an unscoped source.
- Copilot combines applicable instruction sources and defines no precedence
  between them, so these files must not restate rules that live in an `AGENTS.md`.

Each `.github/instructions` file therefore only routes the reader to the owning
guide. Project skills are discovered from `.github/skills`, `.agents/skills`, or
`.claude/skills`; this repository keeps them only in `.agents/skills`.

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
