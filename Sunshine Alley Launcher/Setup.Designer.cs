namespace SunshineAlley_Launcher
{
    partial class Setup
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Setup));
            radioButtonInstall = new System.Windows.Forms.RadioButton();
            radioButtonRepair = new System.Windows.Forms.RadioButton();
            radioButtonUninstall = new System.Windows.Forms.RadioButton();
            radioButtonShortcuts = new System.Windows.Forms.RadioButton();
            labelTitle = new System.Windows.Forms.Label();
            buttonNext = new System.Windows.Forms.Button();
            checkBoxShortcutStartMenu = new System.Windows.Forms.CheckBox();
            checkBoxShortcutDesktop = new System.Windows.Forms.CheckBox();
            labelGameDirectoryPath = new System.Windows.Forms.Label();
            labelInstallDirectoryPath = new System.Windows.Forms.Label();
            labelInstallDirectory = new System.Windows.Forms.Label();
            labelGameDirectory = new System.Windows.Forms.Label();
            toolTip1 = new System.Windows.Forms.ToolTip(components);
            folderBrowserDialogGame = new System.Windows.Forms.FolderBrowserDialog();
            folderBrowserDialogInstall = new System.Windows.Forms.FolderBrowserDialog();
            progressBar1 = new System.Windows.Forms.ProgressBar();
            SuspendLayout();
            // 
            // radioButtonInstall
            // 
            radioButtonInstall.AutoSize = true;
            radioButtonInstall.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            radioButtonInstall.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioButtonInstall.ForeColor = System.Drawing.Color.White;
            radioButtonInstall.Location = new System.Drawing.Point(12, 114);
            radioButtonInstall.Name = "radioButtonInstall";
            radioButtonInstall.Size = new System.Drawing.Size(81, 30);
            radioButtonInstall.TabIndex = 0;
            radioButtonInstall.TabStop = true;
            radioButtonInstall.Text = "Install";
            radioButtonInstall.UseVisualStyleBackColor = false;
            radioButtonInstall.CheckedChanged += radioButtonInstall_CheckedChanged;
            radioButtonInstall.MouseEnter += radioButtonInstall_MouseEnter;
            // 
            // radioButtonRepair
            // 
            radioButtonRepair.AutoSize = true;
            radioButtonRepair.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            radioButtonRepair.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioButtonRepair.ForeColor = System.Drawing.Color.White;
            radioButtonRepair.Location = new System.Drawing.Point(12, 150);
            radioButtonRepair.Name = "radioButtonRepair";
            radioButtonRepair.Size = new System.Drawing.Size(84, 30);
            radioButtonRepair.TabIndex = 1;
            radioButtonRepair.TabStop = true;
            radioButtonRepair.Text = "Repair";
            radioButtonRepair.UseVisualStyleBackColor = false;
            radioButtonRepair.CheckedChanged += radioButtonRepair_CheckedChanged;
            radioButtonRepair.MouseEnter += radioButtonRepair_MouseEnter;
            // 
            // radioButtonUninstall
            // 
            radioButtonUninstall.AutoSize = true;
            radioButtonUninstall.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            radioButtonUninstall.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioButtonUninstall.ForeColor = System.Drawing.Color.White;
            radioButtonUninstall.Location = new System.Drawing.Point(12, 186);
            radioButtonUninstall.Name = "radioButtonUninstall";
            radioButtonUninstall.Size = new System.Drawing.Size(105, 30);
            radioButtonUninstall.TabIndex = 2;
            radioButtonUninstall.TabStop = true;
            radioButtonUninstall.Text = "Uninstall";
            radioButtonUninstall.UseVisualStyleBackColor = false;
            radioButtonUninstall.CheckedChanged += radioButtonUninstall_CheckedChanged;
            radioButtonUninstall.MouseEnter += radioButtonUninstall_MouseEnter;
            // 
            // radioButtonShortcuts
            // 
            radioButtonShortcuts.AutoSize = true;
            radioButtonShortcuts.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            radioButtonShortcuts.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            radioButtonShortcuts.ForeColor = System.Drawing.Color.White;
            radioButtonShortcuts.Location = new System.Drawing.Point(12, 222);
            radioButtonShortcuts.Name = "radioButtonShortcuts";
            radioButtonShortcuts.Size = new System.Drawing.Size(203, 30);
            radioButtonShortcuts.TabIndex = 3;
            radioButtonShortcuts.TabStop = true;
            radioButtonShortcuts.Text = "I just want shortcuts";
            radioButtonShortcuts.UseVisualStyleBackColor = false;
            radioButtonShortcuts.CheckedChanged += radioButtonShortcuts_CheckedChanged;
            radioButtonShortcuts.MouseEnter += radioButtonShortcuts_MouseEnter;
            // 
            // labelTitle
            // 
            labelTitle.AutoSize = true;
            labelTitle.BackColor = System.Drawing.Color.Transparent;
            labelTitle.Font = new System.Drawing.Font("Matura MT Script Capitals", 34F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            labelTitle.ForeColor = System.Drawing.Color.Gold;
            labelTitle.Location = new System.Drawing.Point(141, 31);
            labelTitle.Name = "labelTitle";
            labelTitle.Size = new System.Drawing.Size(516, 61);
            labelTitle.TabIndex = 8;
            labelTitle.Text = "Sunshine Alley Setup";
            labelTitle.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // buttonNext
            // 
            buttonNext.BackColor = System.Drawing.Color.FromArgb(190, 50, 90, 192);
            buttonNext.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            buttonNext.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            buttonNext.ForeColor = System.Drawing.Color.White;
            buttonNext.Location = new System.Drawing.Point(657, 397);
            buttonNext.Name = "buttonNext";
            buttonNext.Size = new System.Drawing.Size(131, 41);
            buttonNext.TabIndex = 9;
            buttonNext.Text = "Next";
            buttonNext.UseVisualStyleBackColor = false;
            buttonNext.Click += buttonNext_Click;
            // 
            // checkBoxShortcutStartMenu
            // 
            checkBoxShortcutStartMenu.AutoSize = true;
            checkBoxShortcutStartMenu.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            checkBoxShortcutStartMenu.Checked = true;
            checkBoxShortcutStartMenu.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxShortcutStartMenu.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            checkBoxShortcutStartMenu.ForeColor = System.Drawing.Color.White;
            checkBoxShortcutStartMenu.Location = new System.Drawing.Point(48, 258);
            checkBoxShortcutStartMenu.Name = "checkBoxShortcutStartMenu";
            checkBoxShortcutStartMenu.Size = new System.Drawing.Size(203, 23);
            checkBoxShortcutStartMenu.TabIndex = 10;
            checkBoxShortcutStartMenu.Text = "Create Start Menu shortcut";
            checkBoxShortcutStartMenu.UseVisualStyleBackColor = false;
            checkBoxShortcutStartMenu.CheckedChanged += checkBoxShortcutStartMenu_CheckedChanged;
            // 
            // checkBoxShortcutDesktop
            // 
            checkBoxShortcutDesktop.AutoSize = true;
            checkBoxShortcutDesktop.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            checkBoxShortcutDesktop.Checked = true;
            checkBoxShortcutDesktop.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxShortcutDesktop.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            checkBoxShortcutDesktop.ForeColor = System.Drawing.Color.White;
            checkBoxShortcutDesktop.Location = new System.Drawing.Point(48, 294);
            checkBoxShortcutDesktop.Name = "checkBoxShortcutDesktop";
            checkBoxShortcutDesktop.Size = new System.Drawing.Size(185, 23);
            checkBoxShortcutDesktop.TabIndex = 11;
            checkBoxShortcutDesktop.Text = "Create Desktop shortcut";
            checkBoxShortcutDesktop.UseVisualStyleBackColor = false;
            checkBoxShortcutDesktop.CheckedChanged += checkBoxShortCutDesktop_CheckedChanged;
            // 
            // labelGameDirectoryPath
            // 
            labelGameDirectoryPath.AutoSize = true;
            labelGameDirectoryPath.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelGameDirectoryPath.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelGameDirectoryPath.ForeColor = System.Drawing.Color.White;
            labelGameDirectoryPath.Location = new System.Drawing.Point(460, 216);
            labelGameDirectoryPath.Name = "labelGameDirectoryPath";
            labelGameDirectoryPath.Size = new System.Drawing.Size(160, 19);
            labelGameDirectoryPath.TabIndex = 12;
            labelGameDirectoryPath.Text = "c:\\Program Files\\Steam";
            labelGameDirectoryPath.Click += labelGameDirectoryPath_Click;
            labelGameDirectoryPath.MouseEnter += labelGameDirectoryPath_MouseEnter;
            // 
            // labelInstallDirectoryPath
            // 
            labelInstallDirectoryPath.AutoSize = true;
            labelInstallDirectoryPath.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelInstallDirectoryPath.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelInstallDirectoryPath.ForeColor = System.Drawing.Color.White;
            labelInstallDirectoryPath.Location = new System.Drawing.Point(460, 249);
            labelInstallDirectoryPath.Name = "labelInstallDirectoryPath";
            labelInstallDirectoryPath.Size = new System.Drawing.Size(103, 19);
            labelInstallDirectoryPath.TabIndex = 13;
            labelInstallDirectoryPath.Text = "c:\\Users\\Local";
            labelInstallDirectoryPath.Click += labelInstallDirectoryPath_Click;
            labelInstallDirectoryPath.MouseEnter += labelInstallDirectoryPath_MouseEnter;
            // 
            // labelInstallDirectory
            // 
            labelInstallDirectory.AutoSize = true;
            labelInstallDirectory.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelInstallDirectory.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelInstallDirectory.ForeColor = System.Drawing.Color.White;
            labelInstallDirectory.Location = new System.Drawing.Point(344, 249);
            labelInstallDirectory.Name = "labelInstallDirectory";
            labelInstallDirectory.Size = new System.Drawing.Size(116, 19);
            labelInstallDirectory.TabIndex = 14;
            labelInstallDirectory.Text = "Install Directory:";
            labelInstallDirectory.Click += labelInstallDirectory_Click;
            labelInstallDirectory.MouseEnter += labelInstallDirectory_MouseEnter;
            // 
            // labelGameDirectory
            // 
            labelGameDirectory.AutoSize = true;
            labelGameDirectory.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelGameDirectory.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelGameDirectory.ForeColor = System.Drawing.Color.White;
            labelGameDirectory.Location = new System.Drawing.Point(344, 216);
            labelGameDirectory.Name = "labelGameDirectory";
            labelGameDirectory.Size = new System.Drawing.Size(114, 19);
            labelGameDirectory.TabIndex = 15;
            labelGameDirectory.Text = "Game Directory:";
            labelGameDirectory.Click += labelGameDirectory_Click;
            labelGameDirectory.MouseEnter += labelGameDirectory_MouseEnter;
            // 
            // progressBar1
            // 
            progressBar1.BackColor = System.Drawing.SystemColors.Control;
            progressBar1.Location = new System.Drawing.Point(12, 368);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new System.Drawing.Size(776, 23);
            progressBar1.TabIndex = 16;
            progressBar1.Value = 50;
            // 
            // Setup
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackgroundImage = Properties.Resources.vikingarms;
            BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(progressBar1);
            Controls.Add(labelGameDirectory);
            Controls.Add(labelInstallDirectory);
            Controls.Add(labelInstallDirectoryPath);
            Controls.Add(labelGameDirectoryPath);
            Controls.Add(checkBoxShortcutDesktop);
            Controls.Add(checkBoxShortcutStartMenu);
            Controls.Add(buttonNext);
            Controls.Add(labelTitle);
            Controls.Add(radioButtonShortcuts);
            Controls.Add(radioButtonUninstall);
            Controls.Add(radioButtonRepair);
            Controls.Add(radioButtonInstall);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Name = "Setup";
            Text = "Setup";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.RadioButton radioButtonInstall;
        private System.Windows.Forms.RadioButton radioButtonRepair;
        private System.Windows.Forms.RadioButton radioButtonUninstall;
        private System.Windows.Forms.RadioButton radioButtonShortcuts;
        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.Button buttonNext;
        private System.Windows.Forms.CheckBox checkBoxShortcutStartMenu;
        private System.Windows.Forms.CheckBox checkBoxShortcutDesktop;
        private System.Windows.Forms.Label labelGameDirectoryPath;
        private System.Windows.Forms.Label labelInstallDirectoryPath;
        private System.Windows.Forms.Label labelInstallDirectory;
        private System.Windows.Forms.Label labelGameDirectory;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialogGame;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialogInstall;
        private System.Windows.Forms.ProgressBar progressBar1;
    }
}