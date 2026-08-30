namespace DesignTool
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            this.grpPath = new System.Windows.Forms.GroupBox();
            this.btnOutputBrowse = new System.Windows.Forms.Button();
            this.txtOutput = new System.Windows.Forms.TextBox();
            this.lblOutput = new System.Windows.Forms.Label();
            this.btnDataBrowse = new System.Windows.Forms.Button();
            this.txtData = new System.Windows.Forms.TextBox();
            this.lblData = new System.Windows.Forms.Label();
            this.btnGeneratorBrowse = new System.Windows.Forms.Button();
            this.txtGenerator = new System.Windows.Forms.TextBox();
            this.lblGenerator = new System.Windows.Forms.Label();
            this.grpOption = new System.Windows.Forms.GroupBox();
            this.chkMerge = new System.Windows.Forms.CheckBox();
            this.chkWarnAsError = new System.Windows.Forms.CheckBox();
            this.chkFull = new System.Windows.Forms.CheckBox();
            this.grpRun = new System.Windows.Forms.GroupBox();
            this.btnStop = new System.Windows.Forms.Button();
            this.btnAll = new System.Windows.Forms.Button();
            this.btnLocal = new System.Windows.Forms.Button();
            this.btnTableGenerate = new System.Windows.Forms.Button();
            this.btnTableCreate = new System.Windows.Forms.Button();
            this.btnEnum = new System.Windows.Forms.Button();
            this.txtLog = new System.Windows.Forms.TextBox();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnClearLog = new System.Windows.Forms.Button();
            this.btnOpenOutput = new System.Windows.Forms.Button();
            this.lblTable = new System.Windows.Forms.Label();
            this.cboTable = new System.Windows.Forms.ComboBox();
            this.btnRefreshTables = new System.Windows.Forms.Button();
            this.btnCreateOne = new System.Windows.Forms.Button();
            this.btnGenerateOne = new System.Windows.Forms.Button();
            this.grpPath.SuspendLayout();
            this.grpOption.SuspendLayout();
            this.grpRun.SuspendLayout();
            this.SuspendLayout();
            //
            // grpPath
            //
            this.grpPath.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpPath.Controls.Add(this.btnOutputBrowse);
            this.grpPath.Controls.Add(this.txtOutput);
            this.grpPath.Controls.Add(this.lblOutput);
            this.grpPath.Controls.Add(this.btnDataBrowse);
            this.grpPath.Controls.Add(this.txtData);
            this.grpPath.Controls.Add(this.lblData);
            this.grpPath.Controls.Add(this.btnGeneratorBrowse);
            this.grpPath.Controls.Add(this.txtGenerator);
            this.grpPath.Controls.Add(this.lblGenerator);
            this.grpPath.Location = new System.Drawing.Point(12, 12);
            this.grpPath.Name = "grpPath";
            this.grpPath.Size = new System.Drawing.Size(760, 116);
            this.grpPath.TabIndex = 0;
            this.grpPath.TabStop = false;
            this.grpPath.Text = "경로";
            //
            // btnOutputBrowse
            //
            this.btnOutputBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOutputBrowse.Location = new System.Drawing.Point(668, 80);
            this.btnOutputBrowse.Name = "btnOutputBrowse";
            this.btnOutputBrowse.Size = new System.Drawing.Size(80, 23);
            this.btnOutputBrowse.TabIndex = 8;
            this.btnOutputBrowse.Text = "찾아보기";
            this.btnOutputBrowse.UseVisualStyleBackColor = true;
            this.btnOutputBrowse.Click += new System.EventHandler(this.btnOutputBrowse_Click);
            //
            // txtOutput
            //
            this.txtOutput.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtOutput.Location = new System.Drawing.Point(120, 82);
            this.txtOutput.Name = "txtOutput";
            this.txtOutput.Size = new System.Drawing.Size(542, 21);
            this.txtOutput.TabIndex = 7;
            //
            // lblOutput
            //
            this.lblOutput.AutoSize = true;
            this.lblOutput.Location = new System.Drawing.Point(14, 85);
            this.lblOutput.Name = "lblOutput";
            this.lblOutput.Size = new System.Drawing.Size(101, 12);
            this.lblOutput.TabIndex = 6;
            this.lblOutput.Text = "출력 (Unity Assets)";
            //
            // btnDataBrowse
            //
            this.btnDataBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnDataBrowse.Location = new System.Drawing.Point(668, 50);
            this.btnDataBrowse.Name = "btnDataBrowse";
            this.btnDataBrowse.Size = new System.Drawing.Size(80, 23);
            this.btnDataBrowse.TabIndex = 5;
            this.btnDataBrowse.Text = "찾아보기";
            this.btnDataBrowse.UseVisualStyleBackColor = true;
            this.btnDataBrowse.Click += new System.EventHandler(this.btnDataBrowse_Click);
            //
            // txtData
            //
            this.txtData.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtData.Location = new System.Drawing.Point(120, 52);
            this.txtData.Name = "txtData";
            this.txtData.Size = new System.Drawing.Size(542, 21);
            this.txtData.TabIndex = 4;
            //
            // lblData
            //
            this.lblData.AutoSize = true;
            this.lblData.Location = new System.Drawing.Point(14, 55);
            this.lblData.Name = "lblData";
            this.lblData.Size = new System.Drawing.Size(89, 12);
            this.lblData.TabIndex = 3;
            this.lblData.Text = "데이터 (Data)";
            //
            // btnGeneratorBrowse
            //
            this.btnGeneratorBrowse.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnGeneratorBrowse.Location = new System.Drawing.Point(668, 20);
            this.btnGeneratorBrowse.Name = "btnGeneratorBrowse";
            this.btnGeneratorBrowse.Size = new System.Drawing.Size(80, 23);
            this.btnGeneratorBrowse.TabIndex = 2;
            this.btnGeneratorBrowse.Text = "찾아보기";
            this.btnGeneratorBrowse.UseVisualStyleBackColor = true;
            this.btnGeneratorBrowse.Click += new System.EventHandler(this.btnGeneratorBrowse_Click);
            //
            // txtGenerator
            //
            this.txtGenerator.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtGenerator.Location = new System.Drawing.Point(120, 22);
            this.txtGenerator.Name = "txtGenerator";
            this.txtGenerator.Size = new System.Drawing.Size(542, 21);
            this.txtGenerator.TabIndex = 1;
            //
            // lblGenerator
            //
            this.lblGenerator.AutoSize = true;
            this.lblGenerator.Location = new System.Drawing.Point(14, 25);
            this.lblGenerator.Name = "lblGenerator";
            this.lblGenerator.Size = new System.Drawing.Size(105, 12);
            this.lblGenerator.TabIndex = 0;
            this.lblGenerator.Text = "DesignGenerator.exe";
            //
            // grpOption
            //
            this.grpOption.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpOption.Controls.Add(this.chkMerge);
            this.grpOption.Controls.Add(this.chkWarnAsError);
            this.grpOption.Controls.Add(this.chkFull);
            this.grpOption.Location = new System.Drawing.Point(12, 134);
            this.grpOption.Name = "grpOption";
            this.grpOption.Size = new System.Drawing.Size(760, 56);
            this.grpOption.TabIndex = 1;
            this.grpOption.TabStop = false;
            this.grpOption.Text = "옵션";
            //
            // chkMerge
            //
            this.chkMerge.AutoSize = true;
            this.chkMerge.Location = new System.Drawing.Point(430, 24);
            this.chkMerge.Name = "chkMerge";
            this.chkMerge.Size = new System.Drawing.Size(203, 16);
            this.chkMerge.TabIndex = 2;
            this.chkMerge.Text = "DLL 병합 (--merge) — 권장하지 않음";
            this.chkMerge.UseVisualStyleBackColor = true;
            //
            // chkWarnAsError
            //
            this.chkWarnAsError.AutoSize = true;
            this.chkWarnAsError.Location = new System.Drawing.Point(200, 24);
            this.chkWarnAsError.Name = "chkWarnAsError";
            this.chkWarnAsError.Size = new System.Drawing.Size(191, 16);
            this.chkWarnAsError.TabIndex = 1;
            this.chkWarnAsError.Text = "경고도 실패로 (--warn-as-error)";
            this.chkWarnAsError.UseVisualStyleBackColor = true;
            //
            // chkFull
            //
            this.chkFull.AutoSize = true;
            this.chkFull.Location = new System.Drawing.Point(16, 24);
            this.chkFull.Name = "chkFull";
            this.chkFull.Size = new System.Drawing.Size(147, 16);
            this.chkFull.TabIndex = 0;
            this.chkFull.Text = "전체 재생성 (--full)";
            this.chkFull.UseVisualStyleBackColor = true;
            //
            // grpRun
            //
            this.grpRun.Anchor = ((System.Windows.Forms.AnchorStyles)(((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.grpRun.Controls.Add(this.btnGenerateOne);
            this.grpRun.Controls.Add(this.btnCreateOne);
            this.grpRun.Controls.Add(this.btnRefreshTables);
            this.grpRun.Controls.Add(this.cboTable);
            this.grpRun.Controls.Add(this.lblTable);
            this.grpRun.Controls.Add(this.btnStop);
            this.grpRun.Controls.Add(this.btnAll);
            this.grpRun.Controls.Add(this.btnLocal);
            this.grpRun.Controls.Add(this.btnTableGenerate);
            this.grpRun.Controls.Add(this.btnTableCreate);
            this.grpRun.Controls.Add(this.btnEnum);
            this.grpRun.Location = new System.Drawing.Point(12, 196);
            this.grpRun.Name = "grpRun";
            this.grpRun.Size = new System.Drawing.Size(760, 106);
            this.grpRun.TabIndex = 2;
            this.grpRun.TabStop = false;
            this.grpRun.Text = "실행";
            //
            // btnStop
            //
            this.btnStop.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnStop.Enabled = false;
            this.btnStop.Location = new System.Drawing.Point(668, 24);
            this.btnStop.Name = "btnStop";
            this.btnStop.Size = new System.Drawing.Size(80, 30);
            this.btnStop.TabIndex = 5;
            this.btnStop.Text = "중지";
            this.btnStop.UseVisualStyleBackColor = true;
            this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
            //
            // btnAll
            //
            this.btnAll.Location = new System.Drawing.Point(536, 24);
            this.btnAll.Name = "btnAll";
            this.btnAll.Size = new System.Drawing.Size(110, 30);
            this.btnAll.TabIndex = 4;
            this.btnAll.Text = "전체 순차 실행";
            this.btnAll.UseVisualStyleBackColor = true;
            this.btnAll.Click += new System.EventHandler(this.btnAll_Click);
            //
            // btnLocal
            //
            this.btnLocal.Location = new System.Drawing.Point(404, 24);
            this.btnLocal.Name = "btnLocal";
            this.btnLocal.Size = new System.Drawing.Size(120, 30);
            this.btnLocal.TabIndex = 3;
            this.btnLocal.Text = "4. 로컬라이제이션";
            this.btnLocal.UseVisualStyleBackColor = true;
            this.btnLocal.Click += new System.EventHandler(this.btnLocal_Click);
            //
            // btnTableGenerate
            //
            this.btnTableGenerate.Location = new System.Drawing.Point(272, 24);
            this.btnTableGenerate.Name = "btnTableGenerate";
            this.btnTableGenerate.Size = new System.Drawing.Size(120, 30);
            this.btnTableGenerate.TabIndex = 2;
            this.btnTableGenerate.Text = "3. 테이블 생성";
            this.btnTableGenerate.UseVisualStyleBackColor = true;
            this.btnTableGenerate.Click += new System.EventHandler(this.btnTableGenerate_Click);
            //
            // btnTableCreate
            //
            this.btnTableCreate.Location = new System.Drawing.Point(140, 24);
            this.btnTableCreate.Name = "btnTableCreate";
            this.btnTableCreate.Size = new System.Drawing.Size(120, 30);
            this.btnTableCreate.TabIndex = 1;
            this.btnTableCreate.Text = "2. 명세서 → 엑셀";
            this.btnTableCreate.UseVisualStyleBackColor = true;
            this.btnTableCreate.Click += new System.EventHandler(this.btnTableCreate_Click);
            //
            // btnEnum
            //
            this.btnEnum.Location = new System.Drawing.Point(16, 24);
            this.btnEnum.Name = "btnEnum";
            this.btnEnum.Size = new System.Drawing.Size(114, 30);
            this.btnEnum.TabIndex = 0;
            this.btnEnum.Text = "1. Enum 생성";
            this.btnEnum.UseVisualStyleBackColor = true;
            this.btnEnum.Click += new System.EventHandler(this.btnEnum_Click);
            //
            // lblTable
            //
            this.lblTable.AutoSize = true;
            this.lblTable.Location = new System.Drawing.Point(16, 71);
            this.lblTable.Name = "lblTable";
            this.lblTable.Size = new System.Drawing.Size(41, 12);
            this.lblTable.TabIndex = 6;
            this.lblTable.Text = "테이블";
            //
            // cboTable
            //
            this.cboTable.FormattingEnabled = true;
            this.cboTable.Location = new System.Drawing.Point(64, 67);
            this.cboTable.Name = "cboTable";
            this.cboTable.Size = new System.Drawing.Size(220, 20);
            this.cboTable.TabIndex = 7;
            //
            // btnRefreshTables
            //
            this.btnRefreshTables.Location = new System.Drawing.Point(292, 66);
            this.btnRefreshTables.Name = "btnRefreshTables";
            this.btnRefreshTables.Size = new System.Drawing.Size(80, 23);
            this.btnRefreshTables.TabIndex = 8;
            this.btnRefreshTables.Text = "목록 갱신";
            this.btnRefreshTables.UseVisualStyleBackColor = true;
            this.btnRefreshTables.Click += new System.EventHandler(this.btnRefreshTables_Click);
            //
            // btnCreateOne
            //
            this.btnCreateOne.Location = new System.Drawing.Point(380, 66);
            this.btnCreateOne.Name = "btnCreateOne";
            this.btnCreateOne.Size = new System.Drawing.Size(140, 23);
            this.btnCreateOne.TabIndex = 9;
            this.btnCreateOne.Text = "선택 테이블 → 엑셀";
            this.btnCreateOne.UseVisualStyleBackColor = true;
            this.btnCreateOne.Click += new System.EventHandler(this.btnCreateOne_Click);
            //
            // btnGenerateOne
            //
            this.btnGenerateOne.Location = new System.Drawing.Point(528, 66);
            this.btnGenerateOne.Name = "btnGenerateOne";
            this.btnGenerateOne.Size = new System.Drawing.Size(140, 23);
            this.btnGenerateOne.TabIndex = 10;
            this.btnGenerateOne.Text = "선택 테이블만 생성";
            this.btnGenerateOne.UseVisualStyleBackColor = true;
            this.btnGenerateOne.Click += new System.EventHandler(this.btnGenerateOne_Click);
            //
            // txtLog
            //
            this.txtLog.Anchor = ((System.Windows.Forms.AnchorStyles)((((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Bottom)
            | System.Windows.Forms.AnchorStyles.Left)
            | System.Windows.Forms.AnchorStyles.Right)));
            this.txtLog.BackColor = System.Drawing.Color.White;
            this.txtLog.Font = new System.Drawing.Font("Consolas", 9F);
            this.txtLog.Location = new System.Drawing.Point(12, 336);
            this.txtLog.Multiline = true;
            this.txtLog.Name = "txtLog";
            this.txtLog.ReadOnly = true;
            this.txtLog.ScrollBars = System.Windows.Forms.ScrollBars.Both;
            this.txtLog.Size = new System.Drawing.Size(760, 260);
            this.txtLog.TabIndex = 4;
            this.txtLog.WordWrap = false;
            //
            // lblStatus
            //
            this.lblStatus.AutoSize = true;
            this.lblStatus.Location = new System.Drawing.Point(14, 317);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(29, 12);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "대기";
            //
            // btnClearLog
            //
            this.btnClearLog.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnClearLog.Location = new System.Drawing.Point(692, 310);
            this.btnClearLog.Name = "btnClearLog";
            this.btnClearLog.Size = new System.Drawing.Size(80, 23);
            this.btnClearLog.TabIndex = 6;
            this.btnClearLog.Text = "로그 지우기";
            this.btnClearLog.UseVisualStyleBackColor = true;
            this.btnClearLog.Click += new System.EventHandler(this.btnClearLog_Click);
            //
            // btnOpenOutput
            //
            this.btnOpenOutput.Anchor = ((System.Windows.Forms.AnchorStyles)((System.Windows.Forms.AnchorStyles.Top | System.Windows.Forms.AnchorStyles.Right)));
            this.btnOpenOutput.Location = new System.Drawing.Point(586, 310);
            this.btnOpenOutput.Name = "btnOpenOutput";
            this.btnOpenOutput.Size = new System.Drawing.Size(100, 23);
            this.btnOpenOutput.TabIndex = 5;
            this.btnOpenOutput.Text = "출력 폴더 열기";
            this.btnOpenOutput.UseVisualStyleBackColor = true;
            this.btnOpenOutput.Click += new System.EventHandler(this.btnOpenOutput_Click);
            //
            // MainForm
            //
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(784, 608);
            this.Controls.Add(this.btnOpenOutput);
            this.Controls.Add(this.btnClearLog);
            this.Controls.Add(this.txtLog);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.grpRun);
            this.Controls.Add(this.grpOption);
            this.Controls.Add(this.grpPath);
            this.MinimumSize = new System.Drawing.Size(700, 520);
            this.Name = "MainForm";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "DesignTool";
            this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.MainForm_FormClosing);
            this.Load += new System.EventHandler(this.MainForm_Load);
            this.grpPath.ResumeLayout(false);
            this.grpPath.PerformLayout();
            this.grpOption.ResumeLayout(false);
            this.grpOption.PerformLayout();
            this.grpRun.ResumeLayout(false);
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.GroupBox grpPath;
        private System.Windows.Forms.Button btnGeneratorBrowse;
        private System.Windows.Forms.TextBox txtGenerator;
        private System.Windows.Forms.Label lblGenerator;
        private System.Windows.Forms.Button btnOutputBrowse;
        private System.Windows.Forms.TextBox txtOutput;
        private System.Windows.Forms.Label lblOutput;
        private System.Windows.Forms.Button btnDataBrowse;
        private System.Windows.Forms.TextBox txtData;
        private System.Windows.Forms.Label lblData;
        private System.Windows.Forms.GroupBox grpOption;
        private System.Windows.Forms.CheckBox chkMerge;
        private System.Windows.Forms.CheckBox chkWarnAsError;
        private System.Windows.Forms.CheckBox chkFull;
        private System.Windows.Forms.GroupBox grpRun;
        private System.Windows.Forms.Button btnStop;
        private System.Windows.Forms.Button btnAll;
        private System.Windows.Forms.Button btnLocal;
        private System.Windows.Forms.Button btnTableGenerate;
        private System.Windows.Forms.Button btnTableCreate;
        private System.Windows.Forms.Button btnEnum;
        private System.Windows.Forms.TextBox txtLog;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnClearLog;
        private System.Windows.Forms.Button btnOpenOutput;
        private System.Windows.Forms.Label lblTable;
        private System.Windows.Forms.ComboBox cboTable;
        private System.Windows.Forms.Button btnRefreshTables;
        private System.Windows.Forms.Button btnCreateOne;
        private System.Windows.Forms.Button btnGenerateOne;
    }
}
