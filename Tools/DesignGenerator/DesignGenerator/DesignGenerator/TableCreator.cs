using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Xml;
using System.Xml.Serialization;
using Microsoft.Office.Interop.Excel;
namespace DesignGenerator
{
    internal class TableCreator
    {
        public Dictionary<string, XmlCreateTableInfo> infos = new Dictionary<string, XmlCreateTableInfo>();
        public void Create(string loadPath, string outputPath, string fileName)
        {
            if (string.IsNullOrEmpty(loadPath))
            {
                Console.WriteLine("TableCreator Path Is Null");
                return;
            }

            GetWorkSheetInfo(loadPath, fileName);

            Save(outputPath);
        }

        public void CreateAll(string loadPath, string outputPath)
        {
            if (string.IsNullOrEmpty(loadPath))
            {
                Console.WriteLine("TableCreator Path Is Null");
                return;
            }

            GetWorkSheetInfo(loadPath, null);
            Save(outputPath);
        }

        private void GetWorkSheetInfo(string path, string fileName)
        {
            var workSheet = GetWorksheet(path);

            object missValue = System.Reflection.Missing.Value;

            //사용되어진 col, row 저장
            int colCount = workSheet.UsedRange.Columns.Count;
            int rowCount = workSheet.UsedRange.Rows.Count;

            string nowTableName = string.Empty;
            int nowTableID = 0;
            for (int i = 2; i <= rowCount; ++i)
            {
                //새로운 테이블 정보 생성
                if (workSheet.get_Range("B" + i).Value2 != null)
                {
                    string tableName = workSheet.get_Range("F" + i).Value2;

                    nowTableName = tableName;
                    if (!string.IsNullOrEmpty(fileName))
                    {
                        if (tableName != fileName) continue;
                    }

                    int tableId = Convert.ToInt32(workSheet.get_Range("B" + i).Value2.Replace("G_", ""));
                    nowTableID = tableId;
                    var tableInfo = new XmlCreateTableInfo();
                    tableInfo.TableId = tableId;
                    infos.Add(tableName, tableInfo);
                    continue;
                }

                //비어있는 줄이면 그냥 넘긴다...
                if (workSheet.get_Range("F" + i).Value2 == null) continue;

                if (infos.TryGetValue(nowTableName, out var info))
                {
                    var colInfo = new XmlCreatTableColInfo();

                    colInfo.IsClient = workSheet.get_Range("C" + i).Value2 == "Y" ? true : false;
                    colInfo.IsServer = workSheet.get_Range("D" + i).Value2 == "Y" ? true : false;
                    colInfo.ColumnName = workSheet.get_Range("F" + i).Value2;
                    colInfo.Type = workSheet.get_Range("G" + i).Value2;
                    colInfo.PK = workSheet.get_Range("H" + i).Value2 == "PK" ? true : false;

                    if (workSheet.get_Range("I" + i).Value2 != null)
                        colInfo.EnumID = Convert.ToInt32(workSheet.get_Range("I" + i).Value2.Replace("E_", ""));
                    if (workSheet.get_Range("J" + i).Value2 != null)
                        colInfo.IDRule = workSheet.get_Range("J" + i).Value2;

                    if (string.IsNullOrEmpty(workSheet.get_Range("K" + i).Value2) || workSheet.get_Range("K" + i).Value2 == "-")
                        colInfo.StringHashIds = 0;
                    else
                        colInfo.StringHashIds = Convert.ToInt32(workSheet.get_Range("K" + i).Value2.Replace("G_", ""));


                    info.Columns.Add(colInfo);
                }
            }
        }

        private Worksheet GetWorksheet(string path)
        {
            Application app = new Application();
            Workbook workBook = app.Workbooks.Open(path, 0, true, 5, "", ":", true, XlPlatform.xlWindows, "\t", false, false, 0, true, 1, 0);

            //첫 번째 Sheet 사용
            Worksheet workSheet = (Worksheet)workBook.Worksheets.get_Item(1);

            return workSheet;
        }

        private void CreateXml(string path)
        {
            var excel = new Microsoft.Office.Interop.Excel.Application();

            foreach (var info in infos)
            {
                XmlDocument xdoc = new XmlDocument();
                XmlNode totalroot = xdoc.CreateElement(info.Key);

                string xmlData = string.Empty;

                var verifyFields = typeof(XmlCreatTableColInfo).GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                //verify
                for (int i = 0; i < 2; ++i)
                {
                    XmlNode verify = xdoc.CreateElement("verify");
                    foreach (var filedInfo in verifyFields)
                    {
                        XmlAttribute att = xdoc.CreateAttribute(filedInfo.Name);
                        verify.Attributes.Append(att);
                    }

                    //TableId는 별도로 넣어주는 거라서 따로 뺐다.
                    XmlAttribute TableId = xdoc.CreateAttribute("TableId");
                    verify.Attributes.Append(TableId);

                    totalroot.AppendChild(verify);
                }
                //record
                for (int i = 0; i < 2; ++i)
                {
                    XmlNode record = xdoc.CreateElement("record");
                    foreach (var column in info.Value.Columns)
                    {
                        XmlNode columnName = xdoc.CreateElement(column.ColumnName);
                        record.AppendChild(columnName);
                    }
                    totalroot.AppendChild(record);
                }

                using (StringWriter stringWriter = new StringWriter())
                {
                    XmlSerializer serializer = new XmlSerializer(totalroot.GetType());
                    serializer.Serialize(stringWriter, totalroot);
                    xmlData = stringWriter.ToString();

                    string checkPath = $"{path}\\{info.Key}.xlsx";
                    Workbook excelworkBook;

                    FileInfo fi = new FileInfo(checkPath);
                    if (fi.Exists)
                    {
                        excelworkBook = excel.Workbooks.Open(checkPath, ReadOnly: false);
                    }
                    else
                    {
                        excelworkBook = excel.Workbooks.Add(Type.Missing);
                        excelworkBook.Worksheets.Add(excelworkBook.Worksheets[1]);
                        excelworkBook.Worksheets.Add(excelworkBook.Worksheets[2]);
                    }

                    // Add a new XML map.
                    XmlMap xmlMap1;
                    if (excelworkBook.XmlMaps.Count == 0)
                        xmlMap1 = excelworkBook.XmlMaps.Add(xmlData);
                    else
                    {
                        excelworkBook.XmlMaps[1].Delete();
                        xmlMap1 = excelworkBook.XmlMaps.Add(xmlData);
                    }

                    Worksheet recordSheet = excelworkBook.Worksheets.Item[1];
                    recordSheet.Name = "record";

                    Worksheet verifySheet = excelworkBook.Worksheets.Item[2];
                    verifySheet.Name = "verify";

                    //verify
                    ListObject verifyListObjects;
                    ListColumns verifyColumns;
                    ListRows verifyRows;
                    if (verifySheet.ListObjects.Count == 0)
                    {
                        Range verifyColumnRange = verifySheet.Range[verifySheet.Cells[1, 1], verifySheet.Cells[1, verifyFields.Length + 1]];
                        verifyListObjects = verifySheet.ListObjects.AddEx(XlListObjectSourceType.xlSrcRange, verifyColumnRange);

                        verifyColumns = verifyListObjects.ListColumns;
                    }
                    else
                    {
                        verifyListObjects = verifySheet.ListObjects[1];
                        verifyColumns = verifyListObjects.ListColumns;


                    }

                    //기존 xmlmap을 지우면 새로 Xpath를 지정해줘야 한다.
                    for (int i = 1; i <= verifyColumns.Count; ++i)
                    {
                        if (i <= verifyFields.Length)
                        {
                            verifyColumns[i].XPath.SetValue(xmlMap1, $"/{info.Key}/verify/@{verifyFields[i - 1].Name}");
                            verifyColumns[i].Name = verifyFields[i - 1].Name;
                        }
                        else
                        {
                            verifyColumns[i].XPath.SetValue(xmlMap1, $"/{info.Key}/verify/@TableId");
                            verifyColumns[i].Name = "TableID";
                        }
                    }

                    verifyRows = verifyListObjects.ListRows;
                    if (verifyRows.Count > 0)
                    {
                        verifyListObjects.DataBodyRange.Delete();
                    }

                    foreach (var column in info.Value.Columns)
                    {
                        ListRow row = verifyRows.AddEx();
                        row.Range[1] = column.ColumnName;
                        row.Range[2] = column.Type;
                        row.Range[3] = column.IsClient == true ? "Y" : "N";
                        row.Range[4] = column.IsServer == true ? "Y" : "N";
                        row.Range[5] = column.PK == true ? "Y" : "N";
                        row.Range[6] = column.EnumID == 0 ? "-" : column.EnumID.ToString();
                        row.Range[7] = string.IsNullOrEmpty(column.IDRule) == true ? "-" : column.IDRule;
                        row.Range[8] = column.StringHashIds == 0 ? "-" : column.StringHashIds.ToString();
                        row.Range[9] = info.Value.TableId;
                    }


                    //record
                    ListObject recordListObjects;
                    ListColumns recordColumns;

                    if (recordSheet.ListObjects.Count == 0)
                    {
                        Range recordRange = recordSheet.Range[recordSheet.Cells[1, 1], recordSheet.Cells[1, info.Value.Columns.Count]];
                        recordListObjects = recordSheet.ListObjects.AddEx(XlListObjectSourceType.xlSrcRange, recordRange);
                        recordColumns = recordListObjects.ListColumns;

                        for (int i = 1; i <= recordColumns.Count; ++i)
                        {
                            recordColumns[i].XPath.SetValue(xmlMap1, $"/{info.Key}/record/{info.Value.Columns[i - 1].ColumnName}");
                            recordColumns[i].Name = info.Value.Columns[i - 1].ColumnName;
                        }
                    }
                    else
                    {
                        recordListObjects = recordSheet.ListObjects[1];
                        recordColumns = recordListObjects.ListColumns;

                        //데이터가 지워진 컬럼 부터 제거한다...
                        List<int> deleteColumnIds = new List<int>();
                        for (int i = 1; i <= recordColumns.Count; ++i)
                        {

                            var column = info.Value.Columns.Find(x => x.ColumnName == recordColumns[i].Name);
                            if (column == null)
                            {
                                recordColumns[i].Delete();
                            }
                        }

                        for (int i = 0; i < info.Value.Columns.Count; ++i)
                        {
                            if (recordColumns.Count < i+1)
                            {
                                recordColumns.Add();
                                continue;
                            }

                            if (recordColumns[i+1].Name == info.Value.Columns[i].ColumnName)
                                continue;

                            
                            var column = recordColumns.Add(i+1);
                            column.Name = info.Value.Columns[i].ColumnName;

                        }
                        ////XPath 재연결
                        for (int i = 1; i <= recordColumns.Count; ++i)
                        {
                            recordColumns[i].XPath.SetValue(xmlMap1, $"/{info.Key}/record/{info.Value.Columns[i - 1].ColumnName}");
                            recordColumns[i].Name = info.Value.Columns[i - 1].ColumnName;
                        }
                    }

                    excel.DisplayAlerts = false;

                    ExcelUtils.CheckRunningAndKill(checkPath, info.Key);
                    excelworkBook.SaveAs(checkPath, XlFileFormat.xlOpenXMLWorkbook, Type.Missing, Type.Missing, Type.Missing, Type.Missing,
        Microsoft.Office.Interop.Excel.XlSaveAsAccessMode.xlExclusive, Type.Missing, Type.Missing, Type.Missing, Type.Missing, Type.Missing);
                    excelworkBook.Close();
                    excel.DisplayAlerts = true;
                    excel.Quit();
                }
            }
        }

        private void Save(string outputPath)
        {
            try
            {
                CreateXml(outputPath);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                //string command = "/C taskkill -F -IM excel.exe";
                //Process.Start("cmd.exe", command);
            }

        }
    }


}
