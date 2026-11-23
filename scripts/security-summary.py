#!/usr/bin/env python3
"""Present cicd source evidence; the workflow's boolean verdict remains the gate."""

import argparse
from collections import defaultdict
import html
import json
import math
import os
from pathlib import Path
import re
from urllib.parse import quote, unquote, urlsplit


SEVERITIES = ("CRITICAL", "HIGH", "MEDIUM", "LOW", "INFO", "NONE", "UNKNOWN")
LEVELS = {"error": "HIGH", "warning": "MEDIUM", "note": "LOW", "none": "INFO"}
SUMMARY_LIMIT = 900_000  # Leave room below GitHub's 1 MiB step-summary limit.
ARTIFACT = "Full report and scan logs: `cicd-security-evidence-<SHA>`."


def markdown(value):
    # Treat scanner output and source-controlled paths as data, not markup.
    value = re.sub(r"[\x00-\x1f\x7f]+", " ", str(value if value is not None else "not reported"))
    if len(value) > 500:
        value = value[:500] + "…"
    return re.sub(r"([\\`*_{}\[\]()#+.!|~-])", r"\\\1", html.escape(value, quote=False))


def repository_revision(repo):
    sha = repo.get("commitSha")
    if not isinstance(sha, str) or not re.fullmatch(r"[0-9a-fA-F]{40}(?:[0-9a-fA-F]{24})?", sha):
        return None, None
    try:
        url = urlsplit(repo.get("repoUrl") or "")
        path = unquote(url.path).strip("/").removesuffix(".git")
        if (url.scheme == "https" and re.fullmatch(r"[a-zA-Z0-9.-]+(?::[0-9]+)?", url.netloc)
                and safe_path(path)):
            return f"https://{url.netloc}/{quote(path, safe='/')}", sha
    except (ValueError, TypeError):
        pass
    return None, None


def safe_path(path):
    return not re.search(r"[\x00-\x1f\x7f:\\]", path) and all(
        segment not in ("", ".", "..") for segment in path.split("/")
    )


def location_label(base, sha, location):
    uri, line, column, end = location
    label = markdown(uri or "unknown location")
    if type(line) is int and line > 0:
        label += f":{line}"
        if type(column) is int and column > 0:
            label += f":{column}"
    path = unquote(uri or "").replace("\\", "/").removeprefix("./")
    if base and safe_path(path):
        href = f"{base}/blob/{sha}/{quote(path, safe='/')}"
        if type(line) is int and line > 0:
            href += f"#L{line}"
            if type(end) is int and end > line:
                href += f"-L{end}"
        return f"[{label}]({href})"
    return label


def sarif_severity(result, rule):
    for item in (result, rule):
        properties = item.get("properties") or {}
        explicit = str(properties.get("severity", "")).upper()
        if explicit in SEVERITIES:
            return explicit
        try:
            score = float(properties.get("security-severity"))
        except (TypeError, ValueError, OverflowError):
            continue
        if math.isfinite(score) and 0 <= score <= 10:
            for cutoff, level in ((9, "CRITICAL"), (7, "HIGH"), (4, "MEDIUM"), (0, "LOW")):
                if score >= cutoff:
                    return level if score else "INFO"
    return LEVELS.get(result.get("level") or (rule.get("defaultConfiguration") or {}).get("level"), "UNKNOWN")


def message_text(message):
    return message.get("text") or message.get("markdown") or "No message reported."


def at_index(items, index):
    return items[index] if type(index) is int and 0 <= index < len(items) else {}


def finding_details(evidence):
    groups = defaultdict(list)
    notifications = []
    active = suppressed = 0
    for run in (evidence.get("sarifReport") or {}).get("runs") or []:
        driver = (run.get("tool") or {}).get("driver") or {}
        tool = driver.get("name") or "unknown scanner"
        rules = driver.get("rules") or []
        by_id = {rule.get("id"): rule for rule in rules if rule.get("id")}
        for invocation in run.get("invocations") or []:
            if invocation.get("executionSuccessful") is False:
                notifications.append(f"{tool}: execution unsuccessful")
            for field in ("toolExecutionNotifications", "toolConfigurationNotifications"):
                for notification in invocation.get(field) or []:
                    notifications.append(f"{tool}: {message_text(notification.get('message') or {})}")
        for result in run.get("results") or []:
            if any(isinstance(item, dict) and item.get("kind") in ("inSource", "external")
                   and item.get("status", "accepted") == "accepted"
                   for item in result.get("suppressions") or []):
                suppressed += 1
                continue
            active += 1
            rule = by_id.get(result.get("ruleId")) or at_index(rules, result.get("ruleIndex"))
            key = (sarif_severity(result, rule), tool, result.get("ruleId") or rule.get("id"),
                   message_text(result.get("message") or {}))
            locations = []
            for location in result.get("locations") or []:
                physical = location.get("physicalLocation") or {}
                artifact = physical.get("artifactLocation") or {}
                indexed = at_index(run.get("artifacts") or [], artifact.get("index"))
                uri = artifact.get("uri") or (indexed.get("location") or {}).get("uri")
                region = physical.get("region") or {}
                locations.append((uri, region.get("startLine"), region.get("startColumn"), region.get("endLine")))
            groups[key].append(locations or [(None, None, None, None)])

    # The source scanner puts Trivy detail in native findings, not SARIF.
    native = evidence.get("findings") or []
    for finding in native:
        severity = str(finding.get("severity")).upper()
        key = (severity if severity in SEVERITIES else "UNKNOWN", finding.get("tool"),
               finding.get("rule"), finding.get("message") or "No message reported.")
        groups[key].append([(finding.get("artifactPath"), finding.get("line"), None, None)])
    return groups, list(dict.fromkeys(notifications)), active, suppressed, len(native)


def report_blocks(evidence):
    if not isinstance(evidence, dict) or type(evidence.get("passed")) is not bool:
        raise ValueError("expected a boolean passed verdict")

    repo = evidence.get("repo") or {}
    base, sha = repository_revision(repo)
    commit = markdown(repo.get("commitSha"))
    if base:
        commit = f"[{sha[:12]}]({base}/commit/{sha})"
    lines = [
        "### cicd source security", "",
        f"**Verdict: {'PASS' if evidence['passed'] else 'FAIL'}**  ",
        f"Outcome: {markdown(evidence.get('outcome'))}  ",
        f"Fail-on severity: {markdown(evidence.get('failOnSeverity'))}  ",
        f"Total findings: {markdown(evidence.get('findingsCount'))}",
    ]
    if repo:
        lines += [f"Commit: {commit}  ", f"Branch: {markdown(repo.get('branch'))}"]
    counts = evidence.get("findingsBySeverity") or {}
    if counts:
        lines += ["", "Findings by severity: " + ", ".join(
            f"{markdown(level)}: {markdown(counts[level])}"
            for level in sorted(counts, key=lambda level: (SEVERITIES.index(level) if level in SEVERITIES else len(SEVERITIES), level))
        )]
    lines += ["", "A passing threshold does not prove that all files or dependencies were examined.",
              "", ARTIFACT]
    yield "\n".join(lines)

    yield "#### Scanner coverage"
    sources = evidence.get("sources") or []
    if sources:
        lines = [
            "| Scanner | Concern | Outcome | Findings | Examined | Rules | Errors / warnings | Threshold |",
            "| --- | --- | --- | ---: | --- | ---: | --- | --- |",
        ]
        for source in sources:
            examined = source.get("examined") or {}
            rules = source.get("rules") or {}
            cells = [markdown(source.get(key)) for key in ("tool", "concern", "outcome", "findingsCount")]
            coverage = markdown(examined.get("count"))
            if examined.get("total"):
                coverage += f" / {markdown(examined['total'])}"
            coverage = f"{coverage} {markdown(examined.get('unit', ''))}".rstrip()
            if examined.get("partial"):
                coverage += f" ({markdown(examined['partial'])} partial)"
            passed = source.get("passed")
            threshold = ("not applicable" if source.get("outcome") == "SKIPPED" else
                         "PASS" if passed is True else "FAIL" if passed is False else "not reported")
            cells += [
                coverage,
                markdown(rules.get("count")),
                f"{markdown(source.get('errorsCount'))} / {markdown(source.get('warningsCount'))}",
                threshold,
            ]
            lines.append("| " + " | ".join(cells) + " |")
        yield "\n".join(lines)
    else:
        yield "No scanner coverage metadata was reported."

    groups, notifications, active, suppressed, native = finding_details(evidence)
    for messages, heading in ((evidence.get("errors"), "Errors"), (evidence.get("warnings"), "Warnings"),
                              (notifications, "Scanner notifications")):
        if messages:
            yield f"#### {heading} ({len(messages)})"
            yield from (f"- {markdown(message)}" for message in messages)

    yield "#### Findings (highest severity first)"
    yield (f"Detailed rows: {active} active SARIF, {native} native; {suppressed} suppressed SARIF result(s) "
           "are retained in the raw evidence. Detail counts and display severity do not determine the verdict.")
    if not groups:
        yield "No detailed active findings were reported. Check the verdict, counts and scanner coverage above."
    for level, tool, rule, message in sorted(groups, key=lambda key: SEVERITIES.index(key[0])):
        results = groups[(level, tool, rule, message)]
        yield f"##### {level} · {markdown(tool)} · {len(results)} result(s)"
        yield f"**Rule:** {markdown(rule)}  \n{markdown(message)}"
        for location in dict.fromkeys(location for result in results for location in result):
            yield f"- {location_label(base, sha, location)}"


def report(path, scan_outcome):
    status = 0
    if scan_outcome != "success":
        reason = "Check setup step logs." if scan_outcome == "skipped" else "See `execution.log` in the evidence artifact."
        blocks = ["### cicd source security", f"No verified scan result (scan {scan_outcome}). {reason}", ARTIFACT]
    else:
        try:
            blocks = list(report_blocks(json.loads(path.read_text(encoding="utf-8"))))
        except (OSError, ValueError, AttributeError, TypeError):
            blocks = ["### cicd source security", "Scan evidence is missing or invalid. Check the scan logs and uploaded evidence.", ARTIFACT]
            status = 1

    path.parent.mkdir(parents=True, exist_ok=True)
    (path.parent / "summary.md").write_text("\n\n".join(blocks) + "\n", encoding="utf-8")
    limited = []
    remaining = SUMMARY_LIMIT
    for block in blocks:
        remaining -= len(block.encode("utf-8")) + 2
        if remaining < 0:
            limited.append("Summary truncated. See `summary.md` in the evidence artifact for the full report.")
            break
        limited.append(block)
    summary = "\n\n".join(limited) + "\n"
    if destination := os.environ.get("GITHUB_STEP_SUMMARY"):
        with open(destination, "a", encoding="utf-8") as output:
            output.write(summary)
    # Keep workflow commands in untrusted evidence inert in the console.
    for line in summary.splitlines():
        print("security: " + line.replace("::", ": :"))
    return status


if __name__ == "__main__":
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    parser.add_argument("--scan-outcome", choices=("success", "failure", "skipped", "cancelled"), default="success")
    args = parser.parse_args()
    raise SystemExit(report(args.evidence, args.scan_outcome))
