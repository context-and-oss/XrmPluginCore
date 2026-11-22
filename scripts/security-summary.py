#!/usr/bin/env python3
"""Render a GitHub job summary from cicd source scan evidence."""

import argparse
import html
import json
from pathlib import Path
import re


def markdown(value):
    # Evidence can contain source-controlled paths and diagnostics. Keep it text
    # in Markdown tables, including when values contain newlines or markup.
    value = re.sub(r"[\x00-\x1f\x7f]+", " ", str(value if value is not None else "not reported"))
    if len(value) > 500:
        value = value[:500] + "…"
    return re.sub(r"([\\`*_{}\[\]()#+.!|~-])", r"\\\1", html.escape(value, quote=False))


def render(evidence):
    if not isinstance(evidence, dict) or type(evidence.get("passed")) is not bool:
        raise ValueError("expected a boolean passed verdict")

    lines = [
        "### cicd source security",
        "",
        f"**Verdict: {'PASS' if evidence['passed'] else 'FAIL'}**  ",
        f"Outcome: {markdown(evidence.get('outcome'))}  ",
        f"Fail-on severity: {markdown(evidence.get('failOnSeverity'))}  ",
        f"Total findings: {markdown(evidence.get('findingsCount'))}",
        "",
        "A passing threshold does not prove that all files or dependencies were examined.",
    ]
    counts = evidence.get("findingsBySeverity") or {}
    if counts:
        lines += ["", "Findings by severity: " + ", ".join(
            f"{markdown(level)}: {markdown(count)}" for level, count in counts.items()
        )]

    sources = evidence.get("sources") or []
    lines += ["", "#### Scanner coverage", ""]
    if sources:
        lines += [
            "| Scanner | Concern | Outcome | Findings | Examined | Rules | Errors / warnings |",
            "| --- | --- | --- | ---: | --- | ---: | --- |",
        ]
        for source in sources:
            examined = source.get("examined") or {}
            rules = source.get("rules") or {}
            cells = [
                markdown(source.get(key)) for key in ("tool", "concern", "outcome", "findingsCount")
            ]
            cells += [
                f"{markdown(examined.get('count'))} {markdown(examined.get('unit', ''))}".rstrip(),
                markdown(rules.get("count")),
                f"{markdown(source.get('errorsCount'))} / {markdown(source.get('warningsCount'))}",
            ]
            lines.append("| " + " | ".join(cells) + " |")
    else:
        lines.append("No scanner coverage metadata was reported.")

    for key in ("errors", "warnings"):
        messages = evidence.get(key) or []
        if messages:
            lines += ["", f"#### {key.capitalize()} ({len(messages)})", ""]
            lines.extend(f"- {markdown(message)}" for message in messages[:20])
            if len(messages) > 20:
                lines.append("- More diagnostics are available in the evidence artifact.")

    lines += ["", "See the `cicd-security-evidence` artifact for full findings and scanner metadata."]
    return "\n".join(lines)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("evidence", type=Path)
    args = parser.parse_args()
    try:
        print(render(json.loads(args.evidence.read_text(encoding="utf-8"))))
    except (OSError, ValueError, AttributeError, TypeError):
        print("### cicd source security\n\nScan evidence is missing or invalid. Check the scan logs and any uploaded evidence.")
        return 1
    # A valid FAIL envelope still gets reported; the workflow grades it after upload.
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
