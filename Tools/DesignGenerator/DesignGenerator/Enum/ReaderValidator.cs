using DesignGenerator.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DesignGenerator.Enum
{
    public sealed class EnumMember
    {
        public string Name;
        public long Value;
        public string Comment;
        public SourceLocation Location;
    }

    public sealed class EnumGroup
    {
        public int GroupId;
        public string TypeName;
        public string Comment;
        public bool IsFlags;                 // 선택 컬럼 FLAGS = "Y"
        public string BaseType = "int";      // 선택 컬럼 BASE_TYPE
        public SourceLocation Location;
        public readonly List<EnumMember> Members = new List<EnumMember>();
    }

    public interface IEnumRecordSource
    {
        string SourceName { get; }

        //레코드를 1-based 순서대로. 값은 컬럼명 → 셀 문자열
        IEnumerable<IDictionary<string, string>> ReadRecords();
    }

    public sealed class XmlEnumRecordSource : IEnumRecordSource
    {
        private readonly string path;
        public XmlEnumRecordSource(string xmlPath) { this.path = xmlPath; }
        public string SourceName { get { return Path.GetFileName(path); } }

        public IEnumerable<IDictionary<string, string>> ReadRecords()
        {
            var settings = new XmlReaderSettings
            {
                IgnoreWhitespace = true,
                IgnoreComments = true,
                DtdProcessing = DtdProcessing.Prohibit,
                CloseInput = true,
            };

            using (var reader = XmlReader.Create(path, settings))
            {
                reader.MoveToContent();
                if (reader.NodeType == XmlNodeType.EndElement)
                    yield break;
                if (reader.IsEmptyElement)
                    yield break;

                reader.Read();

                //XNode.ReadFrom 은 읽고 나면 리더를 다음 노드로 이미 옮겨놓는다.
                //여기서 또 Read() 를 부르면 노드를 하나씩 건너뛰다가 <record> 위에서
                //"ReadElementContentAs() cannot be called on an element that has
                // child elements" 로 터진다. 그래서 전진을 직접 제어한다.
                while (!reader.EOF)
                {
                    if (reader.NodeType == XmlNodeType.Element && reader.Name == "record")
                    {
                        var el = System.Xml.Linq.XNode.ReadFrom(reader) as System.Xml.Linq.XElement;
                        if (el == null)
                            continue;

                        var row = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var c in el.Elements())
                            row[c.Name.LocalName] = c.Value;

                        if (row.Count > 0)
                            yield return row;

                        continue;
                    }

                    reader.Read();
                }

            }
        }
    }

    public static class EnumReader
    {
        private const string COL_USE = "USE";
        private const string COL_GROUP_ID = "GROUP_ID";
        private const string COL_ENUM_NAME = "ENUM_NAME";
        private const string COL_VALUE = "ENUM_VALUE";
        private const string COL_COMMENT = "COMMENT";
        private const string COL_FLAGS = "FLAGS";       // 선택
        private const string COL_BASE_TYPE = "BASE_TYPE";   // 선택

        private const string GROUP_HEADER_MARK = "-";

        private static bool IsUsed(IDictionary<string, string> row)
        {
            string use = Cell(row, COL_USE);
            return string.IsNullOrEmpty(use) || string.Equals(use, "Y", StringComparison.OrdinalIgnoreCase);
        }

        public static List<EnumGroup> Read(IEnumRecordSource source, DiagnosticBag diag)
        {
            string file = source.SourceName;

            //레코드를 한 번 다 읽어 메모리에 올린다
            var records = new List<IDictionary<string, string>>();
            try
            {
                foreach (var r in source.ReadRecords())
                    records.Add(r);
            }
            catch (FileNotFoundException)
            {
                diag.Error("TG0001", SourceLocation.InFile(file),
                    "파일을 찾을 수 없습니다.",
                    "Enum 단계를 먼저 실행했는지 확인하세요.");
                return new List<EnumGroup>();
            }
            catch (XmlException e)
            {
                diag.Error("TG0002", SourceLocation.InFile(file), "XML 파싱 실패: " + e.Message);
                return new List<EnumGroup>();
            }

            var groups = new Dictionary<int, EnumGroup>();
            var ordered = new List<EnumGroup>();

            //1. 그룹 선언 행 (ENUM_VALUE == "-") 만 먼저 수집
            for (int i = 0; i < records.Count; ++i)
            {
                var row = records[i];
                int recNo = i + 1;

                if (Cell(row, COL_VALUE) != GROUP_HEADER_MARK)
                    continue;

                var loc = SourceLocation.At(file, recNo);

                // 그룹 선언 행의 USE=N → enum 타입 전체를 생성하지 않음
                if (!IsUsed(row))
                {
                    diag.Info("TG1015", SourceLocation.At(file, recNo, COL_USE),
                        $"USE 가 '{Cell(row, COL_USE)}' 이므로 enum '{Cell(row, COL_ENUM_NAME)}' 을(를) 생성하지 않습니다.");
                    continue;
                }

                long gid;
                if (!NumberText.TryParseInt(Cell(row, COL_GROUP_ID), out gid))
                {
                    diag.Error("TG1002", SourceLocation.At(file, recNo, COL_GROUP_ID),
                        $"GROUP_ID 를 숫자로 읽을 수 없습니다: '{Cell(row, COL_GROUP_ID)}'");
                    continue;
                }

                string typeName = Cell(row, COL_ENUM_NAME);
                if (string.IsNullOrEmpty(typeName))
                {
                    diag.Error("TG1001", SourceLocation.At(file, recNo, COL_ENUM_NAME),
                        "그룹 선언 행에 enum 타입명이 없습니다.");
                    continue;
                }

                if (groups.ContainsKey((int)gid))
                {
                    diag.Error("TG1006", loc,
                        $"GROUP_ID {gid} 가 두 번 선언됐습니다. (이미 '{groups[(int)gid].TypeName}' 로 선언)",
                        "GROUP_ID 는 enum 타입마다 고유해야 합니다.");
                    continue;
                }

                var g = new EnumGroup
                {
                    GroupId = (int)gid,
                    TypeName = typeName,
                    Comment = Cell(row, COL_COMMENT),
                    IsFlags = string.Equals(Cell(row, COL_FLAGS), "Y", StringComparison.OrdinalIgnoreCase),
                    Location = loc,
                };

                string baseType = Cell(row, COL_BASE_TYPE);
                if (!string.IsNullOrEmpty(baseType)) g.BaseType = baseType;

                groups[g.GroupId] = g;
                ordered.Add(g);
            }

            //2. 값 행
            for (int i = 0; i < records.Count; ++i)
            {
                var row = records[i];
                int recNo = i + 1;

                string rawValue = Cell(row, COL_VALUE);
                if (rawValue == GROUP_HEADER_MARK)                  // 그룹 선언 행은 건너뜀
                    continue;
                if (string.IsNullOrEmpty(rawValue) && string.IsNullOrEmpty(Cell(row, COL_ENUM_NAME)))
                    continue;                                       // 완전 빈 행

                if (!IsUsed(row))
                {
                    diag.Info("TG1015", SourceLocation.At(file, recNo, COL_USE),
                        $"USE 가 '{Cell(row, COL_USE)}' 이므로 '{Cell(row, COL_ENUM_NAME)}' 를 제외합니다.");
                    continue;
                }

                long gid;
                if (!NumberText.TryParseInt(Cell(row, COL_GROUP_ID), out gid))
                {
                    diag.Error("TG1002", SourceLocation.At(file, recNo, COL_GROUP_ID),
                        $"GROUP_ID 를 숫자로 읽을 수 없습니다: '{Cell(row, COL_GROUP_ID)}'");
                    continue;
                }

                EnumGroup group;
                if (!groups.TryGetValue((int)gid, out group))
                {
                    diag.Error("TG1005", SourceLocation.At(file, recNo, COL_GROUP_ID),
                        $"GROUP_ID {gid} 에 대한 그룹 선언 행이 없습니다. 이 값은 버려집니다.",
                        "ENUM_VALUE 가 '-' 인 그룹 선언 행을 추가하세요.");
                    continue;
                }

                string name = Cell(row, COL_ENUM_NAME);
                if (string.IsNullOrEmpty(name))
                {
                    diag.Error("TG1001", SourceLocation.At(file, recNo, COL_ENUM_NAME),
                        "ENUM_NAME 이 비어 있습니다.");
                    continue;
                }

                long value;
                if (!NumberText.TryParseInt(rawValue, out value))
                {
                    diag.Error("TG1002", SourceLocation.At(file, recNo, COL_VALUE),
                        $"ENUM_VALUE 를 숫자로 읽을 수 없습니다: '{rawValue}' ({group.TypeName}.{name})");
                    continue;
                }

                group.Members.Add(new EnumMember
                {
                    Name = name,
                    Value = value,
                    Comment = Cell(row, COL_COMMENT),
                    Location = SourceLocation.At(file, recNo),
                });
            }

            return ordered;
        }

        private static string Cell(IDictionary<string, string> row, string column)
        {
            string v;
            return row.TryGetValue(column, out v) && v != null ? v.Trim() : string.Empty;
        }
    }

    public static class EnumValidator
    {
        public static void Validate(IList<EnumGroup> groups, DiagnosticBag diag)
        {
            var typeNames = new Dictionary<string, EnumGroup>(StringComparer.Ordinal);

            foreach (var group in groups)
            {
                // 타입명 자체가 유효한 C# 식별자인가
                if (!CSharpIdentifier.IsValid(group.TypeName) || CSharpIdentifier.IsKeyword(group.TypeName))
                {
                    diag.Error("TG1003", group.Location,
                        $"enum 타입명 '{group.TypeName}' 을(를) 쓸 수 없습니다 — {CSharpIdentifier.Explain(group.TypeName)}.",
                        "영문자/숫자/밑줄만, 숫자로 시작하지 않게.");
                }

                //서로 다른 GROUP_ID 가 같은 타입명을 쓰면 중복 정의로 컴파일이 깨진다
                EnumGroup dup;
                if (typeNames.TryGetValue(group.TypeName, out dup))
                {
                    diag.Error("TG1010", group.Location,
                        $"enum 타입명 '{group.TypeName}' 이(가) GROUP_ID {dup.GroupId} 와 중복됩니다.");
                }
                else
                {
                    typeNames[group.TypeName] = group;
                }

                if (!IsSupportedBaseType(group.BaseType))
                {
                    diag.Error("TG1012", group.Location,
                        $"BASE_TYPE '{group.BaseType}' 은(는) enum 기반 타입으로 쓸 수 없습니다.",
                        "byte/sbyte/short/ushort/int/uint/long/ulong 중 하나.");
                    group.BaseType = "int";
                }

                if (group.Members.Count == 0)
                {
                    diag.Warning("TG1009", group.Location,
                        $"'{group.TypeName}' 에 값이 하나도 없습니다.");
                    continue;
                }

                var seenName = new Dictionary<string, EnumMember>(StringComparer.Ordinal);
                var seenValue = new Dictionary<long, EnumMember>();

                foreach (var m in group.Members)
                {
                    //타입명 자체가 유효한 C# 식별자인가
                    if (!CSharpIdentifier.IsValid(m.Name) || CSharpIdentifier.IsKeyword(m.Name))
                    {
                        diag.Error("TG1003", m.Location,
                            $"'{group.TypeName}.{m.Name}' 을(를) 쓸 수 없습니다 — {CSharpIdentifier.Explain(m.Name)}.");
                    }

                    EnumMember prev;
                    if (seenName.TryGetValue(m.Name, out prev))
                    {
                        diag.Error("TG1007", m.Location,
                            $"'{group.TypeName}.{m.Name}' 이름이 중복됩니다. (앞선 위치: {prev.Location})");
                    }
                    else
                    {
                        seenName[m.Name] = m;
                    }

                    if (seenValue.TryGetValue(m.Value, out prev))
                    {
                        //컴파일은 되지만 의도치 않은 별칭이 된다. 대부분 실수다
                        diag.Warning("TG1008", m.Location,
                            $"'{group.TypeName}' 에서 값 {m.Value} 가 '{prev.Name}' 와 중복입니다 ('{m.Name}').",
                            "의도한 별칭이 아니면 값을 바꾸세요.");
                    }
                    else
                    {
                        seenValue[m.Value] = m;
                    }

                    if (!FitsIn(group.BaseType, m.Value))
                    {
                        diag.Error("TG1011", m.Location,
                            $"'{group.TypeName}.{m.Name}' 의 값 {m.Value} 가 기반 타입 {group.BaseType} 범위를 벗어납니다.");
                    }
                }


                if (group.IsFlags)
                {
                    foreach (var member in group.Members)
                    {
                        if (member.Value == 0) 
                            continue;
                        if ((member.Value & (member.Value - 1)) != 0)
                        {
                            diag.Warning("TG1013", member.Location,
                                $"[Flags] enum '{group.TypeName}' 의 '{member.Name}' 값 {member.Value} 가 2의 거듭제곱이 아닙니다.",
                                "조합 값이면 무시하세요.");
                        }
                    }
                }
            }
        }

        private static bool IsSupportedBaseType(string t)
        {
            switch (t)
            {
                case "byte":
                case "sbyte":
                case "short":
                case "ushort":
                case "int":
                case "uint":
                case "long":
                case "ulong":
                    return true;
            }
            return false;
        }

        private static bool FitsIn(string baseType, long v)
        {
            switch (baseType)
            {
                case "byte":
                    return v >= byte.MinValue && v <= byte.MaxValue;
                case "sbyte":
                    return v >= sbyte.MinValue && v <= sbyte.MaxValue;
                case "short":
                    return v >= short.MinValue && v <= short.MaxValue;
                case "ushort":
                    return v >= ushort.MinValue && v <= ushort.MaxValue;
                case "int":
                    return v >= int.MinValue && v <= int.MaxValue;
                case "uint":
                    return v >= uint.MinValue && v <= uint.MaxValue;
                default: return true;   // long / ulong
            }
        }
    }
}
