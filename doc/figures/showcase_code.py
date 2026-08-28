# -*- coding: utf-8 -*-
"""골든 트리에서 레코드 타입의 선언을 그대로 오려 온다.

문서가 「이 시트는 이런 코드가 됩니다」라고 말하려면 오른쪽에 놓을 코드가 필요하고, 그것을
사람이 옮겨 적으면 생성기가 바뀌어도 문서는 그대로 남습니다. 그래서 여기서 오려 오는 것은
회귀 테스트가 매번 비교하는 `test/fixtures/golden/doc-showcase/` 의 파일들입니다.

**언어마다 규칙을 따로 두지 않습니다.** 선언을 찾는 방법 하나와, 그 선언이 어디서 끝나는지를
정하는 방법 셋(중괄호·들여쓰기·빈 줄)뿐입니다. 언어가 늘어도 표에 한 줄이 늘 뿐입니다."""
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
REPO = os.path.dirname(os.path.dirname(HERE))
GOLDEN = os.path.join(REPO, "test", "fixtures", "golden", "doc-showcase")

# (골든 안의 디렉터리, 보이는 이름, 코드 펜스 이름, 종류별 타입 이름의 꼴)
#
# 순서가 문서의 탭 순서입니다. 앞의 넷은 이 도구를 쓰는 프로젝트에서 가장 자주 고르는
# 것들이고, 나머지는 이름 순입니다.
#
# **타입 이름의 꼴을 여기 적는 이유**는 언어마다 관례가 다르기 때문입니다 - 대부분은
# `<엔티티>Record` 이지만 C 는 접두어와 `_t` 를 붙이고, 언리얼은 `F` 를 붙이고 `Row` 로
# 끝냅니다. 규칙을 코드로 유추하면 관례가 하나 바뀔 때마다 유추가 늘어나므로, 적어 둡니다.
# 대부분의 언어가 같은 꼴을 씁니다.
PLAIN = {"record": "{entity}Record", "enum": "{entity}", "const": "{entity}"}

LANGUAGES = [
    ("csharp", "C#", "csharp", PLAIN),
    ("typescript", "TypeScript", "typescript", PLAIN),
    ("cpp", "C++", "cpp", PLAIN),
    ("python", "Python", "python", PLAIN),
    ("c", "C", "c", {"record": "DocShowcase_{entity}Record_t",
                     "enum": "DocShowcase_{entity}_t",
                     "const": "{entity}"}),
    ("dart", "Dart", "dart", PLAIN),
    ("go", "Go", "go", PLAIN),
    ("java", "Java", "java", PLAIN),
    ("kotlin", "Kotlin", "kotlin", PLAIN),
    ("lua", "Lua", "lua", PLAIN),
    ("php", "PHP", "php", PLAIN),
    ("ruby", "Ruby", "ruby", PLAIN),
    ("rust", "Rust", "rust", PLAIN),
    ("swift", "Swift", "swift", PLAIN),
    ("unreal", "Unreal", "cpp", {"record": "F{entity}Row",
                                 "enum": "E{entity}",
                                 "const": "F{entity}"}),
]

# 선언이 끝나는 자리를 정하는 방법. 확장자로 고릅니다.
BRACES = {".cs", ".ts", ".h", ".hpp", ".cpp", ".c", ".go", ".rs", ".java",
          ".kt", ".swift", ".dart", ".php"}
INDENT = {".py", ".rb"}
BLANK = {".lua"}

# 선언 위에 붙어 있는 것들 - 문서 주석, 어트리뷰트, 매크로.
ABOVE = re.compile(r"^\s*(//|///|#\s|#$|--|\*|/\*|\*/|@|#\[|\[|UPROPERTY|USTRUCT|GENERATED)")

# 컴파일러가 남긴 것과 받은 패키지. 산출물이 아니므로 보지 않습니다. 리더(`tabbit/`)는
# 여기서 빼지 않습니다 - 자바와 코틀린은 패키지 경로가 `tabbit` 으로 시작하고, 리더 파일에
# 레코드 선언이 있을 리도 없기 때문입니다.
SKIP_DIRS = {"node_modules", "obj", "bin", "target"}


def _declaration_line(lines, type_name):
    """레코드 타입을 선언하는 줄. 없으면 None."""
    patterns = [
        # class · struct · type · interface 뒤나 앞에 이름이 오는 모든 형태.
        re.compile(r"\b(class|struct|type|interface|record)\b[^\n]*\b%s\b" % re.escape(type_name)),
        # Lua 는 선언이 주석 어노테이션입니다.
        re.compile(r"^\s*---@class\s+%s\b" % re.escape(type_name)),
    ]

    for i, line in enumerate(lines):
        # 앞선 줄에 이름만 나오는 것 - 전방 선언과 주석 - 은 고르지 않습니다.
        if line.strip().endswith(";") and "{" not in line:
            continue
        for p in patterns:
            if p.search(line):
                return i
    return None


def _start(lines, at):
    """선언 위에 붙은 주석과 어트리뷰트까지 거슬러 올라간 자리."""
    i = at
    while i > 0 and ABOVE.match(lines[i - 1]) and lines[i - 1].strip() != "":
        i -= 1
    return i


def _end_braces(lines, at):
    depth = 0
    opened = False
    for i in range(at, len(lines)):
        depth += lines[i].count("{") - lines[i].count("}")
        if "{" in lines[i]:
            opened = True
        if opened and depth <= 0:
            return i + 1
    return len(lines)


def _end_indent(lines, at):
    """들여쓰기가 선언과 같은 자리로 돌아오면 끝입니다."""
    base = len(lines[at]) - len(lines[at].lstrip())
    end = at + 1
    for i in range(at + 1, len(lines)):
        stripped = lines[i].strip()
        if stripped == "":
            continue
        indent = len(lines[i]) - len(lines[i].lstrip())
        if indent <= base:
            return end
        end = i + 1
    return end


def _end_blank(lines, at):
    for i in range(at, len(lines)):
        if lines[i].strip() == "":
            return i
    return len(lines)


def _dedent(code):
    """공통 들여쓰기를 걷어냅니다. 네임스페이스 안에 있던 선언이 문서에서 4칸 들어가 보이지
    않도록 하는 것이고, 안쪽의 상대 들여쓰기는 그대로입니다."""
    lines = code.split("\n")
    depths = [len(l) - len(l.lstrip()) for l in lines if l.strip()]
    cut = min(depths) if depths else 0
    return "\n".join(l[cut:] if l.strip() else "" for l in lines)


def _files(root):
    for dirpath, dirnames, filenames in os.walk(root):
        dirnames[:] = [d for d in dirnames if d not in SKIP_DIRS]
        for name in sorted(filenames):
            yield os.path.join(dirpath, name)


def declaration(language, type_name):
    """그 언어의 골든에서 타입 선언 하나를 오려 (코드, 파일 경로) 로 돌려줍니다."""
    root = os.path.join(GOLDEN, language)
    if not os.path.isdir(root):
        raise LookupError("%s 의 골든이 없습니다." % language)

    for path in _files(root):
        ext = os.path.splitext(path)[1]
        if ext not in BRACES and ext not in INDENT and ext not in BLANK:
            continue

        with open(path, encoding="utf-8") as f:
            lines = f.read().replace("\r\n", "\n").split("\n")

        at = _declaration_line(lines, type_name)
        if at is None:
            continue

        start = _start(lines, at)
        if ext in BRACES:
            end = _end_braces(lines, at)
        elif ext in INDENT:
            end = _end_indent(lines, at)
        else:
            end = _end_blank(lines, at)

        code = _dedent("\n".join(lines[start:end]).rstrip())
        return code, os.path.relpath(path, REPO).replace("\\", "/")

    raise LookupError("%s 의 골든에 `%s` 선언이 없습니다." % (language, type_name))


# 파일 맨 위의 "DO NOT EDIT" 배너. 언어마다 주석 기호만 다른 같은 글이고, 탭 15개에 같은 것이
# 15번 실리면 정작 볼 것이 밀려납니다. 문서가 한 번 대신 말합니다.
def _without_banner(lines):
    i = 0
    while i < len(lines) and (lines[i].strip() == ""
                              or lines[i].lstrip()[:2] in ("//", "# ", "--", "#-")
                              or lines[i].strip() in ("#", "--")):
        i += 1
    return lines[i:]


def whole_file(language, entity, fallback_type=None):
    """엔티티 이름이 붙은 파일 하나를 통째로. enum과 상수셋처럼 짧은 것에 씁니다.

    이름이 붙은 파일이 없는 언어 - 전부를 헤더 하나에 내는 언리얼이 그렇습니다 - 는 선언
    하나를 오려 오는 쪽으로 넘깁니다."""
    root = os.path.join(GOLDEN, language)
    if not os.path.isdir(root):
        raise LookupError("%s 의 골든이 없습니다." % language)

    key = entity.lower()

    for path in _files(root):
        ext = os.path.splitext(path)[1]
        if ext not in BRACES and ext not in INDENT and ext not in BLANK:
            continue

        stem = "".join(c for c in os.path.basename(path).lower() if c.isalnum())
        if key not in stem:
            continue

        with open(path, encoding="utf-8") as f:
            lines = f.read().replace("\r\n", "\n").split("\n")

        return (_dedent("\n".join(_without_banner(lines)).strip()),
                os.path.relpath(path, REPO).replace("\\", "/"))

    if fallback_type:
        return declaration(language, fallback_type)

    raise LookupError("%s 의 골든에 `%s` 파일이 없습니다." % (language, entity))


if __name__ == "__main__":
    import sys
    sys.stdout.reconfigure(encoding="utf-8")

    entity = sys.argv[1] if len(sys.argv) > 1 else "Potion"
    kind = sys.argv[2] if len(sys.argv) > 2 else "record"
    for lang, label, _, shapes in LANGUAGES:
        try:
            if kind == "record":
                code, path = declaration(lang, shapes[kind].format(entity=entity))
            else:
                code, path = whole_file(lang, entity, shapes[kind].format(entity=entity))
            print("### %-12s %d줄  %s" % (label, len(code.split("\n")), path))
        except LookupError as e:
            print("### %-12s 없음 - %s" % (label, e))
