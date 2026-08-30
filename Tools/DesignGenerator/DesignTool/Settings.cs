using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace DesignTool
{
    //exe 옆 settings.ini 에 key=value 로 저장한다.
    //레지스트리를 쓰지 않는 이유 : 백업도 안 되고, 형상관리에도 안 올라가고,
    //다른 PC 로 옮길 수도 없다. 텍스트 파일이면 손으로 고칠 수도 있다.
    public sealed class Settings
    {
        public string GeneratorPath = "";
        public string DataPath = "";
        public string OutputPath = "";
        public bool Full;
        public bool WarnAsError;
        public bool Merge;

        private static string FilePath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings.ini"); }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (!File.Exists(FilePath))
                    return s;

                foreach (var raw in File.ReadAllLines(FilePath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;

                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();

                    switch (key)
                    {
                        case "GeneratorPath": s.GeneratorPath = val; break;
                        case "DataPath": s.DataPath = val; break;
                        case "OutputPath": s.OutputPath = val; break;
                        case "Full": s.Full = ToBool(val); break;
                        case "WarnAsError": s.WarnAsError = ToBool(val); break;
                        case "Merge": s.Merge = ToBool(val); break;
                    }
                }
            }
            catch
            {
                //설정을 못 읽는다고 도구가 못 뜰 이유는 없다. 기본값으로 진행한다.
            }
            return s;
        }

        public void Save()
        {
            try
            {
                var sb = new StringBuilder();
                sb.AppendLine("# DesignTool 설정. 직접 고쳐도 된다.");
                sb.AppendLine("GeneratorPath=" + GeneratorPath);
                sb.AppendLine("DataPath=" + DataPath);
                sb.AppendLine("OutputPath=" + OutputPath);
                sb.AppendLine("Full=" + Full);
                sb.AppendLine("WarnAsError=" + WarnAsError);
                sb.AppendLine("Merge=" + Merge);

                File.WriteAllText(FilePath, sb.ToString(), new UTF8Encoding(true));
            }
            catch
            {
                //저장 실패는 무시한다. 다음 실행에서 경로를 다시 넣으면 된다.
            }
        }

        private static bool ToBool(string v)
        {
            return string.Equals(v, "true", StringComparison.OrdinalIgnoreCase) || v == "1";
        }

        //DesignGenerator.exe 를 흔한 위치에서 찾는다. 못 찾으면 빈 문자열.
        public static string FindGenerator()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;

            var candidates = new List<string>
            {
                Path.Combine(baseDir, "DesignGenerator.exe"),
                Path.Combine(baseDir, "..", "DesignGenerator.exe"),
                Path.Combine(baseDir, "..", "..", "..", "DesignGenerator", "bin", "Release", "DesignGenerator.exe"),
                Path.Combine(baseDir, "..", "..", "..", "DesignGenerator", "bin", "Debug", "DesignGenerator.exe"),
            };

            foreach (var c in candidates)
            {
                try
                {
                    string full = Path.GetFullPath(c);
                    if (File.Exists(full)) return full;
                }
                catch
                {
                    //경로 조합이 실패하면 다음 후보로
                }
            }
            return "";
        }
    }
}
