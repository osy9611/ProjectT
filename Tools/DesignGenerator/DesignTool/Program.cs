using System;
using System.Windows.Forms;

namespace DesignTool
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            //폼에서 처리하지 못한 예외로 도구가 조용히 죽는 것을 막는다.
            Application.ThreadException += (s, e) => ShowError(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (s, e) => ShowError(e.ExceptionObject as Exception);

            Application.Run(new MainForm());
        }

        private static void ShowError(Exception ex)
        {
            MessageBox.Show(
                (ex == null ? "알 수 없는 오류" : ex.ToString()) + Environment.NewLine + Environment.NewLine +
                "이 도구가 죽어도 DesignGenerator.exe 는 콘솔에서 그대로 쓸 수 있다.",
                "DesignTool 오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }
}
