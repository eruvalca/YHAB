#!/usr/bin/env python3
"""Validate browser workflow policy against parsed YAML and regression fixtures."""

from __future__ import annotations

import copy
import re
import sys
import tempfile
import unittest
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


def _unconditional_step_uses(job: dict[str, Any], action: str) -> int:
    steps = _sequence(job.get("steps"), "job.steps")
    return sum(
        isinstance(step, dict)
        and step.get("uses") == action
        and "if" not in step
        for step in steps
    )


def _run_values(job: dict[str, Any]) -> list[str]:
    steps = _sequence(job.get("steps"), "job.steps")
    return [
        step["run"]
        for step in steps
        if isinstance(step, dict) and isinstance(step.get("run"), str)
    ]


def _has_active_command(job: dict[str, Any], expression: str) -> bool:
    pattern = re.compile(rf"(?m)^[ \t]*{expression}")
    return any(pattern.search(run) is not None for run in _run_values(job))


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
    for job_name, job_value in deploy_jobs.items():
        job = _mapping(job_value, f"deploy job {job_name!r}")
        steps = job.get("steps", [])
        if not isinstance(steps, list):
            continue
        for step in steps:
            if not isinstance(step, dict):
                continue
            value = step.get("uses")
            _require(
                not isinstance(value, str)
                or value != FULL_BROWSER_ACTION,
                "The main workflow must not select or require browser validation.",
            )
        _require(
            not _has_active_command(
                job,
                r"(?:dotnet\s+test\b[^\r\n]*(?:YHAB\.PlaywrightTests|Category=BrowserSmoke)|"
                r"(?:\.\/)?\.github\/actions\/run-full-browser\b)",
            ),
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
    condition = browser_job.get("if")
    _require(isinstance(condition, str), "The browser job must have an active opt-in condition.")
    condition = condition.strip()
    if condition.startswith("${{") and condition.endswith("}}"):
        condition = condition[3:-2].strip()
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
        _unconditional_step_uses(browser_job, FULL_BROWSER_ACTION) == 1,
        "The browser request must reuse the full-browser action exactly once.",
    )

    nightly_triggers = _mapping(nightly.get("on"), "nightly-browser.on")
    _require(
        "workflow_dispatch" in nightly_triggers,
        "Nightly validation must remain manually runnable.",
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
        _unconditional_step_uses(browser_full, FULL_BROWSER_ACTION) == 1,
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
    report_condition = reporting.get("if")
    if isinstance(report_condition, str):
        report_condition = report_condition.strip()
        if report_condition.startswith("${{") and report_condition.endswith("}}"):
            report_condition = report_condition[3:-2].strip()
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
    runs = _run_values(reporting)
    for suite in ("Browser", "Performance"):
        _require(
            any(
                re.search(
                    rf"(?m)^[ \t]*(?:\.\/)?scripts/Publish-NightlyValidationIssue\.ps1"
                    rf"\b[^\r\n]*-Suite\s+['\"]?{suite}\b",
                    run,
                )
                is not None
                for run in runs
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
