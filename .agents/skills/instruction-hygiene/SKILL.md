---
name: instruction-hygiene
description: >-
  **WORKFLOW SKILL** - Audit, tune, restructure, consolidate, or create the agent
  instruction files a coding agent loads for a repository, applying keep, remove,
  move, and verify hygiene.
  USE FOR: reviewing or improving AGENTS.md, CLAUDE.md, GEMINI.md,
  .github/copilot-instructions.md, path-scoped instruction files, `SKILL.md`, or
  other tool rule files; deciding whether a new rule belongs in instructions and at
  which scope; creating instructions where none exist; removing bloat, vague or
  duplicated rules, and obsolete workarounds.
  DO NOT USE FOR: feature documentation, READMEs, or build and test guides that
  are not agent instructions; or general code or documentation cleanup unrelated
  to agent instruction files.
  INVOKES: repository search, `git log`, and the repository's own documented
  format, build, and test commands.
  FOR SINGLE OPERATIONS: apply the keep, remove, move, verify test to the rule in
  hand and stop; the full inventory is only for a real audit.
---

# Instruction hygiene

An instruction file is part of the context on every applicable request. Each line
competes for attention with the developer's task, the relevant code, tool output,
and the other instruction sources that also loaded. The goal is not brevity for its
own sake: every remaining line has to earn its place by changing what the agent
does.

The bar for every line: **if this line were deleted, would a capable agent get
something wrong?** If not, remove it or move it out of the always-loaded path.

Keep the facts only the team can supply:

- what the system is, and which boundaries are intentional;
- which local choices are deliberate, and which alternatives were already rejected;
- the authoritative commands that build, format, and validate a change;
- what must never break;
- where deeper, rarely needed truth can be retrieved.

Remove what a capable model can infer, the repository can reveal, or tooling
already enforces.

Do not measure quality by line count. A short file with a wrong command is worse
than a long one that records necessary boundaries.

## Modes

| Request | Mode |
| --- | --- |
| Review, audit, or tune the existing instructions | Audit |
| Add one rule, or a code change that touches an instruction file | Single rule |
| Instructions are missing and should exist | Create |
| Sources contradict each other, or a rule lives in several places | Consolidate |
| A materially more capable model, or a repository reorganization, build system, or toolchain change, landed | Re-review |

Single-rule mode runs the same keep, remove, move, verify test; it skips the full
inventory. Consolidate runs all seven steps, and step 6 owns its deletion
exception. Re-review restarts at step 3 for drift in facts, commands, and
environment; repeat steps 1 and 5 as well when the repository layout changed,
since the load set and the placement are then no longer reliable; and re-run
step 2 when the model changed, because the bar that decides **Keep** moves with
capability. Create mode has its own section after step 7. Treat every request that
edits instructions as an eligibility question first: does this rule belong in
instructions, and is this the right scope?

## 1. Inventory what actually loads

Find every instruction source the repository ships, not only the one the user
named. A rule that was moved but not deleted still loads and still competes.

1. List candidate files, including untracked ones so an uncommitted instruction
   file is not missed and excluding ignored paths so generated copies are not
   mistaken for shipped guidance. A single command covering both is in
   [instruction sources](references/instruction-sources.md).
2. Determine which sources load together for the paths in question. Discovery
   rules differ per tool and change between versions; see
   [instruction sources](references/instruction-sources.md) for common locations,
   the commands that report what a tool actually loads, and the scope semantics
   that decide placement.
3. Map effective scope: which rules apply repository-wide, which apply only to a
   subtree or file glob, and which are unreachable because nothing loads them.

## 2. Classify every rule

Read the current content in full; do not skim. Classify each section, paragraph,
and bullet as one of the following. **Fix** and **Add** appear only after steps 3
and 4.

| Verdict | Use it when |
| --- | --- |
| **Keep** | Still true, consequential, and hard to infer from the repository. |
| **Remove** | The model already handles it, a tool enforces it, it is vague, self-praising, or a stale workaround. |
| **Move** | Useful, but not worth its cost on every turn because it applies only to one subtree, file type, or workflow. |
| **Verify** | It asserts a command, path, version, dependency, or external reference that may have drifted. |
| **Fix** | Step 3 proved a surviving claim wrong; the verdict carries the corrected value. |
| **Add** | Step 4 found a fact only this team knows that is absent. |

Verdicts are decided against the codebase, not against taste. "Remove" applies to
generic advice and duplicates; "Keep" is confirmed by step 3, not assumed. Read
[the audit checklist](references/audit-checklist.md) when deciding section by
section, and
[anti-patterns](references/anti-patterns.md) to recognize a pattern quickly.
Mixed-signal files, where a load-bearing constraint is buried among nice-to-haves,
are usually a reordering fix rather than a deletion.

## 3. Verify surviving claims

Confirm what survived as **Keep**, and resolve every **Verify** item here; a claim
that turns out to be wrong becomes a **Fix** that carries the corrected value.

- **Commands** — do the build, format, test, and single-test selections exist as
  scripts or targets, and is the documented invocation still the shortest reliable
  one? Run them, cheapest first, and run every surviving command the environment
  permits; a command that cannot be run is corrected or removed rather than
  documented with a caveat.
- **Versions and dependencies** — compare against the manifests, lockfiles, and
  SDK or tool version declarations that own them. Document a constraint that
  changes how code is written, not a patch pin that drifts.
- **Paths and module names** — does the tree still match, and does a renamed module
  leave a rule pointing at nothing?
- **Referenced documents** — do they still exist, and are they still current? A
  pointer to a finished migration or fix plan is stale by definition.
- **Tool-enforced claims** — if a formatter, analyzer, linter, or test enforces a
  rule, replace the prose with the command that enforces it. Never weaken or
  bypass enforcement to make a file smaller.

Keep only commands that were validated. A wrong command stated with confidence is
worse than no command.

## 4. Find what is missing

The highest-value content is usually absent rather than wrong. Look for:

- **Recent gotchas** — scan recent `git log` and pull request titles. Bug fixes
  whose messages describe a non-obvious trap ("fixed X because Y assumed Z") are
  candidates for a stated constraint.
- **Repeated corrections** — what reviewers keep fixing is what an agent will get
  wrong. Ask the user what they find themselves correcting agents on repeatedly.
- **Forbidden patterns** — what must an agent *not* do? Negative rules are often
  under-documented and more valuable than positive ones.
- **Platform and environment surprises** — OS-specific shell behavior, container
  versus host paths, non-default flags, quoting, and required environment
  variables absent from the example file.
- **The definition of done** — the authoritative validation path, including cases
  where the obvious command is incomplete or wrong.
- **Ordering** — if a brand-new reader saw only the first screen, would they avoid
  the most expensive mistakes? Promote critical constraints and call them out
  visually instead of burying them mid-file.

## 5. Place each surviving rule at the right scope

Scope decides when a rule is paid for, so aim for the narrowest scope that still
covers every case the rule governs and the broadest scope that is still accurate.
Narrow a rule only when the case it would stop covering is deliberate and stated;
silently dropping coverage is the failure this step prevents.

| Scope | Use for | Paid for |
| --- | --- | --- |
| Repository-wide instructions | Architecture, shared commands, universal constraints, definitions of done | Every applicable request |
| Path-scoped instruction file | Conventions or validation for a file type or subtree: globs, framework rules, generated-code rules | Only matching files |
| Nested instructions file | Rules for a directory subtree, where the tool loads nested files hierarchically | Working directory and touched paths |
| Linked document | Procedural detail, tutorials, history, rarely needed reference | Only when followed |
| Skill | A multi-step procedure with decisions, triggered by intent rather than location | Only when activated |
| In-code comment | An invariant that matters at exactly one place | Only when that code is read |

Prefer pointers over copies for content that is legitimately detailed, but do not
create a pointer to a document whose content would go stale faster than the
instruction would; architecture walkthroughs and flow narratives decay quickly.
When several tools read instruction files, keep one canonical home per rule and
let tool-specific files import or point at it rather than restating it
(pattern 12).

A skill is reachable only when its description matches the request, so when a
workflow must apply to one subtree or file type, add a short routing pointer from
an instruction file that already loads for those paths, and keep the description
for intent-based discovery.

## 6. Propose before applying

Present findings as a table so the user can veto without reading a diff:

| Where | Verdict | Why | Proposed change |
| --- | --- | --- | --- |

The Verdict column accepts any of the six values from step 2, so the report states
what will be cut, what will move and where, what will be added, and what still
needs verification. Use one concrete proposed edit per row, not advice.

Ask for confirmation before restructuring, moving, or deleting content. The user
has calibration about their team that the audit does not. Consolidation is the
exception to "ask before deleting": duplicate content that already drifted is a
defect, but still report exactly which copy became canonical.

## 7. Apply, then validate

- Make the edits, including the destination files for every **Move**.
- Apply the placement rules in
  [instruction sources](references/instruction-sources.md) so a moved rule
  actually loads for its intended paths and leaves no orphaned copy behind.
- Validate the change set with the repository's own documented entry point, and
  with whatever the instructions themselves assert: if the file tells an agent to
  run a format, build, or test command, run it and confirm it succeeds. Step 3
  owns the disposition of a command that cannot be run.
- For the change set itself, an instruction-only change with no code impact is
  covered by the repository's format or documentation check; do not invent a build
  step. This does not relax the command check above.
- Validate against behavior, not only against the diff. Run one common bounded
  task the repository performs and record the failure mode you actually observe
  rather than one you imagine. Add the minimum rule that prevents a repeated
  failure, then repeat on a second, unrelated task. A format or lint pass proves
  nothing about whether the instructions work. If no representative task can be
  run, say so and name which rules remain unvalidated.
- Report what changed, what was deliberately left alone, and anything that needs a
  human decision, such as an owner for an operational command or an environment
  assumption.

## Create mode: no instructions exist yet

When the repository ships no agent instructions, there is nothing to classify. Do
not import another project's file, and do not write an inventory of the repository.
Build the first file from step 4 and the scope ladder in step 5:

- Propose the outline, the canonical file location, and what is deliberately left
  out; confirm it before writing.
- Include only what the introduction's keep list admits, even when it reads like
  useful context for a human newcomer. A thorough onboarding guide belongs in the
  README, not here.

## Writing style for instruction content

Apply these while editing, not only while classifying. The
[anti-patterns](references/anti-patterns.md) file owns the rationale and the
before-and-after rewrite for the numbered patterns named below; the remaining
bullets are this section's own.

- **Concrete over abstract.** An exact command, path, or signature beats a
  sentence about being careful. Prefer a code block to prose for anything
  executable or copyable (pattern 8).
- **Show the tempting wrong form.** When a rule has a plausible wrong version,
  give a short ✓/✗ pair instead of a bare prohibition.
- **Reserve `always`, `never`, and `must`** for rules that are genuinely absolute.
- Patterns 1, 2, 4, 11, and 13 own the rest: stable references over drifting patch
  pins, a local decision over a universal truth, a why and the rejected alternative
  for every constraint, no changelog or "last updated" meta-commentary, and no
  prompt coaxing or per-model branches.

## Boundaries

- Do not make an instruction file shorter by removing enforcement from the tools
  that provide it, or by dropping a boundary whose violation is expensive.
- Do not invent rules to fill a file, and do not create instructions for a
  repository that does not need them: say so and stop.
- Prefer a rule that generalizes. A constraint that only prevents a repeat of one
  incident belongs in a test or an issue, not on the always-loaded path.
- Do not treat the documentation-versus-instruction boundary as cosmetic. A
  README, build guide, or test guide owned by humans is referenced by
  instructions, not absorbed into them.
- When a change set fixes the cause of a documented workaround, delete the
  workaround in that same change set. Leaving a "this can now be removed" note
  defers the cleanup until nobody remembers why it mattered.

## Definition of done

- Every discovered rule has a verdict, and every **Verify** item resolved to
  **Keep** or **Fix**.
- Every surviving command was run where the environment permits, and every
  surviving path, version, and document reference was confirmed against the
  repository.
- No rule is stated in more than one loaded source.
- Each surviving rule sits at the narrowest scope that still covers the cases it
  governs.
- The change set was validated with the repository's own entry point, and the
  representative tasks in step 7 were attempted: one bounded task, then a second,
  unrelated one, or the reason a task could not be run is stated along with the
  rules that remain unvalidated.
- In create mode, the outline and scope split were confirmed before writing, and
  every rule traces to a fact only this team knows.
- The report names what changed, what was left alone, and what needs a decision.
