
using IWshRuntimeLibrary;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static SunshineAlley_Launcher.Launcher;
using static System.ComponentModel.Design.ObjectSelectorEditor;
using static System.Net.Mime.MediaTypeNames;
using static System.Windows.Forms.AxHost;
using static System.Windows.Forms.LinkLabel;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;
using Application = System.Windows.Forms.Application;
using File = System.IO.File;
using Formatting = Newtonsoft.Json.Formatting;
using ProgressBar = System.Windows.Forms.ProgressBar;

namespace SunshineAlley_Launcher
{
    public partial class Launcher : Form
    {
        public static bool debug = Program.debug;

        public static bool isGameLaunching = false;
        public static bool isGameRunning = false;
        private int gameProcessID = -1;

        public static Launcher instance;

        public static bool notifySteamBuildVersionInvalid = false;


        public Launcher()
        {
            Debug.Log("Launcher() starting");

            instance = this;
            this.DoubleBuffered = true;
            InitializeComponent();
            this.Text = Program.assemblyName;
            buttonPlay.Enabled = false;
            labelOptionalMods.Visible = false;
            progressBar1.Visible = false;
            labelStatus.Visible = false;
            labelNotify.Visible = false;

            //read persistent config and set state
            checkBoxPersistent.Checked = Config.SVars.Persistent.boolValue;

            //read steam config and set state
            checkBoxSteam.Checked = Config.SVars.UseSteam.boolValue;

            labelGameDirectory.Text = Config.SVars.GameDirectory.stringValue;

            labelStatus.Text = "Click to scan files";
            labelStatus.Enabled = true;


            InitializeRepeaters();
            Debug.Log("Launcher() initialized");

            
        }
        public class GUIInterface
        {
            public static void SetStatusText(string text)
            {
                if (instance.labelStatus.InvokeRequired)
                {
                    instance.labelStatus.Invoke((Action)(() =>
                    {
                        instance.labelStatus.Visible = true;
                        instance.labelStatus.Text = text;
                    }));
                }
                else
                {
                    instance.labelStatus.Visible = true;
                    instance.labelStatus.Text = text;
                }
            }
            public static ProgressBar InitializeProgressBar(int maxValue)
            {
                if (instance.progressBar1.InvokeRequired)
                {
                    instance.progressBar1.Invoke((Action)(() =>
                    {
                        instance.progressBar1.Minimum = 0;
                        instance.progressBar1.Maximum = maxValue;
                        instance.progressBar1.Value = 0;
                        instance.progressBar1.Visible = true;
                    }));
                }
                else
                {
                    instance.progressBar1.Minimum = 0;
                    instance.progressBar1.Maximum = maxValue;
                    instance.progressBar1.Value = 0;
                    instance.progressBar1.Visible = true;
                }

                return instance.progressBar1;
            }
            public static void SetProgressValue(int value)
            {
                if (instance.progressBar1.InvokeRequired)
                {
                    instance.progressBar1.Invoke((Action)(() => instance.progressBar1.Value = value));
                }
                else
                {
                    instance.progressBar1.Value = value;
                }
            }
            public static void HideProgressBar()
            {
                if (instance.progressBar1.InvokeRequired)
                {
                    instance.progressBar1.Invoke((Action)(() => instance.progressBar1.Visible = false));
                }
                else
                {
                    instance.progressBar1.Visible = false;
                }
            }

        }
        public class TaskQueue
        {
            public string Name;
            public static List<TaskQueue> Queue = new List<TaskQueue>();
            public static SemaphoreSlim SemaphoreSlim = new SemaphoreSlim(1, 1);

            public static async Task<TaskQueue> Start(string name)
            {
                //dont accept duplicates
                if (IsQueued(name))
                    return null;

                //add self to queue
                TaskQueue queue = new TaskQueue { Name = name };
                Queue.Add(queue);
                if (debug) Debug.Log($"TaskQueue QUEUE {queue.Name}");

                //wait in queue
                await SemaphoreSlim.WaitAsync();
                if (debug) Debug.Log($"TaskQueue BEGIN {queue.Name}");
                return queue;
            }
            public static bool IsQueued(string name)
            {
                foreach (TaskQueue queued in Queue)
                {
                    if (queued.Name == name)
                        return true;
                }
                return false;
            }
            public void End()
            {
                SemaphoreSlim.Release();
                // Remove self from the queue
                lock (Queue)
                {
                    var itemToRemove = Queue.FirstOrDefault(q => q.Name == this.Name);
                    if (itemToRemove != null)
                    {
                        if (debug) Debug.Log($"TaskQueue END {itemToRemove.Name}");
                        Queue.Remove(itemToRemove);
                    }
                }
            }

        }

        /*
         * 
         * 
         * GUI Controls
         * 
         * 
         */

        private void comboBoxServer_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (debug) Debug.Log("comboBoxServer_SelectedIndexChanged");

            string selected = comboBoxServer.SelectedItem.ToString();
            foreach (GameServer server in GameServer.all)
            {
                if (server.WorldName != selected)
                    continue;
                GameServer.selected = server;
                Config.SVars.SelectedServer.stringValue = selected;
                if (debug) Debug.Log("comboBoxServer_SelectedIndexChanged: " + selected);
            }

            //disable play button until verified
            UpdateButtonPlayState();

            if (GameServer.selected == null)
                return;

            //check file hashes
            if (GameServer.selected.modPack > 0 && GameServer.selected.ModPack.verified == false)
            {
                Mods.VerifyModPack(GameServer.selected.ModPack.modPack);
            }
            //--include frequent refresh

            //update count of optional mods (will update again if file scan).
            Launcher.instance.UpdateLabelOptionalMods();


            //update doorstop.ini if persistent
            if (Config.SVars.Persistent.boolValue == true) CreateDoorstop();
        }
        private void comboBoxServer_KeyPress(object sender, KeyPressEventArgs e)
        {
            e.Handled = true;
        }
        private async void buttonPlay_Click(object sender, EventArgs e)
        {
            if (debug) Debug.Log("buttonPlay_Click");

            //install essentials into game folder, always overwrite (dont overwrite doorstop.ini)
            if (InstallModEssentials(GameServer.selected.modPack) == false)
                return;

            //backup doorstop.ini if not exist
            BackupDoorstop();

            //create new doorstop.ini if not persistent
            if (Config.SVars.Persistent.boolValue == false) CreateDoorstop();

            Config.SVars.ModPackLaunched.stringValue = GameServer.selected.modPack.ToString();

            //start game with or without steam, wait for completion before we restore doorstop.
            await LaunchGame();

            //restore doorstop.ini if not persistent
            if (Config.SVars.Persistent.boolValue == false) RestoreDoorstop();
        }

        private void checkBoxPersistent_CheckedChanged(object sender, EventArgs e)
        {
            updateDoorstop();
        }


        private void checkBoxSteam_CheckedChanged(object sender, EventArgs e)
        {
            if (debug) Debug.Log("checkBoxSteam_CheckedChanged");

            //save state to config
            Config.SVars.UseSteam.boolValue = checkBoxSteam.Checked;
        }

        private void labelOptionalMods_Click(object sender, EventArgs e)
        {
            if (debug) Debug.Log("labelOptionalMods_Click");

            if (GameServer.selected == null)
                return;

            //open optioinal mods GUI
            OptionalMods optionalMods = new OptionalMods(GameServer.selected.ModPack);
            optionalMods.ShowDialog(); // Shows Form2
            Mods.VerifyModPack(GameServer.selected.modPack, resetFailCounter: true);
        }

        private void labelGameDirectory_Click(object sender, EventArgs e)
        {
            if (debug) Debug.Log("labelGameDirectory_Click");

            //browse game directory :: use Shared class to browse, validate and callback
            string dir = Shared.LocateGameDirectory(folderBrowserDialogGame, labelGameDirectory.Text);
            if (!string.IsNullOrEmpty(dir))
            {
                Config.SVars.GameDirectory.stringValue = dir;
                labelGameDirectory.Text = dir;
                updateDoorstop();
            }
        }

        private void labelStatus_Click(object sender, EventArgs e)
        {
            int modPack = 0;
            if (GameServer.selected != null)
            {
                modPack = GameServer.selected.modPack;
            }
            Mods.VerifyModPack(0, resetFailCounter: true);

        }

        private void buttonSetup_Click(object sender, EventArgs e)
        {
            if (debug) Debug.Log("buttonSetup_Click");

            //open SetupGUI
            Setup setup = new Setup();
            setup.ShowDialog(); // Shows Form2

            //do Install() :: will do nothing terminate self if Setup GUI is closed.
            Install install = new Install();
        }


        private void comboBoxServer_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(comboBoxServer, "Select desired launch option.");
            toolTip1.ToolTipTitle = "Launch option";
        }

        private void buttonPlay_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(buttonPlay, "Click to start the game using the selected launch option.");
            toolTip1.ToolTipTitle = "Launch Valheim";
        }

        private void checkBoxPersistent_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(checkBoxPersistent, "Enabling this will redirect your games default startup behaviour to use the selected launch option.");
            toolTip1.ToolTipTitle = "Persistent";
        }

        private void checkBoxSteam_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(checkBoxSteam, "Enabling this will launch the game natively with Steam, enabling Steam features like Steam Overlay");
            toolTip1.ToolTipTitle = "Launch with Steam";
        }

        private void labelGameDirectory_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(labelGameDirectory, "Click to locate where the game is installed.");
            toolTip1.ToolTipTitle = "Game directory";
        }

        private void buttonSetup_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(buttonSetup, "Click to repair or uninstall the launcher.");
            toolTip1.ToolTipTitle = "Setup";
        }

        private void labelOptionalMods_MouseEnter(object sender, EventArgs e)
        {
            toolTip1.SetToolTip(labelOptionalMods, "Click to view and install optional mods.");
            toolTip1.ToolTipTitle = "Optional mods";
        }

        /*
         * 
         * 
         * 
         * REPEATERS
         * 
         * 
         * 
         * 
         */
        public void InitializeRepeaters()
        {

            FastRepeater();
            SlowRepeater();
        }

        public async void FastRepeater()
        {
            while (true)
            {
                await CheckGameProcess();
                await Task.Delay(1000);
            }
        }
        public async void SlowRepeater()
        {
            while (true)
            {
                await RefreshCheckForUpdate();
                await RefreshServerList();
                await RefreshMods();
                await Task.Delay(30000);
            }
        }
        async Task CheckGameProcess()
        {
            //check if game is running
            try
            {
                Process process = Process.GetProcessById(gameProcessID);
                isGameRunning = true;
            }
            catch
            {
                if (Shared.FindSteamRunningAppID() == Shared.steamAppID)
                {
                    isGameRunning = true;
                }
                else
                {
                    isGameRunning = false;
                }
            }

            //update play button
            UpdateButtonPlayState();

            //update persistent button state
            UpdateCheckBoxPersistentState();

            //refresh servers
            await Task.CompletedTask;
        }
        async Task RefreshCheckForUpdate()
        {
            //check for updates
            LauncherUpdate.CheckForUpdate();

            //refresh servers
            await Task.CompletedTask;
        }
        async Task RefreshServerList()
        {
            //refresh server list
            GameServer.Refresh();

            //refresh servers
            await Task.CompletedTask;
        }
        async Task RefreshMods()
        {
            //refresh server list
            if (GameServer.selected != null)
            {
                Mods.VerifyModPack(GameServer.selected.modPack);
            }

            await Task.CompletedTask;
        }

        /*
         * 
         * 
         * METHODS
         * 
         * 
         * 
         */

        //serverside fetch users IP
        //table Devices for machineGuid(index, unique) with public key, MID (Primarykey unique)
        //table DeviceSessions for MID, SessionID, IP, AuthLevel, Connected, LastActivity :: MID,SID,IP unique together.
        //table DeviceAuthorization for MID, PlayerID, Connected :: When game AuthLv2 aquired

        //Device Auth lv0:
        //App send machineGuid, sessionId, public key, signature
        //Server creates Devices record of machineGuid if no exist, update public key
        //Server creates DeviceSessions record or update. AuthLevel=1 :: if DeviceAuthorization exist (last30 days), AuthLevel=2
        //Server returns ServerList (no password) and AuthLevel

        //Game with AuthLv2 sends machineGuid (OneTime only :: includes server jump)
        //Server creates new DeviceAuthorization record

        //Device Authlv1 : Refresh Calls
        //App send machineGuid, sessionId, signature
        //Server gets MID, update DeviceSessions
        //Server returns ServerList (no password) and AuthLevel

        //Device Authlv2 : Refresh Calls identical to lv1.
        //Authlv2 may have future use as player is linked.




        public void RedrawServerListCombo()
        {
            if (debug) Debug.Log("RedrawServerListCombo()");
            SuspendLayout();
            //clear combobox
            comboBoxServer.Invoke((Action)(() => comboBoxServer.Items.Clear()));

            //add all servers
            foreach (GameServer server in GameServer.all)
            {
                comboBoxServer.Invoke((Action)(() => comboBoxServer.Items.Add(server.WorldName)));

            }

            //select selected server
            bool didSelect = false;
            for (int i = 0; i < comboBoxServer.Items.Count; i++)
            {
                if (comboBoxServer.Items[i].ToString() == Config.SVars.SelectedServer.stringValue)
                {
                    comboBoxServer.Invoke((Action)(() => comboBoxServer.SelectedIndex = i));
                    didSelect = true;
                    break;
                }
            }
            if (!didSelect)
                comboBoxServer.Invoke((Action)(() => comboBoxServer.SelectedIndex = 0));

            ResumeLayout(true);
            Refresh();
            if (debug) Debug.Log("Server.RedrawGUI() done");
        }

        private void updateDoorstop()
        {
            bool _checked = checkBoxPersistent.Checked;

            //backup doorstop.ini if not exist
            BackupDoorstop();

            //create new doorstop.ini if persistent
            if (_checked)
            {
                CreateDoorstop();
                Config.SVars.ModPackLaunched.stringValue = GameServer.selected.modPack.ToString();
            }

            //restore doorstop.ini if not persistent
            if (!_checked)
            {
                RestoreDoorstop();
                Config.SVars.ModPackLaunched.stringValue = "-1";
            }


            //save state to config
            Config.SVars.Persistent.boolValue = _checked;
        }





        // DISABLE IF GAME RUNNING
        // EDIT TEXT
        public void UpdateButtonPlayState()
        {
            if (debug) Debug.Log("UpdateButtonPlayState");
            if (GameServer.selected != null
                && (GameServer.selected.modPack < 0 || GameServer.selected.ModPack.verified)
                && isGameLaunching == false
                && isGameRunning == false)
            {
                if (Shared.IsSteamBuildIDOK(gameDirecotory: Config.SVars.GameDirectory.stringValue, serverBuildID: GameServer.selected.SteamBuildID))
                {
                    ChangeButtonPlayState(true);
                    if (notifySteamBuildVersionInvalid)
                    {
                        labelNotify.Text = "";
                        labelNotify.Visible = false;
                        notifySteamBuildVersionInvalid = false;
                    }
                    return;
                }
                else
                {
                    ChangeButtonPlayState(false);
                    labelNotify.Text = "Game version appears to be wrong, please check steam settings.";
                    labelNotify.Visible = true;
                    notifySteamBuildVersionInvalid = true;
                    return;
                }
            }
            ChangeButtonPlayState(false);
        }
        public void ChangeButtonPlayState(bool state)
        {
            if (buttonPlay.Enabled == state)
                return;

            if (buttonPlay.InvokeRequired)
            {
                buttonPlay.Invoke((Action)(() => buttonPlay.Enabled = state));
            }
            else
            {
                buttonPlay.Enabled = state;
            }
        }

        public void UpdateCheckBoxPersistentState()
        {
            if (debug) Debug.Log("UpdateCheckBoxPersistentState");
            if (isGameRunning == false && isGameLaunching == false)
            {
                ChangeCheckBoxPersistentState(true);
            }
            else
            {
                ChangeCheckBoxPersistentState(false);
            }
        }
        public void ChangeCheckBoxPersistentState(bool state)
        {
            if (checkBoxPersistent.Enabled == state)
                return;

            if (checkBoxPersistent.InvokeRequired)
            {
                checkBoxPersistent.Invoke((Action)(() => checkBoxPersistent.Enabled = state));
            }
            else
            {
                checkBoxPersistent.Enabled = state;
            }
        }

        public void NotifyNewOptionalMods()
        {
            labelNotify.Text = "New optional mods discovered. Click the label for optional mods under Play button to view available mods.";
            labelNotify.Visible = true;
        }
        public void UpdateLabelOptionalMods()
        {
            //int all = Mods.OptionalMod.all.Count;
            //int enabled = 0;
            //foreach (Mods.OptionalMod mod in Mods.OptionalMod.all)
            //{
            //    if (mod.enabled) enabled++;
            //}
            //labelOptionalMods.Text = $"{enabled} of {all} optional mods enabled.";
            


            if (GameServer.selected != null && GameServer.selected.modPack > 0 && GameServer.selected.ModPack.verified == true)
            {
                labelOptionalMods.Text = $"{GameServer.selected.ModPack.enabledOptionalMods} of {GameServer.selected.ModPack.availableOptionalMods} optional mods enabled.";
                labelOptionalMods.Visible = true;
            }
            else
            {
                labelOptionalMods.Text = $"optional mods unavailable.";
                labelOptionalMods.Visible = false;
            }
            


        }




        private bool InstallModEssentials(int modPack)
        {
            if (modPack < 0)
                return true;



            //install essentials into game folder, always overwrite (dont overwrite doorstop.ini)
            //do as background task, no freeze.
            string gameDoorstopLib = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.dir_doorstop_libs);
            string installDoorstopLib = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modPack.ToString(), Shared.dir_gameEssentials, Shared.dir_doorstop_libs);

            string gameWinhttp = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_winHttp);
            string installWinhttp = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modPack.ToString(), Shared.dir_gameEssentials, Shared.file_winHttp);


            try
            {
                //copy doorstep lib
                if (Directory.Exists(gameDoorstopLib))
                {
                    Directory.Delete(gameDoorstopLib, true);
                }
                Shared.CopyDirectory(installDoorstopLib, gameDoorstopLib);
            }
            catch
            {
                MessageBox.Show("Unable to deplay DoorstopLibs, game may already be running.");
                return false;
            }

            try
            {
                //copy doorstep lib
                if (File.Exists(gameWinhttp))
                {
                    File.Delete(gameWinhttp);
                }
                File.Copy(installWinhttp, gameWinhttp);
            }
            catch
            {
                MessageBox.Show("Unable to deplay WinHttp, game may already be running.");
                return false;
            }
            return true;
        }

        private async Task LaunchGame()
        {
            if (isGameLaunching)
                return;
            isGameLaunching = true;
            UpdateButtonPlayState();
            UpdateCheckBoxPersistentState();

            try
            {
                Process process = Process.GetProcessById(gameProcessID);
                MessageBox.Show("Game already running\nProcessID: " + gameProcessID.ToString());
                return;
            }
            catch
            {
                //all good
            }

            if (Config.SVars.UseSteam.boolValue)
            {
                await StartWithSteamAsync();
            }
            else
            {
                gameProcessID = StartWithExe();
            }
            //start game with or without steam
            //catch process to prevent duplicate launch
            //isLaunching=true to block duplicate launch

            isGameLaunching = false;
        }

        public static async Task<bool> StartWithSteamAsync()
        {
            int steamRunningAppID = Shared.FindSteamRunningAppID();

            if (steamRunningAppID == Shared.steamAppID)
            {
                MessageBox.Show("Steam reports that Valheim is already running");
                return false;
            }


            string steamExe = Shared.FindSteamExe();
            if (steamExe == null)
            {
                //steam not found.
                if (debug) Debug.Log("steam exe not found");
                return false;
            }
            steamExe = steamExe
                .Replace(@"/", @"\")
                .ToLower();


            Process steam = new Process();

            steam.StartInfo.FileName = steamExe;
            steam.StartInfo.Arguments = "-applaunch " + Shared.steamAppID.ToString() + " -console";
            steam.Start();
            steam.WaitForInputIdle();
            if (Shared.FindSteamRunningAppID() != Shared.steamAppID)
            {
                for (int i = 0; i < 10; i++)
                {
                    await Task.Delay(500);
                    if (Shared.FindSteamRunningAppID() == Shared.steamAppID)
                    {
                        break; //app found, break the wait
                    }
                }
                //if app still not running, then just return as a timeout.
            }

            return true;
        }
        public static int StartWithExe()
        {
            //string exe = Old_Game.folder + @"\" + Shared.valheimExecutable;
            string exe = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.valheimExecutable);

            if (!File.Exists(exe))
            {
                MessageBox.Show("Cannot find the file " + exe, "Cannot find " + Shared.valheimExecutable, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return -1;
            }

            if (!Shared.SteamIsRunning())
            {
                Shared.StartSteam();
            }

            Process valheim = new Process();
            valheim.StartInfo.FileName = exe;
            valheim.StartInfo.Arguments = " -console";
            valheim.Start();
            valheim.WaitForInputIdle();
            return valheim.Id;
        }

        private void BackupDoorstop()
        {
            string doorstop = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_doorstop);
            string doorstopBck = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_doorstopBck);
            if (File.Exists(doorstop) == false)
                return;

            if (File.Exists(doorstopBck) == true)
                return;

            try
            {
                File.Move(doorstop, doorstopBck);
            }
            catch (Exception ex)
            {
                Debug.Log("BackupDoorstop() exception: " + ex.Message);
            }
        }

        private void CreateDoorstop()
        {
            if (debug) Debug.Log("CreateDoorstop() " + GameServer.selected.modPack.ToString());
            //local mods
            if (GameServer.selected.modPack == -1)
            {
                RestoreDoorstop();
            }
            //nomods
            else if (GameServer.selected.modPack == -99)
            {
                CreateNomodsDoorstop();
            }
            //modpack
            else if (GameServer.selected.modPack > 0)
            {
                CreateModPackDoorstop(GameServer.selected.modPack);
            }
        }
        private void RestoreDoorstop()
        {
            //restore by creating default doorstop, ignore the actual backup file
            string file = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_doorstop);
            string[] lines =
                {
              @"# General options for Unity Doorstop"
            , @"[General]"
            , @""
            , @"# Enable Doorstop?"
            , @"enabled = true"
            , @""
            , @"# Path to the assembly to load and execute"
            , @"# NOTE: The entrypoint must be of format `static void Doorstop.Entrypoint.Start()`"
            , @"target_assembly=BepInEx\core\BepInEx.Preloader.dll"
            , @""
            , @"# If true, Unity's output log is redirected to <current folder>\output_log.txt"
            , @"redirect_output_log = false"
            , @""
            , @"# Overrides the default boot.config file path"
            , @"boot_config_override ="
            , @""
            , @"# If enabled, DOORSTOP_DISABLE env var value is ignored"
            , @"# USE THIS ONLY WHEN ASKED TO OR YOU KNOW WHAT THIS MEANS"
            , @"ignore_disable_switch = false"
            , @""
            , @"# Options specific to running under Unity Mono runtime"
            , @"[UnityMono]"
            , @""
            , @"# Overrides default Mono DLL search path"
            , @"# Sometimes it is needed to instruct Mono to seek its assemblies from a different path"
            , @"# (e.g. mscorlib is stripped in original game)"
            , @"# This option causes Mono to seek mscorlib and core libraries from a different folder before Managed"
            , @"# Original Managed folder is added as a secondary folder in the search path"
            , @"# To specify multiple paths, separate them with semicolons (;)"
            , @"dll_search_path_override ="
            , @""
            , @"# If true, Mono debugger server will be enabled"
            , @"debug_enabled = false"
            , @""
            , @"# When debug_enabled is true, specifies the address to use for the debugger server"
            , @"debug_address = 127.0.0.1:10000"
            , @""
            , @"# If true and debug_enabled is true, Mono debugger server will suspend the game execution until a debugger is attached"
            , @"debug_suspend = false"
            };

            File.WriteAllLinesAsync(file, lines);
        }
        private void CreateNomodsDoorstop()
        {
            //restore by creating default doorstop, ignore the actual backup file
            string file = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_doorstop);
            string[] lines =
                {
              @"# General options for Unity Doorstop"
            , @"[General]"
            , @""
            , @"# Enable Doorstop?"
            , @"enabled = false"
            , @""
            , @"# Path to the assembly to load and execute"
            , @"# NOTE: The entrypoint must be of format `static void Doorstop.Entrypoint.Start()`"
            , @"target_assembly=BepInEx\core\BepInEx.Preloader.dll"
            , @""
            , @"# If true, Unity's output log is redirected to <current folder>\output_log.txt"
            , @"redirect_output_log = false"
            , @""
            , @"# Overrides the default boot.config file path"
            , @"boot_config_override ="
            , @""
            , @"# If enabled, DOORSTOP_DISABLE env var value is ignored"
            , @"# USE THIS ONLY WHEN ASKED TO OR YOU KNOW WHAT THIS MEANS"
            , @"ignore_disable_switch = false"
            , @""
            , @"# Options specific to running under Unity Mono runtime"
            , @"[UnityMono]"
            , @""
            , @"# Overrides default Mono DLL search path"
            , @"# Sometimes it is needed to instruct Mono to seek its assemblies from a different path"
            , @"# (e.g. mscorlib is stripped in original game)"
            , @"# This option causes Mono to seek mscorlib and core libraries from a different folder before Managed"
            , @"# Original Managed folder is added as a secondary folder in the search path"
            , @"# To specify multiple paths, separate them with semicolons (;)"
            , @"dll_search_path_override ="
            , @""
            , @"# If true, Mono debugger server will be enabled"
            , @"debug_enabled = false"
            , @""
            , @"# When debug_enabled is true, specifies the address to use for the debugger server"
            , @"debug_address = 127.0.0.1:10000"
            , @""
            , @"# If true and debug_enabled is true, Mono debugger server will suspend the game execution until a debugger is attached"
            , @"debug_suspend = false"
            };
            try
            {
                File.WriteAllLinesAsync(file, lines);
            }
            catch (Exception ex) 
            { 
                Debug.Log("CreateNomodsDoorstop() exception: " + ex.Message); 
            }
            
        }

        private void CreateModPackDoorstop(int modPack)
        {
            //restore by creating default doorstop, ignore the actual backup file
            string file = Path.Combine(Config.SVars.GameDirectory.stringValue, Shared.file_doorstop);
            string[] lines =
                {
              @"# General options for Unity Doorstop"
            , @"[General]"
            , @""
            , @"# Enable Doorstop?"
            , @"enabled = true"
            , @""
            , @"# Path to the assembly to load and execute"
            , @"# NOTE: The entrypoint must be of format `static void Doorstop.Entrypoint.Start()`"
            , @"target_assembly="+Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modPack.ToString(), Shared.dir_preloader)
            , @""
            , @"# If true, Unity's output log is redirected to <current folder>\output_log.txt"
            , @"redirect_output_log = false"
            , @""
            , @"# Overrides the default boot.config file path"
            , @"boot_config_override ="
            , @""
            , @"# If enabled, DOORSTOP_DISABLE env var value is ignored"
            , @"# USE THIS ONLY WHEN ASKED TO OR YOU KNOW WHAT THIS MEANS"
            , @"ignore_disable_switch = false"
            , @""
            , @"# Options specific to running under Unity Mono runtime"
            , @"[UnityMono]"
            , @""
            , @"# Overrides default Mono DLL search path"
            , @"# Sometimes it is needed to instruct Mono to seek its assemblies from a different path"
            , @"# (e.g. mscorlib is stripped in original game)"
            , @"# This option causes Mono to seek mscorlib and core libraries from a different folder before Managed"
            , @"# Original Managed folder is added as a secondary folder in the search path"
            , @"# To specify multiple paths, separate them with semicolons (;)"
            , @"dll_search_path_override ="
            , @""
            , @"# If true, Mono debugger server will be enabled"
            , @"debug_enabled = false"
            , @""
            , @"# When debug_enabled is true, specifies the address to use for the debugger server"
            , @"debug_address = 127.0.0.1:10000"
            , @""
            , @"# If true and debug_enabled is true, Mono debugger server will suspend the game execution until a debugger is attached"
            , @"debug_suspend = false"
            };
            try
            {
                File.WriteAllLinesAsync(file, lines);
            }
            catch (Exception ex)
            {
                Debug.Log("CreateModPackDoorstop() exception: " + ex.Message);
            }
        }

        private void InstallDefaultConfigIfMissing(int modPack)
        {
            if (modPack < 1)
                return;

            string sourceFolder = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modPack.ToString(), Shared.dir_gameEssentials, Shared.dir_defaultConfigs);
            string destinationFolder = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modPack.ToString(), Shared.dir_bepinex, Shared.dir_config);
            string[] filesArray = Directory.GetFiles(sourceFolder, "*.txt", SearchOption.TopDirectoryOnly);
            foreach (string sourceFile in filesArray)
            {
                string fileName = Path.GetFileName(sourceFile);
                //skip last 4 characters of filename (.txt) on copy:
                string destFile = Path.Combine(destinationFolder, fileName[..^4]);
                if (File.Exists(destFile) == false)
                {
                    try
                    {
                        File.Copy(sourceFile, destFile, true);
                    }
                    catch (Exception ex)
                    {
                        Debug.Log("InstallDefaultConfigIfMissing() exception: " + ex.Message);
                    }
                }
            }
        }

        private void label2_Click(object sender, EventArgs e)
        {
            if (GameServer.selected == null)
                return;

            string BepInExfolder = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, GameServer.selected.modPack.ToString(), Shared.dir_bepinex);
            
            // Check if the folder exists before attempting to open it
            if (System.IO.Directory.Exists(BepInExfolder))
            {
                // Use Process.Start to open the folder in File Explorer
                Process.Start("explorer.exe", BepInExfolder);
            }
            else
            {

            }
            {
                Console.WriteLine("Folder does not exist.");
            }

        }
    }
}
