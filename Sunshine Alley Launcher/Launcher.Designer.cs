namespace SunshineAlley_Launcher
{
    partial class Launcher
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Launcher));
            progressBar1 = new System.Windows.Forms.ProgressBar();
            checkBoxPersistent = new System.Windows.Forms.CheckBox();
            buttonPlay = new System.Windows.Forms.Button();
            labelGameDirectory = new System.Windows.Forms.Label();
            comboBoxServer = new System.Windows.Forms.ComboBox();
            labelTitle = new System.Windows.Forms.Label();
            buttonSetup = new System.Windows.Forms.Button();
            labelOptionalMods = new System.Windows.Forms.Label();
            checkBoxSteam = new System.Windows.Forms.CheckBox();
            labelStatus = new System.Windows.Forms.Label();
            toolTip1 = new System.Windows.Forms.ToolTip(components);
            folderBrowserDialogGame = new System.Windows.Forms.FolderBrowserDialog();
            labelNotify = new System.Windows.Forms.Label();
            label2 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // progressBar1
            // 
            progressBar1.BackColor = System.Drawing.SystemColors.Control;
            progressBar1.Location = new System.Drawing.Point(236, 289);
            progressBar1.Name = "progressBar1";
            progressBar1.Size = new System.Drawing.Size(552, 23);
            progressBar1.TabIndex = 17;
            progressBar1.Value = 50;
            // 
            // checkBoxPersistent
            // 
            checkBoxPersistent.AutoSize = true;
            checkBoxPersistent.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            checkBoxPersistent.Checked = true;
            checkBoxPersistent.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxPersistent.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            checkBoxPersistent.ForeColor = System.Drawing.Color.Silver;
            checkBoxPersistent.Location = new System.Drawing.Point(12, 318);
            checkBoxPersistent.Name = "checkBoxPersistent";
            checkBoxPersistent.Size = new System.Drawing.Size(93, 23);
            checkBoxPersistent.TabIndex = 18;
            checkBoxPersistent.Text = "Persistent";
            checkBoxPersistent.UseVisualStyleBackColor = false;
            checkBoxPersistent.CheckedChanged += checkBoxPersistent_CheckedChanged;
            checkBoxPersistent.MouseEnter += checkBoxPersistent_MouseEnter;
            // 
            // buttonPlay
            // 
            buttonPlay.BackColor = System.Drawing.Color.FromArgb(190, 50, 90, 192);
            buttonPlay.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            buttonPlay.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            buttonPlay.ForeColor = System.Drawing.Color.White;
            buttonPlay.Location = new System.Drawing.Point(12, 247);
            buttonPlay.Name = "buttonPlay";
            buttonPlay.Size = new System.Drawing.Size(178, 65);
            buttonPlay.TabIndex = 19;
            buttonPlay.Text = "Play";
            buttonPlay.UseVisualStyleBackColor = false;
            buttonPlay.Click += buttonPlay_Click;
            buttonPlay.MouseEnter += buttonPlay_MouseEnter;
            // 
            // labelGameDirectory
            // 
            labelGameDirectory.AutoSize = true;
            labelGameDirectory.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelGameDirectory.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelGameDirectory.ForeColor = System.Drawing.Color.Gray;
            labelGameDirectory.Location = new System.Drawing.Point(12, 422);
            labelGameDirectory.Name = "labelGameDirectory";
            labelGameDirectory.Size = new System.Drawing.Size(114, 19);
            labelGameDirectory.TabIndex = 20;
            labelGameDirectory.Text = "Game Directory:";
            labelGameDirectory.Click += labelGameDirectory_Click;
            labelGameDirectory.MouseEnter += labelGameDirectory_MouseEnter;
            // 
            // comboBoxServer
            // 
            comboBoxServer.FormattingEnabled = true;
            comboBoxServer.Location = new System.Drawing.Point(12, 218);
            comboBoxServer.Name = "comboBoxServer";
            comboBoxServer.Size = new System.Drawing.Size(178, 23);
            comboBoxServer.TabIndex = 21;
            comboBoxServer.SelectedIndexChanged += comboBoxServer_SelectedIndexChanged;
            comboBoxServer.KeyPress += comboBoxServer_KeyPress;
            comboBoxServer.MouseEnter += comboBoxServer_MouseEnter;
            // 
            // labelTitle
            // 
            labelTitle.AutoSize = true;
            labelTitle.BackColor = System.Drawing.Color.Transparent;
            labelTitle.Font = new System.Drawing.Font("Matura MT Script Capitals", 34F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point);
            labelTitle.ForeColor = System.Drawing.Color.Gold;
            labelTitle.Location = new System.Drawing.Point(206, 21);
            labelTitle.Name = "labelTitle";
            labelTitle.Size = new System.Drawing.Size(374, 61);
            labelTitle.TabIndex = 22;
            labelTitle.Text = "Sunshine Alley";
            labelTitle.TextAlign = System.Drawing.ContentAlignment.TopCenter;
            // 
            // buttonSetup
            // 
            buttonSetup.BackColor = System.Drawing.Color.FromArgb(190, 23, 23, 23);
            buttonSetup.FlatStyle = System.Windows.Forms.FlatStyle.Popup;
            buttonSetup.Font = new System.Drawing.Font("Calibri", 15.75F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            buttonSetup.ForeColor = System.Drawing.Color.White;
            buttonSetup.Location = new System.Drawing.Point(657, 397);
            buttonSetup.Name = "buttonSetup";
            buttonSetup.Size = new System.Drawing.Size(131, 41);
            buttonSetup.TabIndex = 23;
            buttonSetup.Text = "Setup";
            buttonSetup.UseVisualStyleBackColor = false;
            buttonSetup.Click += buttonSetup_Click;
            buttonSetup.MouseEnter += buttonSetup_MouseEnter;
            // 
            // labelOptionalMods
            // 
            labelOptionalMods.AutoSize = true;
            labelOptionalMods.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelOptionalMods.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelOptionalMods.ForeColor = System.Drawing.Color.Silver;
            labelOptionalMods.Location = new System.Drawing.Point(12, 373);
            labelOptionalMods.Name = "labelOptionalMods";
            labelOptionalMods.Size = new System.Drawing.Size(193, 19);
            labelOptionalMods.TabIndex = 24;
            labelOptionalMods.Text = "0 of 0 Optional mods loaded";
            labelOptionalMods.Click += labelOptionalMods_Click;
            labelOptionalMods.MouseEnter += labelOptionalMods_MouseEnter;
            // 
            // checkBoxSteam
            // 
            checkBoxSteam.AutoSize = true;
            checkBoxSteam.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            checkBoxSteam.Checked = true;
            checkBoxSteam.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxSteam.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            checkBoxSteam.ForeColor = System.Drawing.Color.Silver;
            checkBoxSteam.Location = new System.Drawing.Point(12, 347);
            checkBoxSteam.Name = "checkBoxSteam";
            checkBoxSteam.Size = new System.Drawing.Size(150, 23);
            checkBoxSteam.TabIndex = 25;
            checkBoxSteam.Text = "Launch with Steam";
            checkBoxSteam.UseVisualStyleBackColor = false;
            checkBoxSteam.CheckedChanged += checkBoxSteam_CheckedChanged;
            checkBoxSteam.MouseEnter += checkBoxSteam_MouseEnter;
            // 
            // labelStatus
            // 
            labelStatus.AutoSize = true;
            labelStatus.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelStatus.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelStatus.ForeColor = System.Drawing.Color.White;
            labelStatus.Location = new System.Drawing.Point(236, 267);
            labelStatus.Name = "labelStatus";
            labelStatus.Size = new System.Drawing.Size(57, 19);
            labelStatus.TabIndex = 26;
            labelStatus.Text = "Status..";
            labelStatus.Click += labelStatus_Click;
            // 
            // labelNotify
            // 
            labelNotify.AutoSize = true;
            labelNotify.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            labelNotify.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelNotify.ForeColor = System.Drawing.Color.Gold;
            labelNotify.Location = new System.Drawing.Point(236, 154);
            labelNotify.Name = "labelNotify";
            labelNotify.Size = new System.Drawing.Size(84, 19);
            labelNotify.TabIndex = 27;
            labelNotify.Text = "Notification";
            // 
            // label2
            // 
            label2.AutoSize = true;
            label2.BackColor = System.Drawing.Color.FromArgb(100, 50, 50, 50);
            label2.Font = new System.Drawing.Font("Wingdings", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            label2.ForeColor = System.Drawing.Color.Gold;
            label2.Location = new System.Drawing.Point(196, 221);
            label2.Name = "label2";
            label2.Size = new System.Drawing.Size(30, 17);
            label2.TabIndex = 29;
            label2.Text = "1";
            label2.Click += label2_Click;
            // 
            // Launcher
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            BackgroundImage = Properties.Resources.vikingship;
            BackgroundImageLayout = System.Windows.Forms.ImageLayout.Stretch;
            ClientSize = new System.Drawing.Size(800, 450);
            Controls.Add(label2);
            Controls.Add(labelNotify);
            Controls.Add(labelStatus);
            Controls.Add(checkBoxSteam);
            Controls.Add(labelOptionalMods);
            Controls.Add(buttonSetup);
            Controls.Add(labelTitle);
            Controls.Add(comboBoxServer);
            Controls.Add(labelGameDirectory);
            Controls.Add(buttonPlay);
            Controls.Add(checkBoxPersistent);
            Controls.Add(progressBar1);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            Name = "Launcher";
            Text = "Launcher";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ProgressBar progressBar1;
        private System.Windows.Forms.CheckBox checkBoxPersistent;
        private System.Windows.Forms.Button buttonPlay;
        private System.Windows.Forms.Label labelGameDirectory;
        private System.Windows.Forms.ComboBox comboBoxServer;
        private System.Windows.Forms.Label labelTitle;
        private System.Windows.Forms.Button buttonSetup;
        private System.Windows.Forms.Label labelOptionalMods;
        private System.Windows.Forms.CheckBox checkBoxSteam;
        private System.Windows.Forms.Label labelStatus;
        private System.Windows.Forms.ToolTip toolTip1;
        private System.Windows.Forms.FolderBrowserDialog folderBrowserDialogGame;
        private System.Windows.Forms.Label labelNotify;
        private System.Windows.Forms.Label label2;
    }
}