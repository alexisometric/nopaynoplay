#!/usr/bin/env python3
"""Fail when the line coverage of the plugin assembly is below a floor.

Usage: check-coverage.py <coverage.cobertura.xml> [minimum-line-rate] [package]

Why a script and not a coverlet threshold: `coverlet.collector` produces an
all-zero report on .NET 10 (the instrumented module is reloaded without hits),
so coverage used to be either absent from CI or meaningless. The report is now
produced by `dotnet-coverage` (see .config/dotnet-tools.json) and filtered here
on the plugin package, excluding the test assembly.
"""

from __future__ import annotations

import sys
import xml.etree.ElementTree as ET


def main() -> int:
    if len(sys.argv) < 2:
        print("usage: check-coverage.py <coverage.cobertura.xml> [min] [package]", file=sys.stderr)
        return 2

    path = sys.argv[1]
    minimum = float(sys.argv[2]) if len(sys.argv) > 2 else 0.40
    package = sys.argv[3] if len(sys.argv) > 3 else "Jellyfin.Plugin.NoPayNoPlay"

    root = ET.parse(path).getroot()

    covered = valid = 0
    for pkg in root.iter("package"):
        if pkg.get("name") != package:
            continue
        for cls in pkg.iter("class"):
            for line in cls.iter("line"):
                valid += 1
                if int(line.get("hits", "0")) > 0:
                    covered += 1

    if valid == 0:
        print(f"::error::No instrumented lines found for package '{package}'")
        return 1

    rate = covered / valid
    print(f"{package}: {covered}/{valid} lines = {rate:.1%} (floor {minimum:.0%})")
    if rate < minimum:
        print(f"::error::Line coverage {rate:.1%} is below the {minimum:.0%} floor")
        return 1
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
