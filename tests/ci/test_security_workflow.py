import os
from pathlib import Path
import re
import subprocess
import tempfile
import textwrap
import unittest


ROOT = Path(__file__).resolve().parents[2]
WORKFLOW = (ROOT / ".github/workflows/cicd-security.yml").read_text(encoding="utf-8")


def run_block(step):
    body = WORKFLOW.split(f"      - name: {step}\n", 1)[1]
    return textwrap.dedent(re.search(r"^        run: \|\n((?:^          .*\n)+)", body, re.M)[1])


class SecurityWorkflowTests(unittest.TestCase):
    def test_execution_error_is_preserved_and_never_reported_as_a_pass(self):
        with tempfile.TemporaryDirectory() as directory:
            runner = Path(directory)
            evidence = runner / "cicd-security-evidence"
            evidence.mkdir()
            raw = evidence / "evidence-security.json"
            raw.write_text('{"passed": true}', encoding="utf-8")
            env = dict(os.environ, RUNNER_TEMP=directory, GITHUB_SHA="a" * 40,
                       GITHUB_SERVER_URL="https://github.com", GITHUB_REPOSITORY="example/project",
                       GITHUB_STEP_SUMMARY=str(runner / "job.md"), SCAN_BRANCH="feature/example",
                       CICD_SECURITY_MODULE="pinned-module", SCAN_OUTCOME="failure")
            fake_dagger = 'dagger() { echo "test diagnostic" >&2; return 17; }\n'
            scan = subprocess.run(["bash", "-e", "-c", fake_dagger + run_block("Scan source")],
                                  cwd=ROOT, env=env, capture_output=True, text=True)
            self.assertEqual(scan.returncode, 17, scan.stderr)
            self.assertFalse(raw.exists())
            self.assertNotIn("test diagnostic", scan.stderr)
            self.assertIn("test diagnostic", (evidence / "execution.log").read_text(encoding="utf-8"))

            # A failed export may leave a partial file with an apparent PASS.
            raw.write_text('{"passed": true}', encoding="utf-8")
            for outcome in ("failure", "skipped"):
                with self.subTest(outcome=outcome):
                    report = subprocess.run(["bash", "-e", "-c", run_block("Summarize evidence")],
                                            cwd=ROOT, env=dict(env, SCAN_OUTCOME=outcome),
                                            capture_output=True, text=True)
                    self.assertEqual(report.returncode, 0, report.stderr)
                    summary = (evidence / "summary.md").read_text(encoding="utf-8")
                    self.assertIn(f"No verified scan result (scan {outcome})", summary)
                    self.assertNotIn("Verdict: PASS", summary)

    def test_artifact_upload_precedes_verdict_even_after_failure(self):
        upload, verdict = WORKFLOW.split("      - name: Upload evidence\n", 1)[1].split("      - name: Verdict\n", 1)
        self.assertIn("if: ${{ !cancelled() }}", upload)
        for file in ("summary.md", "execution.log", "evidence-security.json"):
            self.assertIn(f"cicd-security-evidence/{file}", upload)
        self.assertIn("jq -e '.passed == true'", verdict)


if __name__ == "__main__":
    unittest.main()
