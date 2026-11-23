import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest


SCRIPT = Path(__file__).resolve().parents[2] / "scripts" / "security-summary.py"


class SecuritySummaryTests(unittest.TestCase):
    def report(self, raw_evidence):
        with tempfile.TemporaryDirectory() as directory:
            evidence = Path(directory) / "evidence-security.json"
            if raw_evidence is not None:
                evidence.write_text(raw_evidence, encoding="utf-8")
            env = dict(os.environ, GITHUB_STEP_SUMMARY=str(Path(directory) / "job.md"))
            result = subprocess.run([sys.executable, str(SCRIPT), str(evidence)],
                                    capture_output=True, text=True, env=env)
            summary = evidence.with_name("summary.md").read_text(encoding="utf-8")
            return result, summary, Path(env["GITHUB_STEP_SUMMARY"]).read_text(encoding="utf-8")

    def test_passing_threshold_does_not_hide_skipped_coverage(self):
        result, _, _ = self.report(json.dumps({
            "passed": True,
            "outcome": "FINDINGS",
            "failOnSeverity": "HIGH",
            "findingsCount": 1,
            "findingsBySeverity": {"LOW": 1},
            "sources": [{"tool": "trivy", "concern": "sca", "outcome": "SKIPPED", "findingsCount": 0}],
            "warnings": ["Dependency inventory was unavailable"],
        }))
        self.assertEqual(result.returncode, 0)
        self.assertIn("Verdict: PASS", result.stdout)
        self.assertIn("LOW: 1", result.stdout)
        self.assertIn("trivy | sca | SKIPPED | 0 | not reported", result.stdout)
        self.assertIn("Dependency inventory was unavailable", result.stdout)

    def test_failed_verdict_is_reported_without_preventing_upload(self):
        result, _, _ = self.report('{"passed": false, "outcome": "ERROR", "errors": ["scanner failed"]}')
        self.assertEqual(result.returncode, 0)
        self.assertIn("Verdict: FAIL", result.stdout)
        self.assertIn("scanner failed", result.stdout)

    def test_missing_and_invalid_evidence_fail_reporting(self):
        for evidence in (None, "{", "[]", "{}", '{"passed": "true"}', '{"passed": 1}'):
            with self.subTest(evidence=evidence):
                result, _, _ = self.report(evidence)
                self.assertEqual(result.returncode, 1)
                self.assertIn("missing or invalid", result.stdout)
                self.assertNotIn("Verdict: PASS", result.stdout)

    def test_diagnostics_and_table_cells_are_escaped(self):
        result, _, _ = self.report(json.dumps({
            "passed": False,
            "sources": [{"tool": "scanner|name"}],
            "warnings": ["<details>\n[link](https://example.com)"],
        }))
        self.assertEqual(result.returncode, 0)
        self.assertIn(r"scanner\|name", result.stdout)
        self.assertIn(r"&lt;details&gt; \[link\]\(https://example\.com\)", result.stdout)


    def test_groups_sarif_and_native_findings_and_preserves_provenance(self):
        sha = "a" * 40
        base = "https://github.com/example/project"

        def finding(line, status=None):
            return {
                "ruleIndex": 0, "message": {"text": "Review this input"},
                "suppressions": [{"kind": "inSource", "status": status}] if status else [],
                "locations": [{"physicalLocation": {"artifactLocation": {"index": 0},
                               "region": {"startLine": line, "endLine": line + 1}}}],
            }

        result, summary, job_summary = self.report(json.dumps({
            "passed": False, "findingsCount": 3,
            "repo": {"repoUrl": base, "branch": "feature/example", "commitSha": sha},
            "sources": [{"tool": "Roslyn", "examined": {"count": 8, "total": 10, "partial": 2, "unit": "files"}}],
            "findings": [{"tool": "Trivy", "severity": "CRITICAL", "rule": "CVE-EXAMPLE",
                          "message": "Update the dependency", "artifactPath": "project.assets.json"}],
            "sarifReport": {"runs": [{
                "tool": {"driver": {"name": "Roslyn", "rules": [
                    {"id": "example-rule", "defaultConfiguration": {"level": "warning"}},
                ]}},
                "artifacts": [{"location": {"uri": "src/My%20File.cs"}}],
                "results": [finding(2), finding(4, "rejected"), finding(7, "accepted")],
                "invocations": [{"toolExecutionNotifications": [{"message": {"text": "Parser warning"}}]}],
            }]},
        }))
        self.assertEqual(result.returncode, 0)
        self.assertEqual(summary, job_summary)
        self.assertIn("8 / 10 files (2 partial)", summary)
        self.assertIn("Parser warning", result.stdout)
        self.assertIn(f"{base}/commit/{sha}", summary)
        self.assertIn(f"{base}/blob/{sha}/src/My%20File.cs#L2-L3", summary)
        self.assertIn("#L4-L5", summary)
        self.assertNotIn("#L7", summary)
        self.assertIn("2 active SARIF, 1 native; 1 suppressed", summary)
        self.assertIn("MEDIUM · Roslyn · 2 result(s)", summary)
        self.assertLess(summary.index("CRITICAL · Trivy"), summary.index("MEDIUM · Roslyn"))

    def test_unsafe_locations_and_console_commands_remain_text(self):
        result, summary, _ = self.report(json.dumps({
            "passed": False, "warnings": ["line one\n::error::not a command"],
            "repo": {"repoUrl": "https://github.com/example/project", "commitSha": "a" * 40},
            "findings": [{"artifactPath": path, "line": 1} for path in (
                "../outside.cs", "%2e%2e/outside.cs", "file:///tmp/example.cs", "/tmp/example.cs",
            )],
        }))
        self.assertEqual(result.returncode, 0)
        self.assertNotIn("/blob/", summary)
        self.assertIn("file:///tmp/example", summary)
        self.assertNotIn("::error::", result.stdout)
        self.assertTrue(all(line.startswith("security: ") for line in result.stdout.splitlines()))

    def test_job_summary_is_bounded_but_artifact_is_complete(self):
        result, summary, job_summary = self.report(json.dumps({
            "passed": False, "warnings": ["é" * 500] * 1000 + ["last diagnostic"],
        }))
        self.assertEqual(result.returncode, 0)
        self.assertIn("last diagnostic", summary)
        self.assertIn("Summary truncated", job_summary)
        self.assertNotIn("last diagnostic", job_summary)
        self.assertLess(len(job_summary.encode("utf-8")), 1024 * 1024)


if __name__ == "__main__":
    unittest.main()
