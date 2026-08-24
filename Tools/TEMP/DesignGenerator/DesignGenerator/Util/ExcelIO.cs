using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Xml;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.CodeDom;
using System.CodeDom.Compiler;
using Excel = Microsoft.Office.Interop.Excel;

namespace DesignGenerator.Util
{
    #region Excel
    //Excel Application 하나를 열어두고 워크북만 종료한다. 반드시 using 으로
    public sealed class ExcelSession : IDisposable
    {
        private Excel.Application app;
        private Excel.Workbooks workbooks;
        private bool disposed;
        private bool calcApplied;

        public ExcelSession()
        {
            app = new Excel.Application();
            app.Visible = false;
            app.DisplayAlerts = false;
            app.ScreenUpdating = false;
            app.EnableEvents = false;
            app.AskToUpdateLinks = false;

            // app.Calculation 은 여기서 설정하면 안 된다.
            //
            //   Calculation 은 Application 의 속성이지만 실제로는 "활성 워크북"의 계산 모드를
            //   바꾸는 것이라, 워크북이 하나도 열려 있지 않으면 Excel 이 거부한다.
            //   → COMException 0x800A03EC
            //   Excel 을 막 띄운 이 시점이 정확히 그 상태다.
            //
            //   같은 줄에 있는 Visible / DisplayAlerts / ScreenUpdating / EnableEvents /
            //   AskToUpdateLinks 는 순수 Application 속성이라 워크북 없이도 된다.
            //   Calculation 만 다르다.
            //
            //   → 첫 워크북을 연 직후로 미룬다. ApplyManualCalculation() 참고.

            workbooks = app.Workbooks;
        }

        public bool Ready { get { return app.Ready; } }

        /// <summary>
        /// 수동 계산 모드로 전환. 워크북이 열린 뒤에만 성공하므로 Open/NewWorkbook 직후에 부른다.
        /// 한 번 성공하면 세션 내내 유지되므로 다시 시도하지 않는다.
        /// </summary>
        private void ApplyManualCalculation()
        {
            if (calcApplied) return;
            try
            {
                app.Calculation = Excel.XlCalculation.xlCalculationManual;
                calcApplied = true;
            }
            catch (COMException)
            {
                // 아직 워크북이 없는 상태. 다음 Open 에서 다시 시도한다.
                // 끝내 실패해도 계산이 느려질 뿐 결과는 같으므로 던지지 않는다.
            }
        }

        public Excel.Workbook Open(string path, bool readOnly = true)
        {
            Excel.Workbook wb = workbooks.Open(
                Filename: path,
                UpdateLinks: 0,                 // 링크 갱신 안 함 (켜져 있으면 파일당 수 초)
                ReadOnly: readOnly,
                IgnoreReadOnlyRecommended: true,
                Notify: false,
                AddToMru: false);

            ApplyManualCalculation();           // 워크북이 생긴 지금 적용
            return wb;
        }

        public Excel.Workbook NewWorkbook()
        {
            Excel.Workbook wb = workbooks.Add();
            ApplyManualCalculation();           // 워크북이 생긴 지금 적용
            return wb;
        }

        public void Close(Excel.Workbook wb, bool saveChanges = false)
        {
            if (wb == null)
                return;
            try
            {
                wb.Close(saveChanges);
            }
            finally
            {
                Release(wb);
            }
        }

        public static void Release(object comObject)
        {
            if (comObject == null)
                return;
            try
            {
                if (Marshal.IsComObject(comObject))
                    Marshal.FinalReleaseComObject(comObject);
            }
            catch
            {
            }
        }

        public void Dispose()
        {
            if (disposed)
                return;
            disposed = true;

            try
            {
                if (app != null)
                {
                    app.ScreenUpdating = true;
                    app.DisplayAlerts = true;
                    app.EnableEvents = true;

                    // Calculation 은 되돌리지 않는다.
                    // 워크북이 전부 닫힌 뒤라 여기서 건드리면 또 0x800A03EC 이고,
                    // 그러면 이 catch 에 걸려 아래 Quit() 까지 건너뛰어 EXCEL.EXE 가 남는다.
                    // 어차피 바로 Quit 하므로 되돌릴 이유도 없다.

                    app.Quit();
                }
            }
            catch
            {

            }

            Release(workbooks);
            workbooks = null;
            Release(app);
            app = null;

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
    }

    public static class ExcelUtils
    {
        //xlsx 하나를 XmlMap으로 Xml 내보내기
        public static void ExportXml(string filePath, string outputPath)
        {
            if (string.IsNullOrEmpty(filePath))
                throw new ArgumentException("ExportXml: 입력 경로가 비었습니다.");
            if (!File.Exists(filePath))
                throw new FileNotFoundException("엑셀 파일을 찾을 수 없습니다.", filePath);

            using (var session = new ExcelSession())
            {
                ExportOne(session, filePath, outputPath);
            }
        }

        //폴더 안의 모든 xlsx를 XML 로. Excel 인스턴스는 하나만 쓴다.
        //true면 xlsx가 XML보다 새로운 것만 처리
        public static int ExportAllXml(string folderPath, string outputPath, bool changedOnly = false)
        {
            if (string.IsNullOrEmpty(folderPath))
                throw new ArgumentException("ExportAllXml: 입력 폴더가 비었습니다.");
            if (!Directory.Exists(folderPath))
                throw new DirectoryNotFoundException("폴더가 없습니다: " + folderPath);

            Directory.CreateDirectory(outputPath);

            string[] files = CollectExcelFiles(folderPath);
            if (changedOnly)
                files = files.Where(f => IsNewerThanXml(f, outputPath)).ToArray();

            if (files.Length == 0)
            {
                Console.WriteLine(changedOnly ? "변경된 엑셀 파일이 없습니다." : "폴더 내에 엑셀 파일이 없습니다.");
                return 0;
            }

            var sw = Stopwatch.StartNew();
            using (var session = new ExcelSession())
            {
                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    ExportOne(session, files[i], Path.Combine(outputPath, name + ".xml"));
                    Console.WriteLine($"[{i + 1}/{files.Length}] {name}");
                }
            }
            Console.WriteLine($"XML 내보내기 완료: {files.Length}개, {sw.Elapsed.TotalSeconds:F1}s");
            return files.Length;
        }

        private static void ExportOne(ExcelSession session, string xlsxPath, string xmlPath)
        {
            Excel.Workbook wb = null;
            Excel.XmlMaps maps = null;
            Excel.XmlMap map = null;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(xmlPath));
                wb = session.Open(xlsxPath);
                maps = wb.XmlMaps;
                if (maps.Count == 0)
                    throw new InvalidOperationException($"{Path.GetFileName(xlsxPath)} 에 XmlMap 이 없습니다.");

                map = maps[1];
                wb.SaveAsXMLData(xmlPath, map);
            }
            finally
            {
                ExcelSession.Release(map);
                ExcelSession.Release(maps);
                session.Close(wb);
            }
        }

        public static string[] CollectExcelFiles(string folderPath)
        {
            return Directory.EnumerateFiles(folderPath)
                .Where(f =>
                {
                    string ext = Path.GetExtension(f).ToLowerInvariant();
                    if (ext != ".xlsx" && ext != ".xls" && ext != ".xlsb") return false;
                    return !Path.GetFileName(f).StartsWith("~$");   // 엑셀 임시 파일
                })
                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        private static bool IsNewerThanXml(string xlsx, string xmlDir)
        {
            string xml = Path.Combine(xmlDir, Path.GetFileNameWithoutExtension(xlsx) + ".xml");
            if (!File.Exists(xml))
                return true;
            return File.GetLastWriteTimeUtc(xlsx) > File.GetLastWriteTimeUtc(xml);
        }

        public static void KillAllExcel()
        {
            foreach (var p in Process.GetProcessesByName("EXCEL"))
            {
                try
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
    }
    #endregion

    #region XML
    public sealed class XmlManager
    {
        public static XmlDocument LoadXML(string address)
        {
            if (!File.Exists(address))
                throw new FileNotFoundException("XML 파일을 찾을 수 없습니다.", address);

            var xml = new XmlDocument();
            xml.Load(address);
            return xml;
        }

        public static void LoadXML(string address, string outputPath, Action<XmlDocument, string> callback = null)
        {
            var xml = LoadXML(address);
            if (callback != null)
                callback(xml, outputPath);
        }

        public static void LoadAllXML(string address, string outputPath, Action<XmlDocument, string> callback = null)
        {
            foreach (var doc in LoadAllXML(address))
                if (callback != null)
                    callback(doc, outputPath);
        }

        public static List<XmlDocument> LoadAllXML(string address)
        {
            var data = new List<XmlDocument>();
            if (!Directory.Exists(address)) return data;

            // 파일 순서를 고정한다. GetFiles 순서에 기대면 OS/파일시스템에 따라 달라진다.
            foreach (var file in Directory.GetFiles(address, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var xml = new XmlDocument();
                xml.Load(file);
                data.Add(xml);
            }
            return data;
        }

        //파일명까지 같이 필요할 때 
        public static List<KeyValuePair<string, XmlDocument>> LoadAllXmlWithName(string address)
        {
            var data = new List<KeyValuePair<string, XmlDocument>>();
            if (!Directory.Exists(address))
                return data;

            foreach (var file in Directory.GetFiles(address, "*.xml").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var xml = new XmlDocument();
                xml.Load(file);
                data.Add(new KeyValuePair<string, XmlDocument>(Path.GetFileName(file), xml));
            }
            return data;
        }

    }
    #endregion

    #region 코드 출력
    public static class GeneratorUtils
    {
        //실제로 파일을 사용했으면
        public static bool ExportGenerator(CodeCompileUnit unit, string outputPath)
        {
            if (string.IsNullOrEmpty(outputPath))
                throw new ArgumentException("outputPath 가 비었습니다.");

            var provider = CodeDomProvider.CreateProvider("CSharp");
            var options = new CodeGeneratorOptions { BracingStyle = "C" };

            var builder = new StringBuilder();
            using (var sourceWriter = new StringWriter(builder))
                provider.GenerateCodeFromCompileUnit(unit, sourceWriter, options);

            return WriteIfChanged(outputPath, builder.ToString());
        }

        public static bool WriteIfChanged(string path, string content)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            content = content.Replace("\r\n", "\n").Replace("\r", "\n");

            if (File.Exists(path))
            {
                string existing = File.ReadAllText(path, Encoding.UTF8)
                                      .Replace("\r\n", "\n").Replace("\r", "\n");
                if (string.Equals(existing, content, StringComparison.Ordinal))
                    return false;
            }

            File.WriteAllText(path, content, new UTF8Encoding(true));
            return true;
        }
    }
    #endregion

    #region DLL
    public static class DllExporter
    {
        /// <summary>
        /// 절대 Design.dll 안으로 병합되면 안 되는 어셈블리.
        /// 병합되면 유니티에 같은 타입이 두 벌 존재하게 되고, 특히 protobuf-net 은
        /// [ProtoContract] 어트리뷰트의 "타입 동일성" 이 깨져 직렬화가 조용히 이상해진다.
        /// </summary>
        private static readonly string[] NeverMerge =
        {
            "protobuf-net", "protobuf-net.core",
            "unityengine", "unityeditor",
            "mscorlib", "netstandard", "newtonsoft.json",
            "system", "system.core", "system.xml",
        };

        // ── ILRepack.exe 찾기 ──────────────────────────────────────────
        //
        //  기존에는 {exe}\ILRepack\tools\ILRepack.exe 한 곳만 봤다.
        //  NuGet 으로 설치하면 거기 안 생기므로 못 찾는다.
        //  설치 방식(packages.config / PackageReference)에 따라 위치가 다르므로 전부 뒤진다.

        /// <summary>ILRepack.exe 경로. 밖에서 직접 지정할 수도 있다.</summary>
        public static string ILRepackPath;

        public static string FindILRepack()
        {
            var tried = new List<string>();

            // ① 코드/환경변수로 직접 지정
            foreach (var direct in new[] { ILRepackPath, Environment.GetEnvironmentVariable("ILREPACK") })
            {
                if (string.IsNullOrEmpty(direct)) continue;
                tried.Add(direct);
                if (File.Exists(direct)) return direct;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            // ② 실행 폴더 밑 (기존 기대 위치 포함)
            foreach (var rel in new[]
            {
                Path.Combine("ILRepack", "tools", "ILRepack.exe"),
                Path.Combine("ILRepack", "ILRepack.exe"),
                "ILRepack.exe",
            })
            {
                string p = Path.Combine(baseDir, rel);
                tried.Add(p);
                if (File.Exists(p)) return p;
            }

            // ③ 솔루션의 packages 폴더 (packages.config 방식).
            //    bin\Debug 에서 위로 올라가며 찾는다.
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string hit = SearchUnder(Path.Combine(dir.FullName, "packages"), "ILRepack*", tried);
                if (hit != null) return hit;
            }

            // ④ 전역 NuGet 캐시 (PackageReference 방식)
            string home = Environment.GetEnvironmentVariable("USERPROFILE");
            if (!string.IsNullOrEmpty(home))
            {
                string hit = SearchUnder(Path.Combine(home, ".nuget", "packages", "ilrepack"), "*", tried);
                if (hit != null) return hit;
            }

            throw new FileNotFoundException(
                "ILRepack.exe 를 찾을 수 없습니다.\n\n" +
                "찾아본 위치:\n  " + string.Join("\n  ", tried) + "\n\n" +
                "아래 중 하나를 하세요.\n" +
                "  1) NuGet 에서 ILRepack 패키지 설치 → 위 ③/④ 에서 자동으로 찾습니다\n" +
                "  2) ILRepack.exe 를 " + Path.Combine(baseDir, "ILRepack", "tools") + " 에 복사\n" +
                "  3) 환경변수 ILREPACK 에 ILRepack.exe 전체 경로 지정\n" +
                "  4) 병합하지 않기 — DllExporter.CopyDlls 를 쓰면 ILRepack 자체가 필요 없습니다.\n" +
                "     DataMgr.dll 과 LocalData.dll 은 서로 참조하지 않으므로 합칠 이유가 없습니다.");
        }

        private static string SearchUnder(string root, string dirPattern, List<string> tried)
        {
            tried.Add(Path.Combine(root, "*", "...", "ILRepack.exe"));
            if (!Directory.Exists(root)) return null;
            try
            {
                foreach (var d in Directory.GetDirectories(root, dirPattern)
                                           .OrderByDescending(x => x, StringComparer.OrdinalIgnoreCase))
                {
                    var hits = Directory.GetFiles(d, "ILRepack.exe", SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
            }
            catch
            {
                // 접근 거부 등은 무시하고 다음 후보로
            }
            return null;
        }

        public static void MergeDll(string folderPath, string outputPath)
        {
            string outName = Path.GetFileNameWithoutExtension(outputPath);

            // 폴더의 *.dll 을 전부 병합하던 것을 걸러낸다.
            // 누가 이 폴더에 dll 하나 떨어뜨리면 조용히 같이 묶이던 문제도 같이 사라진다.
            var dllFiles = new List<string>();
            foreach (var f in Directory.GetFiles(folderPath, "*.dll")
                                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(f);

                if (string.Equals(name, outName, StringComparison.OrdinalIgnoreCase))
                    continue;   // 이전 실행의 산출물

                if (NeverMerge.Contains(name.ToLowerInvariant()))
                {
                    Console.WriteLine($"  [제외] {Path.GetFileName(f)} — 병합하지 않고 외부 참조로 둡니다.");
                    Console.WriteLine($"         이 파일은 유니티 프로젝트에 '따로' 넣으세요.");
                    continue;
                }

                dllFiles.Add(f);
            }

            if (dllFiles.Count == 0)
                throw new InvalidOperationException($"{folderPath} 에 병합할 dll 이 없습니다.");

            string ilRepackPath = FindILRepack();

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            Console.WriteLine("ILRepack: " + ilRepackPath);
            Console.WriteLine("DLL 병합 입력:");
            foreach (var f in dllFiles) Console.WriteLine("  + " + Path.GetFileName(f));

            // /lib: 로 참조 검색 경로를 넘긴다.
            // 이게 없으면 ILRepack 이 protobuf-net 을 못 찾아서, 어쩔 수 없이
            // protobuf-net.dll 을 입력 폴더에 두게 되고 → 그대로 병합돼 버린다.
            var libs = new[] { AppDomain.CurrentDomain.BaseDirectory, folderPath }
                .Where(d => !string.IsNullOrEmpty(d) && Directory.Exists(d))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Select(d => "/lib:\"" + d.TrimEnd('\\') + "\"");

            string arguments = $"/out:\"{outputPath}\" "
                             + string.Join(" ", libs) + " "
                             + string.Join(" ", dllFiles.Select(f => "\"" + f + "\""));

            var startInfo = new ProcessStartInfo
            {
                FileName = ilRepackPath,
                Arguments = arguments,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using (var process = new Process { StartInfo = startInfo })
            {
                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                string error = process.StandardError.ReadToEnd();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new Exception("ILRepack 실패: " + error + output);

                Console.WriteLine("DLL 병합 완료: " + outputPath);
            }

            WarnStaleOutputs(Path.GetDirectoryName(outputPath), dllFiles, outputPath);
        }

        /// <summary>
        /// 병합 산출물 옆에 중간 dll 이 남아 있으면 알린다.
        /// Design.dll 과 DataMgr.dll 이 같이 유니티에 올라가면 타입이 두 벌이 되어
        /// "The type 'X' exists in both ..." 로 깨진다.
        /// </summary>
        private static void WarnStaleOutputs(string outputDir, List<string> inputs, string outputPath)
        {
            var mergedNames = new HashSet<string>(
                inputs.Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase);

            foreach (var f in Directory.GetFiles(outputDir, "*.dll"))
            {
                if (string.Equals(f, outputPath, StringComparison.OrdinalIgnoreCase)) continue;
                if (!mergedNames.Contains(Path.GetFileName(f))) continue;

                Console.WriteLine($"  [경고] {Path.GetFileName(f)} 이(가) 출력 폴더에 남아 있습니다.");
                Console.WriteLine($"         {Path.GetFileName(outputPath)} 에 이미 포함돼 있어 타입이 중복됩니다. 삭제하세요.");
            }
        }

        /// <summary>
        /// 병합하지 않고 중간 dll 을 그대로 배치한다. ILRepack 이 필요 없다.
        /// DataMgr.dll 과 LocalData.dll 은 서로 참조하지 않는 독립 어셈블리라 합칠 이유가 없다.
        /// </summary>
        public static void CopyDlls(string folderPath, string outputDir)
        {
            Directory.CreateDirectory(outputDir);

            foreach (var f in Directory.GetFiles(folderPath, "*.dll")
                                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (NeverMerge.Contains(name.ToLowerInvariant()))
                {
                    Console.WriteLine($"  [건너뜀] {Path.GetFileName(f)} — 유니티 프로젝트에 따로 두세요.");
                    continue;
                }
                File.Copy(f, Path.Combine(outputDir, Path.GetFileName(f)), true);
                Console.WriteLine("  복사: " + Path.GetFileName(f));
            }

            string stale = Path.Combine(outputDir, "Design.dll");
            if (File.Exists(stale))
                Console.WriteLine("  [경고] Design.dll 이 남아 있습니다. 병합 모드에서 만든 것이면 삭제하세요 (타입 중복).");
        }


        public static void ExportStringToDll(string data, string outputPath)
        {
            Compile(new[] { CSharpSyntaxTree.ParseText(data) }, outputPath);
        }

        public static void ExportCSToDll(string folderPath, string outputPath)
        {
            var trees = Directory.EnumerateFiles(folderPath, "*.cs")
                                 .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                 .Select(f => CSharpSyntaxTree.ParseText(File.ReadAllText(f, Encoding.UTF8)))
                                 .ToArray();
            if (trees.Length == 0)
                throw new InvalidOperationException($"{folderPath} 에 컴파일할 .cs 가 없습니다.");

            Compile(trees, outputPath);
        }

        private static void Compile(IEnumerable<SyntaxTree> trees, string outputPath)
        {
            string protobufNetPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "protobuf-net.dll");
            if (!File.Exists(protobufNetPath))
                throw new FileNotFoundException("protobuf-net.dll 을 찾을 수 없습니다.", protobufNetPath);

            var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

            var compile = CSharpCompilation.Create("DynamicAssembly")
                .WithOptions(compileOptions)
                .AddReferences(MetadataReference.CreateFromFile(typeof(Console).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(System.Linq.Enumerable).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(System.MemoryExtensions).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(typeof(XmlWriter).Assembly.Location))
                .AddReferences(MetadataReference.CreateFromFile(protobufNetPath))
                .AddSyntaxTrees(trees)
                .AddSyntaxTrees(CSharpSyntaxTree.ParseText("using ProtoBuf;"));

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            using (var memoryStream = new MemoryStream())
            {
                EmitResult result = compile.Emit(memoryStream);

                if (!result.Success)
                {
                    var sb = new StringBuilder("생성된 코드 컴파일 실패:\n");
                    foreach (var d in result.Diagnostics.Where(x => x.Severity == DiagnosticSeverity.Error).Take(50))
                    {
                        var span = d.Location.GetLineSpan();
                        sb.AppendLine($"  {d.Id} {span.Path}({span.StartLinePosition.Line + 1},{span.StartLinePosition.Character + 1}): {d.GetMessage()}");
                    }
                    // 기존에는 콘솔에 찍고 그냥 넘어가서, 깨진 dll 로 다음 단계가 진행됐습니다.
                    throw new Exception(sb.ToString());
                }

                File.WriteAllBytes(outputPath, memoryStream.ToArray());
            }
        }

        public static void MoveDllFile(string filePath, string targetPath)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            File.Copy(filePath, targetPath, true);
        }
    }
    #endregion
}
