#!/usr/bin/env python3
"""Check i18n key parity across src/Localization/strings.*.json vs strings.en.json.

- FAILS on EXTRA keys in a non-English bundle (typos, renamed keys leaving orphans).
- Emits one GHA warning annotation per language for MISSING keys (not translated yet;
  the runtime fallback to English covers them, but they should eventually be filled).

Use --require-full (e.g. before a release) to fail on missing keys too.
Exit code 0 on success, 1 on problems.
"""
import argparse
import json
import sys
from pathlib import Path

LOC_DIR = Path(__file__).resolve().parent.parent / "src" / "Localization"

# CLDR plural categories that English never uses. Some languages (ru, uk, sr, hr,
# cs, sk, pl) legitimately carry `.few`/`.many`/`.zero`/`.two` variants of a key that
# has no English equivalent — those are NOT orphans, so ignore them in the extra check.
PLURAL_ONLY_CATEGORIES = {"few", "many", "zero", "two"}


def is_plural_extra(key: str) -> bool:
    return key.rsplit(".", 1)[-1] in PLURAL_ONLY_CATEGORIES


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__)
    ap.add_argument(
        "--require-full",
        action="store_true",
        help="fail on missing keys too (strict mode, e.g. before a release)",
    )
    args = ap.parse_args()

    en_path = LOC_DIR / "strings.en.json"
    en = json.loads(en_path.read_text(encoding="utf-8"))
    problems = 0

    for f in sorted(LOC_DIR.glob("strings.*.json")):
        lang = f.stem.split(".")[1]
        if lang == "en":
            continue
        d = json.loads(f.read_text(encoding="utf-8"))
        missing = sorted(set(en) - set(d))
        extra = sorted(k for k in set(d) - set(en) if not is_plural_extra(k))

        if extra:
            problems += 1
            sample = ", ".join(extra[:8])
            print(
                f"::error::i18n {lang}: {len(extra)} clé(s) orpheline(s) absente(s) "
                f"de strings.en.json: {sample}"
            )

        if missing:
            if args.require_full:
                problems += 1
                sample = ", ".join(missing[:8])
                print(
                    f"::error::i18n {lang}: {len(missing)} clé(s) manquante(s) "
                    f"(--require-full): {sample}"
                )
            else:
                sample = ", ".join(missing[:5])
                print(
                    f"::warning::i18n {lang}: {len(missing)} clé(s) non traduite(s) "
                    f"vs en (fallback EN) — ex: {sample}"
                )

    if problems:
        print(f"i18n parity FAILED: {problems} problème(s)")
        return 1
    print("i18n parity OK")
    return 0


if __name__ == "__main__":
    sys.exit(main())
