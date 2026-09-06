"""The gate must reject failed, missing, stale, or disagreeing artifacts."""

from pathlib import Path
import subprocess
import sys
import tempfile
import unittest

from native_gate import run_gate


class NativeGateTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory(prefix="barewire native gate ")
        self.addCleanup(self.temp.cleanup)
        self.sample = Path(self.temp.name)
        (self.sample / "expected.txt").write_bytes(b"agrees\n")
        self.compiler = self.sample / "compiler.py"

    def compiler_body(self, body):
        self.compiler.write_text(body)
        return [sys.executable, str(self.compiler)]

    def producing(self, program):
        return self.compiler_body(
            "from pathlib import Path\nimport sys\n"
            "p = Path(sys.argv[sys.argv.index('-o') + 1])\n"
            f"p.write_text('#!' + sys.executable + '\\n' + {program!r})\n"
            "p.chmod(0o700)\n"
        )

    def test_success_requires_fresh_agreeing_artifact(self):
        run_gate(self.producing("print('agrees')\n"), self.sample, 5)

    def test_nonzero_program_exit_fails_despite_matching_stdout(self):
        with self.assertRaises(subprocess.CalledProcessError):
            run_gate(self.producing("print('agrees')\nraise SystemExit(7)\n"), self.sample, 5)

    def test_mismatched_transcript_fails(self):
        with self.assertRaises(ValueError):
            run_gate(self.producing("print('different')\n"), self.sample, 5)

    def test_missing_oracle_is_never_generated(self):
        expected = self.sample / "expected.txt"
        expected.unlink()
        with self.assertRaises(FileNotFoundError):
            run_gate(self.producing("print('agrees')\n"), self.sample, 5)
        self.assertFalse(expected.exists())
        self.assertFalse((self.sample / "targets").exists())

    def test_stale_artifact_cannot_mask_missing_compiler_output(self):
        run_gate(self.producing("print('agrees')\n"), self.sample, 5)
        with self.assertRaises(FileNotFoundError):
            run_gate(self.compiler_body("pass\n"), self.sample, 5)

    def test_compiler_failure_is_a_failed_gate(self):
        with self.assertRaises(subprocess.CalledProcessError):
            run_gate(self.compiler_body("raise SystemExit(3)\n"), self.sample, 5)

    def test_nonterminating_program_fails(self):
        with self.assertRaises(subprocess.TimeoutExpired):
            run_gate(self.producing("import time\ntime.sleep(60)\n"), self.sample, 0.5)


if __name__ == "__main__":
    unittest.main()
