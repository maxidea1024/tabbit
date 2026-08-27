using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using NPOI.SS.UserModel;
using NPOI.SS.Util;
using NPOI.XSSF.UserModel;

namespace Clover.Authoring;

/// <summary>
/// `data/*.tsv` 를 워크북으로 렌더링한다.
/// </summary>
/// <remarks>
/// **격자는 이미 `.tsv` 에 있다.** 그래서 이 도구가 하는 것은 셀을 옮겨 적고 서식을 얹는
/// 것뿐이고, 값을 계산하거나 컬럼을 만들지 않는다. 어느 격자가 어느 워크북 어느 탭이 되는지는
/// `tools/workbooks.tsv` 가 정한다.
///
/// 팔레트는 `spec/layout/primary-layout-figures.py` 의 것이다 — 워크북이 문서의 그림과 같은 모습이
/// 되고, 대조표에서 그림과 실물을 나란히 둘 수 있다.
/// </remarks>
internal static class Program
{
    private static readonly string[] HeaderKeys = { ":field", ":type", ":desc", ":target", ":variant" };
    private static readonly Regex Integer = new(@"^-?[0-9]{1,15}$", RegexOptions.Compiled);

    private static int Main()
    {
        string tools = AppContext.BaseDirectory;
        string root = FindProjectRoot(tools);
        if (root is null)
        {
            Console.Error.WriteLine("samples/clover 를 찾지 못했습니다.");
            return 1;
        }

        string dataDir = Path.Combine(root, "data");
        Directory.CreateDirectory(Path.Combine(root, "xlsx"));
        var plan = ReadPlan(Path.Combine(root, "tools", "workbooks.tsv"));
        int locked = 0;

        foreach (var group in plan.GroupBy(entry => entry.Workbook))
        {
            var workbook = new XSSFWorkbook();
            var style = new Palette(workbook);
            int sheets = 0;
            foreach (var entry in group)
            {
                string path = Path.Combine(dataDir, entry.Grid + ".tsv");
                if (!File.Exists(path))
                {
                    Console.Error.WriteLine($"없는 격자: {entry.Grid}.tsv");
                    return 1;
                }

                Render(workbook.CreateSheet(entry.Tab), ReadGrid(path), style);
                sheets++;
            }

            string outPath = Path.Combine(root, "xlsx", group.Key + ".xlsx");
            try
            {
                using var stream = File.Create(outPath);
                workbook.Write(stream, leaveOpen: false);
            }
            catch (IOException)
            {
                // 엑셀이 그 워크북을 열고 있으면 쓸 수 없습니다. 나머지를 계속 쓰고, 어느
                // 파일이 남았는지 알려주는 편이 낫습니다 — 전체를 다시 돌리면 됩니다.
                Console.Error.WriteLine(
                    $"{Path.GetFileName(outPath),-16} 쓰지 못했습니다 — 다른 프로그램이 열고 있습니다.");
                locked++;
                continue;
            }

            Console.WriteLine($"{Path.GetFileName(outPath),-16} 탭 {sheets,2}개");
        }

        return locked == 0 ? 0 : 2;
    }

    /// <summary>`samples/clover` 를 위로 올라가며 찾는다. 빌드 산출물 안에서 실행되므로.</summary>
    private static string FindProjectRoot(string start)
    {
        var dir = new DirectoryInfo(start);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "data"))
                && File.Exists(Path.Combine(dir.FullName, "tools", "workbooks.tsv")))
                return dir.FullName;
            dir = dir.Parent;
        }

        return null;
    }

    private sealed record PlanEntry(string Workbook, string Tab, string Grid);

    private static List<PlanEntry> ReadPlan(string path)
    {
        var plan = new List<PlanEntry>();
        foreach (string line in File.ReadAllLines(path))
        {
            if (line.Length == 0 || line[0] == '#')
                continue;

            string[] cells = line.Split('\t');
            if (cells.Length >= 3)
                plan.Add(new PlanEntry(cells[0].Trim(), cells[1].Trim(), cells[2].Trim()));
        }

        return plan;
    }

    private static List<string[]> ReadGrid(string path)
        => File.ReadAllLines(path).Select(line => line.Split('\t')).ToList();

    // ------------------------------------------------------------------ 렌더링

    private static void Render(ISheet sheet, List<string[]> grid, Palette p)
    {
        int width = grid.Max(row => row.Length);
        var widths = new int[width];
        int fieldRow = -1;
        int lastHeaderRow = -1;
        int dataOrdinal = 0;

        for (int r = 0; r < grid.Count; r++)
        {
            string[] cells = grid[r];
            string marker = cells.Length > 0 ? cells[0] : "";
            RowKind kind = Classify(marker);

            if (kind == RowKind.Blank)
                continue;

            bool stripe = false;
            if (kind == RowKind.Data || kind == RowKind.Excluded)
                stripe = dataOrdinal++ % 2 == 1;

            var row = sheet.CreateRow(r);
            for (int c = 0; c < width; c++)
            {
                string value = c < cells.Length ? cells[c] : "";
                if (value.Length == 0 && kind == RowKind.Data)
                    continue;
                var cell = row.CreateCell(c);
                WriteValue(cell, value, kind);
                cell.CellStyle = p.For(kind, c == 0, value, stripe);
                if (value.Length > widths[c])
                    widths[c] = value.Length;
            }

            if (kind == RowKind.Declaration || kind == RowKind.Header)
                lastHeaderRow = Math.Max(lastHeaderRow, r);
            if (marker == ":field")
                fieldRow = r;
        }

        for (int c = 0; c < width; c++)
        {
            // 한글은 폭을 두 배로 잡는다. NPOI 의 단위는 1/256 자이다.
            int chars = Math.Clamp(widths[c] + 2, 4, 46);
            sheet.SetColumnWidth(c, chars * 300);
        }

        if (fieldRow >= 0)
        {
            int lastRow = grid.Count - 1;
            if (lastRow > fieldRow)
                sheet.SetAutoFilter(new CellRangeAddress(fieldRow, lastRow, 1, width - 1));
        }

        // 마커 열과 헤더를 고정한다. 오른쪽으로 스크롤해도 어느 행인지 보인다.
        sheet.CreateFreezePane(2, lastHeaderRow + 1);
    }

    private static void WriteValue(ICell cell, string value, RowKind kind)
    {
        // 빈 칸은 **빈 셀**이어야 합니다. 빈 문자열을 쓰면 「값이 있는 셀」이 되고, 다형 그룹의
        // 합집합 컬럼처럼 비어 있어야 하는 자리가 「그 변종의 것이 아닌 값을 담았다」로 걸립니다.
        if (value.Length == 0)
        {
            cell.SetBlank();
            return;
        }

        if (kind == RowKind.Data
            && Integer.IsMatch(value)
            && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long number))
        {
            cell.SetCellValue(number);
            return;
        }

        cell.SetCellValue(value);
    }

    private static RowKind Classify(string marker)
    {
        if (marker.StartsWith(":table", StringComparison.Ordinal)
            || marker.StartsWith(":enum", StringComparison.Ordinal)
            || marker.StartsWith(":const", StringComparison.Ordinal))
            return RowKind.Declaration;

        if (HeaderKeys.Contains(marker))
            return RowKind.Header;

        if (marker == "#" || marker == "//")
            return RowKind.Excluded;

        return marker.Length == 0 ? RowKind.Data : RowKind.Data;
    }

    private enum RowKind
    {
        Blank,
        Declaration,
        Header,
        Data,
        Excluded,
    }

    // ------------------------------------------------------------------ 서식

    /// <summary>
    /// 스타일을 키로 캐시한다.
    /// </summary>
    /// <remarks>
    /// **셀마다 `CreateCellStyle` 을 부르면 `.xlsx` 의 스타일 한도(64k)에 걸리고 파일이 몇 배로
    /// 붇는다.** 이 데이터는 셀이 10만 개를 넘으므로 캐시가 전제이다.
    /// </remarks>
    private sealed class Palette
    {
        private const string Grid = "D8DDE2";
        private const string Text = "1F2328";
        private const string Marker = "1A5FA8";
        private const string DeclBg = "E3EDF8";
        private const string HeaderBg = "F1F6FB";
        private const string Excluded = "9AA1A8";
        private const string Hash = "C0392B";
        private const string StripeBg = "F7F9FA";

        private readonly XSSFWorkbook _workbook;
        private readonly Dictionary<string, ICellStyle> _cache = new();

        public Palette(XSSFWorkbook workbook) => _workbook = workbook;

        public ICellStyle For(RowKind kind, bool markerColumn, string value, bool stripe)
        {
            bool hash = value == "#" || value == "//";
            string key = $"{kind}|{markerColumn}|{hash}|{stripe}";
            if (_cache.TryGetValue(key, out var cached))
                return cached;

            var style = _workbook.CreateCellStyle();
            Border(style);

            string fill = kind switch
            {
                RowKind.Declaration => DeclBg,
                RowKind.Header => HeaderBg,
                // 데이터 행의 교차 색상. 눈이 행을 따라가는 것을 돕는 정도까지만 칠합니다.
                _ => stripe ? StripeBg : null,
            };
            if (fill is not null)
            {
                ((XSSFCellStyle)style).SetFillForegroundColor(Color(fill));
                style.FillPattern = FillPattern.SolidForeground;
            }

            string ink = Text;
            bool bold = false;
            if (hash)
            {
                ink = Hash;
                bold = true;
            }
            else if (kind == RowKind.Excluded)
            {
                ink = Excluded;
            }
            else if (markerColumn && (kind == RowKind.Declaration || kind == RowKind.Header))
            {
                ink = Marker;
                bold = kind == RowKind.Declaration;
            }

            var font = _workbook.CreateFont();
            font.FontHeightInPoints = 10;
            font.FontName = "Malgun Gothic";
            font.IsBold = bold;
            ((XSSFFont)font).SetColor(Color(ink));
            style.SetFont(font);
            style.VerticalAlignment = VerticalAlignment.Center;

            _cache[key] = style;
            return style;
        }

        private void Border(ICellStyle style)
        {
            var s = (XSSFCellStyle)style;
            style.BorderTop = BorderStyle.Thin;
            style.BorderBottom = BorderStyle.Thin;
            style.BorderLeft = BorderStyle.Thin;
            style.BorderRight = BorderStyle.Thin;
            var line = Color(Grid);
            s.SetTopBorderColor(line);
            s.SetBottomBorderColor(line);
            s.SetLeftBorderColor(line);
            s.SetRightBorderColor(line);
        }

        private static XSSFColor Color(string hex)
            => new XSSFColor(
                new[]
                {
                    Convert.ToByte(hex.Substring(0, 2), 16),
                    Convert.ToByte(hex.Substring(2, 2), 16),
                    Convert.ToByte(hex.Substring(4, 2), 16),
                },
                null);
    }
}
