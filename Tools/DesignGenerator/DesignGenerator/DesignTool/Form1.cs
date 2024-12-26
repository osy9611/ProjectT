using Microsoft.WindowsAPICodePack.Dialogs;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;
using Newtonsoft;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DesignTool
{
    public enum SettingInfo
    {
        FolderPath,
        GeneratePath
    }

    public partial class Form1 : Form
    {
        string[] settingData;

        public Form1()
        {
            InitializeComponent();
            LoadSettingInfo();
        }

        private void ExcuteEXE(params object[] args)
        {
            string expPath = $"{Directory.GetCurrentDirectory()}\\Gen\\DesignGenerator.exe";
            string sendArgs = string.Empty;

            for (int i = 0; i < args.Length; ++i)
            {
                sendArgs += args[i].ToString() + " ";
            }

            Process p = new Process();
            p.StartInfo.RedirectStandardError = true;
            p.StartInfo.RedirectStandardOutput = true;
            p.StartInfo.UseShellExecute = false;
            p.StartInfo.FileName = expPath;
            p.StartInfo.Arguments = sendArgs;
            p.Start();
            p.WaitForExit();

            string stdout = p.StandardOutput.ReadToEnd();
            string stderr = p.StandardError.ReadToEnd();

            if (string.IsNullOrEmpty(stderr))
                MessageBox.Show($"Complete!");
            else
                MessageBox.Show($"{stdout} \n {stderr}");
        }

        #region ButtonEvents
        private void EnumGenButton_Click(object sender, EventArgs e)
        {
            ExcuteEXE("Enum", settingData[(int)SettingInfo.FolderPath], settingData[(int)SettingInfo.GeneratePath]);
        }


        private void CreateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(CreateTableText.Text))
            {
                MessageBox.Show($"테이블 이름을 적어주세요");
                return;
            }


            ExcuteEXE("TableCreate", settingData[(int)SettingInfo.FolderPath], CreateTableText.Text);
        }

        private void CreateAllButton_Click(object sender, EventArgs e)
        {
            ExcuteEXE("TableCreate", settingData[(int)SettingInfo.FolderPath], settingData[(int)SettingInfo.GeneratePath], "null");
        }



        private void GenerateButton_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(GenerateTableText.Text))
            {
                MessageBox.Show($"테이블 이름을 적어주세요");
                return;
            }

            ExcuteEXE("TableGenerate", settingData[(int)SettingInfo.FolderPath], settingData[(int)SettingInfo.GeneratePath], "One", GenerateTableText.Text);
        }

        private void GenerateAllButton_Click(object sender, EventArgs e)
        {
            ExcuteEXE("TableGenerate", settingData[(int)SettingInfo.FolderPath], settingData[(int)SettingInfo.GeneratePath], "All");
        }

        private void LocalGenButton_Click(object sender, EventArgs e)
        {
            ExcuteEXE("Local", settingData[(int)SettingInfo.FolderPath], settingData[(int)SettingInfo.GeneratePath]);
        }
        #endregion


        private void FolderPathButton_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                FolderPathText.Text = dialog.FileName;
                SaveSettingInfo(SettingInfo.FolderPath, dialog.FileName);
                settingData[(int)SettingInfo.FolderPath] = dialog.FileName;
            }
        }

        private void GenerateOutputPathButton_Click(object sender, EventArgs e)
        {
            CommonOpenFileDialog dialog = new CommonOpenFileDialog();
            dialog.IsFolderPicker = true;

            if (dialog.ShowDialog() == CommonFileDialogResult.Ok)
            {
                if (Path.GetFileName(dialog.FileName) == "Assets")
                {
                    if (!Directory.Exists($"{dialog.FileName}\\Automation"))
                    {
                        string baseFolder = $"{dialog.FileName}\\Automation";
                        string enumFolderPath = Path.Combine(baseFolder, "Enum");
                        Directory.CreateDirectory(enumFolderPath);

                        string dllFolderPath = Path.Combine(baseFolder, "Dll");
                        Directory.CreateDirectory(dllFolderPath);

                        string tableFolderPath = Path.Combine(baseFolder, "Table");
                        Directory.CreateDirectory(tableFolderPath);

                        string localFolderPath = Path.Combine(baseFolder, "Local");
                        Directory.CreateDirectory(localFolderPath);
                    }
                    GenerateOutputPathText.Text = dialog.FileName;
                    SaveSettingInfo(SettingInfo.GeneratePath, dialog.FileName);
                    settingData[(int)SettingInfo.GeneratePath] = dialog.FileName;
                }
                else
                {
                    MessageBox.Show($"에셋 폴더 경로를 선택해야합니다.");
                }
            }
        }

        private void LoadSettingInfo()
        {
            string settingPath = System.IO.Directory.GetCurrentDirectory() + "\\setting.json";
            settingData = new string[System.Enum.GetValues(typeof(SettingInfo)).Length];

            FileInfo fi = new FileInfo(settingPath);
            if (fi.Exists)
            {
                using (StreamReader file = File.OpenText(settingPath))
                using (JsonTextReader reader = new JsonTextReader(file))
                {
                    JObject json = (JObject)JToken.ReadFrom(reader);
                    if (json.ContainsKey(SettingInfo.FolderPath.ToString()))
                    {
                        FolderPathText.Text = json[SettingInfo.FolderPath.ToString()].ToString();
                        settingData[(int)SettingInfo.FolderPath] = json[SettingInfo.FolderPath.ToString()].ToString();
                    }
                    if (json.ContainsKey(SettingInfo.GeneratePath.ToString()))
                    {
                        GenerateOutputPathText.Text = json[SettingInfo.GeneratePath.ToString()].ToString();
                        settingData[(int)SettingInfo.GeneratePath] = json[SettingInfo.GeneratePath.ToString()].ToString();
                    }

                }
            }
        }

        private void SaveSettingInfo(SettingInfo info, string path)
        {
            string settingPath = System.IO.Directory.GetCurrentDirectory() + "\\setting.json";

            JObject json;
            if (File.Exists(settingPath))
            {
                using (StreamReader file = File.OpenText(settingPath))
                using (JsonTextReader reader = new JsonTextReader(file))
                {
                    json = (JObject)JToken.ReadFrom(reader);
                }
            }
            else
            {
                json = new JObject();
            }

            if (!json.ContainsKey(info.ToString()))
            {
                json.Add(info.ToString(), path);
            }
            else
            {
                json[info.ToString()] = path;
            }

            File.WriteAllText(settingPath, json.ToString());
        }
    }
}
