## Summary

<!-- What changed, and why. Link the issue with "Closes #123" when applicable. -->

## Scope

- [ ] Application behavior
- [ ] UI, rendering, or Fluent components
- [ ] Account and Identity workflows
- [ ] Persistence, migrations, or data shape
- [ ] Build, CI, or infrastructure
- [ ] Documentation only

## Validation

- [ ] `dotnet build YHAB.slnx`
- [ ] `dotnet format YHAB.slnx --severity warn --verify-no-changes`
- [ ] Unit and component tests
- [ ] Affected infrastructure suites (PostgreSQL, Aspire, browser)
- [ ] Not run — explained below

Commands and results:

<!-- Include actual passed/failed/skipped counts. State blockers instead of claiming success. -->

## Browser validation

- [ ] Not requested, or `run-full-browser` was applied and its non-required run passed

Browser runs are opt-in and never merge-blocking. Apply `run-full-browser` to a ready pull request when UI, rendering, Playwright, or other high-risk changes warrant the full non-performance Chromium suite. While the label remains, new commits also request a run. The full suite and performance cases run daily in nightly CI, which reports failures and regressions through rolling issues.

## Deployment and data

- [ ] No migrations or destructive data changes
- [ ] Migrations remain compatible with the currently deployed revision
- [ ] Deployment artifacts checked with `aspire publish`

## UI evidence

<!-- Screenshots or recordings for visible changes; omit when not applicable. -->

## Documentation review

- [ ] Documentation updated or confirmed unchanged (agents also state `Documentation review: complete.` in their final response)
