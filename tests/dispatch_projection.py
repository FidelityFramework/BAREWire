#!/usr/bin/env python3
"""Reject drift between the hosted scalar equations and their Clef projection."""
from pathlib import Path
import re

root = Path(__file__).resolve().parents[1]
source = (root / "src/Platform/DispatchRegions.fs").read_text()
projection = (root / "src/Platform/DispatchRegions.Clef.fs").read_text()
start = "    [<Literal>]"
hosted = source[source.index(start):source.index("    // This implementation represents")]
expected = re.sub(r"(?<=\d)L\b", "", hosted.replace("int64", "int"))
actual = projection[projection.index(start):]
if actual != expected:
    raise SystemExit("Clef dispatch scalar projection differs from hosted equations")
print("Clef dispatch projection: exact scalar equations preserved")
