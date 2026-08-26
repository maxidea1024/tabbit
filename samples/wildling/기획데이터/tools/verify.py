# -*- coding: utf-8 -*-
"""`samples/wildling` 을 대조한다.

**되는지가 아니라 맞는지를 봅니다.** 변환이 성공으로 끝나도 확인되지 않는 것이 있습니다 —
`csharp` 타깃은 파일을 쓰기만 하고 컴파일하지 않고, 리더가 와이어를 무엇으로 검사하는지는
언어마다 따로입니다. 그 자리들을 여기서 봅니다.

`doc/도구-보고.md` 의 항목이 고쳐졌는지 확인하는 절차이기도 합니다 — 우회를 되돌릴 수 있게
되면 그 항목이 닫힙니다.

    python samples/wildling/기획데이터/tools/verify.py            # 전부
    python samples/wildling/기획데이터/tools/verify.py --list      # 검사 목록만

저장소 루트에서 돌리는 것을 전제하지 않습니다 — 스크립트 위치에서 찾아 올라갑니다.
"""
import glob
import io
import json
import os
import re
import shutil
import subprocess
import sys

sys.stdout.reconfigure(encoding="utf-8", errors="replace")

HERE = os.path.dirname(os.path.abspath(__file__))
DESIGN = os.path.normpath(os.path.join(HERE, ".."))
SAMPLE = os.path.normpath(os.path.join(DESIGN, ".."))
ROOT = os.path.normpath(os.path.join(SAMPLE, "..", ".."))

DATA = os.path.join(DESIGN, "data")
RECIPE = os.path.join(DESIGN, "recipe.jsonc")
GENERATED = os.path.join(SAMPLE, "unity", "Assets", "Tabbit", "Generated")
TABLES = os.path.join(SAMPLE, "unity", "Assets", "StreamingAssets", "tables")
SCRATCH = os.path.join(DESIGN, ".verify")

results = []


def report(name, ok, detail="", note=""):
    results.append((name, ok, detail, note))
    mark = {True: "  OK  ", False: "  !!  ", None: "  --  "}[ok]
    print("%s %-38s %s" % (mark, name, detail))
    if note:
        print("       %s" % note)


# ---------------------------------------------------------------- 격자 읽기

def grids(path):
    out, cur = [], None
    for line in io.open(path, encoding="utf-8"):
        c = line.rstrip("\n").split("\t")
        head = c[0]
        if head[:6] == ":table" or head[:5] == ":enum" or head[:6] == ":const":
            cur = {
                "kind": head.split()[0][1:],
                "name": head.split()[1].split("(")[0],
                "meta": head[head.find("(") + 1:head.rfind(")")] if "(" in head else "",
                "fields": [], "types": [], "rows": [],
            }
            out.append(cur)
        elif head == ":field" and cur is not None:
            cur["fields"] = c[1:]
        elif head == ":type" and cur is not None:
            cur["types"] = c[1:]
        elif head in (":desc", ":target", ":variant"):
            pass
        elif cur is not None and head == "" and any(x for x in c[1:]):
            cur["rows"].append(c[1:])
    return out


def all_grids():
    for f in sorted(glob.glob(os.path.join(DATA, "*.tsv"))):
        for e in grids(f):
            yield e


def camel(name):
    name = name.split("@")[0].lstrip("*")
    parts = [w for w in name.split("_") if w]
    return parts[0] + "".join(w[:1].upper() + w[1:] for w in parts[1:])


# ---------------------------------------------------------------- 1. 데이터 자체

def check_references():
    """참조가 대상을 찾는가. 변환의 검사와 별개로, 데이터가 맞는지를 먼저 본다."""
    keys, refs = {}, []

    for e in all_grids():
        if e["kind"] != "table":
            continue

        # 기본 인덱스. `key=` 가 있으면 그것이고, 복합이면 참조 대상이 되지 않는다.
        primary = None
        m = re.search(r'key="?([^";]+)"?', e["meta"])
        if m and "," not in m.group(1):
            primary = m.group(1).strip()
        elif not m and e["fields"]:
            primary = e["fields"][0].split("@")[0].lstrip("*")

        if primary:
            idx = next((i for i, n in enumerate(e["fields"])
                        if n.split("@")[0].lstrip("*") == primary), None)
            if idx is not None:
                keys[e["name"]] = set(
                    r[idx] for r in e["rows"] if idx < len(r) and r[idx])

        for i, name in enumerate(e["fields"]):
            t = e["types"][i] if i < len(e["types"]) else ""
            m = re.match(r"foreign\s+([A-Za-z0-9_]+)", t)
            if m:
                refs.append((e, i, name, m.group(1)))

    missing = []
    for e, i, name, target in refs:
        for r in e["rows"]:
            if i >= len(r) or not r[i] or r[i] == "-":
                continue
            for v in r[i].split(";"):
                if v and v not in keys.get(target, set()):
                    missing.append("%s.%s -> %s : %s" % (e["name"], name, target, v))

    report("참조가 대상을 찾는가", not missing,
           "참조 컬럼 %d개 · 미해결 %d개" % (len(refs), len(missing)),
           missing[0] if missing else "")


def check_composite_keys():
    """복합 키의 조합이 유일한가. 모델이 검사하지만, 데이터 쪽에서 먼저 본다."""
    bad = []
    total = 0
    for e in all_grids():
        if e["kind"] != "table":
            continue
        m = re.search(r'key="([^"]+)"', e["meta"])
        if not m:
            continue
        for key in m.group(1).split(";"):
            parts = [p.strip() for p in key.split(",") if p.strip()]
            if len(parts) < 2:
                continue
            total += 1
            idx = [next((i for i, n in enumerate(e["fields"])
                         if n.split("@")[0].lstrip("*") == p), None) for p in parts]
            if any(i is None for i in idx):
                continue
            seen = set()
            for r in e["rows"]:
                if not r or not r[0]:
                    continue
                combo = tuple(r[i] if i < len(r) else "" for i in idx)
                if combo in seen:
                    bad.append("%s(%s) : %s" % (e["name"], key.strip(), "|".join(combo)))
                seen.add(combo)

    report("복합 키의 조합이 유일한가", not bad,
           "복합 키 %d개 · 겹침 %d개" % (total, len(bad)),
           bad[0] if bad else "")


# ---------------------------------------------------------------- 2. 변환

def run(args, cwd=ROOT):
    return subprocess.run(args, cwd=cwd, capture_output=True, text=True,
                          encoding="utf-8", errors="replace")


def check_convert():
    p = run(["dotnet", "run", "--project", "src/Tabbit.csproj", "--",
             "--recipe", os.path.relpath(RECIPE, ROOT).replace("\\", "/")])
    out = p.stdout + p.stderr
    ok = "All work is done successfully" in out
    fatal = [l for l in out.splitlines() if l.startswith("[F]")]
    report("변환이 끝까지 도는가", ok, "" if ok else "%d줄이 [F]" % len(fatal),
           fatal[0][:150] if fatal else "")
    return out


def check_validation(out):
    """검증이 실제로 돌았는가. 규칙은 액세서를 컴파일해 돌기 때문에 컴파일의 뒤이다."""
    if "does not compile" in out:
        report("검증 규칙이 도는가", False, "액세서가 컴파일되지 않습니다",
               "도구 보고 §7 · §8")
        return
    ran = re.findall(r"\[rules/([^\]]+)\]", out)
    summary = re.search(r"Validation: (\d+) error\(s\), (\d+) warning\(s\)", out)

    # 빌드 캐시가 산 실행은 검증을 다시 돌리지 않으므로 규칙이 말하지 않습니다. 그때는 이
    # 검사가 답할 것이 없으니 건너뜁니다 — 통과로 세면 캐시가 게이트를 통과시킵니다.
    if not ran and not summary:
        report("검증 규칙이 도는가", None, "캐시된 실행입니다 — `.tabbit` 을 지우고 다시")
        return

    detail = "규칙 %d개" % len(set(ran)) if ran else ""
    if summary:
        detail += " · 오류 %s · 경고 %s" % (summary.group(1), summary.group(2))

    report("검증 규칙이 도는가", bool(ran) and summary is not None and summary.group(1) == "0",
           detail.strip(" ·"))


# ---------------------------------------------------------------- 3. 생성 코드

def check_csharp_compiles():
    """**`csharp` 타깃은 컴파일하지 않는다.** 그래서 여기서 한다."""
    if not shutil.which("dotnet"):
        report("생성 C#이 컴파일되는가", None, "dotnet 이 없습니다")
        return

    if os.path.isdir(SCRATCH):
        shutil.rmtree(SCRATCH, ignore_errors=True)
    os.makedirs(SCRATCH)

    io.open(os.path.join(SCRATCH, "Check.csproj"), "w", encoding="utf-8").write(
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>netstandard2.1</TargetFramework>
    <Nullable>disable</Nullable>
    <ImplicitUsings>disable</ImplicitUsings>
    <LangVersion>latest</LangVersion>
    <EnableDefaultCompileItems>false</EnableDefaultCompileItems>
    <NoWarn>CS1591</NoWarn>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="%s/**/*.cs" />
  </ItemGroup>
</Project>
""" % GENERATED.replace("\\", "/"))

    p = run(["dotnet", "build", "-v", "q", "--nologo"], cwd=SCRATCH)
    errors = sorted(set(re.findall(r"error (CS\d+)", p.stdout + p.stderr)))
    ok = not errors
    report("생성 C#이 컴파일되는가", ok,
           "" if ok else "오류 종류 %s" % ", ".join(errors),
           "" if ok else "netstandard2.1 로 컴파일했습니다 — 유니티도 같습니다")
    shutil.rmtree(SCRATCH, ignore_errors=True)


def check_reader_kinds():
    """리더가 와이어를 무엇으로 검사하는가. 언어마다 따로 적히므로 갈릴 수 있다."""
    if not os.path.isdir(GENERATED):
        report("C# 리더가 배열을 배열로 보는가", None, "생성물이 없습니다")
        return

    arrays = []
    for e in all_grids():
        if e["kind"] != "table":
            continue
        for i, name in enumerate(e["fields"]):
            t = e["types"][i] if i < len(e["types"]) else ""
            if t.split("(")[0].strip().endswith("[]") and "[" not in name:
                arrays.append((e["name"], camel(name)[:1].upper() + camel(name)[1:]))

    bad = []
    for table, field in arrays:
        f = os.path.join(GENERATED, "tables", table + "Table.cs")
        if not os.path.exists(f):
            continue
        text = io.open(f, encoding="utf-8").read()
        m = re.search(r'CheckColumn\(column, "%s\.%s", TcbTable\.(\w+)' % (table, field), text)
        if m and m.group(1) != "KindArray":
            bad.append("%s.%s -> %s" % (table, field, m.group(1)))

    report("C# 리더가 배열을 배열로 보는가", not bad,
           "셀 배열 컬럼 %d개" % len(arrays),
           bad[0] + " (도구 보고 §9)" if bad else "")


# ---------------------------------------------------------------- 4. 우회가 남아 있는가

def check_workarounds():
    """우회는 결함의 자리표이다. 지워지면 그 항목이 닫힌다."""
    recipe = io.open(RECIPE, encoding="utf-8").read()

    report("Naming 을 error 로 둘 수 있는가",
           '"OnViolation": "error"' in recipe,
           "지금 warn 입니다" if '"warn"' in recipe else "",
           "도구 보고 §2" if '"warn"' in recipe else "")

    dashes = 0
    for e in all_grids():
        groups = [n[:-len(".$type")] for n in e["fields"] if n.endswith(".$type")]
        if not groups:
            continue
        union = [i for i, n in enumerate(e["fields"])
                 if any(n.startswith(g + ".") for g in groups) and not n.endswith(".$type")]
        for r in e["rows"]:
            dashes += sum(1 for i in union if i < len(r) and r[i] == "-")

    report("다형 합집합의 빈 칸을 비울 수 있는가", dashes == 0,
           "지금 `-` 가 %d칸" % dashes, "도구 보고 §1" if dashes else "")

    arrays = sum(1 for e in all_grids() if e["kind"] == "const"
                 for i, n in enumerate(e["fields"]) if n == "type"
                 for r in e["rows"] if i < len(r) and r[i].endswith("[]") and r[i] != "int[]")
    report("`string[]` 상수를 되살릴 수 있는가", arrays > 0,
           "지금 0개" if not arrays else "%d개" % arrays,
           "도구 보고 §4" if not arrays else "")

    report("`Boss` 를 되돌릴 수 있는가", '"Boss"' not in recipe,
           "지금 ExcludeSheets 에 있습니다" if '"Boss"' in recipe else "",
           "다형과 참조 배열 §11 의 5단계" if '"Boss"' in recipe else "")


# ---------------------------------------------------------------- 실행

CHECKS = [
    ("참조와 키", [check_references, check_composite_keys]),
    ("변환", None),
    ("생성 코드", [check_csharp_compiles, check_reader_kinds]),
    ("우회", [check_workarounds]),
]


def main():
    if "--list" in sys.argv:
        for title, fns in CHECKS:
            print(title)
        return 0

    print("=" * 62)
    print("데이터 자체")
    print("=" * 62)
    check_references()
    check_composite_keys()

    print()
    print("=" * 62)
    print("변환")
    print("=" * 62)
    out = check_convert()
    check_validation(out)

    print()
    print("=" * 62)
    print("생성 코드 — 변환이 확인하지 않는 것")
    print("=" * 62)
    check_csharp_compiles()
    check_reader_kinds()

    print()
    print("=" * 62)
    print("우회가 남아 있는가 — 지워지면 그 항목이 닫힙니다")
    print("=" * 62)
    check_workarounds()

    failed = [name for name, ok, _, _ in results if ok is False]
    skipped = [name for name, ok, _, _ in results if ok is None]

    print()
    print("-" * 62)
    print("통과 %d · 실패 %d · 건너뜀 %d"
          % (len(results) - len(failed) - len(skipped), len(failed), len(skipped)))
    for name in failed:
        print("  !! %s" % name)

    return 1 if failed else 0


if __name__ == "__main__":
    sys.exit(main())
