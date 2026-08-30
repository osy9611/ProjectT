using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using DesignGenerator.Core;
using DesignGenerator.Util;
using Excel = Microsoft.Office.Interop.Excel;

namespace DesignGenerator.Table
{
    public class TableCreator
    {
        public Dictionary<string, XmlCreateTableInfo> infos = new Dictionary<string, XmlCreateTableInfo>();
        public DiagnosticBag Diagnostics = new DiagnosticBag();

        // 명세서 열 (A=1 … L=12)
        private const int COL_ID = 2;   // B  G_1011
        private const int COL_CLIENT = 3;   // C
        private const int COL_SERVER = 4;   // D
        private const int COL_DESC = 5;   // E  column명(한글)
        private const int COL_NAME = 6;   // F  실제 테이블 / 컬럼명
        private const int COL_TYPE = 7;   // G
        private const int COL_PK = 8;   // H  식별자
        private const int COL_ENUM = 9;   // I  EnumIds     E_1004
        private const int COL_IDRULE = 10;  // J  ID Rule(json)
        private const int COL_REF = 11;  // K  string hash Ids → 실제로는 참조 테이블 ID
        private const int COL_FIELDNO = 12;  // L  FieldNo (선택. 없으면 무시)
        private const int COL_MAX = COL_FIELDNO;

        //verify 시트 컬럼. 리플렉션 순서에 기대지 않도록 여기서 고정한다
        private static readonly string[] VerifyColumns =
        {
            "ColumnName", "Type", "IsClient", "IsServer", "PK",
            "EnumID", "IDRule", "StringHashIds", "FieldNo", "TableId",
        };

        public bool Create(string loadPath, string outputPath, string fileName)
        {
            return Run(loadPath, outputPath, fileName);
        }

        public bool CreateAll(string loadPath, string outputPath)
        {
            return Run(loadPath, outputPath, null);
        }

        private bool Run(string loadPath, string outputPath, string fileName)
        {
            if (string.IsNullOrEmpty(loadPath))
            {
                Diagnostics.Error("TG0001", SourceLocation.InFile("(명세서)"), "명세서 경로가 비었습니다.");
                return false;
            }
            if (!File.Exists(loadPath))
            {
                Diagnostics.Error("TG0001", SourceLocation.InFile(loadPath), "명세서 파일을 찾을 수 없습니다.");
                return false;
            }

            GetWorkSheetInfo(loadPath, fileName);
            if (Diagnostics.HasError) return false;

            CreateXml(outputPath);
            return !Diagnostics.HasError;
        }

        //명세서 읽기
        private void GetWorkSheetInfo(string path, string fileName)
        {
            string file = Path.GetFileName(path);

            using (var session = new ExcelSession())
            {
                Excel.Workbook wb = null;
                Excel.Sheets sheets = null;
                Excel.Worksheet sheet = null;
                Excel.Range used = null;


                try
                {
                    wb = session.Open(path);
                    sheets = wb.Worksheets;
                    sheet = (Excel.Worksheet)sheets.get_Item(1);   // 첫 시트 = 기획테이블

                    used = sheet.UsedRange;
                    int lastRow = used.Row + used.Rows.Count - 1;

                    object[,] v;
                    Excel.Range block = sheet.Range[sheet.Cells[1, 1], sheet.Cells[lastRow, COL_MAX]];
                    try
                    {
                        v = (object[,])
                            block.Value2;
                    }
                    finally
                    {
                        ExcelSession.Release(block);
                    }

                    if (v == null)
                    {
                        Diagnostics.Error("TG2000", SourceLocation.InFile(file), "명세서 시트가 비어 있습니다.");
                        return;
                    }

                    int rows = v.GetLength(0);
                    string nowTable = null;

                    for (int i = 2; i <= rows; ++i)
                    {
                        var loc = SourceLocation.At(file, i);

                        string id = Str(v[i, COL_ID]);
                        if (!string.IsNullOrEmpty(id))
                        {
                            // 테이블 헤더 행
                            string tableName = Str(v[i, COL_NAME]);
                            nowTable = tableName;

                            if (!string.IsNullOrEmpty(fileName) && tableName != fileName) continue;

                            int tableId;
                            if (!int.TryParse(id.Replace("G_", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out tableId))
                            {
                                Diagnostics.Error("TG2002", loc, $"TableId 를 읽을 수 없습니다: '{id}'");
                                continue;
                            }
                            if (string.IsNullOrEmpty(tableName))
                            {
                                Diagnostics.Error("TG2002", loc, $"{id} 의 테이블 이름(F열)이 비어 있습니다.");
                                continue;
                            }
                            if (infos.ContainsKey(tableName))
                            {
                                Diagnostics.Error("TG2001", loc, $"테이블 이름 '{tableName}' 이 중복 선언됐습니다.");
                                continue;
                            }

                            infos.Add(tableName, new XmlCreateTableInfo
                            {
                                TableId = tableId,
                                TableName = tableName,
                                SpecRow = i,
                            });
                            continue;
                        }

                        // 컬럼 행
                        string colName = Str(v[i, COL_NAME]);
                        if (string.IsNullOrEmpty(colName)) continue;     // 빈 줄

                        XmlCreateTableInfo info;
                        if (nowTable == null || !infos.TryGetValue(nowTable, out info)) continue;


                        var c = new XmlCreatTableColInfo
                        {
                            ColumnName = colName,
                            Type = Str(v[i, COL_TYPE]),
                            IsClient = Str(v[i, COL_CLIENT]) == "Y",
                            IsServer = Str(v[i, COL_SERVER]) == "Y",
                            PK = Str(v[i, COL_PK]) == "PK",
                            SpecRow = i,
                        };

                        string e = Str(v[i, COL_ENUM]);
                        if (!string.IsNullOrEmpty(e) && e != "-")
                        {
                            int enumId;
                            if (int.TryParse(e.Replace("E_", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out enumId))
                                c.EnumID = enumId;
                            else
                                Diagnostics.Error("TG2002", loc, $"EnumIds 를 읽을 수 없습니다: '{e}'");
                        }

                        string rule = Str(v[i, COL_IDRULE]);
                        if (!string.IsNullOrEmpty(rule) && rule != "-") c.IDRule = rule;

                        string reference = Str(v[i, COL_REF]);       // K열 = 참조 테이블 ID
                        if (!string.IsNullOrEmpty(reference) && reference != "-")
                        {
                            int refId;
                            if (int.TryParse(reference.Replace("G_", ""), NumberStyles.Integer, CultureInfo.InvariantCulture, out refId))
                                c.StringHashIds = refId;
                            else
                                Diagnostics.Error("TG2002", loc, $"참조 테이블 ID 를 읽을 수 없습니다: '{reference}'");
                        }

                        string fieldNo = Str(v[i, COL_FIELDNO]);     // L열 (선택)
                        if (!string.IsNullOrEmpty(fieldNo) && fieldNo != "-")
                        {
                            int fn;
                            if (int.TryParse(fieldNo, NumberStyles.Integer, CultureInfo.InvariantCulture, out fn) && fn > 0)
                                c.FieldNo = fn;
                            else
                                Diagnostics.Error("TG2002", loc, $"FieldNo 를 읽을 수 없습니다: '{fieldNo}'");
                        }

                        info.Columns.Add(c);
                    }

                }
                finally
                {
                    ExcelSession.Release(used);
                    ExcelSession.Release(sheet);
                    ExcelSession.Release(sheets);
                    session.Close(wb);    
                }
            }
        }

        //Value2 는 셀 서식에 따라 string / double / bool / null 을 섞어 돌려준다
        private static string Str(object cell)
        {
            if (cell == null)
                return string.Empty;

            var s = cell as string;
            if (s != null)
                return s.Trim();

            if (cell is double)
            {
                double d = (double)cell;
                if (d == Math.Floor(d) && Math.Abs(d) < 1e15)
                    return ((long)d).ToString(CultureInfo.InvariantCulture);
                return d.ToString(CultureInfo.InvariantCulture);
            }

            if (cell is bool)
                return ((bool)cell) ? "Y" : "N";

            return Convert.ToString(cell, CultureInfo.InvariantCulture).Trim();
        }

        //테이블 xlsx 생성/갱신
        private void CreateXml(string path)
        {
            Directory.CreateDirectory(path);

            using (var session = new ExcelSession())
            {
                foreach (var kv in infos)
                {
                    string tableName = kv.Key;
                    var info = kv.Value;
                    string checkPath = Path.Combine(path, tableName + ".xlsx");

                    Excel.Workbook wb = null;
                    try
                    {
                        string xmlSchema = BuildSchemaXml(tableName, info);

                        bool exists = File.Exists(checkPath);
                        wb = exists ? session.Open(checkPath, readOnly: false) : CreateNewWorkbook(session);

                        var maps = wb.XmlMaps;
                        Excel.XmlMap map;
                        if (maps.Count == 0) map = maps.Add(xmlSchema);
                        else { maps[1].Delete(); map = maps.Add(xmlSchema); }

                        var sheets = wb.Worksheets;
                        var recordSheet = (Excel.Worksheet)sheets.get_Item(1);
                        var verifySheet = (Excel.Worksheet)sheets.get_Item(2);
                        recordSheet.Name = "record";
                        verifySheet.Name = "verify";

                        WriteVerifySheet(verifySheet, map, tableName, info);
                        SyncRecordSheet(recordSheet, map, tableName, info);

                        wb.SaveAs(checkPath);
                        Console.WriteLine($"  {tableName}.xlsx 갱신");
                    }
                    catch (Exception e)
                    {
                        Diagnostics.Error("TG2050", SourceLocation.InFile(tableName + ".xlsx"),
                            "테이블 엑셀 생성 실패: " + e.Message);
                    }
                    finally
                    {
                        session.Close(wb, saveChanges: false);
                    }
                }
            }
        }

        private static Excel.Workbook CreateNewWorkbook(ExcelSession session)
        {
            var wb = session.NewWorkbook();
            var sheets = wb.Worksheets;
            while (sheets.Count < 2) sheets.Add();
            return wb;
        }

        private static string BuildSchemaXml(string tableName, XmlCreateTableInfo info)
        {
            var sb = new System.Text.StringBuilder();
            sb.Append("<").Append(tableName).Append(">");

            sb.Append("<verify");
            foreach (var c in VerifyColumns) sb.Append(" ").Append(c).Append("=\"\"");
            sb.Append("/>");
            sb.Append("<verify");
            foreach (var c in VerifyColumns) sb.Append(" ").Append(c).Append("=\"\"");
            sb.Append("/>");

            for (int i = 0; i < 2; i++)
            {
                sb.Append("<record>");
                foreach (var col in info.Columns)
                    sb.Append("<").Append(col.ColumnName).Append("></").Append(col.ColumnName).Append(">");
                sb.Append("</record>");
            }

            sb.Append("</").Append(tableName).Append(">");
            return sb.ToString();
        }

        private void WriteVerifySheet(Excel.Worksheet sheet, Excel.XmlMap map, string tableName, XmlCreateTableInfo info)
        {
            var listObjects = sheet.ListObjects;
            Excel.ListObject lo;

            if (listObjects.Count == 0)
            {
                var header = sheet.Range[sheet.Cells[1, 1], sheet.Cells[1, VerifyColumns.Length]];
                lo = listObjects.AddEx(Excel.XlListObjectSourceType.xlSrcRange, header);
            }
            else lo = listObjects[1];

            var cols = lo.ListColumns;
            while (cols.Count < VerifyColumns.Length) cols.Add();

            for (int i = 1; i <= VerifyColumns.Length; ++i)
            {
                cols[i].Name = VerifyColumns[i - 1];
                cols[i].XPath.SetValue(map, $"/{tableName}/verify/@{VerifyColumns[i - 1]}");
            }

            if (lo.ListRows.Count > 0) lo.DataBodyRange.Delete();

            foreach (var col in info.Columns)
            {
                var row = lo.ListRows.AddEx();
                row.Range[1] = col.ColumnName;
                row.Range[2] = col.Type;
                row.Range[3] = col.IsClient ? "Y" : "N";
                row.Range[4] = col.IsServer ? "Y" : "N";
                row.Range[5] = col.PK ? "Y" : "N";
                row.Range[6] = col.EnumID == 0 ? "-" : col.EnumID.ToString(CultureInfo.InvariantCulture);
                row.Range[7] = string.IsNullOrEmpty(col.IDRule) ? "-" : col.IDRule;
                row.Range[8] = col.StringHashIds == 0 ? "-" : col.StringHashIds.ToString(CultureInfo.InvariantCulture);
                row.Range[9] = col.FieldNo == 0 ? "-" : col.FieldNo.ToString(CultureInfo.InvariantCulture);
                row.Range[10] = info.TableId;
            }
        }

        private void SyncRecordSheet(Excel.Worksheet sheet, Excel.XmlMap map, string tableName, XmlCreateTableInfo info)
        {
            var listObjects = sheet.ListObjects;
            Excel.ListObject lo;

            if (listObjects.Count == 0)
            {
                var header = sheet.Range[sheet.Cells[1, 1], sheet.Cells[1, Math.Max(1, info.Columns.Count)]];
                lo = listObjects.AddEx(Excel.XlListObjectSourceType.xlSrcRange, header);

                var newCols = lo.ListColumns;
                for (int i = 1; i <= info.Columns.Count; ++i)
                {
                    if (newCols.Count < i) newCols.Add();
                    newCols[i].Name = info.Columns[i - 1].ColumnName;
                    newCols[i].XPath.SetValue(map, $"/{tableName}/record/{info.Columns[i - 1].ColumnName}");
                }
                return;
            }

            lo = listObjects[1];
            var cols = lo.ListColumns;

            var wanted = new HashSet<string>(info.Columns.Select(c => c.ColumnName), StringComparer.Ordinal);

            //1.명세서에서 사라진 컬럼 삭제. 뒤에서 앞으로
            for (int i = cols.Count; i >= 1; --i)
            {
                string name = cols[i].Name;
                if (!wanted.Contains(name))
                {
                    Console.WriteLine($"    [{tableName}] 컬럼 삭제: {name}");
                    cols[i].Delete();
                }
            }

            //2.현재 시트에 남아 있는 컬럼 이름 수집
            var present = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 1; i <= cols.Count; ++i) present[cols[i].Name] = i;


            //3.새 컬럼은 맨 뒤에 추가. 중간에 삽입하지 않아야 기존 데이터가 밀리지 않는다
            foreach (var col in info.Columns)
            {
                if (present.ContainsKey(col.ColumnName))
                    continue;

                var added = cols.Add();
                added.Name = col.ColumnName;
                present[col.ColumnName] = cols.Count;
                Console.WriteLine($"    [{tableName}] 컬럼 추가: {col.ColumnName}");
            }

            //4.XPath를 이름 기준으로 재연결.
            for (int i = 1; i <= cols.Count; ++i)
            {
                string name = cols[i].Name;
                if (!wanted.Contains(name))
                    continue;
                cols[i].XPath.SetValue(map, $"/{tableName}/record/{name}");
            }
        }
    }
}
