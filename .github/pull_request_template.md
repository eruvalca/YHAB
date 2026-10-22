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

- [ ] The `Browser smoke` suite covers this change
- [ ] The full Chromium suite is required — add the `run-full-browser` label and wait for `Browser validation gate` to pass

## Deployment and data

- [ ] No migrations or destructive data changes
- [ ] Migrations remain compatible with the currently deployed revision
- [ ] Deployment artifacts checked with `aspire publish`

## UI evidence

<!-- Screenshots or recordings for visible changes; omit when not applicable. -->

## Documentation review

- [ ] Documentation updated or confirmed unchanged (agents also state `Documentation review: complete.` in their final response)
