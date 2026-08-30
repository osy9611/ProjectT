using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace DesignTool
{
    //DesignGenerator.exe 를 호출하는 껍데기.
    //테이블 생성 로직은 여기에 한 줄도 두지 않는다. 이 폼이 깨져도 생성기는 콘솔로 그대로 돌아간다.
    public partial class MainForm : Form
    {
        private Settings settings = new Settings();
        private Runner runner = new Runner();
        private readonly Queue<Step> queue = new Queue<Step>();

        private sealed class Step
        {
            public string Name;
            public string Arguments;
        }

        //디자이너는 생성자를 실제로 실행한다. 여기에는 InitializeComponent 말고 아무것도 두지 않는다.
        //파일을 읽거나 프로세스를 만지는 코드가 들어가면 디자이너가 열리지 않는다.
        public MainForm()
        {
            InitializeComponent();
        }

        private void MainForm_Load(object sender, EventArgs e)
        {
            settings = Settings.Load();

            if (string.IsNullOrEmpty(settings.GeneratorPath))
                settings.GeneratorPath = Settings.FindGenerator();

            txtGenerator.Text = settings.GeneratorPath;
            txtData.Text = settings.DataPath;
            txtOutput.Text = settings.OutputPath;
            chkFull.Checked = settings.Full;
            chkWarnAsError.Checked = settings.WarnAsError;
            chkMerge.Checked = settings.Merge;

            if (string.IsNullOrEmpty(txtGenerator.Text))
                Log("DesignGenerator.exe 를 자동으로 찾지 못했다. 경로를 직접 지정할 것.");

            RefreshTableList();
        }

        //데이터 폴더에서 테이블 이름을 긁어 콤보에 채운다.
        //Table\Tables\*.xlsx 가 1순위, 없으면 Table\Xml\*.xml 로 대신한다.
        private void RefreshTableList()
        {
            string keep = cboTable.Text;
            cboTable.Items.Clear();

            string data = txtData.Text.Trim();
            if (!Directory.Exists(data)) return;

            var names = new List<string>();
            CollectNames(Path.Combine(data, "Table", "Tables"), "*.xlsx", names);
            if (names.Count == 0)
                CollectNames(Path.Combine(data, "Table", "Xml"), "*.xml", names);

            names.Sort(StringComparer.OrdinalIgnoreCase);
            foreach (var n in names) cboTable.Items.Add(n);

            //갱신 전에 고른 테이블이 아직 있으면 그대로 둔다.
            if (!string.IsNullOrEmpty(keep) && cboTable.Items.Contains(keep))
                cboTable.SelectedItem = keep;
            else if (cboTable.Items.Count > 0)
                cboTable.SelectedIndex = 0;
        }

        private static void CollectNames(string dir, string pattern, List<string> into)
        {
            if (!Directory.Exists(dir)) return;

            foreach (var f in Directory.GetFiles(dir, pattern))
            {
                string name = Path.GetFileName(f);
                if (name.StartsWith("~$")) continue;    //엑셀 임시 파일
                into.Add(Path.GetFileNameWithoutExtension(name));
            }
        }

        //콤보에서 테이블 이름을 꺼낸다. 직접 입력한 값도 받는다.
        private string SelectedTable()
        {
            string name = cboTable.Text.Trim();
            if (name.Length == 0)
            {
                MessageBox.Show(this, "테이블을 선택할 것. 목록이 비어 있으면 [목록 갱신] 을 누를 것.",
                                "DesignTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            return name;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (runner.IsRunning)
            {
                var r = MessageBox.Show(this, "실행 중이다. 중지하고 닫을까?", "DesignTool",
                                        MessageBoxButtons.YesNo, MessageBoxIcon.Question);
                if (r != DialogResult.Yes) { e.Cancel = true; return; }
                runner.Kill();
            }
            SaveSettings();
        }

        private void SaveSettings()
        {
            settings.GeneratorPath = txtGenerator.Text.Trim();
            settings.DataPath = txtData.Text.Trim();
            settings.OutputPath = txtOutput.Text.Trim();
            settings.Full = chkFull.Checked;
            settings.WarnAsError = chkWarnAsError.Checked;
            settings.Merge = chkMerge.Checked;
            settings.Save();
        }

        //공통 옵션. 생성기의 플래그 이름과 1:1로 맞춘다.
        private string CommonFlags()
        {
            string s = "";
            if (chkFull.Checked) s += " --full";
            if (chkWarnAsError.Checked) s += " --warn-as-error";
            if (chkMerge.Checked) s += " --merge";
            return s;
        }

        private bool CheckPaths(bool needOutput)
        {
            if (!File.Exists(txtGenerator.Text.Trim()))
            {
                MessageBox.Show(this, "DesignGenerator.exe 경로가 올바르지 않다.", "DesignTool",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (!Directory.Exists(txtData.Text.Trim()))
            {
                MessageBox.Show(this, "데이터 폴더가 없다.", "DesignTool",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            if (needOutput && string.IsNullOrEmpty(txtOutput.Text.Trim()))
            {
                MessageBox.Show(this, "출력 폴더를 지정할 것.", "DesignTool",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            return true;
        }

        private void Enqueue(string name, string args)
        {
            queue.Enqueue(new Step { Name = name, Arguments = args });
        }

        private void RunQueue()
        {
            if (queue.Count == 0)
            {
                SetBusy(false);
                SetStatus("완료");
                return;
            }

            Step step = queue.Dequeue();
            SetBusy(true);
            SetStatus(step.Name + " 실행 중...");
            Log("");
            Log("──────── " + step.Name + " ────────");
            Log("> " + Path.GetFileName(txtGenerator.Text) + " " + step.Arguments);

            try
            {
                runner.Start(
                    txtGenerator.Text.Trim(),
                    step.Arguments,
                    Path.GetDirectoryName(txtGenerator.Text.Trim()),
                    line => BeginInvoke((Action)(() => Log(line))),
                    code => BeginInvoke((Action)(() => OnStepDone(step, code))));
            }
            catch (Exception ex)
            {
                Log("[오류] " + ex.Message);
                queue.Clear();
                SetBusy(false);
                SetStatus("실패");
            }
        }

        private void OnStepDone(Step step, int code)
        {
            Log(string.Format("[{0}] 종료코드 {1}", step.Name, code));

            if (code != 0)
            {
                //실패하면 뒤 단계를 돌리지 않는다. 깨진 입력으로 다음 단계를 태울 이유가 없다.
                if (queue.Count > 0)
                {
                    Log("실패해서 남은 단계를 취소한다.");
                    queue.Clear();
                }
                SetBusy(false);
                SetStatus(step.Name + " 실패 (종료코드 " + code + ")");
                return;
            }

            RunQueue();
        }

        private void SetBusy(bool busy)
        {
            btnEnum.Enabled = !busy;
            btnTableCreate.Enabled = !busy;
            btnTableGenerate.Enabled = !busy;
            btnLocal.Enabled = !busy;
            btnAll.Enabled = !busy;
            btnCreateOne.Enabled = !busy;
            btnGenerateOne.Enabled = !busy;
            btnRefreshTables.Enabled = !busy;
            grpPath.Enabled = !busy;
            grpOption.Enabled = !busy;
            btnStop.Enabled = busy;
            Cursor = busy ? Cursors.AppStarting : Cursors.Default;
        }

        private void SetStatus(string text)
        {
            lblStatus.Text = text;
        }

        private void Log(string line)
        {
            txtLog.AppendText(line + Environment.NewLine);
        }

        //각 명령의 인자는 DesignGenerator 의 사용법과 같다.
        //  Enum          <dataPath> <outputPath>
        //  TableCreate   <dataPath> <tableName|null>
        //  TableGenerate <dataPath> <outputPath> <All|One> [tableName]
        //  Local         <dataPath> <outputPath>

        private string ArgsEnum()
        {
            return "Enum " + Runner.Quote(txtData.Text.Trim()) + " " +
                   Runner.Quote(txtOutput.Text.Trim()) + CommonFlags();
        }

        private string ArgsTableCreate()
        {
            return "TableCreate " + Runner.Quote(txtData.Text.Trim()) + " null" + CommonFlags();
        }

        private string ArgsTableGenerate()
        {
            return "TableGenerate " + Runner.Quote(txtData.Text.Trim()) + " " +
                   Runner.Quote(txtOutput.Text.Trim()) + " All" + CommonFlags();
        }

        private string ArgsLocal()
        {
            return "Local " + Runner.Quote(txtData.Text.Trim()) + " " +
                   Runner.Quote(txtOutput.Text.Trim()) + CommonFlags();
        }

        private string ArgsTableCreateOne(string tableName)
        {
            return "TableCreate " + Runner.Quote(txtData.Text.Trim()) + " " +
                   Runner.Quote(tableName) + CommonFlags();
        }

        private string ArgsTableGenerateOne(string tableName)
        {
            return "TableGenerate " + Runner.Quote(txtData.Text.Trim()) + " " +
                   Runner.Quote(txtOutput.Text.Trim()) + " One " +
                   Runner.Quote(tableName) + CommonFlags();
        }

        private void btnEnum_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(true)) return;
            SaveSettings();
            Enqueue("Enum 생성", ArgsEnum());
            RunQueue();
        }

        private void btnTableCreate_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(false)) return;
            SaveSettings();
            Enqueue("명세서 → 엑셀", ArgsTableCreate());
            RunQueue();
        }

        private void btnTableGenerate_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(true)) return;
            SaveSettings();
            Enqueue("테이블 생성", ArgsTableGenerate());
            RunQueue();
        }

        private void btnLocal_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(true)) return;
            SaveSettings();
            Enqueue("로컬라이제이션", ArgsLocal());
            RunQueue();
        }

        private void btnAll_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(true)) return;
            SaveSettings();

            //Enum 이 먼저 나와야 테이블이 enum 값을 해석할 수 있다. 순서를 지킨다.
            Enqueue("Enum 생성", ArgsEnum());
            Enqueue("명세서 → 엑셀", ArgsTableCreate());
            Enqueue("테이블 생성", ArgsTableGenerate());
            Enqueue("로컬라이제이션", ArgsLocal());
            RunQueue();
        }

        private void btnRefreshTables_Click(object sender, EventArgs e)
        {
            RefreshTableList();
            Log(string.Format("테이블 목록 {0}개.", cboTable.Items.Count));
        }

        private void btnCreateOne_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(false)) return;
            string name = SelectedTable();
            if (name == null) return;

            SaveSettings();
            Enqueue("명세서 → 엑셀 (" + name + ")", ArgsTableCreateOne(name));
            RunQueue();
        }

        private void btnGenerateOne_Click(object sender, EventArgs e)
        {
            if (!CheckPaths(true)) return;
            string name = SelectedTable();
            if (name == null) return;

            SaveSettings();
            Enqueue("테이블 생성 (" + name + ")", ArgsTableGenerateOne(name));
            RunQueue();
        }

        private void btnStop_Click(object sender, EventArgs e)
        {
            queue.Clear();
            runner.Kill();
            Log("사용자가 중지했다.");
            SetBusy(false);
            SetStatus("중지됨");
        }

        private void btnClearLog_Click(object sender, EventArgs e)
        {
            txtLog.Clear();
        }

        private void btnOpenOutput_Click(object sender, EventArgs e)
        {
            string dir = txtOutput.Text.Trim();
            if (!Directory.Exists(dir))
            {
                MessageBox.Show(this, "출력 폴더가 없다.", "DesignTool",
                                MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Process.Start("explorer.exe", Runner.Quote(dir));
        }

        private void btnGeneratorBrowse_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Title = "DesignGenerator.exe 선택";
                dlg.Filter = "실행 파일 (*.exe)|*.exe";
                if (File.Exists(txtGenerator.Text))
                    dlg.InitialDirectory = Path.GetDirectoryName(txtGenerator.Text);

                if (dlg.ShowDialog(this) == DialogResult.OK)
                    txtGenerator.Text = dlg.FileName;
            }
        }

        private void btnDataBrowse_Click(object sender, EventArgs e)
        {
            string picked = PickFolder("데이터 폴더 선택", txtData.Text);
            if (picked == null) return;

            txtData.Text = picked;
            RefreshTableList();
        }

        private void btnOutputBrowse_Click(object sender, EventArgs e)
        {
            string picked = PickFolder("출력 폴더 선택 (Unity Assets)", txtOutput.Text);
            if (picked != null) txtOutput.Text = picked;
        }

        private string PickFolder(string description, string current)
        {
            using (var dlg = new FolderBrowserDialog())
            {
                dlg.Description = description;
                if (Directory.Exists(current)) dlg.SelectedPath = current;

                return dlg.ShowDialog(this) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }
    }
}
