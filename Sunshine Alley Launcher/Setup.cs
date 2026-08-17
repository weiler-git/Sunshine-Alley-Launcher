using IWshRuntimeLibrary;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Numerics;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.Window;
using File = System.IO.File;
using ProgressBar = System.Windows.Forms.ProgressBar;

namespace SunshineAlley_Launcher
{
    public partial class Setup : Form
    {
        public enum Mode
        {
            Install,
            Repair,
            Uninstall,
            Shortcuts
        }
        public Mode mode;
        public static bool debug = Program.debug;

        public static int MarginX = 5;
        public static int MarginY = 5;
        public static int WidestDirectoryLabel = 0;
        public static Point defaultPointRadioButtonInstall;
        public static Point defaultPointRadioButtonRepair;
        public static Point defaultPointradioButtonUninstall;
        public static Point defaultPointradioButtonShortcuts;

        //private const string steamSubkey = @"SOFTWARE\Valve\Steam";
        //private const string steamKeyDirectory = "SteamPath";
        //private const string steamValheimInstallDirectory = @"\steamapps\common\Valheim";
        //private const string valheimExecutable = "valheim.exe";
        private const string defaultInstallDirectoryName = "Sunshine Alley";
        //private const string launcherDownloadURL = @"https://sunshinealley.games/ModPacks/Sunshine Alley Launcher.exe";

        public Setup()
        {
            this.DoubleBuffered = true;
            InitializeComponent();
            checkBoxShortcutStartMenu.Visible = false;
            checkBoxShortcutDesktop.Visible = false;
            labelGameDirectory.Visible = false;
            labelGameDirectoryPath.Visible = false;
            labelInstallDirectory.Visible = false;
            labelInstallDirectoryPath.Visible = false;
            progressBar1.Visible = false;

            //get default values
            defaultPointRadioButtonInstall = new Point(radioButtonInstall.Location.X, radioButtonInstall.Location.Y);
            defaultPointRadioButtonRepair = new Point(radioButtonRepair.Location.X, radioButtonRepair.Location.Y);
            defaultPointradioButtonUninstall = new Point(radioButtonUninstall.Location.X, radioButtonUninstall.Location.Y);
            defaultPointradioButtonShortcuts = new Point(radioButtonShortcuts.Location.X, radioButtonShortcuts.Location.Y);

            WidestDirectoryLabel = labelGameDirectory.Size.Width;
            if (labelInstallDirectory.Width > WidestDirectoryLabel) { WidestDirectoryLabel = labelInstallDirectory.Width; }

            //set the X positions of labels (Y will be moved later according to selected mode)
            labelInstallDirectory.Location = new Point(defaultPointRadioButtonInstall.X + MarginX, 0);
            labelInstallDirectoryPath.Location = new Point(defaultPointRadioButtonInstall.X + MarginX + WidestDirectoryLabel + MarginX, 0);
            labelGameDirectory.Location = new Point(defaultPointRadioButtonInstall.X + MarginX, 0);
            labelGameDirectoryPath.Location = new Point(defaultPointRadioButtonInstall.X + MarginX + WidestDirectoryLabel + MarginX, 0);

            //set the X positions of checkboxes (Y will be moved later according to selected mode)
            checkBoxShortcutStartMenu.Location = new Point(defaultPointRadioButtonInstall.X + MarginX, 0);
            checkBoxShortcutDesktop.Location = new Point(defaultPointRadioButtonInstall.X + MarginX, 0);

            SetDefaultInstallDirectory();
            SetDefaultGameDirectory();

            if (Program.isInstalled)
            {
                radioButtonRepair.Checked = true;
                radioButtonInstall.Enabled = false;
                radioButtonInstall.Visible = false;
            }
            else
            {
                radioButtonInstall.Checked = true;
                radioButtonRepair.Enabled = false;
                radioButtonUninstall.Enabled = false;
                radioButtonShortcuts.Enabled = false;
                radioButtonRepair.Visible = false;
                radioButtonUninstall.Visible = false;
                radioButtonShortcuts.Visible = false;
            }
            


        }

        /*
         * 
         * 
         * 
         *  GUI CONTROLS
         *  
         *  
         *  
         */
        private void radioButtonInstall_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonChanged();
        }

        private void radioButtonRepair_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonChanged();
        }

        private void radioButtonUninstall_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonChanged();
        }

        private void radioButtonShortcuts_CheckedChanged(object sender, EventArgs e)
        {
            RadioButtonChanged();
        }

        private void RadioButtonChanged()
        {
            if (radioButtonInstall.Checked) { mode = Mode.Install; }
            if (radioButtonRepair.Checked) { mode = Mode.Repair; }
            if (radioButtonUninstall.Checked) { mode = Mode.Uninstall; }
            if (radioButtonShortcuts.Checked) { mode = Mode.Shortcuts; }
            SetMode();
        }

        private void UpdateNextButton()
        {
            if (mode == Mode.Install)
            {
                buttonNext.Text = "Install";
            }
            else if (mode == Mode.Repair)
            {
                buttonNext.Text = "Repair";
            }
            else if (mode == Mode.Uninstall)
            {
                buttonNext.Text = "Uninstall";
            }
            else if (mode == Mode.Shortcuts)
            {
                buttonNext.Text = "Create";
            }
        }
        private void buttonNext_Click(object sender, EventArgs e)
        {
            if (mode == Mode.Install)
            {
                bool trying = true;
                while (trying)
                {
                    if (!Shared.ValidateGameDirectory(labelGameDirectoryPath.Text))
                    {
                        DialogResult result = MessageBox.Show("Game directory is invalid or " + Shared.valheimExecutable + " not found.", "Please locate game directory", MessageBoxButtons.OKCancel);
                        if (result == System.Windows.Forms.DialogResult.OK)
                        {
                            string dir = Shared.LocateGameDirectory(folderBrowserDialogGame, labelGameDirectoryPath.Text);
                            if (!string.IsNullOrEmpty(dir))
                                labelGameDirectoryPath.Text = dir;
                        }
                        else
                        {
                            trying = false;
                            return;
                        }
                    }
                    else
                    {
                        trying = false;
                        break;
                    }
                }

                Program.SetupCallback.installDirectory = labelInstallDirectoryPath.Text;
                Program.SetupCallback.gameDirectory = labelGameDirectoryPath.Text;
                Program.SetupCallback.createStartMenuShortcut = checkBoxShortcutStartMenu.Checked;
                Program.SetupCallback.createDesktopShortcut = checkBoxShortcutDesktop.Checked;
                Program.SetupCallback.doInstall = true;
                //proceed
                this.Close();
            }
            else if (mode == Mode.Repair)
            {
                DialogResult result = MessageBox.Show("Repairing will reset all user configurations and mod configuration files.\nAll files will be reinstalled as new.", "Confirm repair", MessageBoxButtons.YesNo);
                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    Program.SetupCallback.doRepair = true;
                    Repair_DownloadNewLauncher();
                    //proceed
                    //this.Close(); we will wait for download before closing, this is therefor handled in downloader.
                }
            }
            else if (mode == Mode.Uninstall)
            {
                DialogResult result = MessageBox.Show("Uninstalling will remove all files and configurations, including mod configuration files.", "Confirm uninstall", MessageBoxButtons.YesNo);
                if (result == System.Windows.Forms.DialogResult.Yes)
                {
                    Program.SetupCallback.doUninstall = true;
                    //proceed
                    this.Close();
                }
            }
            else if (mode == Mode.Shortcuts)
            {
                Program.SetupCallback.createStartMenuShortcut = checkBoxShortcutStartMenu.Checked;
                Program.SetupCallback.createDesktopShortcut = checkBoxShortcutDesktop.Checked;
                Program.SetupCallback.doCreateShortcuts = true;
                //proceed
                this.Close();
            }
        }
        private void checkBoxShortcutStartMenu_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void checkBoxShortCutDesktop_CheckedChanged(object sender, EventArgs e)
        {

        }

        private void labelGameDirectoryPath_Click(object sender, EventArgs e)
        {
            string result = Shared.LocateGameDirectory(folderBrowserDialogGame, labelGameDirectoryPath.Text);
            if (!string.IsNullOrEmpty(result))
                labelGameDirectoryPath.Text = result;
        }

        private void labelInstallDirectoryPath_Click(object sender, EventArgs e)
        {
            LocateInstallDirectory();
        }

        private void labelInstallDirectory_Click(object sender, EventArgs e)
        {
            LocateInstallDirectory();
        }
        private void labelGameDirectory_Click(object sender, EventArgs e)
        {
            string result = Shared.LocateGameDirectory(folderBrowserDialogGame, labelGameDirectoryPath.Text);
            if (!string.IsNullOrEmpty(result))
                labelGameDirectoryPath.Text = result;
        }

        private void radioButtonRepair_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Repair";
            toolTip1.SetToolTip(radioButtonRepair, "Removes all files installed with this app. \nRemoves all configurations saved in registry. \nRemoves all mod configurations. \nDownloads a new launcher and installs everything as new.");

        }

        private void radioButtonUninstall_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Uninstall";
            toolTip1.SetToolTip(radioButtonUninstall, "Removes all files installed with this app. \nRemoves all configurations saved in registry. \nRemoves all mod configurations.");

        }

        private void radioButtonInstall_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Install";
            toolTip1.SetToolTip(radioButtonInstall, "Installs this app into Install Directory, including optional shortcuts.");
        }

        private void radioButtonShortcuts_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Create Shortcuts";
            toolTip1.SetToolTip(radioButtonShortcuts, "Creates new shortcuts.");

        }
        private void labelInstallDirectory_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Install directory";
            toolTip1.SetToolTip(labelInstallDirectory, "Click to change the install location of this app.");
        }
        private void labelInstallDirectoryPath_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.ToolTipTitle = "Install directory";
            toolTip1.SetToolTip(labelInstallDirectoryPath, "Click to change the install location of this app.");
        }


        private void labelGameDirectory_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(labelGameDirectory, "Click to locate where the game is installed.");
            toolTip1.ToolTipTitle = "Game directory";
        }
        private void labelGameDirectoryPath_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(labelGameDirectoryPath, "Click to locate where the game is installed.");
            toolTip1.ToolTipTitle = "Game directory";
        }


        /*
         * 
         * 
         * METHODS
         * 
         * 
         */









        private void SetMode()
        {
            Debug.Log("Setting mode to " + mode.ToString());

            // Suspend layout updates
            this.SuspendLayout();

            if (mode == Mode.Install)
            {

                //position directory labels
                labelInstallDirectory.Location = new Point(labelInstallDirectory.Location.X, radioButtonInstall.Location.Y + radioButtonInstall.Size.Height + MarginY);
                labelInstallDirectoryPath.Location = new Point(labelInstallDirectoryPath.Location.X, radioButtonInstall.Location.Y + radioButtonInstall.Size.Height + MarginY);

                labelGameDirectory.Location = new Point(labelGameDirectory.Location.X, labelInstallDirectory.Location.Y + labelInstallDirectory.Size.Height + MarginY);
                labelGameDirectoryPath.Location = new Point(labelGameDirectoryPath.Location.X, labelInstallDirectory.Location.Y + labelInstallDirectory.Size.Height + MarginY);

                labelInstallDirectory.Visible = true;
                labelInstallDirectoryPath.Visible = true;
                labelGameDirectory.Visible = true;
                labelGameDirectoryPath.Visible = true;

                //position checkboxes
                checkBoxShortcutStartMenu.Location = new Point(checkBoxShortcutStartMenu.Location.X, labelGameDirectory.Location.Y + labelGameDirectory.Size.Height + MarginY);
                checkBoxShortcutDesktop.Location = new Point(checkBoxShortcutStartMenu.Location.X, checkBoxShortcutStartMenu.Location.Y + checkBoxShortcutStartMenu.Size.Height + MarginY);

                checkBoxShortcutStartMenu.Visible = true;
                checkBoxShortcutDesktop.Visible = true;

                //position radio buttons
                radioButtonRepair.Location = new Point(defaultPointRadioButtonRepair.X, checkBoxShortcutDesktop.Location.Y + checkBoxShortcutDesktop.Size.Height + MarginY);
                radioButtonUninstall.Location = new Point(defaultPointradioButtonUninstall.X, radioButtonRepair.Location.Y + radioButtonRepair.Size.Height + MarginY);
                radioButtonShortcuts.Location = new Point(defaultPointradioButtonShortcuts.X, radioButtonUninstall.Location.Y + radioButtonUninstall.Size.Height + MarginY);

                this.BackgroundImage = Properties.Resources.vikingarms;
            }
            else if (mode == Mode.Repair)
            {
                //hide directory labels and checkboxes
                labelInstallDirectory.Visible = false;
                labelInstallDirectoryPath.Visible = false;
                labelGameDirectory.Visible = false;
                labelGameDirectoryPath.Visible = false;
                checkBoxShortcutStartMenu.Visible = false;
                checkBoxShortcutDesktop.Visible = false;

                //position radio buttons
                radioButtonRepair.Location = new Point(defaultPointRadioButtonRepair.X, radioButtonInstall.Location.Y + radioButtonInstall.Size.Height + MarginY);
                radioButtonUninstall.Location = new Point(defaultPointradioButtonUninstall.X, radioButtonRepair.Location.Y + radioButtonRepair.Size.Height + MarginY);
                radioButtonShortcuts.Location = new Point(defaultPointradioButtonShortcuts.X, radioButtonUninstall.Location.Y + radioButtonUninstall.Size.Height + MarginY);

                this.BackgroundImage = Properties.Resources.vikingarms;
            }
            else if (mode == Mode.Uninstall)
            {
                //hide directory labels and checkboxes
                labelInstallDirectory.Visible = false;
                labelInstallDirectoryPath.Visible = false;
                labelGameDirectory.Visible = false;
                labelGameDirectoryPath.Visible = false;
                checkBoxShortcutStartMenu.Visible = false;
                checkBoxShortcutDesktop.Visible = false;

                //position radio buttons
                radioButtonRepair.Location = new Point(defaultPointRadioButtonRepair.X, radioButtonInstall.Location.Y + radioButtonInstall.Size.Height + MarginY);
                radioButtonUninstall.Location = new Point(defaultPointradioButtonUninstall.X, radioButtonRepair.Location.Y + radioButtonRepair.Size.Height + MarginY);
                radioButtonShortcuts.Location = new Point(defaultPointradioButtonShortcuts.X, radioButtonUninstall.Location.Y + radioButtonUninstall.Size.Height + MarginY);


                this.BackgroundImage = Properties.Resources._3646288;
            }
            else if (mode == Mode.Shortcuts)
            {
                //position radio buttons
                radioButtonRepair.Location = new Point(defaultPointRadioButtonRepair.X, radioButtonInstall.Location.Y + radioButtonInstall.Size.Height + MarginY);
                radioButtonUninstall.Location = new Point(defaultPointradioButtonUninstall.X, radioButtonRepair.Location.Y + radioButtonRepair.Size.Height + MarginY);
                radioButtonShortcuts.Location = new Point(defaultPointradioButtonShortcuts.X, radioButtonUninstall.Location.Y + radioButtonUninstall.Size.Height + MarginY);

                labelInstallDirectory.Visible = false;
                labelInstallDirectoryPath.Visible = false;
                labelGameDirectory.Visible = false;
                labelGameDirectoryPath.Visible = false;

                //position checkboxes
                checkBoxShortcutStartMenu.Location = new Point(checkBoxShortcutStartMenu.Location.X, radioButtonShortcuts.Location.Y + radioButtonShortcuts.Size.Height + MarginY);
                checkBoxShortcutDesktop.Location = new Point(checkBoxShortcutStartMenu.Location.X, checkBoxShortcutStartMenu.Location.Y + checkBoxShortcutStartMenu.Size.Height + MarginY);

                checkBoxShortcutStartMenu.Visible = true;
                checkBoxShortcutDesktop.Visible = true;

                this.BackgroundImage = Properties.Resources.vikingarms;
            }
            UpdateNextButton();

            this.ResumeLayout(true);
            this.Refresh();
        }

        private void SetDefaultGameDirectory()
        {
            string directory = Shared.GetSteamDirectory() + Shared.steamValheimInstallDirectory;
            if (Shared.ValidateGameDirectory(directory))
            {
                labelGameDirectoryPath.Text = Path.GetFullPath(directory);
            }
            else
            {
                labelGameDirectoryPath.Text = "Please click here to locate Valheim game folder";
            }
        }
        private string GetDefaultInstallDirectory()
        {
            return Path.Combine(Environment.ExpandEnvironmentVariables("%LOCALAPPDATA%"), defaultInstallDirectoryName);
        }
        private void SetDefaultInstallDirectory()
        {
            string directory = GetDefaultInstallDirectory();
            labelInstallDirectoryPath.Text = Path.GetFullPath(directory);
        }
        private void LocateInstallDirectory()
        {
            folderBrowserDialogInstall.SelectedPath = "";
            if (Directory.Exists(labelInstallDirectoryPath.Text))
                folderBrowserDialogInstall.SelectedPath = labelInstallDirectoryPath.Text;

            if (folderBrowserDialogInstall.ShowDialog() == DialogResult.OK)
            {
                labelInstallDirectoryPath.Text = folderBrowserDialogInstall.SelectedPath;
            }
        }


        private static bool isDownloadAndPrepareNewLauncherForRepair = false;
        private static bool cacheRadioInstallState;
        private static bool cacheRadioUninstallState;
        private static bool cacheRadioShortcutsState;
        private async void Repair_DownloadNewLauncher()
        {
            if (isDownloadAndPrepareNewLauncherForRepair)
                return;
            isDownloadAndPrepareNewLauncherForRepair = true;

            string json = "";
            string signature = "";
            string url = @"https://sunshinealley.games/valheimapi?Exec=LauncherCheckUpdate";
            await Shared.PostData((string result) => Repair_Callback_CheckForUpdate(result), url, json, "launcher:" + Program.machineGuid, signature);
        }

        private async void Repair_Callback_CheckForUpdate(string fileUrl) { 

            using (var httpClient = new HttpClient())
            {
                cacheRadioInstallState = radioButtonInstall.Enabled;
                cacheRadioUninstallState = radioButtonUninstall.Enabled;
                cacheRadioShortcutsState = radioButtonShortcuts.Enabled;
                try
                {
                    // Get the file size to calculate progress
                    long fileSize = await Shared.GetFileSizeAsync(httpClient, fileUrl);

                    // Initialize the progress bar
                    progressBar1.Minimum = 0;
                    progressBar1.Maximum = (int)fileSize;
                    progressBar1.Value = 0;
                    progressBar1.Visible = true;
                    radioButtonInstall.Enabled = false;
                    radioButtonUninstall.Enabled = false;
                    radioButtonShortcuts.Enabled = false;
                    buttonNext.Enabled = false;

                    // since we are repairing, install directory should exist.
                    string destination = Program.GetDownloadExePath();
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                    // Download the file asynchronously and update the progress bar
                    await Shared.DownloadFileAsync((string result) => Repair_Callback_FileDownloaded(result), progressBar1, httpClient, fileUrl, destination);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Repair_RestoreGUI();
                }
            }
        }
        private void Repair_Callback_FileDownloaded(string result)
        {
            //MessageBox.Show("Download complete!", "Success", MessageBoxButtons.OK, MessageBoxIcon.Information);
            

            if (result == "OK")
            {
                this.Close();
                return;
            }
            MessageBox.Show(result, "Failed to download file");
            Repair_RestoreGUI();
        }
        private void Repair_RestoreGUI()
        {
            radioButtonInstall.Enabled = cacheRadioInstallState;
            radioButtonUninstall.Enabled = cacheRadioUninstallState;
            radioButtonShortcuts.Enabled = cacheRadioShortcutsState;
            buttonNext.Enabled = true;
            progressBar1.Visible = false;
            isDownloadAndPrepareNewLauncherForRepair = false;
        }

        





    }













}
