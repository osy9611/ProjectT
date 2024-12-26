namespace DesignTool
{
    partial class Form1
    {
        /// <summary>
        /// 필수 디자이너 변수입니다.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// 사용 중인 모든 리소스를 정리합니다.
        /// </summary>
        /// <param name="disposing">관리되는 리소스를 삭제해야 하면 true이고, 그렇지 않으면 false입니다.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form 디자이너에서 생성한 코드

        /// <summary>
        /// 디자이너 지원에 필요한 메서드입니다. 
        /// 이 메서드의 내용을 코드 편집기로 수정하지 마세요.
        /// </summary>
        private void InitializeComponent()
        {
            this.tabControl1 = new System.Windows.Forms.TabControl();
            this.tabPage1 = new System.Windows.Forms.TabPage();
            this.groupBox1 = new System.Windows.Forms.GroupBox();
            this.tabControl2 = new System.Windows.Forms.TabControl();
            this.tabPage3 = new System.Windows.Forms.TabPage();
            this.CreateAllButton = new System.Windows.Forms.Button();
            this.CreateTableText = new System.Windows.Forms.TextBox();
            this.CreateButton = new System.Windows.Forms.Button();
            this.tabPage4 = new System.Windows.Forms.TabPage();
            this.GenerateAllButton = new System.Windows.Forms.Button();
            this.GenerateTableText = new System.Windows.Forms.TextBox();
            this.GenerateButton = new System.Windows.Forms.Button();
            this.groupBox2 = new System.Windows.Forms.GroupBox();
            this.LocalGenButton = new System.Windows.Forms.Button();
            this.EnumGroupBox = new System.Windows.Forms.GroupBox();
            this.EnumGenButton = new System.Windows.Forms.Button();
            this.tabPage2 = new System.Windows.Forms.TabPage();
            this.groupBox5 = new System.Windows.Forms.GroupBox();
            this.FolderPathButton = new System.Windows.Forms.Button();
            this.FolderPathText = new System.Windows.Forms.TextBox();
            this.groupBox6 = new System.Windows.Forms.GroupBox();
            this.GenerateOutputPathButton = new System.Windows.Forms.Button();
            this.GenerateOutputPathText = new System.Windows.Forms.TextBox();
            this.groupBox3 = new System.Windows.Forms.GroupBox();
            this.tabControl1.SuspendLayout();
            this.tabPage1.SuspendLayout();
            this.groupBox1.SuspendLayout();
            this.tabControl2.SuspendLayout();
            this.tabPage3.SuspendLayout();
            this.tabPage4.SuspendLayout();
            this.groupBox2.SuspendLayout();
            this.EnumGroupBox.SuspendLayout();
            this.tabPage2.SuspendLayout();
            this.groupBox5.SuspendLayout();
            this.groupBox6.SuspendLayout();
            this.SuspendLayout();
            // 
            // tabControl1
            // 
            this.tabControl1.Controls.Add(this.tabPage1);
            this.tabControl1.Controls.Add(this.tabPage2);
            this.tabControl1.Location = new System.Drawing.Point(0, 0);
            this.tabControl1.Name = "tabControl1";
            this.tabControl1.SelectedIndex = 0;
            this.tabControl1.Size = new System.Drawing.Size(358, 469);
            this.tabControl1.TabIndex = 0;
            // 
            // tabPage1
            // 
            this.tabPage1.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage1.Controls.Add(this.groupBox1);
            this.tabPage1.Controls.Add(this.groupBox2);
            this.tabPage1.Controls.Add(this.EnumGroupBox);
            this.tabPage1.Location = new System.Drawing.Point(4, 22);
            this.tabPage1.Name = "tabPage1";
            this.tabPage1.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage1.Size = new System.Drawing.Size(350, 443);
            this.tabPage1.TabIndex = 0;
            this.tabPage1.Text = "Gen";
            // 
            // groupBox1
            // 
            this.groupBox1.Controls.Add(this.tabControl2);
            this.groupBox1.Location = new System.Drawing.Point(8, 115);
            this.groupBox1.Name = "groupBox1";
            this.groupBox1.Size = new System.Drawing.Size(330, 189);
            this.groupBox1.TabIndex = 1;
            this.groupBox1.TabStop = false;
            this.groupBox1.Text = "Table";
            // 
            // tabControl2
            // 
            this.tabControl2.Controls.Add(this.tabPage3);
            this.tabControl2.Controls.Add(this.tabPage4);
            this.tabControl2.Location = new System.Drawing.Point(6, 20);
            this.tabControl2.Name = "tabControl2";
            this.tabControl2.SelectedIndex = 0;
            this.tabControl2.Size = new System.Drawing.Size(318, 158);
            this.tabControl2.TabIndex = 0;
            // 
            // tabPage3
            // 
            this.tabPage3.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage3.Controls.Add(this.CreateAllButton);
            this.tabPage3.Controls.Add(this.CreateTableText);
            this.tabPage3.Controls.Add(this.CreateButton);
            this.tabPage3.Location = new System.Drawing.Point(4, 22);
            this.tabPage3.Name = "tabPage3";
            this.tabPage3.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage3.Size = new System.Drawing.Size(310, 132);
            this.tabPage3.TabIndex = 0;
            this.tabPage3.Text = "Creator";
            // 
            // CreateAllButton
            // 
            this.CreateAllButton.Location = new System.Drawing.Point(162, 74);
            this.CreateAllButton.Name = "CreateAllButton";
            this.CreateAllButton.Size = new System.Drawing.Size(109, 31);
            this.CreateAllButton.TabIndex = 2;
            this.CreateAllButton.Text = "CreateAll";
            this.CreateAllButton.UseVisualStyleBackColor = true;
            this.CreateAllButton.Click += new System.EventHandler(this.CreateAllButton_Click);
            // 
            // CreateTableText
            // 
            this.CreateTableText.Location = new System.Drawing.Point(33, 38);
            this.CreateTableText.Name = "CreateTableText";
            this.CreateTableText.Size = new System.Drawing.Size(238, 21);
            this.CreateTableText.TabIndex = 1;
            // 
            // CreateButton
            // 
            this.CreateButton.Location = new System.Drawing.Point(33, 74);
            this.CreateButton.Name = "CreateButton";
            this.CreateButton.Size = new System.Drawing.Size(109, 31);
            this.CreateButton.TabIndex = 0;
            this.CreateButton.Text = "Create";
            this.CreateButton.UseVisualStyleBackColor = true;
            this.CreateButton.Click += new System.EventHandler(this.CreateButton_Click);
            // 
            // tabPage4
            // 
            this.tabPage4.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage4.Controls.Add(this.GenerateAllButton);
            this.tabPage4.Controls.Add(this.GenerateTableText);
            this.tabPage4.Controls.Add(this.GenerateButton);
            this.tabPage4.Location = new System.Drawing.Point(4, 22);
            this.tabPage4.Name = "tabPage4";
            this.tabPage4.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage4.Size = new System.Drawing.Size(310, 132);
            this.tabPage4.TabIndex = 1;
            this.tabPage4.Text = "Generator";
            // 
            // GenerateAllButton
            // 
            this.GenerateAllButton.Location = new System.Drawing.Point(162, 74);
            this.GenerateAllButton.Name = "GenerateAllButton";
            this.GenerateAllButton.Size = new System.Drawing.Size(109, 31);
            this.GenerateAllButton.TabIndex = 5;
            this.GenerateAllButton.Text = "GenerateAll";
            this.GenerateAllButton.UseVisualStyleBackColor = true;
            this.GenerateAllButton.Click += new System.EventHandler(this.GenerateAllButton_Click);
            // 
            // GenerateTableText
            // 
            this.GenerateTableText.Location = new System.Drawing.Point(33, 38);
            this.GenerateTableText.Name = "GenerateTableText";
            this.GenerateTableText.Size = new System.Drawing.Size(238, 21);
            this.GenerateTableText.TabIndex = 4;
            // 
            // GenerateButton
            // 
            this.GenerateButton.Location = new System.Drawing.Point(33, 74);
            this.GenerateButton.Name = "GenerateButton";
            this.GenerateButton.Size = new System.Drawing.Size(109, 31);
            this.GenerateButton.TabIndex = 3;
            this.GenerateButton.Text = "Generate";
            this.GenerateButton.UseVisualStyleBackColor = true;
            this.GenerateButton.Click += new System.EventHandler(this.GenerateButton_Click);
            // 
            // groupBox2
            // 
            this.groupBox2.Controls.Add(this.LocalGenButton);
            this.groupBox2.Location = new System.Drawing.Point(8, 319);
            this.groupBox2.Name = "groupBox2";
            this.groupBox2.Size = new System.Drawing.Size(330, 98);
            this.groupBox2.TabIndex = 1;
            this.groupBox2.TabStop = false;
            this.groupBox2.Text = "Localize";
            // 
            // LocalGenButton
            // 
            this.LocalGenButton.Location = new System.Drawing.Point(95, 31);
            this.LocalGenButton.Name = "LocalGenButton";
            this.LocalGenButton.Size = new System.Drawing.Size(133, 38);
            this.LocalGenButton.TabIndex = 1;
            this.LocalGenButton.Text = "Generate";
            this.LocalGenButton.UseVisualStyleBackColor = true;
            this.LocalGenButton.Click += new System.EventHandler(this.LocalGenButton_Click);
            // 
            // EnumGroupBox
            // 
            this.EnumGroupBox.Controls.Add(this.EnumGenButton);
            this.EnumGroupBox.Location = new System.Drawing.Point(8, 6);
            this.EnumGroupBox.Name = "EnumGroupBox";
            this.EnumGroupBox.Size = new System.Drawing.Size(330, 94);
            this.EnumGroupBox.TabIndex = 0;
            this.EnumGroupBox.TabStop = false;
            this.EnumGroupBox.Text = "Enum";
            // 
            // EnumGenButton
            // 
            this.EnumGenButton.Location = new System.Drawing.Point(95, 33);
            this.EnumGenButton.Name = "EnumGenButton";
            this.EnumGenButton.Size = new System.Drawing.Size(133, 38);
            this.EnumGenButton.TabIndex = 0;
            this.EnumGenButton.Text = "Generate";
            this.EnumGenButton.UseVisualStyleBackColor = true;
            this.EnumGenButton.Click += new System.EventHandler(this.EnumGenButton_Click);
            // 
            // tabPage2
            // 
            this.tabPage2.BackColor = System.Drawing.SystemColors.Control;
            this.tabPage2.Controls.Add(this.groupBox5);
            this.tabPage2.Controls.Add(this.groupBox6);
            this.tabPage2.Location = new System.Drawing.Point(4, 22);
            this.tabPage2.Name = "tabPage2";
            this.tabPage2.Padding = new System.Windows.Forms.Padding(3);
            this.tabPage2.Size = new System.Drawing.Size(350, 443);
            this.tabPage2.TabIndex = 1;
            this.tabPage2.Text = "Setting";
            // 
            // groupBox5
            // 
            this.groupBox5.Controls.Add(this.FolderPathButton);
            this.groupBox5.Controls.Add(this.FolderPathText);
            this.groupBox5.Location = new System.Drawing.Point(8, 6);
            this.groupBox5.Name = "groupBox5";
            this.groupBox5.Size = new System.Drawing.Size(336, 87);
            this.groupBox5.TabIndex = 2;
            this.groupBox5.TabStop = false;
            this.groupBox5.Text = "FolderPath";
            // 
            // FolderPathButton
            // 
            this.FolderPathButton.Location = new System.Drawing.Point(255, 37);
            this.FolderPathButton.Name = "FolderPathButton";
            this.FolderPathButton.Size = new System.Drawing.Size(75, 23);
            this.FolderPathButton.TabIndex = 1;
            this.FolderPathButton.Text = "Find";
            this.FolderPathButton.UseVisualStyleBackColor = true;
            this.FolderPathButton.Click += new System.EventHandler(this.FolderPathButton_Click);
            // 
            // FolderPathText
            // 
            this.FolderPathText.Location = new System.Drawing.Point(6, 37);
            this.FolderPathText.Name = "FolderPathText";
            this.FolderPathText.Size = new System.Drawing.Size(236, 21);
            this.FolderPathText.TabIndex = 0;
            // 
            // groupBox6
            // 
            this.groupBox6.Controls.Add(this.GenerateOutputPathButton);
            this.groupBox6.Controls.Add(this.GenerateOutputPathText);
            this.groupBox6.Location = new System.Drawing.Point(8, 99);
            this.groupBox6.Name = "groupBox6";
            this.groupBox6.Size = new System.Drawing.Size(336, 87);
            this.groupBox6.TabIndex = 2;
            this.groupBox6.TabStop = false;
            this.groupBox6.Text = "GenerateOutput";
            // 
            // GenerateOutputPathButton
            // 
            this.GenerateOutputPathButton.Location = new System.Drawing.Point(255, 37);
            this.GenerateOutputPathButton.Name = "GenerateOutputPathButton";
            this.GenerateOutputPathButton.Size = new System.Drawing.Size(75, 23);
            this.GenerateOutputPathButton.TabIndex = 1;
            this.GenerateOutputPathButton.Text = "Find";
            this.GenerateOutputPathButton.UseVisualStyleBackColor = true;
            this.GenerateOutputPathButton.Click += new System.EventHandler(this.GenerateOutputPathButton_Click);
            // 
            // GenerateOutputPathText
            // 
            this.GenerateOutputPathText.Location = new System.Drawing.Point(6, 37);
            this.GenerateOutputPathText.Name = "GenerateOutputPathText";
            this.GenerateOutputPathText.Size = new System.Drawing.Size(236, 21);
            this.GenerateOutputPathText.TabIndex = 0;
            // 
            // groupBox3
            // 
            this.groupBox3.Location = new System.Drawing.Point(595, 176);
            this.groupBox3.Name = "groupBox3";
            this.groupBox3.Size = new System.Drawing.Size(8, 8);
            this.groupBox3.TabIndex = 2;
            this.groupBox3.TabStop = false;
            this.groupBox3.Text = "groupBox3";
            // 
            // Form1
            // 
            this.AutoScaleDimensions = new System.Drawing.SizeF(7F, 12F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.ClientSize = new System.Drawing.Size(364, 471);
            this.Controls.Add(this.groupBox3);
            this.Controls.Add(this.tabControl1);
            this.Name = "Form1";
            this.Text = "DesignTool";
            this.tabControl1.ResumeLayout(false);
            this.tabPage1.ResumeLayout(false);
            this.groupBox1.ResumeLayout(false);
            this.tabControl2.ResumeLayout(false);
            this.tabPage3.ResumeLayout(false);
            this.tabPage3.PerformLayout();
            this.tabPage4.ResumeLayout(false);
            this.tabPage4.PerformLayout();
            this.groupBox2.ResumeLayout(false);
            this.EnumGroupBox.ResumeLayout(false);
            this.tabPage2.ResumeLayout(false);
            this.groupBox5.ResumeLayout(false);
            this.groupBox5.PerformLayout();
            this.groupBox6.ResumeLayout(false);
            this.groupBox6.PerformLayout();
            this.ResumeLayout(false);

        }

        #endregion
        private System.Windows.Forms.TabControl tabControl1;
        private System.Windows.Forms.TabPage tabPage1;
        private System.Windows.Forms.GroupBox EnumGroupBox;
        private System.Windows.Forms.Button EnumGenButton;
        private System.Windows.Forms.TabPage tabPage2;
        private System.Windows.Forms.GroupBox groupBox1;
        private System.Windows.Forms.TabControl tabControl2;
        private System.Windows.Forms.TabPage tabPage3;
        private System.Windows.Forms.TabPage tabPage4;
        private System.Windows.Forms.Button CreateAllButton;
        private System.Windows.Forms.TextBox CreateTableText;
        private System.Windows.Forms.Button CreateButton;
        private System.Windows.Forms.GroupBox groupBox2;
        private System.Windows.Forms.Button LocalGenButton;
        private System.Windows.Forms.GroupBox groupBox3;
        private System.Windows.Forms.Button GenerateAllButton;
        private System.Windows.Forms.TextBox GenerateTableText;
        private System.Windows.Forms.Button GenerateButton;
        private System.Windows.Forms.GroupBox groupBox6;
        private System.Windows.Forms.Button GenerateOutputPathButton;
        private System.Windows.Forms.TextBox GenerateOutputPathText;
        private System.Windows.Forms.GroupBox groupBox5;
        private System.Windows.Forms.Button FolderPathButton;
        private System.Windows.Forms.TextBox FolderPathText;
    }
}

