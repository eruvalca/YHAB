# Section-by-section audit checklist

Use with the verdicts from `SKILL.md`. For each
section, the question is always the same: does this change agent behavior in a way
the agent could not have determined from the repository itself?

This file is the section index: what to keep and what to verify per section. The
[anti-patterns](anti-patterns.md) file owns why a failing shape fails and how to
rewrite it, so entries below point at the relevant pattern instead of repeating its
rationale.

Names in examples are illustrative. Match the section to the repository's actual
content rather than looking for these headings.

## Project overview

**Keep** one or two sentences naming what the system is and what it does when the
purpose is not obvious from the README — a repository the agent has never seen
benefits from orientation more than one it can infer.

**Remove** marketing copy, project history, stakeholder lists, and anything that
reads like a pitch (pattern 9). **Verify** that the description still matches the
system's current scope.

## Architecture and boundaries

**Keep** the boundaries that are invisible in the file tree: which component owns
a contract, which old-looking directory is still load-bearing, which source is
generated, where a domain rule must live. These are exactly the facts a model
cannot infer, and the ones whose violation is expensive.

**Remove** narrative walkthroughs of request or data flow (pattern 10). Replace
them with the constraint that survives refactoring, such as "auth state is read
only through `getSession()`", or with a link to a document a real process keeps
current.

**Verify** module names and paths, and flag directories that were renamed or
deleted since the text was written.

## Tech stack and versions

**Keep** major-version constraints that change how code should be written, and
unusual dependencies with the reason they exist.

**Remove** patch-version pins and restatements of what the manifest already lists
(pattern 1).

**Verify** every version claim against the file that owns it — manifest, lockfile,
SDK declaration, or CI configuration. A stale version signals stale everything
below it.

## Directory and module structure

**Keep** responsibilities that are non-obvious from a directory name,
cross-cutting facts that are not visible in the tree at all (`internal/` is
unsupported, `legacy/` is still in production), and any structure whose role
decides where a change belongs.

**Remove** ASCII trees that duplicate a directory listing, and one-line
descriptions for self-explanatory names (pattern 3). **Verify** any surviving
structure against the real tree.

## Build, test, and validation commands

**Keep** the exact invocations that work, especially where the obvious command is
wrong or incomplete; load-bearing non-default flags; the selection syntax for
running a single test; and format, lint, or verify commands whose names are not
discoverable. This is usually the highest-value section in the file because it
prevents wasted exploration and gives a concrete definition of done.

**Remove** commands that no longer exist and long inventories of every available
script. **Verify** by running the surviving commands, cheapest first, as step 3
requires.

## Conventions and style

**Keep** conventions that override common defaults, project-specific naming and
helper rules, and negative rules carrying the why and the wrong-form example
required by the
[writing style](../SKILL.md#writing-style-for-instruction-content) section.

**Remove** generic practice (pattern 2) and anything a formatter, analyzer, linter,
or test already enforces (pattern 15) — point at the command instead. **Verify**
that existing code actually follows a stated convention (pattern 14); if it does
not, either the convention is aspirational and should say so, or the instruction is
wrong.

## Gotchas and war stories

Usually the most under-invested section and the most valuable.

**Keep** specific failures that cost the team time, patterns that look correct but
break in production, upstream quirks with their workaround, and invariants whose
reason is not visible locally.

**Remove** stories about features that no longer exist and war stories without an
actionable takeaway. **Add** from recent history: fix commits and pull request
discussion where a reviewer explained a non-obvious cause are ready-made entries.

## Platform and environment

**Keep** OS-specific shell behavior, container versus host path differences,
required environment variables that are not in the example file, and tooling
versions that change behavior.

**Remove** setup advice a script or make target already performs, and generic
"install Git" style prerequisites. **Verify** that required variables and flags
still exist.

## "Notes for AI assistants" and meta sections

Audit these hardest; they accumulate the most noise.

**Keep** what agents specifically and repeatedly get wrong in this repository, and
constraints an agent cannot infer, such as a build that only works on one platform
or a language version older than the local toolchain.

**Remove** reminders the harness itself provides and per-model instructions
(patterns 6 and 13). Describe the outcome and the constraints instead of the
reasoning ritual.

## References to other documents

**Verify** every "see `X.md`" pointer: does the file exist, is it current, and has
the work it describes already finished (pattern 5)? Migration plans, fix prompts,
and temporary design notes are the most common dead references.

A pointer is worth keeping only when the destination is maintained. Otherwise
inline the one fact the agent needs and drop the pointer.

## Visual hierarchy and length

After the content audit, look at the shape of the file:

- Are the most expensive mistakes stated near the top, or buried in the middle of
  a long document (pattern 7)?
- Are critical constraints visually distinct, or mixed in with preferences of
  equal-looking weight?
- Are sections ordered by what a first-time reader needs first?
- Is prose carrying weight a list or a code block could carry better (pattern 8)?
- For a genuinely long file, is there a short summary at the top?

Length alone is not a defect. A precise 100-line file can beat a vague 30-line one.

## Sanity questions before finishing

1. If a new agent read only the first screen, would it avoid the most expensive
   mistakes?
2. Is anything here that a capable model would already do correctly by default?
3. Is there guidance in recent review feedback that is still missing?
4. If the file were deleted entirely, what is the first thing that would go wrong?
5. Can each surviving rule be traced to a fact only this team knows?
