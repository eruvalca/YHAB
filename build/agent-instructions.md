# Agent instruction sources and placement

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

Repository-owned project skills are discovered from `.github/skills`,
`.agents/skills`, or `.claude/skills`; YHAB keeps its own skills in
`.agents/skills`. First-party Aspire workflow skills instead come from the plugin
configured in `.github/copilot/settings.json`; do not copy that plugin into the
repository.

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
- A detailed, reusable YHAB workflow belongs in a repository-owned
  `.agents/skills` entry with a clear activation description, plus a short
  conditional routing instruction when its use is mandatory. First-party product
  workflows should come from their configured plugin instead of a copied bundle.
  Reference existing setup, feature, build, and test documentation instead of
  copying it into instructions or skills.
