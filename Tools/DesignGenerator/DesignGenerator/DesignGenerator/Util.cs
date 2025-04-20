using DocumentFormat.OpenXml.Office2021.DocumentTasks;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.Win32;
using OfficeOpenXml.Utils;
using ProtoBuf;
using System;
using System.CodeDom;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Xml;

namespace DesignGenerator
{
    public class ExcelUtils
    {
        public static void CheckRunningAndKill(string path, string fileName)
        {
            Process[] processes = Process.GetProcessesByName("EXCEL");

            foreach (Process process in processes)
            {
                try
                {
                    string processFilePath = process.MainModule.FileName;
                    if (processFilePath.Equals(path, StringComparison.OrdinalIgnoreCase))
                    {
                        process.Kill();
                        process.WaitForExit();


                        Console.WriteLine($"해당 엑셀이 실행중이기 떄문에 종료 {fileName}");
                    }
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
            }
        }

        public static void ExportXml(string filePath, string outputPath)
        {
            if (string.IsNullOrEmpty(filePath))
                Console.WriteLine($"ExportXml Path Is Null");
            FileInfo fi = new FileInfo(filePath);
            if (fi.Exists)
            {
                var excel = new Microsoft.Office.Interop.Excel.Application();
                Microsoft.Office.Interop.Excel.Workbook workbook = excel.Workbooks.Open(filePath);
                workbook.SaveAsXMLData(outputPath, workbook.XmlMaps[1]);

                workbook.Close();
                excel.Quit();
            }
        }

        public static async System.Threading.Tasks.Task ExportAllXml(string folderPath, string outputPath)
        {
            if (string.IsNullOrEmpty(folderPath))
                Console.WriteLine($"ExportAllXml Path Is Null");

            if (Directory.Exists(folderPath))
            {
                string[] excelFiles = Directory.GetFiles(folderPath, "*.xlsx")
                                           .Union(Directory.GetFiles(folderPath, "*.xls"))
                                           .ToArray();

                if (excelFiles.Any())
                {
                    foreach (var filePath in excelFiles)
                    {
                        string fileName = Path.GetFileNameWithoutExtension(filePath);
                        //임시 파일들은 제외
                        if (fileName.StartsWith("~$"))
                            continue;

                        await System.Threading.Tasks.Task.Run(() =>
                        {
                            var excel = new Microsoft.Office.Interop.Excel.Application();
                            Microsoft.Office.Interop.Excel.Workbook workbook = excel.Workbooks.Open(filePath);

                            workbook.SaveAsXMLData(outputPath + $"\\{fileName}.xml", workbook.XmlMaps[1]);

                            workbook.Close();
                            excel.Quit();

                            //Console.WriteLine($"{fileName} Save Complete!");
                        });

                        Console.WriteLine($"{fileName} Save Complete!");
                    }
                    GC.Collect();
                }
                else
                {
                    Console.WriteLine("폴더 내에 엑셀 파일이 없습니다.");
                }
            }
            else
            {
                Console.WriteLine("폴더가 존재하지 않습니다.");
            }
        }
    }

    //XMl Create Table Info
    public class XmlCreateTableInfo
    {
        private int tableId;
        public int TableId { get => tableId; set => tableId = value; }

        private List<XmlCreatTableColInfo> columns;
        public List<XmlCreatTableColInfo> Columns { get => columns; }

        public XmlCreateTableInfo()
        {
            tableId = 0;
            columns = new List<XmlCreatTableColInfo>();
        }
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
        public int StringHashIds;
    }


    #region XML
    public class XmlManager
    {
        public static void LoadXML(string address, string outputPath, System.Action<XmlDocument, string> callback = null)
        {
            XmlDocument xml = new XmlDocument();
            xml.Load(address);
            if (xml == null)
                return;

            callback?.Invoke(xml, outputPath);
        }

        public static XmlDocument LoadXML(string address)
        {
            XmlDocument xml = new XmlDocument();
            xml.Load(address);
            return xml;
        }

        public static void LoadAllXML(string address, string outputPath, System.Action<XmlDocument, string> callback = null)
        {
            XmlDocument xml = new XmlDocument();

            string[] excelFiles = Directory.GetFiles(address, "*.xml")
                                           .ToArray();

            if (excelFiles.Any())
            {
                foreach (var file in excelFiles)
                {
                    xml.Load(file);
                }
            }
            callback?.Invoke(xml, outputPath);
        }

        public static List<XmlDocument> LoadAllXML(string address)
        {
            List<XmlDocument> data = new List<XmlDocument>();

            string[] excelFiles = Directory.GetFiles(address, "*.xml")
                                           .ToArray();

            if (excelFiles.Any())
            {
                foreach (var file in excelFiles)
                {
                    XmlDocument xml = new XmlDocument();
                    xml.Load(file);
                    data.Add(xml);
                }
            }
            return data;
        }
    }
    #endregion

    public class Tab
    {
        public static string TAB1 = "\t";
        public static string TAB2 = "\t\t";
        public static string TAB3 = "\t\t\t";
        public static string TAB4 = "\t\t\t\t";
        public static string TAB5 = "\t\t\t\t\t";
        public static string TAB6 = "\t\t\t\t\t\t";
        public static string TAB7 = "\t\t\t\t\t\t\t";
    }

    public class GeneratorUtils
    {
        public static void ExportGenerator(CodeCompileUnit unit, string outputPath)
        {

            if (string.IsNullOrEmpty(outputPath))
            {
                Console.WriteLine("outputPath is Numm");
                return;
            }

            CodeDomProvider provider = CodeDomProvider.CreateProvider("CSharp");
            CodeGeneratorOptions options = new CodeGeneratorOptions();
            options.BracingStyle = "C";

            StringBuilder builder = new StringBuilder();
            using (StringWriter sourceWriter = new StringWriter(builder))
            {
                provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);

                StreamWriter sw;
                sw = new StreamWriter(outputPath);
                byte[] bytes = Encoding.Default.GetBytes(builder.ToString());
                sw.Write(Encoding.UTF8.GetString(bytes));
                sw.Flush();
                sw.Close();
            }
        }
    }

    public class EnumValueData
    {
        public string EnumName;
        public int EnumValue;
        public string Comment;

        public EnumValueData(string enumName, string enumValue, string comment)
        {
            EnumName = enumName;
            EnumValue = int.Parse(enumValue);
            Comment = comment;
        }
    }

    public class EnumInfo
    {
        public string EnumType;
        public string Comment;
        public int GroupID;

        public List<EnumValueData> Values = new List<EnumValueData>();

        public EnumInfo(string enumType, string comment, int groupID)
        {
            EnumType = enumType;
            Comment = comment;
            GroupID = groupID;
        }

        public void AddInfo(string enumName, string enumValue, string comment)
        {
            var data = new EnumValueData(enumName, enumValue, comment);
            Values.Add(data);
        }
    }

    public class DefineType
    {
        static public string ConverSystemTypeName(string type)
        {
            switch (type)
            {
                case "sbyte":
                    return "System.SByte";
                case "short":
                    return "System.Int16";
                case "int":
                    return "System.Int32";
                case "long":
                    return "System.Int64";
                case "byte":
                    return "System.UInt8";
                case "ushort":
                    return "System.UInt16";
                case "unit":
                    return "System.UInt32";
                case "ulong":
                    return "System.UInt64";
                case "string":
                    return "System.String";
                case "bool":
                    return "System.Boolean";
                case "float":
                    return "System.Single";
                case "double":
                    return "System.Double";
            }
            return string.Empty;
        }
        static public string ConvertTypeName(string type)
        {
            switch (type)
            {
                case "Int8":
                    return "sbyte";
                case "Int16":
                    return "short";
                case "Int32":
                    return "int";
                case "Int64":
                    return "long";
                case "UInt8":
                    return "byte";
                case "UInt16":
                    return "ushort";
                case "UInt32":
                    return "uint";
                case "UInt64":
                    return "ulong";
                case "string":
                case "bool":
                case "float":
                case "double":
                    return type;
            }

            return string.Empty;
        }


        static public object ConvertDataType(string type, string data)
        {
            switch (type)
            {
                case "sbyte":
                    return sbyte.Parse(data);
                case "short":
                    return short.Parse(data);
                case "int":
                    return int.Parse(data);
                case "long":
                    return long.Parse(data);
                case "byte":
                    return byte.Parse(data);
                case "ushort":
                    return ushort.Parse(data);
                case "uint":
                    return uint.Parse(data);
                case "ulong":
                    return ulong.Parse(data);
                case "string":
                    return data;
                case "bool":
                    return data == "Y" ? true : false;
                case "float":
                    return float.Parse(data);
                case "double":
                    return double.Parse(data);
            }

            return null;
        }

        static public bool CheckTypeName(string name)
        {
            switch (name)
            {
                case "Int8":
                case "UInt8":
                case "Int16":
                case "UInt16":
                case "Int32":
                case "UInt32":
                case "Int64":
                case "UInt64":
                case "float":
                case "double":
                case "bool":
                case "string":
                    return true;
            }

            return false;
        }
    }

    public class TablePKData
    {
        public string ColumnName;
        public string Type;
        public bool IsListRule;
        public bool IsListRuleFindPK;
        public int EnumId;

        public TablePKData(string col, string type, int enumId)
        {
            ColumnName = col;
            Type = type;
            EnumId = enumId;
        }
    }

    public class TableVarData
    {
        public string ColumnName;
        public string Type;
        public string RefTable;
        public int EnumId;
        public TableVarData(string col, string type, int enumId, string refTable = "")
        {
            ColumnName = col;
            Type = type;
            EnumId = enumId;
            RefTable = refTable;
        }
    }

    public class TableDataInfo
    {
        private List<TablePKData> pkData = new List<TablePKData>();
        public List<TablePKData> PKData
        {
            get => pkData;
        }

        private bool useListRule = false;
        public bool UseListRule { get => useListRule; set => useListRule = value; }

        private List<TableVarData> varData = new List<TableVarData>();
        public List<TableVarData> VarData
        {
            get => varData;
        }

        List<object[]> varObjectData = new List<object[]>();
        public List<object[]> VarObjectData
        {
            get => varObjectData;
        }

        private int talbeID = 0;
        public int TableID { get => talbeID; set => talbeID = value; }

        private string tableName;
        public string TableName { get => tableName; set => tableName = value; }

        public void AddPKData(string ColumName, string Type, int enumId)
        {
            pkData.Add(new TablePKData(ColumName, Type, enumId));
        }

        public void AddVarData(string ColumName, string Type, int enumId, string refTable = "")
        {
            varData.Add(new TableVarData(ColumName, Type, enumId, refTable));
        }

        public void AddIsListRule(string ColumnName)
        {
            var data = pkData.Find(x => x.ColumnName == ColumnName);
            if (data != null)
                data.IsListRule = true;
        }

        public void AddFindPKListRule(string ColumnName)
        {
            var data = pkData.Find(x => x.ColumnName == ColumnName);
            if (data != null)
                data.IsListRuleFindPK = true;
        }

        public string GetStringVerDatas(string varFormat, string insertVar)
        {
            string result = string.Empty;
            foreach (TableVarData data in varData)
            {
                result += string.Format(varFormat, data.ColumnName);

                if (varData[varData.Count - 1].ColumnName != data.ColumnName)
                {
                    result += insertVar;
                }
            }

            return result;
        }

        public string GetPkString(string varFormat, string insertVar)
        {
            string result = string.Empty;
            foreach (TablePKData data in pkData)
            {
                result += string.Format(varFormat, data.ColumnName);

                if (pkData[pkData.Count - 1].ColumnName != data.ColumnName)
                {
                    result += insertVar;
                }
            }

            return result;
        }

        public string GetListIdRuleString(string varFormat, string insertVar)
        {
            string result = string.Empty;

            foreach (TablePKData data in pkData)
            {
                if (data.IsListRuleFindPK)
                {
                    result += string.Format(varFormat, data.ColumnName) + insertVar;
                }

            }

            if (!string.IsNullOrEmpty(result))
                result = result.Substring(0, result.Length - insertVar.Length);

            return result;
        }

        public bool IsListIdRule()
        {
            foreach (TablePKData data in pkData)
            {
                if (data.IsListRuleFindPK)
                {
                    return true;
                }
            }

            return false;
        }

        public bool IsRefTable()
        {
            foreach (TableVarData data in varData)
            {
                if (!string.IsNullOrEmpty(data.RefTable))
                    return true;
            }

            return false;
        }
    }

    public enum LangaugeType
    {
        Ko,
        En,
        Jp
    }

    public class LocalDataInfo
    {
        private int enumID;
        public int EnumID { get => enumID; set => enumID = value; }

        private bool isUse;
        public bool IsUse { get => isUse; set => isUse = value; }

        private string localName;
        public string LocalName { get => localName; set => localName = value; }

        private string ko;
        public string Ko { get => ko; set => ko = value; }

        private string jp;
        public string Jp { get => jp; set => jp = value; }


        private string en;
        public string En { get => en; set => en = value; }
    }

    public class DllExporter
    {
        public static void MergeDll(string folderPath, string outputPath)
        {
            //string protobufNetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.dll");
            string[] dllFiles = Directory.GetFiles(folderPath, "*.dll")
                                                       .ToArray();

            string ilRepackPath = $"{AppContext.BaseDirectory}\\ILRepack\\tools\\ILRepack.exe";

            if (File.Exists(ilRepackPath))
            {
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = ilRepackPath,
                    Arguments = $"/out:{outputPath} {string.Join(" ", dllFiles)}",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };

                using (Process process = new Process { StartInfo = startInfo })
                {
                    process.Start();

                    // 비동기로 출력을 읽어올 수도 있음
                    string output = process.StandardOutput.ReadToEnd();
                    string error = process.StandardError.ReadToEnd();

                    process.WaitForExit();

                    if (process.ExitCode == 0)
                    {
                        Console.WriteLine("DLLs merged successfully.");
                    }
                    else
                    {
                        Console.WriteLine($"Error during merging: {error}");
                    }
                }
            }
            else
            {
                Console.WriteLine($"{ilRepackPath} Not Found");
            }

        }

        public static void ExportStringToDll(string data, string outputPath)
        {
            string protobufNetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.dll");
            //string protobufNetCorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.Core.dll");

            //c# 컴파일러 옵션 설정
            CSharpCompilationOptions compileOptions = new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);

            //c# 컴파일러 생성
            CSharpCompilation complie = CSharpCompilation.Create("DynamicAssembly")
                    .WithOptions(compileOptions)
                    //.AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)) // mscorlib
                    .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)) // System.Console 등
                    .AddReferences(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(typeof(System.MemoryExtensions).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(typeof(XmlWriter).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(protobufNetPath))
                    //.AddReferences(MetadataReference.CreateFromFile(protobufNetCorePath))
                    .AddSyntaxTrees(CSharpSyntaxTree.ParseText(data))
                    .AddSyntaxTrees(CSharpSyntaxTree.ParseText("using ProtoBuf;"));

            using (MemoryStream memoryStream = new MemoryStream())
            {
                //컴파일 결과
                EmitResult result = complie.Emit(memoryStream);

                if (result.Success)
                {
                    File.WriteAllBytes(outputPath, memoryStream.ToArray());
                }
                else
                {
                    // 컴파일 실패 시 에러 메시지 출력
                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();

                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                            Console.WriteLine($"Error ({diagnostic.Id}) in {lineSpan.Path} at line {lineSpan.StartLinePosition.Line + 1}, column {lineSpan.StartLinePosition.Character + 1}: {diagnostic.GetMessage()}");
                    }
                }
            }
        }

        public static void ExportCSToDll(string folderPath, string outputPath)
        {
            string protobufNetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.dll");
            //string protobufNetCorePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.Core.dll");

            IEnumerable<string> sourceFiles = Directory.EnumerateFiles(folderPath, "*.cs");

            //c# 컴파일러 옵션 설정
            CSharpCompilationOptions compileOptions = new CSharpCompilationOptions(Microsoft.CodeAnalysis.OutputKind.DynamicallyLinkedLibrary);

            //c# 컴파일러 생성
            CSharpCompilation complie = CSharpCompilation.Create("DynamicAssembly")
                    .WithOptions(compileOptions)
                    //.AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location)) // mscorlib
                    .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location)) // System.Console 등
                    .AddReferences(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(typeof(System.MemoryExtensions).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(typeof(XmlWriter).Assembly.Location))
                    .AddReferences(MetadataReference.CreateFromFile(protobufNetPath))
                    //.AddReferences(MetadataReference.CreateFromFile(protobufNetCorePath))
                    .AddSyntaxTrees(sourceFiles.Select(file => CSharpSyntaxTree.ParseText(File.ReadAllText(file))))
                    .AddSyntaxTrees(CSharpSyntaxTree.ParseText("using ProtoBuf;"));

            using (MemoryStream memoryStream = new MemoryStream())
            {
                //컴파일 결과
                EmitResult result = complie.Emit(memoryStream);

                if (result.Success)
                {
                    File.WriteAllBytes(outputPath, memoryStream.ToArray());
                }
                else
                {
                    // 컴파일 실패 시 에러 메시지 출력
                    foreach (Diagnostic diagnostic in result.Diagnostics)
                    {
                        FileLinePositionSpan lineSpan = diagnostic.Location.GetLineSpan();
                        if (diagnostic.Severity == DiagnosticSeverity.Error)
                            Console.WriteLine($"Error ({diagnostic.Id}) in {lineSpan.Path} at line {lineSpan.StartLinePosition.Line + 1}, column {lineSpan.StartLinePosition.Character + 1}: {diagnostic.GetMessage()}");
                    }
                }
            }
        }

        public static void MoveDllFile(string filePath, string targetPath)
        {
            System.IO.File.Copy(filePath, targetPath, true);
        }
    }
}