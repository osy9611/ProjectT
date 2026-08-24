using DesignGenerator.Core;
using DesignGenerator.DataManager;
using DesignGenerator.Enum;
using DesignGenerator.Localize;
using DesignGenerator.Table;
using DesignGenerator.Util;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DesignGenerator
{
    internal class Program
    {
        private static bool jsonOutput;
        private static bool full;
        private static bool warnAsError;

        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
#if !TestMode
                string[] testArgs_Enum = { "Enum", "D:\\Project\\ProjectT\\DesignTable\\Data", "D:\\Project\\ProjectT\\Client\\Assets" };
                string[] testArgs_TableCreator = { "TableCreate", "D:\\Project\\ProjectT\\DesignTable\\Data", "null" };
                string[] testArgs_TableGenerate = { "TableGenerate", "D:\\Project\\ProjectT\\DesignTable\\Data", "D:\\Project\\ProjectT\\Client\\Assets", "All" };
                string[] testArgs_Local = { "Local", "D:\\Project\\ProjectT\\DesignTable\\Data", "D:\\Project\\ProjectT\\Client\\Assets" };
                List<string> rest = ParseFlags(testArgs_TableGenerate);

#else
                List<string> rest = ParseFlags(args);
#endif

                if (rest.Count == 0)
                {
                    PrintUsage();
                    return 2;
                }

                switch (rest[0])
                {
                    case "Enum":
                        return RunEnum(rest);
                    case "TableCreate":
                        return RunTableCreate(rest);
                    case "TableGenerate":
                        return RunTableGenerate(rest);
                    case "Local":
                        return RunLocal(rest);
                    default:
                        Console.Error.WriteLine($"알 수 없는 명령: {rest[0]}");
                        PrintUsage();
                        return 2;
                }
            }
            catch (Exception e)
            {
                Console.Error.WriteLine(e);
                return 1;
            }
        }

        private static List<string> ParseFlags(string[] args)
        {
            var rest = new List<string>();

            foreach (string arg in args)
            {
                switch (arg)
                {
                    case "--full":
                        full = true;
                        break;
                    case "--changed-only":
                        full = false;
                        break;
                    case "--format=json":
                        jsonOutput = true;
                        break;
                    case "--warn-as-error":
                        warnAsError = true;
                        break;
                    default: rest.Add(arg); break;
                }
            }
            return rest;
        }

        private static void PrintUsage()
        {
            Console.WriteLine(@"사용법
                                  DesignGenerator Enum          <dataPath> <outputPath>
                                  DesignGenerator TableCreate   <dataPath> <tableName|null>
                                  DesignGenerator TableGenerate <dataPath> <outputPath> <All|One> [tableName]
                                  DesignGenerator Local         <dataPath> <outputPath>

                                옵션
                                  --full            전체 재생성 (기본은 변경된 엑셀만)
                                  --format=json     진단을 JSON 으로 출력 (CI 용)
                                  --warn-as-error   경고도 실패로 취급

                                종료코드  0 성공 / 1 실패 / 2 사용법 오류");
        }

        private static int Report(DiagnosticBag diag, bool ok)
        {
            Console.Write(jsonOutput ? diag.ToJson() + Environment.NewLine : diag.ToReport());

            if (!ok || diag.HasError) return 1;
            if (warnAsError && diag.WarningCount > 0)
            {
                Console.Error.WriteLine("--warn-as-error: 경고가 있어 실패로 처리합니다.");
                return 1;
            }
            return 0;
        }

        private static int RunEnum(List<string> args)
        {
            if (args.Count < 3)
            {
                PrintUsage();
                return 2;
            }

            var result = EnumPipeline.Run(new EnumGenerateOptions
            {
                DataRoot = args[1],
                OutputRoot = args[2],
                TreatWarningsAsErrors = warnAsError
            });

            Console.WriteLine($"enum {result.Groups.Count}개 / 멤버 {result.Groups.Sum(g => g.Members.Count)}개" +
                           (result.FileWritten ? " — DesignEnum.cs 갱신" : " — 변경 없음 \n"));
            return Report(result.Diagnostics, result.Succeeded);
        }

        private static int RunTableCreate(List<string> args)
        {
            if (args.Count < 3)
            {
                PrintUsage();
                return 2;
            }

            string dataPath = args[1];
            string tableName = args[2];

            var creator = new TableCreator();
            bool ok = tableName == "null"
               ? creator.CreateAll(Path.Combine(dataPath, "table명세서.xlsx"), Path.Combine(dataPath, "Table", "Tables"))
               : creator.Create(Path.Combine(dataPath, "table명세서.xlsx"), Path.Combine(dataPath, "Table", "Tables"), tableName);
            return Report(creator.Diagnostics, ok);
        }

        private static int RunTableGenerate(List<string> args)
        {
            if (args.Count < 4)
            {
                PrintUsage();
                return 2;
            }

            string folderPath = args[1];
            string outputPath = args[2];
            string genType = args[3];

            string clientDir = Path.Combine(folderPath, "Table", "Client");
            string xmlDir = Path.Combine(folderPath, "Table", "Xml");
            string dllDir = Path.Combine(folderPath, "Dll");
            string automationDll = Path.Combine(outputPath, "Automation", "Dll");

            var tableGen = new Table.TableGenerator();
            var dataMgrGen = new DataMgrGenerator();
            var serializerGen = new DataMgrSerializerGenerator();

            tableGen.OnMgrSet += dataMgrGen.SetData;
            tableGen.OnSerializerSet += serializerGen.SetData;

            string tableName = null;
            bool ok;

            if (genType == "One")
            {
                if (args.Count < 5) { PrintUsage(); return 2; }
                tableName = args[4];
                ok = tableGen.Load(folderPath, tableName, xmlDir, clientDir);
            }
            else if (genType == "All")
            {
                ok = tableGen.LoadAll(folderPath, xmlDir, clientDir, changedOnly: !full);
            }
            else
            {
                Console.Error.WriteLine($"genType 은 All 또는 One 이어야 합니다: '{genType}'");
                return 2;
            }

            int code = Report(tableGen.Diagnostics, ok);
            if (code != 0)
            {
                Console.Error.WriteLine("검증에 실패해 코드/데이터를 생성하지 않았습니다.");
                return code;
            }

            dataMgrGen.Create();
            dataMgrGen.Save(clientDir);
            serializerGen.Create();
            serializerGen.Save(clientDir);

            DllExporter.ExportCSToDll(clientDir, Path.Combine(dllDir, "DataMgr.dll"));
            DllExporter.MergeDll(dllDir, Path.Combine(automationDll, "Design.dll"));

            string tableOut = Path.Combine(outputPath, "Automation", "Table");
            if (genType == "One") tableGen.ExportDataByteFile(automationDll, tableOut, tableName);
            else tableGen.ExportAllDataByteFile(automationDll, tableOut);

            Console.WriteLine("TableGenerate 완료");
            return 0;
        }

        private static int RunLocal(List<string> args)
        {
            if (args.Count < 3)
            {
                PrintUsage();
                return 2;
            }

            string folderPath = args[1];
            string outputPath = args[2];
            string automation = Path.Combine(outputPath, "Automation");

            var localGen = new LocalizeGenerator();
            bool ok = localGen.Create(folderPath, automation, changedOnly: !full);

            int code = Report(localGen.Diagnostics, ok);
            if (code != 0)
                return code;

            DllExporter.MergeDll(Path.Combine(folderPath, "Dll"),
                                 Path.Combine(automation, "Dll", "Design.dll"));
            localGen.ExportDataByteFile(automation);

            Console.WriteLine("Local 완료");
            return 0;
        }
    }
}
