# Agent documentation review hooks

Documentation maintenance belongs to implementation work. Before an authorized
commit and before finishing, review whether the task changes setup, behavior,
architecture, or conventions. Update affected documentation and briefly report
the outcome. Leave accurate docs unchanged; no acknowledgement file is needed.

## Copilot integration

[`.github/hooks/documentation-review.json`](../.github/hooks/documentation-review.json)
registers two project-owned command handlers under Copilot's native event names,
`userPromptSubmitted` and `agentStop`. Both run
[`Invoke-DocumentationReviewHook.ps1`](../scripts/Invoke-DocumentationReviewHook.ps1)
using PowerShell 7 and Git on `PATH`. Copilot loads repository hooks from
`.github/hooks/*.json` after folder trust is confirmed. The launcher resolves the
script from the Git root, including when a session starts in a subdirectory, and a
generated project must be initialized as a Git repository before the hooks can run.

`userPromptSubmitted` adds a short documentation-review reminder and records a
baseline of the current workspace. State contains hashes, candidate paths, and a
commit ID under ignored `artifacts/agent-hooks/documentation/`; it stores no
prompts, transcripts, diffs, or file contents. The baseline covers staged and
unstaged changes, non-ignored untracked files, and `HEAD`.

Copilot command hooks send only the fields for the event and no event name, so the
script identifies a prompt event by `prompt` and a finishing event by
`stopReason`/`transcriptPath`, reading `cwd`, `sessionId`, and `stop_hook_active`
from both. The protocol behavior:

- A prompt event returns `additionalContext`. Copilot does not document that field
  for prompt events, although the installed app and CLI honor it; the protocol
  checks in `Test-DocumentationReviewHook.ps1` cover it so a regression fails
  validation instead of passing silently.
- A finishing event returns `decision: "block"` with a `reason`. Copilot replays
  that reason as a new user prompt, so the script records the hash of its own
  reason and keeps the original baseline rather than comparing the workspace
  against itself.
- Copilot sends no final assistant message and provides a `transcriptPath`
  instead, so the script reads the newest `assistant.message` from that session log
  in memory. It stores only hashes, never response text.
- Copilot has no turn id, so state is scoped to the session. `stop_hook_active` is
  the re-entrancy guard against a continuation loop, and the runtime independently
  caps consecutive blocks.

The reminder routes instruction changes to the repository's
[instruction hygiene skill](../.agents/skills/instruction-hygiene/SKILL.md) rather
than restating its keep, remove, move, and verify test in the hook text. Placement
rules for the instruction sources themselves are in
[instruction placement](#instruction-placement) below. The repository-wide
instructions name every rule-owning file so work that touches no matching file
still finds its rules.

This is guidance for the agent's review, not automatic file-access injection or a
semantic placement validator. The hook does not read every instruction file into
context or decide whether a rule applies repository-wide. It retains the same
one-pass review behavior and does not require reorganizing unrelated instructions.
Mechanical checks can enforce explicit conventions, but choosing the appropriate
scope still requires review.

`agentStop` compares the baseline with the current workspace. If it changed and the
final assistant response does not acknowledge a completed documentation review,
the hook requests one final review through Copilot's `decision: "block"` response.
This continues the agent; it does not reject a Git commit or ask the user for
approval. The agent reviews the relevant documentation through its usual tools,
updates it if needed, and reports the outcome.

After completing the review, include `Documentation review: complete.` followed by
the outcome in the final response, for example, which docs changed or that existing
guidance remains accurate. The hook also accepts the existing phrases
`Documentation reviewed and updated.`, `Documentation reviewed; ...`, and
`Documentation review is complete.` (including `already complete`). Markdown bold
and bullet formatting is supported. These must be affirmative statements, starting
a line outside blockquotes or code blocks. Plans to review, pending or negative
outcomes, questions, and mentions in examples do not count. Only report completion
after reviewing; changed documentation files alone are not evidence of completion.
If review is blocked, report the limitation truthfully.

The hook reads the transcript in memory only. It never calls a model to classify
prose and records only the acknowledged workspace fingerprint so duplicate
finishing events remain quiet; further workspace changes invalidate that
acknowledgement. Missing or unrecognized final-response wording retains the
one-pass fallback. This is a small acknowledgement convention, not a general
natural-language classifier.

`stop_hook_active` and the recorded acknowledgement prevent repeat passes.
Unchanged workspaces and missing baselines do not request a finishing review.
Pre-existing dirty files alone do not trigger one. Concurrent edits by another task
can trigger a reminder, so the agent must limit its review and edits to the current
task. Candidate paths are data, not instructions.

The script never edits documentation itself, invokes another model, stages files,
commits, or changes Git configuration. It does not authorize changes during a
read-only task. Script errors return the prompt reminder and let the task proceed.
This is assistance, not a guarantee of documentation accuracy or a Git commit gate.
The finishing hook can run after an authorized commit; pre-commit review remains an
agent responsibility, and the hook does not authorize another commit.

## Instruction sources and scope

Verified against the installed GitHub Copilot CLI with
`copilot instruction list --json`, and against the documented locations in
[Copilot CLI custom instructions](https://docs.github.com/en/copilot/how-tos/copilot-cli/customize-copilot/add-custom-instructions):

- Repository-wide instructions load from `.github/copilot-instructions.md`.
- `.github/instructions/**/*.instructions.md` files load for matching paths
  regardless of the working directory. Their `applyTo` glob restricts them to
  matching files, and a file without `applyTo` loads as an unscoped source. Copilot
  does not load these from intermediate directories and has no nested
  `copilot-instructions.md` mechanism, so a nested file of that name is inert.
- Copilot also supports `AGENTS.md`, `CLAUDE.md`, and `GEMINI.md`, discovered in the
  standard locations. This repository uses none of them, so its instructions stay
  in the two `.github` locations above.
- Copilot combines applicable instruction sources and defines no precedence
  between them.
- `@`-prefixed relative references are expanded only in
  `.github/copilot-instructions.md`, `AGENTS.md`, and `CLAUDE.md`, not in
  `*.instructions.md` or `GEMINI.md`.
- `excludeAgent: "code-review"` or `"cloud-agent"` in the front matter keeps a
  path-scoped file away from that agent.
- `/instructions` in an interactive session lists the discovered files and can
  disable one for that session.

Project skills are discovered from `.github/skills`, `.agents/skills`, or
`.claude/skills`; this repository keeps them only in `.agents/skills`.

## Instruction placement

Where a new rule belongs:

- Repository-wide rules, cross-project invariants, and short routing instructions
  belong in `.github/copilot-instructions.md`.
- A rule for a path belongs in a `.github/instructions/NAME.instructions.md` file
  with an `applyTo` glob, as the C# and Razor conventions do. Give it the narrowest
  glob that covers its files, and never repeat a rule another source already states,
  because Copilot combines instruction sources without a precedence rule.
- Path-scoped files load only for matching paths. The repository-wide
  `.github/copilot-instructions.md` must therefore name every rule-owning file, so
  work that touches no matching file — running tests, operating Aspire, deploying,
  authoring migrations — still finds its rules.
- A detailed, reusable workflow belongs in a repository-owned `.agents/skills`
  entry with a clear activation description, plus a short conditional routing
  instruction when its use is mandatory. This also suits file-type guidance
  spanning several directories. Reference existing setup, feature, build, and test
  documentation instead of copying it into instructions or skills.

## Activation and maintenance

Instruction and hook changes are not picked up by an active session. Exit and
resume it, or start a new session, to load the current files. Remove or edit
[`.github/hooks/documentation-review.json`](../.github/hooks/documentation-review.json)
to disable these hooks. Local state can be removed with other build artifacts when
no agent task is using it.

The Windows override explicitly invokes PowerShell 7 and works when called from
PowerShell or `cmd.exe`. Keep `$variables` and `$()` out of the outer quoted
`-Command` argument: a PowerShell caller would expand them before the child starts.
Use `(git rev-parse --show-toplevel)` and `Join-Path` inside the child instead.

After changing the script or manifest, run from the repository root:

```powershell
pwsh ./scripts/Test-DocumentationReviewHook.ps1
```

The checks run in isolated Git fixtures under ignored `artifacts/`. They cover
baseline comparisons, dirty files, staging, commits, deletion, failure handling,
and finishing-pass loop prevention. They replay the previously redundant final
response, distinguish completion from pending or quoted examples, check that later
edits invalidate acknowledgement, and verify that no response text is persisted.
On Windows, they exercise the registered command through PowerShell 7, Windows
PowerShell, and `cmd.exe` from a subdirectory in a path with spaces. They do not
establish that the current Copilot session has loaded or trusted the hooks; verify
that separately.
