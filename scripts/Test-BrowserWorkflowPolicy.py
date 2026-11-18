#!/usr/bin/env python3
"""Validate browser workflow policy against parsed YAML and regression fixtures."""

from __future__ import annotations

import copy
import re
import sys
import tempfile
import unittest
from collections.abc import Iterator
from pathlib import Path
from typing import Any

import yaml


ROOT = Path(__file__).resolve().parent.parent
WORKFLOWS = ROOT / ".github" / "workflows"
EXPECTED_REQUEST_EVENTS = {
    "labeled",
    "ready_for_review",
    "reopened",
    "synchronize",
}
EXPECTED_NIGHTLY_CRON = "17 6 * * *"
FULL_BROWSER_ACTION = "./.github/actions/run-full-browser"
BROWSER_SELECTOR = re.compile(
    r"YHAB\.PlaywrightTests|Category=BrowserSmoke|run-full-browser", re.IGNORECASE
)
REPORTED_SUITES = {"Browser": "browser-full", "Performance": "performance"}


class UniqueKeyLoader(yaml.SafeLoader):
    """Load GitHub Actions-style YAML while rejecting ambiguous duplicate keys."""

    yaml_implicit_resolvers = copy.deepcopy(yaml.SafeLoader.yaml_implicit_resolvers)
    for first_character, resolvers in yaml_implicit_resolvers.items():
        yaml_implicit_resolvers[first_character] = [
            (tag, pattern)
            for tag, pattern in resolvers
            if tag != "tag:yaml.org,2002:bool"
        ]
    _boolean_pattern = re.compile(r"^(?:true|True|TRUE|false|False|FALSE)$")
    for first_character in "tTfF":
        yaml_implicit_resolvers.setdefault(first_character, []).append(
            ("tag:yaml.org,2002:bool", _boolean_pattern)
        )

    def construct_mapping(
        self, node: yaml.MappingNode, deep: bool = False
    ) -> dict[str, Any]:
        keys: set[Any] = set()
        for key_node, value_node in node.value:
            if key_node.tag == "tag:yaml.org,2002:merge":
                continue
            key = self.construct_object(key_node, deep=deep)
            if key in keys:
                raise yaml.constructor.ConstructorError(
                    "while constructing a mapping",
                    node.start_mark,
                    f"duplicate key {key!r}",
                    key_node.start_mark,
                )
            keys.add(key)
        self.flatten_mapping(node)
        return super().construct_mapping(node, deep=deep)


class PolicyViolation(Exception):
    """A workflow is valid YAML but does not meet the repository's policy."""


def _require(condition: bool, message: str) -> None:
    if not condition:
        raise PolicyViolation(message)


def _load_yaml(path: Path) -> dict[str, Any]:
    try:
        value = yaml.load(path.read_text(encoding="utf-8"), Loader=UniqueKeyLoader)
    except yaml.YAMLError as error:
        raise PolicyViolation(f"{path.name} is invalid or ambiguous YAML: {error}") from error
    _require(isinstance(value, dict), f"{path.name} must contain a YAML mapping.")
    return value


def _mapping(value: Any, description: str) -> dict[str, Any]:
    _require(isinstance(value, dict), f"{description} must be a YAML mapping.")
    return value


def _sequence(value: Any, description: str) -> list[Any]:
    _require(isinstance(value, list), f"{description} must be a YAML list.")
    return value


def _job(workflow: dict[str, Any], name: str, description: str) -> dict[str, Any]:
    jobs = _mapping(workflow.get("jobs"), f"{description}.jobs")
    return _mapping(jobs.get(name), f"{description} job {name!r}")


def _expression(value: Any) -> str | None:
    if not isinstance(value, str):
        return None
    value = value.strip()
    if value.startswith("${{") and value.endswith("}}"):
        value = value[3:-2].strip()
    return value


def _steps(job: dict[str, Any]) -> list[dict[str, Any]]:
    steps = job.get("steps", [])
    if not isinstance(steps, list):
        return []
    return [step for step in steps if isinstance(step, dict)]


def _step_uses(job: dict[str, Any], action: str, *, unconditional: bool) -> int:
    return sum(
        step.get("uses") == action and (not unconditional or "if" not in step)
        for step in _steps(job)
    )


def _has_active_command(job: dict[str, Any], expression: str) -> bool:
    pattern = re.compile(rf"(?m)^[ \t]*{expression}")
    return any(
        isinstance(step.get("run"), str)
        and "if" not in step
        and pattern.search(step["run"]) is not None
        for step in _steps(job)
    )


def _policy_strings(value: Any, key: Any = None) -> Iterator[str]:
    """Yield parsed string values, omitting comment lines from shell scripts."""
    if isinstance(value, dict):
        for child_key, child in value.items():
            yield from _policy_strings(child, child_key)
    elif isinstance(value, list):
        for child in value:
            yield from _policy_strings(child, key)
    elif isinstance(value, str):
        if key == "run":
            yield from (
                line for line in value.splitlines() if not line.lstrip().startswith("#")
            )
        else:
            yield value


def validate_workflows(workflows: dict[str, dict[str, Any]]) -> None:
    deploy = workflows["deploy"]
    request = workflows["browser-request"]
    nightly = workflows["nightly-browser"]

    deploy_jobs = _mapping(deploy.get("jobs"), "deploy.jobs")
    forbidden_browser_jobs = [
        name
        for name in deploy_jobs
        if isinstance(name, str) and name.lower().startswith("browser-")
    ]
    _require(
        not forbidden_browser_jobs,
        "The main workflow must not define browser validation jobs.",
    )
    deploy_job = _mapping(deploy_jobs.get("deploy"), "deploy job")
    _require(
        set(_sequence(deploy_job.get("needs"), "deploy.needs"))
        == {"fast", "tests", "publish"},
        "Main deployment must not depend on a browser job.",
    )
    _require(
        not any(BROWSER_SELECTOR.search(value) for value in _policy_strings(deploy)),
        "The main workflow must not select or require browser validation.",
    )

    request_triggers = _mapping(request.get("on"), "browser-request.on")
    _require(
        set(request_triggers) == {"pull_request"},
        "The request workflow must run only on pull requests.",
    )
    pull_request = _mapping(
        request_triggers.get("pull_request"), "browser-request.on.pull_request"
    )
    events = _sequence(
        pull_request.get("types"), "browser-request.on.pull_request.types"
    )
    _require(
        len(events) == len(EXPECTED_REQUEST_EVENTS)
        and all(isinstance(event, str) for event in events)
        and set(events) == EXPECTED_REQUEST_EVENTS,
        "Opt-in browser runs must support only labeled, ready_for_review, reopened, and synchronize events.",
    )

    request_jobs = _mapping(request.get("jobs"), "browser-request.jobs")
    browser_job = _mapping(request_jobs.get("browser-full"), "browser-full job")
    condition = _expression(browser_job.get("if"))
    _require(condition is not None, "The browser job must have an active opt-in condition.")
    guard = re.compile(
        r"\s*contains\s*\(\s*github\.event\.pull_request\.labels\.\*\.name\s*,\s*"
        r"['\"]run-full-browser['\"]\s*\)\s*&&\s*"
        r"github\.event\.pull_request\.draft\s*==\s*false\s*&&\s*"
        r"\(\s*github\.event\.action\s*!=\s*['\"]labeled['\"]\s*\|\|\s*"
        r"github\.event\.label\.name\s*==\s*['\"]run-full-browser['\"]\s*\)\s*"
    )
    _require(
        guard.fullmatch(condition) is not None,
        "The browser job must require the opt-in label, a non-draft PR, and the requested label event.",
    )

    _require(
        "concurrency" not in request,
        "The browser concurrency group must be job-level, not workflow-level.",
    )
    _require(
        isinstance(browser_job.get("concurrency"), dict),
        "Only the guarded browser job may acquire the browser concurrency group.",
    )
    _require(
        sum(
            isinstance(job, dict) and "concurrency" in job
            for job in request_jobs.values()
        )
        == 1,
        "Only the guarded browser job may acquire the browser concurrency group.",
    )
    _require(
        _step_uses(browser_job, FULL_BROWSER_ACTION, unconditional=True) == 1,
        "The browser request must reuse the full-browser action exactly once.",
    )
    _require(
        all(
            not isinstance(job, dict)
            or _step_uses(job, FULL_BROWSER_ACTION, unconditional=False) == 0
            for name, job in request_jobs.items()
            if name != "browser-full"
        ),
        "Only the guarded browser job may run the full-browser action.",
    )

    nightly_triggers = _mapping(nightly.get("on"), "nightly-browser.on")
    _require(
        set(nightly_triggers) == {"schedule", "workflow_dispatch"},
        "Nightly validation must run only on its schedule and manual dispatch.",
    )
    schedules = _sequence(nightly_triggers.get("schedule"), "nightly-browser schedule")
    _require(
        len(schedules) == 1
        and isinstance(schedules[0], dict)
        and schedules[0].get("cron") == EXPECTED_NIGHTLY_CRON,
        "Both suites must share the documented daily schedule.",
    )

    nightly_jobs = _mapping(nightly.get("jobs"), "nightly-browser.jobs")
    browser_full = _mapping(nightly_jobs.get("browser-full"), "nightly browser-full job")
    performance = _mapping(nightly_jobs.get("performance"), "nightly performance job")
    _require(
        "if" not in browser_full and "if" not in performance,
        "Both nightly validation suites must run on every scheduled and manual run.",
    )
    _require(
        _step_uses(browser_full, FULL_BROWSER_ACTION, unconditional=True) == 1,
        "Nightly browser validation must reuse the full-browser action.",
    )
    _require(
        _has_active_command(
            performance,
            r"dotnet\s+test\b[^\r\n]*--filter-trait\s+Category=Performance(?:\s|$)",
        ),
        "Nightly performance validation must select performance cases.",
    )

    reporting = _job(nightly, "report-failures", "nightly-browser")
    report_needs = _sequence(reporting.get("needs"), "report-failures.needs")
    _require(
        {"browser-full", "performance"}.issubset(set(report_needs)),
        "Nightly failure reporting must depend on both validation suites.",
    )
    report_condition = _expression(reporting.get("if"))
    report_condition_pattern = re.compile(
        r"\s*!\s*cancelled\s*\(\s*\)\s*&&\s*"
        r"github\.ref\s*==\s*['\"]refs/heads/main['\"]\s*&&\s*"
        r"\(\s*needs\.browser-full\.result\s*==\s*['\"]failure['\"]\s*\|\|\s*"
        r"needs\.performance\.result\s*==\s*['\"]failure['\"]\s*\)\s*"
    )
    _require(
        isinstance(report_condition, str)
        and report_condition_pattern.fullmatch(report_condition) is not None,
        "Nightly reporting must run when either validation suite fails.",
    )
    permissions = _mapping(reporting.get("permissions"), "report-failures.permissions")
    _require(
        permissions.get("issues") == "write",
        "Nightly reporting must be able to create issues.",
    )
    for suite, job_name in REPORTED_SUITES.items():
        publisher = re.compile(
            rf"(?m)^[ \t]*(?:\.\/)?scripts/Publish-NightlyValidationIssue\.ps1"
            rf"\b[^\r\n]*-Suite\s+['\"]?{suite}\b"
        )
        suite_failed = re.compile(
            rf"\s*needs\.{re.escape(job_name)}\.result\s*==\s*['\"]failure['\"]\s*"
        )
        _require(
            any(
                isinstance(step.get("run"), str)
                and publisher.search(step["run"]) is not None
                and suite_failed.fullmatch(_expression(step.get("if")) or "") is not None
                for step in _steps(reporting)
            ),
            f"{suite} failures and regressions must be reported.",
        )


def validate_repository(root: Path = ROOT) -> None:
    workflow_dir = root / ".github" / "workflows"
    workflows = {
        name: _load_yaml(workflow_dir / f"{name}.yml")
        for name in ("deploy", "browser-request", "nightly-browser")
    }
    validate_workflows(workflows)


def _fixture_workflows() -> dict[str, dict[str, Any]]:
    workflow_dir = WORKFLOWS
    return {
        name: _load_yaml(workflow_dir / f"{name}.yml")
        for name in ("deploy", "browser-request", "nightly-browser")
    }


class BrowserWorkflowPolicyTests(unittest.TestCase):
    def setUp(self) -> None:
        self.workflows = _fixture_workflows()

    def test_repository_workflows_satisfy_policy(self) -> None:
        validate_workflows(self.workflows)

    def test_unrelated_concurrency_environment_value_is_ignored(self) -> None:
        self.workflows["browser-request"]["env"]["concurrency"] = "unrelated"
        validate_workflows(self.workflows)

    def test_unrelated_matrix_types_are_ignored(self) -> None:
        self.workflows["browser-request"]["jobs"]["browser-full"]["strategy"] = {
            "matrix": {"types": ["one", "two"]}
        }
        validate_workflows(self.workflows)

    def test_step_push_environment_and_input_are_ignored(self) -> None:
        step = self.workflows["browser-request"]["jobs"]["browser-full"]["steps"][0]
        step["env"] = {"PUSH": "true", "SCHEDULE": "nightly"}
        step["with"] = {"push": "true"}
        validate_workflows(self.workflows)

    def test_trigger_example_string_does_not_change_real_triggers(self) -> None:
        self.workflows["browser-request"]["env"]["EXAMPLE"] = (
            "on:\n  pull_request:\n    types: [opened]\n"
        )
        validate_workflows(self.workflows)

    def test_quoted_push_trigger_is_rejected(self) -> None:
        self.workflows["browser-request"]["on"]["push"] = {}
        with self.assertRaisesRegex(PolicyViolation, "only on pull requests"):
            validate_workflows(self.workflows)

    def test_scalar_example_cannot_mask_wrong_pull_request_events(self) -> None:
        self.workflows["browser-request"]["env"]["EXAMPLE"] = (
            "on:\n  pull_request:\n    types: "
            "[labeled, synchronize, reopened, ready_for_review]\n"
        )
        self.workflows["browser-request"]["on"]["pull_request"]["types"] = ["opened"]
        with self.assertRaisesRegex(PolicyViolation, "only labeled"):
            validate_workflows(self.workflows)

    def test_action_text_in_environment_does_not_replace_action_step(self) -> None:
        browser_job = self.workflows["browser-request"]["jobs"]["browser-full"]
        browser_job["steps"] = [
            {"run": "echo no browser", "env": {"EXAMPLE": f"uses: {FULL_BROWSER_ACTION}"}}
        ]
        with self.assertRaisesRegex(PolicyViolation, "reuse the full-browser action"):
            validate_workflows(self.workflows)

    def test_comment_cannot_satisfy_active_job_guard(self) -> None:
        self.workflows["browser-request"]["jobs"]["browser-full"]["if"] = "true"
        with self.assertRaisesRegex(PolicyViolation, "require the opt-in label"):
            validate_workflows(self.workflows)

    def test_loose_guard_clause_cannot_satisfy_active_job_guard(self) -> None:
        self.workflows["browser-request"]["jobs"]["browser-full"]["if"] += " || true"
        with self.assertRaisesRegex(PolicyViolation, "require the opt-in label"):
            validate_workflows(self.workflows)

    def test_conditionally_skipped_action_does_not_satisfy_action_policy(self) -> None:
        browser_job = self.workflows["browser-request"]["jobs"]["browser-full"]
        action = next(
            step for step in browser_job["steps"] if step.get("uses") == FULL_BROWSER_ACTION
        )
        action["if"] = "false"
        with self.assertRaisesRegex(PolicyViolation, "reuse the full-browser action"):
            validate_workflows(self.workflows)

    def test_schedule_example_string_does_not_mask_wrong_cron(self) -> None:
        self.workflows["nightly-browser"]["env"]["DOCUMENTED_SCHEDULE"] = (
            "- cron: '17 6 * * *'"
        )
        self.workflows["nightly-browser"]["on"]["schedule"][0]["cron"] = "0 3 * * *"
        with self.assertRaisesRegex(PolicyViolation, "documented daily schedule"):
            validate_workflows(self.workflows)

    def test_commented_performance_command_does_not_satisfy_policy(self) -> None:
        performance = self.workflows["nightly-browser"]["jobs"]["performance"]
        step = next(
            step
            for step in performance["steps"]
            if isinstance(step.get("run"), str)
            and "--filter-trait Category=Performance" in step["run"]
        )
        step["run"] = step["run"].replace(
            "dotnet test", "# dotnet test", 1
        )
        with self.assertRaisesRegex(PolicyViolation, "must select performance cases"):
            validate_workflows(self.workflows)

    def test_conditionally_skipped_performance_command_does_not_satisfy_policy(self) -> None:
        performance = self.workflows["nightly-browser"]["jobs"]["performance"]
        step = next(
            step
            for step in performance["steps"]
            if "--filter-trait Category=Performance" in step.get("run", "")
        )
        step["if"] = "false"
        with self.assertRaisesRegex(PolicyViolation, "must select performance cases"):
            validate_workflows(self.workflows)

    def test_failure_report_must_be_guarded_by_its_own_suite(self) -> None:
        reporting = self.workflows["nightly-browser"]["jobs"]["report-failures"]
        step = next(step for step in reporting["steps"] if "-Suite Browser" in step.get("run", ""))
        for condition in ("false", "needs.performance.result == 'failure'"):
            with self.subTest(condition=condition):
                step["if"] = condition
                with self.assertRaisesRegex(PolicyViolation, "Browser failures"):
                    validate_workflows(self.workflows)

    def _deploy_test_step(self) -> dict[str, Any]:
        tests_job = self.workflows["deploy"]["jobs"]["tests"]
        return next(step for step in tests_job["steps"] if "dotnet test" in step.get("run", ""))

    def test_matrix_selected_browser_project_is_rejected(self) -> None:
        tests_job = self.workflows["deploy"]["jobs"]["tests"]
        tests_job["strategy"]["matrix"]["project"].append("YHAB.PlaywrightTests")
        with self.assertRaisesRegex(PolicyViolation, "must not select or require browser"):
            validate_workflows(self.workflows)

    def test_continued_browser_filter_is_rejected(self) -> None:
        step = self._deploy_test_step()
        step["run"] = step["run"].replace(
            "--no-build", "--no-build `\n  --filter-trait Category=BrowserSmoke", 1
        )
        with self.assertRaisesRegex(PolicyViolation, "must not select or require browser"):
            validate_workflows(self.workflows)

    def test_browser_text_in_shell_comment_is_ignored(self) -> None:
        step = self._deploy_test_step()
        step["run"] = "# YHAB.PlaywrightTests run in browser-request.yml\n" + step["run"]
        validate_workflows(self.workflows)

    def test_unguarded_job_cannot_run_full_browser_action(self) -> None:
        self.workflows["browser-request"]["jobs"]["other"] = {
            "runs-on": "ubuntu-24.04",
            "steps": [{"uses": FULL_BROWSER_ACTION}],
        }
        with self.assertRaisesRegex(PolicyViolation, "Only the guarded browser job"):
            validate_workflows(self.workflows)

    def test_nightly_extra_trigger_is_rejected(self) -> None:
        self.workflows["nightly-browser"]["on"]["push"] = {}
        with self.assertRaisesRegex(PolicyViolation, "only on its schedule"):
            validate_workflows(self.workflows)

    def test_disabled_nightly_job_is_rejected(self) -> None:
        for job_name in ("browser-full", "performance"):
            with self.subTest(job_name):
                self.workflows = _fixture_workflows()
                self.workflows["nightly-browser"]["jobs"][job_name]["if"] = "${{ false }}"
                with self.assertRaisesRegex(PolicyViolation, "every scheduled and manual run"):
                    validate_workflows(self.workflows)

    def test_duplicate_yaml_keys_are_rejected(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "duplicate.yml"
            path.write_text("on:\n  push: {}\non:\n  pull_request: {}\n", encoding="utf-8")
            with self.assertRaisesRegex(PolicyViolation, "duplicate key"):
                _load_yaml(path)

    def test_merge_keys_are_applied_before_policy_checks(self) -> None:
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "merge.yml"
            path.write_text(
                "defaults: &defaults\n"
                "  value: inherited\n"
                "job:\n"
                "  <<: *defaults\n"
                "  value: overridden\n",
                encoding="utf-8",
            )
            workflow = _load_yaml(path)
        self.assertEqual(workflow["job"]["value"], "overridden")


def main() -> int:
    suite = unittest.defaultTestLoader.loadTestsFromTestCase(BrowserWorkflowPolicyTests)
    result = unittest.TextTestRunner(verbosity=2).run(suite)
    return 0 if result.wasSuccessful() else 1


if __name__ == "__main__":
    sys.exit(main())
