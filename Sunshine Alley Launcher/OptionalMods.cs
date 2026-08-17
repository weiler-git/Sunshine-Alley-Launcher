using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Printing;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SunshineAlley_Launcher.Setup;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Button;
using CheckBox = System.Windows.Forms.CheckBox;

namespace SunshineAlley_Launcher
{
    public partial class OptionalMods : Form
    {
        private static ModPack ModPack;
        private static bool debug = Program.debug;
        private class OptionalModControl
        {
            public CheckBox checkBox;
            public string modName;
            public string modPack;
            public static List<OptionalModControl> all = new List<OptionalModControl>();
            public void CheckedChanged(object sender, EventArgs e, string param)
            {
                Config.Set("Optional[" + modPack + "][" + modName + "].Enabled", checkBox.Checked);
            }
        }
        public OptionalMods(ModPack modPack)
        {
            if (modPack == null)
                this.Close();
            ModPack = modPack;

            this.DoubleBuffered = true;
            InitializeComponent();

            SuspendLayout();
            int i = 0;
            int top = labelAffected1.Top;
            int height = labelAffected1.Height;
            int margin = 5;
            foreach (GameServer server in GameServer.all)
            {

                if (server.ModPack == ModPack)
                {
                    Label label = new Label();
                    label.Text = server.WorldName;
                    label.Top = top + (height * i) + (margin * i);
                    label.Left = labelAffected1.Left;
                    label.Width = checkBoxMod1.Left - labelAffected1.Left - margin;
                    label.Height = labelAffected1.Height;

                    label.BackColor = System.Drawing.Color.Black;
                    label.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
                    label.ForeColor = System.Drawing.Color.White;

                    this.Controls.Add(label);
                    i++;
                }
            }

            labelAffected1.Visible = false;

            labelModpackTitle.Text = "Optional Mods for modpack: " + ModPack.ToString() + "";

            string prefix = "Optional[" + ModPack.ToString() + "]".ToLower();
            i = 0;
            top = checkBoxMod1.Top;
            height = checkBoxMod1.Height;
            margin = 5;
            foreach (KeyValuePair<int, Config.Entry> kvp in Config.Entry.instances)
            {
                if (kvp.Value.key.ToLower().StartsWith(prefix.ToLower()) && kvp.Value.key.EndsWith("Known"))
                {
                    OptionalModControl optionalModControl = new OptionalModControl();
                    optionalModControl.modPack = ModPack.ToString();
                    optionalModControl.modName = ExtractModName(kvp.Value.key, ModPack.ToString());
                    optionalModControl.checkBox = new CheckBox();

                    optionalModControl.checkBox.Text = optionalModControl.modName;
                    optionalModControl.checkBox.Top = top + (height * i) + (margin * i);
                    optionalModControl.checkBox.Left = checkBoxMod1.Left;
                    optionalModControl.checkBox.Width = 200;
                    optionalModControl.checkBox.Height = checkBoxMod1.Height;

                    optionalModControl.checkBox.BackColor = System.Drawing.Color.Black;
                    optionalModControl.checkBox.Font = new System.Drawing.Font("Calibri", 12F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point);
                    optionalModControl.checkBox.ForeColor = System.Drawing.Color.White;

                    optionalModControl.checkBox.Checked = Config.GetBool("Optional[" + optionalModControl.modPack + "][" + optionalModControl.modName + "].Enabled");
                    optionalModControl.checkBox.CheckedChanged += (sender, e) => optionalModControl.CheckedChanged(sender, e, "Parameter for Cloned CheckBox");
                    this.Controls.Add(optionalModControl.checkBox);
                    i++;

                }
            }
            checkBoxMod1.Visible = false;

            ResumeLayout(true);
            Refresh();
        }


        static string ExtractModName(string key, string modPack)
        {
            string searchString = "Optional[" + modPack + "][";
            int startIndex = key.IndexOf(searchString);

            if (startIndex != -1)
            {
                startIndex += searchString.Length;
                int endIndex = key.IndexOf("]", startIndex);

                if (endIndex != -1)
                {
                    return key.Substring(startIndex, endIndex - startIndex);
                }
            }

            return null;
        }
    }
}
