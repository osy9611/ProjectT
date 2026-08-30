using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
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
    //Excel Application 하나를 열어두고 워크북만 여닫는다. 반드시 using 으로
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

            //app.Calculation 은 여기서 설정하면 안 된다.
            //Application 의 속성이지만 실제로는 활성 워크북의 계산 모드를 바꾸는 것이라
            //워크북이 하나도 없으면 COMException 0x800A03EC 가 난다.
            //위의 Visible / DisplayAlerts 등은 순수 Application 속성이라 상관없다.
            //ApplyManualCalculation() 에서 첫 워크북을 연 뒤에 적용한다.

            workbooks = app.Workbooks;
        }

        public bool Ready { get { return app.Ready; } }

        /// <summary>
        /// 수동 계산 모드로 전환. 워크북이 열린 뒤에만 성공하므로 Open/NewWorkbook 직후에 부른다.
        /// 한 번 성공하면 세션 내내 유지된다.
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
                //아직 워크북이 없다. 다음 Open 에서 다시 시도한다.
                //끝내 실패해도 계산이 느려질 뿐 결과는 같으므로 던지지 않는다.
            }
        }

        public Excel.Workbook Open(string path, bool readOnly = true)
        {
            Excel.Workbook wb = workbooks.Open(
                Filename: path,
                UpdateLinks: 0,                 //링크 갱신 안 함. 켜져 있으면 파일당 수 초
                ReadOnly: readOnly,
                IgnoreReadOnlyRecommended: true,
                Notify: false,
                AddToMru: false);

            ApplyManualCalculation();
            return wb;
        }

        public Excel.Workbook NewWorkbook()
        {
            Excel.Workbook wb = workbooks.Add();
            ApplyManualCalculation();
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

                    //Calculation 은 되돌리지 않는다. 워크북이 전부 닫힌 뒤라
                    //여기서 건드리면 0x800A03EC 가 나고, catch 에 걸려 아래 Quit() 을
                    //건너뛰면 EXCEL.EXE 가 프로세스에 남는다.
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
        //xlsx 하나를 XmlMap 으로 XML 내보내기
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

        //폴더 안의 모든 xlsx 를 XML 로. Excel 인스턴스는 하나만 쓴다.
        //changedOnly 면 xlsx 가 XML 보다 새로운 것만 처리
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
                    return !Path.GetFileName(f).StartsWith("~$");   //엑셀 임시 파일
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

            //파일 순서를 고정한다. GetFiles 순서는 OS / 파일시스템에 따라 달라진다
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
        //실제로 썼으면 true
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
        /// 병합하면 안 되는 어셈블리. 병합되면 유니티에 같은 타입이 두 벌 존재하게 되고,
        /// protobuf-net 은 [ProtoContract] 타입 동일성이 깨져 직렬화가 조용히 어긋난다.
        /// </summary>
        private static readonly string[] NeverMerge =
        {
            "protobuf-net", "protobuf-net.core",
            "unityengine", "unityeditor",
            "mscorlib", "netstandard", "newtonsoft.json",
            "system", "system.core", "system.xml",
        };

        /// <summary>ILRepack.exe 경로. 밖에서 직접 지정할 수 있다.</summary>
        public static string ILRepackPath;

        public static string FindILRepack()
        {
            var tried = new List<string>();

            //1. 코드 / 환경변수로 직접 지정
            foreach (var direct in new[] { ILRepackPath, Environment.GetEnvironmentVariable("ILREPACK") })
            {
                if (string.IsNullOrEmpty(direct)) continue;
                tried.Add(direct);
                if (File.Exists(direct)) return direct;
            }

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            //2. 실행 폴더 밑
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

            //3. 솔루션의 packages 폴더(packages.config 방식). bin 에서 위로 올라가며 찾는다
            var dir = new DirectoryInfo(baseDir);
            for (int i = 0; i < 6 && dir != null; i++, dir = dir.Parent)
            {
                string hit = SearchUnder(Path.Combine(dir.FullName, "packages"), "ILRepack*", tried);
                if (hit != null) return hit;
            }

            //4. 전역 NuGet 캐시(PackageReference 방식)
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
                //접근 거부 등은 무시하고 다음 후보로
            }
            return null;
        }

        public static void MergeDll(string folderPath, string outputPath)
        {
            string outName = Path.GetFileNameWithoutExtension(outputPath);

            //병합 대상을 골라낸다. 폴더의 *.dll 을 통째로 넘기면
            //누가 dll 하나 떨어뜨렸을 때 조용히 같이 묶인다.
            var dllFiles = new List<string>();
            foreach (var f in Directory.GetFiles(folderPath, "*.dll")
                                       .OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
            {
                string name = Path.GetFileNameWithoutExtension(f);

                if (string.Equals(name, outName, StringComparison.OrdinalIgnoreCase))
                    continue;   //이전 실행의 산출물

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

            ///lib: 로 참조 검색 경로를 넘긴다. 이게 없으면 ILRepack 이 protobuf-net 을
            //못 찾아서 입력 폴더에 두게 되고, 그대로 병합돼 버린다.
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

            ValidateForUnity(outputPath);
            WarnStaleOutputs(Path.GetDirectoryName(outputPath), dllFiles, outputPath);
        }

        /// <summary>
        /// 산출물이 유니티가 로드할 수 있는 PE 인지 확인한다.
        ///
        /// ILRepack 은 빈 .rsrc 섹션을 만들면서 PE 를 망가뜨리는 경우가 있다.
        /// 리소스 데이터 디렉터리에 RVA 는 써 넣고 크기를 0 으로 두며,
        /// 그 .rsrc 가 .reloc 과 같은 RVA 를 차지한다.
        ///   유니티  : "Resource section is too small ... but it's 0 long"
        ///   .NET FW : Assembly.LoadFrom 이 FileLoadException 0x80070057
        /// 여기서 잡지 않으면 유니티에 넣을 때까지 모른다.
        /// </summary>
        public static void ValidateForUnity(string dllPath)
        {
            byte[] d = File.ReadAllBytes(dllPath);
            try
            {
                int pe = BitConverter.ToInt32(d, 0x3c);
                if (d[pe] != 'P' || d[pe + 1] != 'E') return;      //PE 가 아니면 판단하지 않는다

                int opt = pe + 24;
                ushort magic = BitConverter.ToUInt16(d, opt);
                int ddOff = opt + (magic == 0x10b ? 96 : 112);     //PE32 / PE32+

                int resRva = BitConverter.ToInt32(d, ddOff + 2 * 8);
                int resSize = BitConverter.ToInt32(d, ddOff + 2 * 8 + 4);

                if (resRva != 0 && resSize < 16)
                    throw new InvalidOperationException(
                        Path.GetFileName(dllPath) + " 의 PE 리소스 섹션이 깨졌습니다 " +
                        $"(rva={resRva:X}, size={resSize}).\n" +
                        "  ILRepack 이 빈 .rsrc 섹션을 만들면서 생기는 문제입니다.\n" +
                        "  유니티가 이 dll 을 거부합니다:\n" +
                        "    Could not load image ... Resource section is too small,\n" +
                        "    must be at least 16 bytes long but it's 0 long\n" +
                        "  → --no-merge 로 실행하세요. 병합 없이 DataMgr.dll / LocalData.dll 을 그대로 배치합니다.\n" +
                        "    (Roslyn 이 만든 원본은 정상입니다. ILRepack 만 거치면 깨집니다)");
            }
            catch (IndexOutOfRangeException)
            {
                //PE 파싱 실패는 여기서 판단하지 않는다
            }
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
        /// DataMgr.dll 과 LocalData.dll 은 서로 참조하지 않는 독립 어셈블리다.
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


        /// <summary>
        /// 병합된 Design.dll 또는 개별 dll 중 먼저 찾히는 것을 연다.
        /// 병합 모드/비병합 모드 어느 쪽이든 동작하게 하기 위한 것.
        /// </summary>
        public static Assembly LoadGeneratedAssembly(string dllFolder, params string[] candidates)
        {
            foreach (var name in candidates)
            {
                string path = Path.Combine(dllFolder, name);
                if (File.Exists(path))
                    return LoadAssemblyFromBytes(path, dllFolder);
            }
            throw new FileNotFoundException(
                $"{dllFolder} 에서 {string.Join(" / ", candidates)} 중 어느 것도 찾지 못했습니다.");
        }

        /// <summary>
        /// 파일을 읽어서 바이트로 로드한다. Assembly.LoadFrom 을 쓰지 않는다.
        ///
        ///  LoadFrom 은 이 상황에서 세 가지가 걸린다.
        ///   1. 파일을 프로세스가 끝날 때까지 잠근다.
        ///      대상이 유니티 Assets 폴더 안이라, 다음 실행에서 덮어쓸 때 충돌한다.
        ///   2. 매니페스트 모듈 이름이 파일명과 다르면(ILRepack 산출물이 'Design.dll' 이 아니라
        ///      'Design' 으로 기록되는 경우가 있다) 로더가 모듈 파일을 못 찾아
        ///      FileLoadException 0x80070057 (E_INVALIDARG) 를 던진다.
        ///   3. 다른 PC 에서 받은 파일이면 Zone.Identifier(MOTW) 때문에 막힌다.
        ///
        ///  바이트 로드는 이 셋을 전부 우회한다. 참조(protobuf-net 등)는 실행 폴더에서
        ///  평소대로 해석되고, 못 찾으면 아래 Resolve 훅이 dll 폴더도 뒤진다.
        /// </summary>
        private static Assembly LoadAssemblyFromBytes(string path, string probeDir)
        {
            HookAssemblyResolve(probeDir);
            HookAssemblyResolve(AppDomain.CurrentDomain.BaseDirectory);

            byte[] raw = File.ReadAllBytes(path);
            return Assembly.Load(raw);
        }

        private static readonly List<string> probeDirs = new List<string>();
        private static bool resolverHooked;

        private static void HookAssemblyResolve(string dir)
        {
            if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return;

            if (!probeDirs.Contains(dir, StringComparer.OrdinalIgnoreCase))
                probeDirs.Add(dir);

            if (resolverHooked) return;
            resolverHooked = true;

            AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
            {
                string simple = new AssemblyName(e.Name).Name;
                foreach (var d in probeDirs)
                {
                    string candidate = Path.Combine(d, simple + ".dll");
                    if (File.Exists(candidate))
                        return Assembly.Load(File.ReadAllBytes(candidate));
                }
                return null;
            };
        }

        /// <summary>
        /// protobuf-net.dll 을 찾는다. 생성된 dll 이 이 파일을 참조하므로
        /// 실제 파일로 존재해야 하고, exe 안에 embed 되면 안 된다.
        /// 단일 exe 로 묶을 때는 FodyWeavers.xml 의 ExcludeAssemblies 에 넣어
        /// exe 옆에 남겨둔다.
        /// </summary>
        public static string FindProtobufNet()
        {
            var tried = new List<string>();

            foreach (var dir in new[]
            {
                AppDomain.CurrentDomain.BaseDirectory,
                Environment.CurrentDirectory,
            })
            {
                if (string.IsNullOrEmpty(dir)) continue;
                string p = Path.Combine(dir, "protobuf-net.dll");
                if (tried.Contains(p)) continue;
                tried.Add(p);
                if (File.Exists(p)) return p;
            }

            throw new FileNotFoundException(
                "protobuf-net.dll 을 찾을 수 없습니다.\n\n" +
                "찾아본 위치:\n  " + string.Join("\n  ", tried) + "\n\n" +
                "이 파일은 exe 옆에 '실제 파일' 로 있어야 합니다.\n" +
                "생성되는 DataMgr.dll / LocalData.dll 이 이 파일을 참조하고,\n" +
                "유니티에도 '같은 파일' 을 넣어야 버전이 맞습니다.\n\n" +
                "단일 exe 로 묶으셨다면 FodyWeavers.xml 을 확인하세요:\n" +
                "  <Costura ExcludeAssemblies=\"protobuf-net\" />\n" +
                "이게 없으면 protobuf-net 이 exe 안에 embed 돼서 디스크에 파일이 없습니다.");
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

        /// <summary>
        /// 생성 코드를 컴파일할 때 걸어줄 참조 목록.
        ///
        /// Costura 로 exe 안에 embed 된 어셈블리는 Assembly.Load(byte[]) 로 로드되어
        /// Assembly.Location 이 빈 문자열이다. 그대로 MetadataReference.CreateFromFile
        /// 에 넘기면 ArgumentException 으로 죽는다.
        /// mscorlib / System.Core / System.Xml 은 GAC 라 embed 되지 않아 안전하고,
        /// System.Memory 는 생성 코드가 쓰지 않으므로 없으면 건너뛴다.
        /// </summary>
        private static List<MetadataReference> BuildReferences(string protobufNetPath)
        {
            var refs = new List<MetadataReference>();

            AddRef(refs, typeof(object), required: true);                 //mscorlib
            AddRef(refs, typeof(System.Linq.Enumerable), required: true); //System.Core
            AddRef(refs, typeof(XmlWriter), required: true);              //System.Xml

            //생성 코드는 Span / Memory 를 쓰지 않는다. 있으면 넣고 없으면 넘어간다
            AddRef(refs, typeof(System.MemoryExtensions), required: false);

            refs.Add(MetadataReference.CreateFromFile(protobufNetPath));
            return refs;
        }

        private static void AddRef(List<MetadataReference> refs, Type t, bool required)
        {
            string name = t.Assembly.GetName().Name;
            string loc = t.Assembly.Location;

            if (!string.IsNullOrEmpty(loc) && File.Exists(loc))
            {
                refs.Add(MetadataReference.CreateFromFile(loc));
                return;
            }

            //embed 된 어셈블리. exe 옆에 실제 파일이 있으면 그걸 쓴다
            string probe = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name + ".dll");
            if (File.Exists(probe))
            {
                refs.Add(MetadataReference.CreateFromFile(probe));
                return;
            }

            if (!required)
            {
                Console.WriteLine($"  [정보] {name} 을(를) 참조에서 제외합니다 (exe 안에 embed 되어 파일 경로가 없음).");
                return;
            }

            throw new InvalidOperationException(
                name + " 의 파일 경로를 알 수 없어 코드 생성 참조로 걸 수 없습니다.\n" +
                "  단일 exe(Costura)로 묶으면 embed 된 어셈블리는 Assembly.Location 이 빕니다.\n" +
                "  이 어셈블리는 디스크에 있어야 하므로 FodyWeavers.xml 에 추가하세요:\n" +
                "    <Costura ExcludeAssemblies=\"protobuf-net|" + name + "\" />");
        }

        private static void Compile(IEnumerable<SyntaxTree> trees, string outputPath)
        {
            string protobufNetPath = FindProtobufNet();

            var compileOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary);

            // 어셈블리 이름은 반드시 출력 파일명과 같아야 한다.
            //
            //   예전에는 "DynamicAssembly" 로 고정돼 있었다. 그래서 DataMgr.dll 과
            //   LocalData.dll 이 '둘 다' DynamicAssembly 라는 이름을 갖는다.
            //   ILRepack 이 하나로 합쳐줄 때는 결과가 Design 하나뿐이라 드러나지 않았지만,
            //   병합을 안 하고 둘 다 유니티에 넣으면 같은 이름의 어셈블리가 두 개가 되어
            //   유니티가 임포트를 포기한다 → CS0246 'DesignTable' 을 찾을 수 없음.
            string assemblyName = Path.GetFileNameWithoutExtension(outputPath);

            var compile = CSharpCompilation.Create(assemblyName)
                .WithOptions(compileOptions)
                .AddReferences(BuildReferences(protobufNetPath))
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
