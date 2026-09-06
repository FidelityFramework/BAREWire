#!/usr/bin/env python3
"""Compile and execute RoundTrip, then compare its exact bytes with the oracle."""

import argparse
from pathlib import Path
import subprocess
import sys


def run_gate(composer, sample, timeout):
    # Read the independent oracle first. Never create it from the run under test.
    expected = (sample / "expected.txt").read_bytes()
    binary = sample / "targets" / "roundtrip"
    binary.parent.mkdir(parents=True, exist_ok=True)
    # A compiler that returns success without producing an artifact must not pass
    # by running an executable left behind by a previous compiler.
    binary.unlink(missing_ok=True)
    subprocess.run(
        [*composer, "compile", "RoundTrip.fidproj", "-o", str(binary), "-k"],
        cwd=sample, check=True, timeout=timeout,
    )
    result = subprocess.run(
        [str(binary)], cwd=sample, check=True, timeout=timeout,
        stdout=subprocess.PIPE,
    )
    if result.stdout != expected:
        raise ValueError("native transcript differs from samples/RoundTrip/expected.txt")


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("composer", type=Path, help="the Composer executable to verify")
    parser.add_argument("--timeout", type=float, default=180, help="seconds per process")
    args = parser.parse_args()
    sample = Path(__file__).resolve().parents[1] / "samples" / "RoundTrip"
    try:
        run_gate([str(args.composer.resolve())], sample, args.timeout)
    except (OSError, ValueError, subprocess.SubprocessError) as error:
        print(f"native gate failed: {error}", file=sys.stderr)
        return 1
    print("native differential: agrees")
    return 0


if __name__ == "__main__":
    sys.exit(main())
