using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesignGenerator.Core
{
    public enum Severity
    {
        Info,
        Warning,
        Error
    }

    //문제가 발생한 위치. 엑셀 record 시트의 몇 번째 데이터 행인지까지 가리킨다
    public struct SourceLocation
    {
        public string File;        // "enum.xml"
        public int RecordIndex;    // 1-based. 0 이면 파일 전체를 가리킴
        public string Field;       // "ENUM_NAME" 등. null 이면 레코드 전체

        public static SourceLocation InFile(string file)
        {
            return new SourceLocation { File = file, RecordIndex = 0, Field = null };
        }

        public static SourceLocation At(string file, int recordIndex, string field = null)
        {
            return new SourceLocation { File = file, RecordIndex = recordIndex, Field = field };
        }

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(string.IsNullOrEmpty(File) ? "?" : File);
            if (RecordIndex > 0)
                sb.Append(" #").Append(RecordIndex.ToString(CultureInfo.InvariantCulture));

            if (!string.IsNullOrEmpty(Field))
                sb.Append(' ').Append(Field);

            return sb.ToString();
        }
    }

    public sealed class Diagnostic
    {
        public Severity Severity;
        public string Code;          // "TG1003"
        public string Message;
        public SourceLocation Location;
        public string Hint;          // 고치는 방법 한 줄 (선택)

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Severity == Severity.Error ? "error" :
                       Severity == Severity.Warning ? "warning" : "info");
            sb.Append(' ').Append(Code);
            sb.Append(": [").Append(Location).Append("] ");
            sb.Append(Message);
            if (!string.IsNullOrEmpty(Hint))
                sb.Append("  → ").Append(Hint);
            return sb.ToString();
        }
    }

    public sealed class DiagnosticBag
    {
        private readonly List<Diagnostic> items = new List<Diagnostic>();

        public IReadOnlyList<Diagnostic> Items { get { return items; } }
        public int ErrorCount { get { return items.Count(x => x.Severity == Severity.Error); } }
        public int WarningCount { get { return items.Count(x => x.Severity == Severity.Warning); } }
        public bool HasError { get { return ErrorCount > 0; } }

        public void Error(string code, SourceLocation loc, string message, string hint = null)
        {
            items.Add(new Diagnostic { Severity = Severity.Error, Code = code, Location = loc, Message = message, Hint = hint });
        }

        public void Warning(string code, SourceLocation loc, string message, string hint = null)
        {
            items.Add(new Diagnostic { Severity = Severity.Warning, Code = code, Location = loc, Message = message, Hint = hint });
        }

        public void Info(string code, SourceLocation loc, string message)
        {
            items.Add(new Diagnostic { Severity = Severity.Info, Code = code, Location = loc, Message = message });
        }

        public void AddRange(DiagnosticBag other)
        {
            if (other != null) items.AddRange(other.items);
        }

        //심각도 → 파일 → 레코드 순으로 정렬
        public string ToReport(int maxItems = 200)
        {
            var sb = new StringBuilder();

            var ordered = items
                .OrderByDescending(x => x.Severity)
                .ThenBy(x => x.Location.File, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Location.RecordIndex)
                .ToList();

            foreach (var d in ordered.Take(maxItems))
                sb.AppendLine(d.ToString());

            if (ordered.Count > maxItems)
                sb.AppendLine($"... 외 {ordered.Count - maxItems}건 더 있습니다.");

            sb.AppendLine();
            sb.AppendLine($"오류 {ErrorCount}건, 경고 {WarningCount}건.");
            return sb.ToString();
        }

        //CI 용 JSON 출력
        public string ToJson()
        {
            var sb = new StringBuilder();
            sb.Append("{\"errors\":").Append(ErrorCount)
              .Append(",\"warnings\":").Append(WarningCount)
              .Append(",\"diagnostics\":[");

            for (int i = 0; i < items.Count; i++)
            {
                var d = items[i];
                if (i > 0)
                    sb.Append(',');

                sb.Append("{\"severity\":\"").Append(d.Severity.ToString().ToLowerInvariant()).Append('"')
                  .Append(",\"code\":\"").Append(J(d.Code)).Append('"')
                  .Append(",\"file\":\"").Append(J(d.Location.File)).Append('"')
                  .Append(",\"record\":").Append(d.Location.RecordIndex)
                  .Append(",\"field\":\"").Append(J(d.Location.Field)).Append('"')
                  .Append(",\"message\":\"").Append(J(d.Message)).Append('"')
                  .Append('}');
            }
            sb.Append("]}");
            return sb.ToString();
        }
        private static string J(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
        }
    }
}
