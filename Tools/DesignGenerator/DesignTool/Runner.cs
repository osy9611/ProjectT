using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace DesignTool
{
    //DesignGenerator.exe 를 자식 프로세스로 돌리고 출력을 줄 단위로 흘려준다.
    //이 도구에는 테이블 생성 로직이 한 줄도 없다. 전부 저쪽 exe 가 한다.
    public sealed class Runner
    {
        private Process process;

        public bool IsRunning
        {
            get { return process != null && !process.HasExited; }
        }

        //onLine / onDone 은 UI 스레드가 아닌 곳에서 불린다. 호출부에서 Invoke 할 것.
        public void Start(string exePath, string arguments, string workingDir,
                          Action<string> onLine, Action<int> onDone)
        {
            if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
                throw new FileNotFoundException("DesignGenerator.exe 를 찾을 수 없다.", exePath ?? "");

            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                WorkingDirectory = string.IsNullOrEmpty(workingDir)
                                   ? Path.GetDirectoryName(exePath)
                                   : workingDir,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,

                //자식은 시스템 기본 코드페이지로 쓴다. 맞춰주지 않으면 한글이 깨진다.
                StandardOutputEncoding = Encoding.Default,
                StandardErrorEncoding = Encoding.Default,
            };

            process = new Process { StartInfo = psi, EnableRaisingEvents = true };

            process.OutputDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };
            process.ErrorDataReceived += (s, e) => { if (e.Data != null) onLine(e.Data); };

            process.Exited += (s, e) =>
            {
                //Exited 는 출력 스트림이 다 비워지기 전에 올 수 있다.
                //WaitForExit() 를 한 번 더 불러야 남은 줄까지 받는다.
                int code;
                try
                {
                    process.WaitForExit();
                    code = process.ExitCode;
                }
                catch
                {
                    code = -1;
                }
                onDone(code);
            };

            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
        }

        public void Kill()
        {
            try
            {
                if (IsRunning)
                {
                    process.Kill();
                    process.WaitForExit(3000);
                }
            }
            catch
            {
                //이미 끝났으면 무시
            }
        }

        //인자에 공백이 있으면 따옴표로 감싼다.
        public static string Quote(string s)
        {
            if (string.IsNullOrEmpty(s)) return "\"\"";
            return s.IndexOf(' ') >= 0 ? "\"" + s + "\"" : s;
        }
    }
}
