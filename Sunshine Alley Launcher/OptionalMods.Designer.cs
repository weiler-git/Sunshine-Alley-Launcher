namespace SunshineAlley_Launcher
{
    partial class OptionalMods
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
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(OptionalMods));
            checkBoxMod1 = new System.Windows.Forms.CheckBox();
            labelModpackTitle = new System.Windows.Forms.Label();
            labelAffectedTitle = new System.Windows.Forms.Label();
            labelAffected1 = new System.Windows.Forms.Label();
            SuspendLayout();
            // 
            // checkBoxMod1
            // 
            checkBoxMod1.AutoSize = true;
            checkBoxMod1.BackColor = System.Drawing.Color.Black;
            checkBoxMod1.Checked = true;
            checkBoxMod1.CheckState = System.Windows.Forms.CheckState.Checked;
            checkBoxMod1.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            checkBoxMod1.ForeColor = System.Drawing.Color.Silver;
            checkBoxMod1.Location = new System.Drawing.Point(226, 35);
            checkBoxMod1.Name = "checkBoxMod1";
            checkBoxMod1.Size = new System.Drawing.Size(93, 23);
            checkBoxMod1.TabIndex = 19;
            checkBoxMod1.Text = "Persistent";
            checkBoxMod1.UseVisualStyleBackColor = false;
            // 
            // labelModpackTitle
            // 
            labelModpackTitle.AutoSize = true;
            labelModpackTitle.BackColor = System.Drawing.Color.Black;
            labelModpackTitle.Font = new System.Drawing.Font("Calibri", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelModpackTitle.ForeColor = System.Drawing.Color.White;
            labelModpackTitle.Location = new System.Drawing.Point(226, 9);
            labelModpackTitle.Name = "labelModpackTitle";
            labelModpackTitle.Size = new System.Drawing.Size(222, 23);
            labelModpackTitle.TabIndex = 27;
            labelModpackTitle.Text = "Optional Mods, modpack: x";
            // 
            // labelAffectedTitle
            // 
            labelAffectedTitle.AutoSize = true;
            labelAffectedTitle.BackColor = System.Drawing.Color.Black;
            labelAffectedTitle.Font = new System.Drawing.Font("Calibri", 14F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelAffectedTitle.ForeColor = System.Drawing.Color.White;
            labelAffectedTitle.Location = new System.Drawing.Point(12, 9);
            labelAffectedTitle.Name = "labelAffectedTitle";
            labelAffectedTitle.Size = new System.Drawing.Size(136, 23);
            labelAffectedTitle.TabIndex = 28;
            labelAffectedTitle.Text = "Affected Servers";
            // 
            // labelAffected1
            // 
            labelAffected1.AutoSize = true;
            labelAffected1.BackColor = System.Drawing.Color.Black;
            labelAffected1.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
            labelAffected1.ForeColor = System.Drawing.Color.White;
            labelAffected1.Location = new System.Drawing.Point(12, 36);
            labelAffected1.Name = "labelAffected1";
            labelAffected1.Size = new System.Drawing.Size(114, 19);
            labelAffected1.TabIndex = 29;
            labelAffected1.Text = "Affected Servers";
            // 
            // OptionalMods
            // 
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            AutoSizeMode = System.Windows.Forms.AutoSizeMode.GrowAndShrink;
            BackColor = System.Drawing.Color.Black;
            ClientSize = new System.Drawing.Size(474, 393);
            Controls.Add(labelAffected1);
            Controls.Add(labelAffectedTitle);
            Controls.Add(labelModpackTitle);
            Controls.Add(checkBoxMod1);
            Icon = (System.Drawing.Icon)resources.GetObject("$this.Icon");
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "OptionalMods";
            ShowInTaskbar = false;
            Text = "OptionalMods";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.CheckBox checkBoxMod1;
        private System.Windows.Forms.Label labelModpackTitle;
        private System.Windows.Forms.Label labelAffectedTitle;
        private System.Windows.Forms.Label labelAffected1;
    }
}