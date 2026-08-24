using System;
using System.Collections.Generic;
using System.Globalization;

namespace DesignGenerator.Util
{
    #region 타입 매핑
    public sealed class TypeDef
    {
        public string SpecName;     // 명세서 표기      : Int32
        public string CsName;       // C# 키워드        : int
        public string SystemName;   // CodeDom 용       : System.Int32
        public bool IsIntegral;
        public bool IsFloating;
        public long Min, Max;       // 정수 범위 (IsIntegral 일 때만 의미)
    }

    public static class DefineType
    {
        private static readonly TypeDef[] All = new[]
         {
            new TypeDef{ SpecName="Int8",   CsName="sbyte",  SystemName="System.SByte",   IsIntegral=true, Min=sbyte.MinValue,  Max=sbyte.MaxValue  },
            new TypeDef{ SpecName="UInt8",  CsName="byte",   SystemName="System.Byte",    IsIntegral=true, Min=byte.MinValue,   Max=byte.MaxValue   },
            new TypeDef{ SpecName="Int16",  CsName="short",  SystemName="System.Int16",   IsIntegral=true, Min=short.MinValue,  Max=short.MaxValue  },
            new TypeDef{ SpecName="UInt16", CsName="ushort", SystemName="System.UInt16",  IsIntegral=true, Min=ushort.MinValue, Max=ushort.MaxValue },
            new TypeDef{ SpecName="Int32",  CsName="int",    SystemName="System.Int32",   IsIntegral=true, Min=int.MinValue,    Max=int.MaxValue    },
            new TypeDef{ SpecName="UInt32", CsName="uint",   SystemName="System.UInt32",  IsIntegral=true, Min=uint.MinValue,   Max=uint.MaxValue   },
            new TypeDef{ SpecName="Int64",  CsName="long",   SystemName="System.Int64",   IsIntegral=true, Min=long.MinValue,   Max=long.MaxValue   },
            new TypeDef{ SpecName="UInt64", CsName="ulong",  SystemName="System.UInt64",  IsIntegral=true, Min=0,               Max=long.MaxValue   },
            new TypeDef{ SpecName="float",  CsName="float",  SystemName="System.Single",  IsFloating=true },
            new TypeDef{ SpecName="double", CsName="double", SystemName="System.Double",  IsFloating=true },
            new TypeDef{ SpecName="bool",   CsName="bool",   SystemName="System.Boolean" },
            new TypeDef{ SpecName="string", CsName="string", SystemName="System.String"  },
        };

        private static readonly Dictionary<string, TypeDef> BySpec = Build(t => t.SpecName);
        private static readonly Dictionary<string, TypeDef> ByCs = Build(t => t.CsName);

        private static Dictionary<string, TypeDef> Build(Func<TypeDef, string> key)
        {
            var d = new Dictionary<string, TypeDef>(StringComparer.Ordinal);
            foreach (var t in All) d[key(t)] = t;
            return d;
        }

        //명세서 표기(Int32) 또는 C# 키워드(int) 어느 쪽이든 받아서 정의를 돌려준다.
        public static TypeDef Find(string type)
        {
            if (string.IsNullOrEmpty(type)) return null;
            type = type.Trim();

            TypeDef t;
            if (BySpec.TryGetValue(type, out t)) return t;
            if (ByCs.TryGetValue(type, out t)) return t;
            return null;
        }

        public static bool IsKnown(string type) { return Find(type) != null; }

        //명세서 표기 → C# 키워드. (기존 시그니처 유지)
        public static string ConvertTypeName(string type)
        {
            var t = Find(type);
            if (t == null)
                throw new ArgumentException($"알 수 없는 TYPE '{type}' 입니다. (Int8/16/32/64, UInt8/16/32/64, float, double, bool, string)");
            return t.CsName;
        }

        //C# 키워드 → CodeDom 용 시스템 타입명.
        public static string ConverSystemTypeName(string type)
        {
            var t = Find(type);
            if (t == null)
                throw new ArgumentException($"알 수 없는 타입 '{type}' 입니다.");
            return t.SystemName;
        }

        public static object ConvertDataType(string type, string data)
        {
            object value;
            string error;

            if (!TryConvertDataType(type, data, out value, out error))
                throw new FormatException(error);
            return value;
        }

        public static bool TryConvertDataType(string type, string data, out object value, out string error)
        {
            value = null;
            error = null;

            var t = Find(type);
            if (t == null)
            {
                error = $"알 수 없는 TYPE '{type}'";
                return false;
            }

            string raw = data == null ? string.Empty : data.Trim();

            if (t.CsName == "string")
            {
                value = raw;
                return true;
            }

            if (t.CsName == "bool")
            {
                switch (raw)
                {
                    case "Y":
                    case "y":
                    case "true":
                    case "True":
                    case "TRUE":
                    case "1":
                        value = true;
                        return true;
                    case "N":
                    case "n":
                    case "false":
                    case "False":
                    case "FALSE":
                    case "0":
                        value = false;
                        return true;
                    case "":
                        value = false;
                        return true;   // 빈 셀은 false 로
                }
                error = $"bool 컬럼에 '{raw}' 는 쓸 수 없습니다. Y/N (또는 true/false, 1/0) 만 가능합니다";
                return false;
            }

            if (raw.Length == 0)
            {
                value = Zero(t);
                return true;
            }

            if (t.IsFloating)
            {
                double d;
                if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d))
                {
                    error = $"{t.SpecName} 컬럼에 '{raw}' 는 숫자가 아닙니다";
                    return false;
                }

                value = (t.CsName == "float") ? (object)(float)d : (object)d;
                return true;
            }

            //정수. 엑셀이 "3.0" 으로 내보내는 경우까지 받아준다.
            long n;
            if (!long.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
            {
                double d;
                if (double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out d)
                    && d == Math.Floor(d) && Math.Abs(d) < 9.2e18)
                {
                    n = (long)d;
                }
                else
                {
                    error = $"{t.SpecName} 컬럼에 '{raw}' 는 정수가 아닙니다";
                    return false;
                }
            }

            if (n < t.Min || n > t.Max)
            {
                error = $"{t.SpecName} 범위({t.Min}~{t.Max})를 벗어난 값 {n}";
                return false;
            }

            value = Narrow(t, n);
            return true;
        }

        private static object Zero(TypeDef t)
        {
            switch (t.CsName)
            {
                case "sbyte":
                    return (sbyte)0;
                case "byte":
                    return (byte)0;
                case "short":
                    return (short)0;
                case "ushort":
                    return (ushort)0;
                case "int":
                    return 0;
                case "uint":
                    return (uint)0;
                case "long":
                    return 0L;
                case "ulong":
                    return (ulong)0;
                case "float":
                    return 0f;
                case "double":
                    return 0d;
                default:
                    return null;
            }
        }

        private static object Narrow(TypeDef t, long n)
        {
            switch (t.CsName)
            {
                case "sbyte":
                    return (sbyte)n;
                case "byte":
                    return (byte)n;
                case "short":
                    return (short)n;
                case "ushort":
                    return (ushort)n;
                case "int":
                    return (int)n;
                case "uint":
                    return (uint)n;
                case "long":
                    return n;
                case "ulong":
                    return (ulong)n;
                default:
                    return n;
            }
        }

        public static bool CheckTypeName(string name)
        {
            return IsKnown(name);
        }
    }
    #endregion

    #region 명세서 파싱용 타입
    public class XmlCreateTableInfo
    {
        public int TableId { get; set; }
        public string TableName { get; set; }
        public int SpecRow { get; set; }          // 명세서 몇 번째 행에서 왔는지 (진단용)

        private readonly List<XmlCreatTableColInfo> columns = new List<XmlCreatTableColInfo>();
        public List<XmlCreatTableColInfo> Columns { get { return columns; } }

        public XmlCreateTableInfo() { TableId = 0; }
    }

    public class XmlCreatTableColInfo
    {
        public string ColumnName;
        public string Type;
        public bool IsClient;
        public bool IsServer;
        public bool PK;
        public int EnumID;
        public string IDRule;
        public int StringHashIds;     // "참조 테이블 ID" (RefTableId)
        public int FieldNo;           // protobuf 필드 번호. 0 이면 미지정
        public int SpecRow;           // 진단용
    }
    #endregion

    #region Enum
    public class EnumValueData
    {
        public string EnumName;
        public int EnumValue;
        public string Comment;

        public EnumValueData(string enumName, string enumValue, string comment)
        {
            EnumName = enumName;
            Comment = comment;

            int v;
            if (!int.TryParse(enumValue, NumberStyles.Integer, CultureInfo.InvariantCulture, out v))
                throw new FormatException($"enum '{enumName}' 의 값 '{enumValue}' 을(를) 정수로 읽을 수 없습니다.");
            EnumValue = v;
        }
    }

    public class EnumInfo
    {
        public string EnumType;
        public string Comment;
        public int GroupID;
        public List<EnumValueData> Values = new List<EnumValueData>();

        // 이름 → 값. 셀마다 Values.Find(...) 로 선형 탐색하던 것을 대체한다.
        private Dictionary<string, int> lookup;

        public EnumInfo(string enumType, string comment, int groupID)
        {
            EnumType = enumType;
            Comment = comment;
            GroupID = groupID;
        }

        public void AddInfo(string enumName, string enumValue, string comment)
        {
            Values.Add(new EnumValueData(enumName, enumValue, comment));
            lookup = null;
        }

        public bool TryGetValue(string enumName, out int value)
        {
            if (lookup == null)
            {
                lookup = new Dictionary<string, int>(Values.Count, StringComparer.Ordinal);
                foreach (var v in Values) lookup[v.EnumName] = v.EnumValue;
            }
            return lookup.TryGetValue(enumName, out value);
        }
    }
    #endregion

    #region TableData
    public class TablePKData
    {
        public string ColumnName;
        public string Type;
        public bool IsListRule;
        public bool IsListRuleFindPK;
        public int EnumId;

        public TablePKData(string col, string type, int enumId)
        {
            ColumnName = col; Type = type; EnumId = enumId;
        }
    }

    public class TableVarData
    {
        public string ColumnName;
        public string Type;
        public string RefTable;      // 참조 테이블 ID(문자열). 비어 있으면 참조 없음
        public int EnumId;
        public int FieldNo;          // protobuf 필드 번호

        public TableVarData(string col, string type, int enumId, string refTable = "", int fieldNo = 0)
        {
            ColumnName = col; Type = type; EnumId = enumId; RefTable = refTable ?? ""; FieldNo = fieldNo;
        }
    }

    public class TableDataInfo
    {
        private readonly List<TablePKData> pkData = new List<TablePKData>();
        public List<TablePKData> PKData { get { return pkData; } }

        private readonly List<TableVarData> varData = new List<TableVarData>();
        public List<TableVarData> VarData { get { return varData; } }

        private readonly List<object[]> varObjectData = new List<object[]>();
        public List<object[]> VarObjectData { get { return varObjectData; } }

        public bool UseListRule { get; set; }
        public int TableID { get; set; }
        public string TableName { get; set; }

        //FindNo가 명세서에 없이 위치 기반으로 부여됬는지
        public bool FieldNoIsPositional { get; set; }

        public void AddPKData(string columnName, string type, int enumId)
        {
            pkData.Add(new TablePKData(columnName, type, enumId));
        }

        public void AddVarData(string columnName, string type, int enumId, string refTable = "", int fieldNo = 0)
        {
            varData.Add(new TableVarData(columnName, type, enumId, refTable, fieldNo));
        }

        public void AddIsListRule(string columnName)
        {
            var d = pkData.Find(x => x.ColumnName == columnName);
            if (d != null) d.IsListRule = true;
        }

        public void AddFindPKListRule(string columnName)
        {
            var d = pkData.Find(x => x.ColumnName == columnName);
            if (d != null) d.IsListRuleFindPK = true;
        }

        public string GetStringVerDatas(string varFormat, string insertVar)
        {
            return Join(varData.ConvertAll(x => x.ColumnName), varFormat, insertVar);
        }

        public string GetPkString(string varFormat, string insertVar)
        {
            return Join(pkData.ConvertAll(x => x.ColumnName), varFormat, insertVar);
        }

        public string GetListIdRuleString(string varFormat, string insertVar)
        {
            var names = new List<string>();
            foreach (var d in pkData)
            {
                if (d.IsListRuleFindPK)
                    names.Add(d.ColumnName);
            }

            return Join(names, varFormat, insertVar);
        }

        private static string Join(List<string> names, string format, string sep)
        {
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < names.Count; i++)
            {
                if (i > 0) sb.Append(sep);
                sb.Append(string.Format(format, names[i]));
            }
            return sb.ToString();
        }

        public bool IsListIdRule()
        {
            foreach (var d in pkData) if (d.IsListRuleFindPK) return true;
            return false;
        }

        public bool IsRefTable()
        {
            foreach (var d in varData) if (!string.IsNullOrEmpty(d.RefTable)) return true;
            return false;
        }

    }
    #endregion

    #region 로컬라이즈
    public enum LangaugeType
    {
        Ko,
        En,
        Jp
    }

    public class LocalDataInfo
    {
        public int EnumID { get; set; }
        public bool IsUse { get; set; }
        public string LocalName { get; set; }
        public string Ko { get; set; }
        public string Jp { get; set; }
        public string En { get; set; }
        public string SourceFile { get; set; }
        public int RecordIndex { get; set; }
    }
    #endregion

    public static class Tab
    {
        public const string TAB1 = "\t";
        public const string TAB2 = "\t\t";
        public const string TAB3 = "\t\t\t";
        public const string TAB4 = "\t\t\t\t";
        public const string TAB5 = "\t\t\t\t\t";
        public const string TAB6 = "\t\t\t\t\t\t";
        public const string TAB7 = "\t\t\t\t\t\t\t";
    }
}
