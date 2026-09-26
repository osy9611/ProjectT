using DesignGenerator.Core;
using DesignGenerator.Util;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace DesignGenerator.Table
{
    public static class TableValidator
    {
        //테이블 전체를 파싱한 뒤 한 번 호출한다. 스키마 / 참조 레벨 검사
        public static void ValidateSchema(Dictionary<int, TableDataInfo> tables, Dictionary<int, EnumInfo> enums, DiagnosticBag diag)
        {
            var usedEnums = new HashSet<int>();

            foreach (var t in tables.Values)
            {
                var loc = SourceLocation.InFile(t.TableName);

                //PK
                if (t.PKData.Count == 0)
                {
                    diag.Error("TG2004", loc,
                        "PK 컬럼이 없습니다.",
                        "PK 가 없으면 Get()/중복검사가 성립하지 않습니다.");
                }

                foreach (var pk in t.PKData)
                {
                    if (pk.Type == "float" || pk.Type == "double")
                        diag.Error("TG2044", loc, $"PK 컬럼 '{pk.ColumnName}' 에 부동소수점 타입을 사용할 수 없습니다.");
                }

                //컬럼명 중복
                foreach (var g in t.VarData.GroupBy(x => x.ColumnName, StringComparer.Ordinal))
                {
                    if (g.Count() > 1)
                        diag.Error("TG2005", loc, $"컬럼명 '{g.Key}' 이(가) {g.Count()}번 나옵니다.");
                }

                //타입
                foreach (var c in t.VarData)
                {
                    if (!DefineType.IsKnown(c.Type))
                        diag.Error("TG2006", loc, $"'{c.ColumnName}' 의 TYPE '{c.Type}' 을(를) 알 수 없습니다.");

                    if (c.EnumId != 0)
                    {
                        usedEnums.Add(c.EnumId);
                        if (enums != null && !enums.ContainsKey(c.EnumId))
                            diag.Error("TG2007", loc,
                                $"'{c.ColumnName}' 이 참조하는 EnumIds E_{c.EnumId} 가 enum.xlsx 에 없습니다.");
                    }
                }

                //참조 테이블
                var refTargets = new Dictionary<int, List<string>>();
                foreach (var c in t.VarData)
                {
                    if (string.IsNullOrEmpty(c.RefTable)) continue;

                    int refId;
                    if (!int.TryParse(c.RefTable, NumberStyles.Integer, CultureInfo.InvariantCulture, out refId))
                    {
                        diag.Error("TG2008", loc, $"'{c.ColumnName}' 의 참조 테이블 ID '{c.RefTable}' 를 읽을 수 없습니다.");
                        continue;
                    }
                    if (!tables.ContainsKey(refId))
                    {
                        diag.Error("TG2008", loc, $"'{c.ColumnName}' 이 참조하는 테이블 G_{refId} 가 없습니다.");
                        continue;
                    }

                    var target = tables[refId];
                    if (target.PKData.Count != 1)
                    {
                        diag.Error("TG2045", loc, $"'{c.ColumnName}' 의 참조 대상 '{target.TableName}' 은 단일 PK 테이블이어야 합니다.");
                        continue;
                    }
                    if (c.Type != target.PKData[0].Type)
                    {
                        diag.Error("TG2046", loc,
                            $"'{c.ColumnName}' 의 타입 '{c.Type}' 이 참조 대상 '{target.TableName}' 의 PK 타입 '{target.PKData[0].Type}' 과 다릅니다.");
                        continue;
                    }
                    if (c.Type != "sbyte" && c.Type != "short" && c.Type != "int" && c.Type != "long")
                    {
                        diag.Error("TG2047", loc, $"'{c.ColumnName}' 의 참조 타입 '{c.Type}' 에는 현재 -1 없음 값 규칙을 적용할 수 없습니다.");
                        continue;
                    }

                    List<string> list;
                    if (!refTargets.TryGetValue(refId, out list))
                        refTargets[refId] = list = new List<string>();
                    list.Add(c.ColumnName);
                }

                foreach (var kv in refTargets)
                {
                    if (kv.Value.Count > 1)
                        diag.Info("TG2012", loc,
                            $"테이블 G_{kv.Key} 를 {kv.Value.Count}개 컬럼이 참조합니다: {string.Join(", ", kv.Value)}");
                }

                //FieldNo
                if (t.FieldNoIsPositional)
                {
                    diag.Warning("TG2040", loc,
                        "FieldNo 가 명세서에 없어 컬럼 순서로 부여됩니다.",
                        "명세서에 FieldNo(L열)를 명시하면 컬럼을 중간에 끼워도 기존 .bytes 가 유효합니다.");
                }
                else
                {
                    var seen = new Dictionary<int, string>();
                    foreach (var c in t.VarData)
                    {
                        if (c.FieldNo <= 0)
                        {
                            diag.Error("TG2041", loc, $"'{c.ColumnName}' 의 FieldNo 가 없습니다. (같은 테이블의 다른 컬럼에는 있습니다)");
                            continue;
                        }
                        string prev;
                        if (seen.TryGetValue(c.FieldNo, out prev))
                            diag.Error("TG2042", loc, $"FieldNo {c.FieldNo} 가 '{prev}' 와 '{c.ColumnName}' 에 중복 지정됐습니다.");
                        else
                            seen[c.FieldNo] = c.ColumnName;
                    }
                }

                //ID Rule
                if (t.UseListRule && !t.IsListIdRule())
                    diag.Error("TG2043", loc, "UseListRule 이 true 인데 FindPKId 로 지정된 PK 컬럼이 없습니다.");
            }

            if (enums != null)
            {
                foreach (var kv in enums.OrderBy(x => x.Key))
                {
                    if (!usedEnums.Contains(kv.Key))
                        diag.Warning("TG2014", SourceLocation.InFile("enum.xlsx"),
                            $"enum '{kv.Value.EnumType}' (G_{kv.Key}) 을(를) 아무 테이블도 사용하지 않습니다.");
                }
            }

            //같은 컬럼명인데 테이블마다 타입 / enum 이 다른 경우
            var byName = new Dictionary<string, List<KeyValuePair<string, TableVarData>>>(StringComparer.Ordinal);
            foreach (var t in tables.Values)
            {
                foreach (var c in t.VarData)
                {
                    List<KeyValuePair<string, TableVarData>> list;
                    if (!byName.TryGetValue(c.ColumnName, out list))
                        byName[c.ColumnName] = list = new List<KeyValuePair<string, TableVarData>>();
                    list.Add(new KeyValuePair<string, TableVarData>(t.TableName, c));
                }
            }

            foreach (var kv in byName)
            {
                if (kv.Value.Count < 2) continue;
                var loc = SourceLocation.InFile("(명세서)");

                if (kv.Value.Select(x => x.Value.Type).Distinct(StringComparer.Ordinal).Count() > 1)
                    diag.Warning("TG2015", loc,
                        $"컬럼 '{kv.Key}' 의 TYPE 이 테이블마다 다릅니다: " +
                        string.Join(", ", kv.Value.Select(x => x.Key + "=" + x.Value.Type)));

                var enumIds = kv.Value.Select(x => x.Value.EnumId).Distinct().ToList();
                if (enumIds.Count > 1 && enumIds.Contains(0))
                    diag.Warning("TG2016", loc,
                        $"컬럼 '{kv.Key}' 이 어떤 테이블엔 EnumIds 가 있고 어떤 테이블엔 없습니다: " +
                        string.Join(", ", kv.Value.Select(x => x.Key + "=" + (x.Value.EnumId == 0 ? "(없음)" : "E_" + x.Value.EnumId))));
            }
        }

        //레코드를 전부 읽은 뒤 PK 중복 검사. 파싱 중에는 타입 검사가 돈다
        public static void ValidatePrimaryKeys(TableDataInfo t, DiagnosticBag diag)
        {
            if (t.PKData.Count == 0)
                return;
            var pkIndex = new List<int>();
            foreach (var pk in t.PKData)
            {
                int idx = t.VarData.FindIndex(x => x.ColumnName == pk.ColumnName);
                if (idx >= 0)
                    pkIndex.Add(idx);
            }

            if (pkIndex.Count == 0)
                return;

            var seen = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int r = 0; r < t.VarObjectData.Count; r++)
            {
                var row = t.VarObjectData[r];
                var key = string.Concat(pkIndex.Select(i =>
                {
                    if (i >= row.Length || row[i] == null)
                        return "-1:";

                    string value = Convert.ToString(row[i], CultureInfo.InvariantCulture);
                    return value.Length.ToString(CultureInfo.InvariantCulture) + ":" + value;
                }));

                int prev;
                if (seen.TryGetValue(key, out prev))
                {
                    string values = string.Join(", ", pkIndex.Select(i =>
                        i < row.Length && row[i] != null
                            ? Convert.ToString(row[i], CultureInfo.InvariantCulture)
                            : "(null)"));
                    diag.Error("TG2032", SourceLocation.At(t.TableName, r + 1),
                        $"PK 중복: [{values}] (앞선 위치: #{prev + 1})");
                }
                else
                    seen[key] = r;
            }
        }
    }
}
