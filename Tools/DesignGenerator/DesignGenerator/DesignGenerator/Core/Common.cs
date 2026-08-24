using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesignGenerator.Core
{
    public static class CSharpIdentifier
    {
        private static readonly HashSet<string> Keywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "abstract","as","base","bool","break","byte","case","catch","char","checked","class","const",
            "continue","decimal","default","delegate","do","double","else","enum","event","explicit",
            "extern","false","finally","fixed","float","for","foreach","goto","if","implicit","in","int",
            "interface","internal","is","lock","long","namespace","new","null","object","operator","out",
            "override","params","private","protected","public","readonly","ref","return","sbyte","sealed",
            "short","sizeof","stackalloc","static","string","struct","switch","this","throw","true","try",
            "typeof","uint","ulong","unchecked","unsafe","ushort","using","virtual","void","volatile","while",
        };

        public static bool IsValid(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            char first = name[0];
            if (!char.IsLetter(first) && first != '_')
                return false;

            for (int i = 1; i < name.Length; ++i)
            {
                char c = name[i];
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return false;
            }

            //한글 등 비 ASCII는 C# 문법상 합업이지만 Unity/IL2CPP 조합에서 사고가 잦아 막음
            for (int i = 0; i < name.Length; i++)
                if (name[i] > 127) return false;
            return true;
        }

        public static bool IsKeyword(string name)
        {
            return name != null && Keywords.Contains(name);
        }

        public static string Explain(string name)
        {
            if (string.IsNullOrEmpty(name))
                return "비어 있습니다.";
            if (IsKeyword(name))
                return "C# 예약어입니다";
            if (char.IsDigit(name[0]))
                return "숫자로 시작할 수 없습니다";

            for (int i = 0; i < name.Length; i++)
            {
                char c = name[i];
                if (c > 127)
                    return $"ASCII 가 아닌 문자 '{c}' 가 있습니다";
                if (char.IsWhiteSpace(c))
                    return "공백이 들어 있습니다";
                if (!char.IsLetterOrDigit(c) && c != '_')
                    return $"사용할 수 없는 문자 '{c}' 가 있습니다";
            }
            return "유효하지 않습니다";
        }
    }

    public static class GeneratedFileWriter
    {
        //실제로 파일을 사용했으면 true, 내용이 같아서 건너뛰었으면 false
        public static bool WriteIfChanged(string path, string content)
        {
            if (string.IsNullOrEmpty(path))
                throw new ArgumentException("출력 경로가 비어 있습니다.", nameof(path));

            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            //줄바꿈을 통일해두지 않으면 OS/도구에 따라 매번 "변경됨"으로 잡힘
            content = Normalize(content);

            if (File.Exists(path))
            {
                string existing = Normalize(File.ReadAllText(path, Encoding.UTF8));
                if (string.Equals(existing, content, StringComparison.Ordinal))
                    return false;
            }

            //BOM 있는 UTF-8. Unity가 한글 주석을 안전하게 읽음
            File.WriteAllText(path, content, new UTF8Encoding(true));
            return true;
        }

        private static string Normalize(string s)
        {
            return s.Replace("\r\n", "\n").Replace("\r", "\n");
        }
    }

    //XML 문서 주석에 넣을 텍스트 이스케이프
    public static class XmlDocText
    {
        public static string Escape(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return s.Replace("&", "&amp;")
                    .Replace("<", "&lt;")
                    .Replace(">", "&gt;");
        }

        //여러 줄 주석을 한 줄로 눕힌다 (/// 주석은 줄바꿈에 취약)
        public static string Flatten(string s)
        {
            if (string.IsNullOrEmpty(s))
                return string.Empty;

            return s.Replace("\r\n", " ").Replace('\n', ' ').Replace('\r', ' ').Trim();
        }
    }

    public static class NumberText
    {
        public static bool TryParseInt(string raw, out long value)
        {
            value = 0;
            if (string.IsNullOrEmpty(raw))
                return false;

            raw = raw.Trim();

            long l;
            if (long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out l))
            {
                value = l;
                return true;
            }

            // "3.0" 같이 엑셀이 실수로 내보낸 정수
            double d;
            if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                && d == Math.Floor(d) && Math.Abs(d) < 9.2e18)
            {
                value = (long)d;
                return true;
            }

            // "0x1F"
            if (raw.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                long h;
                if (long.TryParse(raw.Substring(2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out h))
                {
                    value = h;
                    return true;
                }
            }

            return false;
        }
    }
}
