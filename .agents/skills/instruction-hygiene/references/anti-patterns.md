# Anti-patterns

Recurring shapes of low-signal instruction content, each with why it fails and
what to write instead. Examples are illustrative; match the pattern, not the
wording.

These entries are numbered because `SKILL.md` and the audit checklist cite them as
`pattern N`. Renumbering silently breaks those citations, so after adding,
removing, or reordering an entry, re-confirm every `pattern N` reference.

## 1. Restating the manifest

**Bad**

```
## Stack
- Node 20.11.1
- TypeScript 5.4.2
- Vitest 1.6.0
- fastify 4.26.0
```

**Why** Every version is pinned in the manifest. They drift on the next bump, and
an agent can read the manifest faster than it can decide whether the copy is
trustworthy. A stale list here makes the rest of the file look stale too.

**Rewrite** Drop the inventory. Keep only a constraint that changes how code is
written:

```
## Runtime constraints
- Node 20 runs locally, but the deployed function runtime is Node 18: do not use
  ES2023 array helpers (`toSorted`, `toReversed`, `with`) outside test setup.
- Do not add a second HTTP framework. Fastify plugins are the extension point.
```

## 2. Generic engineering advice

**Bad**

```
- Write clean, maintainable code
- Handle errors appropriately
- Use meaningful names
- Keep functions small
- Add tests for new behavior
```

**Why** A capable model already does this. The statements are too vague to resolve
a real decision, and they dilute the local rules around them: an agent that reads
one generic rule may conclude the whole file operates at that level of generality.

**Rewrite** Delete, and replace with the local decision the sentence was standing
in for:

```
- Map validation failures to 400, missing resources to 404, and concurrency
  conflicts to 409 through the existing ProblemDetails helpers.
```

## 3. Directory trees that duplicate a listing

**Bad**

```
src/
├── components/   # React components
├── hooks/        # Custom React hooks
├── utils/        # Utility functions
└── api/          # API client code
```

**Why** Every label is already in the directory name. The tree replicates output
a single listing produces and starts lying after the first refactor.

**Rewrite** Describe only the non-obvious:

```
## Module boundaries
- `src/engine/` is pure logic and must not import `src/api/`; a CLI reuses it
  without an HTTP client.
- Anything imported from `src/experimental/` outside that directory is a bug.
```

## 4. Rules with no rationale

**Bad**

```
- Always go through the data layer
- Don't call the clock directly
- Prefer composition over inheritance
```

**Why** Without a why, an agent cannot tell when the rule is load-bearing, so it
either abandons the rule in the one case that mattered or applies it mechanically
where it hurts.

**Rewrite** One line of rationale, including the alternative that was rejected when
the rule records a decision rather than a preference:

```
- All database access goes through `src/data/`. Direct driver calls from handlers
  are what made the 2024 engine swap a two-month project; the data layer is the seam.
- Use `AppClock`, never the system clock. Tests freeze it, and a stray `new Date()`
  silently breaks date-boundary coverage.
```

## 5. Dead pointers

**Bad**

```
> See MIGRATION_PLAN_V2.md for the auth refactor.
> See FIX_BALANCE_PROMPT.md for the pattern to follow.
```

**Why** These documents are written during a change and rarely maintained
afterward. When the work finishes they become misleading, and sometimes they are
deleted, leaving a pointer to nothing.

**Rewrite** Check each pointer for existence and currency. If the work is done,
inline the durable takeaway and drop the reference; if the destination is
genuinely maintained, keep the pointer and make its owner explicit.

## 6. Harness-level reminders

**Bad**

```
## Notes for AI assistants
1. Think step by step
2. Be careful with destructive commands
3. Read files before editing them
4. Use the right tool for the job
5. Run the tests before saying you're done
```

**Why** The agent runtime already supplies this behavior, so the section consumes
attention without changing an outcome. Worse, it crowds out the project-specific
notes that belong in that section.

**Rewrite** Keep only where this repository diverges from the default:

```
## Notes for AI assistants
1. This project builds only on Windows; the build invokes PowerShell scripts.
2. Money is always an integer count of minor units. Never use floating point,
   including in tests.
3. When a snapshot test fails after an intentional format change, update the
   snapshot and state that you did; never delete the assertion.
```

## 7. Buried critical constraints

**Bad** The one fact that decides success — a platform restriction, an ordering
requirement, a production-only rule — appears in the middle of section 9 of a
12-section document.

**Why** An agent that acts on the first screen will fail expensively before it ever
reaches the warning, then misattribute the failure.

**Rewrite** Promote it to the top and make it visually distinct:

```
> **Migrations run before the new application version.** The previous release must
> keep working against the new schema. See "Database changes" for the two-step
> procedure.
```

## 8. A wall of prose

**Symptom** A long file with few headings, no lists, and rare code blocks. Rules
run together inside paragraphs.

**Why** Agents and humans both skim. If structure does not surface the load-bearing
content, it is missed regardless of how accurate the prose is.

**Rewrite** Convert rules to bullets, commands to code blocks, and constraints to
callouts. If the document is long because the system is genuinely complex, add a
short summary at the top and order sections by what a new reader needs first.

## 9. Zero-signal prose

**Bad**

```
We pride ourselves on a robust engineering culture, clear communication, and a
codebase that emphasizes quality, maintainability, and developer experience.
```

**Why** It contains no fact and no rule. Every sentence that does not tell the
agent something it would otherwise get wrong is cost without benefit.

**Rewrite** Delete.

## 10. Architecture narrative instead of constraints

**Bad** Four paragraphs describing how a request flows through the gateway,
service, and cache.

**Why** If accurate, the agent could have read it from a handful of files; if
inaccurate after a refactor, it is actively misleading. Flow narratives decay
faster than almost any other content.

**Rewrite** Point at a maintained design document, or reduce to the constraint the
narrative was protecting:

```
## Sessions
- Auth state is read through `getSession()` only; reading cookies elsewhere
  bypasses rotation and has caused production logouts.
- Tokens rotate on every request. Do not cache them outside the session store.
```

## 11. The instruction file as a changelog

**Bad**

```
Last updated: 2026-03-11 by R. Patel
Recent changes:
- Added the auth section
- Updated build commands
```

**Why** Version control already records this, and a "last updated" line rots the
first time someone edits a typo without touching it, which erodes trust in
everything below it.

**Rewrite** Delete. Put the date and reason in the commit message.

## 12. The same rule in several loaded sources

**Bad** `AGENTS.md` states the test command, and a path-scoped instruction file
restates it with an older flag.

**Why** Tools that load both sources often define no precedence between them, so
the agent receives contradictory guidance with no way to resolve it. The copies
drift by construction.

**Rewrite** Keep one canonical home and delete the other copy. A tool-specific
file should import or point at the canonical file rather than rephrase it.

## 13. Prompt coaxing and per-model branches

**Bad**

```
- Take a deep breath and think carefully before acting
- Act as a world-class principal engineer
- Read every file before making any change
- If model A, inspect three files first; if model B, ask for confirmation
```

**Why** None of this is project knowledge. Capable reasoning models do not need
encouragement, rigid procedural directions can cause needless exploration or
conflict with available tools, and model-version branches become stale faster than
the architecture does.

**Rewrite** Describe the outcome, the constraints, and the evidence required:

```
Make the smallest change that addresses the root cause, preserve public behavior,
and run the tests for the affected project.
```

## 14. Aspirational rules stated as fact

**Bad**

```
- All handlers validate input with the shared schema helper
```
when half the handlers predate the helper.

**Why** An agent that trusts the claim will misread the code it then sees, and may
"fix" a legacy path that intentionally remains different. Once one stated rule is
wrong, the reader discounts the others.

**Rewrite** Say what is true and what is intended:

```
- New handlers must validate input with `validateInput`. Roughly half the existing
  handlers predate it; migrating them is tracked in issue #4123 and out of scope
  for unrelated changes.
```

## 15. Restating what the tooling enforces

**Bad**

```
## Style
- Indentation is two spaces
- Imports are sorted alphabetically
- No unused variables
- Maximum line length is 100
```

**Why** A formatter, linter, analyzer, or test already decides all of this, and the
prose drifts the moment the configuration changes. It also invites the reader to
trust the list instead of running the command that is authoritative, so a rule that
the tooling no longer enforces keeps consuming attention.

**Rewrite** Replace the list with the command that enforces it, and keep only what
no tool can decide:

```
## Style
- Run `npm run format` and `npm run lint` before finishing; CI runs both.
- Do not silence a repository rule locally to get a change through. If a rule is
  wrong for a case, raise it instead of adding a suppression.
```
