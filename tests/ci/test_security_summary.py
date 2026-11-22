import json
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
            return subprocess.run([sys.executable, str(SCRIPT), str(evidence)], capture_output=True, text=True)

    def test_passing_threshold_does_not_hide_skipped_coverage(self):
        result = self.report(json.dumps({
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
        result = self.report('{"passed": false, "outcome": "ERROR", "errors": ["scanner failed"]}')
        self.assertEqual(result.returncode, 0)
        self.assertIn("Verdict: FAIL", result.stdout)
        self.assertIn("scanner failed", result.stdout)

    def test_missing_and_invalid_evidence_fail_reporting(self):
        for evidence in (None, "{", "[]", "{}", '{"passed": "true"}', '{"passed": 1}'):
            with self.subTest(evidence=evidence):
                result = self.report(evidence)
                self.assertEqual(result.returncode, 1)
                self.assertIn("missing or invalid", result.stdout)
                self.assertNotIn("Verdict: PASS", result.stdout)

    def test_diagnostics_and_table_cells_are_escaped(self):
        result = self.report(json.dumps({
            "passed": False,
            "sources": [{"tool": "scanner|name"}],
            "warnings": ["<details>\n[link](https://example.com)"],
        }))
        self.assertEqual(result.returncode, 0)
        self.assertIn(r"scanner\|name", result.stdout)
        self.assertIn(r"&lt;details&gt; \[link\]\(https://example\.com\)", result.stdout)


if __name__ == "__main__":
    unittest.main()
