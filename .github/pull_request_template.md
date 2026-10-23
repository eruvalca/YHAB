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

- [ ] `Browser validation gate` passed for the tier selected from the changed paths

UI/rendering and Playwright changes require the full Chromium suite; other application changes require smoke. Documentation-only changes require neither. Apply `run-full-browser` to request additional full coverage without rerunning unrelated jobs; while present, it also requests full coverage on new commits. This extra run does not replace the required tier.

## Deployment and data

- [ ] No migrations or destructive data changes
- [ ] Migrations remain compatible with the currently deployed revision
- [ ] Deployment artifacts checked with `aspire publish`

## UI evidence

<!-- Screenshots or recordings for visible changes; omit when not applicable. -->

## Documentation review

- [ ] Documentation updated or confirmed unchanged (agents also state `Documentation review: complete.` in their final response)
