# Instruction sources and their scope

Where agent instructions commonly live, what each location means for scope, and
how to confirm what a specific tool actually loads. Verify against the installed
tool: discovery rules and file names change between versions, and a file that
looks authoritative on disk may not be loaded at all.

## Discovery

Prefer the tool over the documentation when the tool can answer. Some agents
expose a command that lists the instruction sources they load; when yours does,
prefer it to the documentation (GitHub Copilot CLI, for example, reports them with
`copilot instruction list --json`). Treat that output as the record of what the
current session loads, and run it from the directory you are auditing: it is
working-directory sensitive, so it does not enumerate nested `AGENTS.md`-style
files that are not on the path from the repository root to the working directory.
That is a limit of the listing, not of what loads: a nested file the agent reaches
while working on a path still applies. Path-scoped instruction files with an
`applyTo` glob are always listed, and skill files are omitted entirely.

For the files themselves, this `git ls-files` command covers tracked and untracked
files in one pass while excluding ignored ones, producing the on-disk listing from
any working directory for the candidate locations listed below. The `:(top)` prefix
is what makes the patterns repository-relative rather than working-directory-relative;
without it, running the command inside a subtree silently lists that subtree alone
and exits successfully.

Double-quote the patterns, because `cmd.exe` does not treat single quotes as
quoting: a single-quoted pattern reaches git literally and either fails with an
`outside repository` pathspec error or silently matches nothing. PowerShell and a
POSIX shell accept either quoting.

```
git ls-files -co --exclude-standard -- ":(top)*AGENTS.md" ":(top)*CLAUDE.md" ":(top)*GEMINI.md" ":(top)*instructions.md" ":(top)*SKILL.md" ":(top).cursorrules" ":(top).windsurfrules" ":(top)*.cursor/rules/**" ":(top)*.windsurf/rules/**" ":(top)*.github/agents/**" ":(top)*.claude/agents/**"
```

Reconcile the two: the file listing is the record of what exists, and the tool's
listing is that tool's record of what loads. Treat absence from a listing as
evidence only when the source's location rules say that tool should have listed it;
otherwise judge the candidate by its location rules and never assume it is inert.
Enumerate skills with `copilot skill list --json`: the instruction listing omits
skills, and the skill listing is the only one that covers user-level, plugin, and
`copilot skill add` sources and reports load failures. `--json` lists only the
skills that loaded; a rejected `SKILL.md` and its reason go to standard error with
exit code 0, so read stderr as well. Sources are deduplicated by name, and the
higher-precedence copy wins: a repository skill hides a same-named user-level or
plugin skill, and the hidden one then disappears from the listing with no report.
Reconcile names as well as paths, or an audit will miss the copy that actually
loads.

A plain file search is the fallback when the checkout is not a git repository, or
when the tool can list its sources but not their paths.

## Common locations

| Location | Kind | Typical scope |
| --- | --- | --- |
| `AGENTS.md` (repository root) | Portable, agent-neutral | Repository-wide; the usual canonical home |
| `AGENTS.md` (nested in a subtree) | Portable, agent-neutral | The subtree: the working directory, the directories between it and the repository root, and the directories of files the agent is working on; a tool's static listing may show only the first two |
| `.github/copilot-instructions.md` | GitHub Copilot | Repository-wide |
| `.github/instructions/**/*.instructions.md` | GitHub Copilot | Files matched by the front-matter `applyTo` glob; loaded independently of the working directory |
| `CLAUDE.md` | Claude Code | Root and nested files; commonly left as an import of the canonical file |
| `GEMINI.md` | Gemini CLI | Tool-specific equivalent of the canonical file |
| `.cursor/rules/**` (and legacy `.cursorrules`) | Cursor | Rules carry their own globs; some are always applied |
| `.windsurfrules`, `.windsurf/rules/**` | Windsurf | Tool-specific, glob-scoped |
| `.github/skills`, `.agents/skills`, `.claude/skills` | Project skills | Loaded on activation, not on every request |
| `.github/agents/**`, `.claude/agents/**` | Custom agent definitions | Rule-bearing bodies that load when that agent is selected, not on every request |
| `~/.copilot/skills`, `~/.agents/skills`, directories added with `copilot skill add` | User-level skills | Loaded for that user in every repository; a skill whose name matches one in a higher-precedence source is deduplicated to a single entry |

An instruction file with no scoping metadata is still loaded, just unscoped. Do
not assume a file is inert because its glob looks narrow; confirm with the
tool's listing command.

This table covers repository sources. A user's own instruction files and settings
also load in every repository, from outside the file listing, and can contradict
the repository; check what the tool loads with that user configuration in place
rather than assuming the repository is the only input.

## Scope semantics that decide placement

- **Combination without precedence.** Several tools load repository-wide and
  path-scoped sources together and define no precedence between them, so a rule
  that appears in two sources has no tie-breaker. `SKILL.md` step 5 owns the
  disposition.
- **Discovery by path, not by relevance.** A path-scoped source applies because a
  glob matched, not because the rule is relevant to the task. Keep it narrow so a
  rule about one subsystem does not consume attention elsewhere.
- **Nested files are not portable across tools.** Some agents load nested
  instruction files hierarchically and others read only the file they find; a rule
  placed in a nested file can silently stop applying when the reader changes.
  Linked documents and skills are more portable, at the cost of one hop.
- **Skills are intent-triggered.** A skill loads when its description matches the
  request, which suits multi-step procedures with decisions. It is the wrong home
  for a rule that must apply to every request; a skill that never activates is
  invisible guidance.
- **Tool-specific files drift.** A `CLAUDE.md`, `.cursorrules`, or equivalent
  copy is the most common source of contradictory guidance. Reduce it to an import
  or a one-line pointer at the canonical file, and confirm the pointer mechanism
  the tool actually supports.

## Placement checklist

- Does the rule load for exactly the paths where it is true, and does no other
  loaded source already state it? If another source states it, delete this copy
  rather than rewording it.
- Would a reader following a pointer ever be blocked by the hop? If the content is
  needed mid-task and stays stable, inline the decision and link the detail.
- Does the moved rule leave a dangling reference or a summary behind that now
  duplicates it?
- After the move, does the tool's listing command still show the rule for the
  intended paths?
