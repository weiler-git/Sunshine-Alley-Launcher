using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using static SunshineAlley_Launcher.Launcher;

namespace SunshineAlley_Launcher
{
    public class LauncherUpdate
    {
        public Launcher.TaskQueue TaskQueue;

        public static bool debug = Program.debug;



        public static void CheckForUpdate()
        {
            new LauncherUpdate().CheckHash();
        }
        public async void CheckHash()
        {
            TaskQueue = await Launcher.TaskQueue.Start("CheckForUpdate");
            if (TaskQueue == null)
                return;

            if (debug) Debug.Log("CheckForUpdate()");
            Launcher.GUIInterface.SetStatusText("Checking for updates..");
            
            
            string hash = "";
            await Task.Run(() =>
            {
                hash = Shared.HashExecutable();
            });
            
            
            string json = JsonConvert.SerializeObject(hash, Formatting.None);
            string signature = "";
            string url = Shared.webURL + "/valheimapi?Exec=LauncherCheckUpdate";
#if DEBUG
            await Task.Delay(300);
            Launcher.GUIInterface.SetStatusText("(debug) Checking for updates..");
            await Task.Delay(300);
            Launcher.GUIInterface.SetStatusText("(debug) Checking for updates.. fake delay");
            await Task.Delay(300);
            Launcher.GUIInterface.SetStatusText("(debug) Checking for updates..");
            await Task.Delay(300);
            Launcher.GUIInterface.SetStatusText("(debug) Checking for updates.. fake delay");
            await Task.Delay(300);
            Launcher.GUIInterface.SetStatusText("(debug) Checking for updates..");
            await Task.Delay(200);
#endif

            await Shared.PostData((string result) => Callback_CheckHash(result), url, json, "launcher:" + Program.machineGuid, signature);
            
            if (debug) Debug.Log("CheckForUpdate() done!");
        }
        private async void Callback_CheckHash(string result)
        {
            if (debug) Debug.Log("Callback_CheckForUpdate()");

            if (result.ToLower().StartsWith("error") || string.IsNullOrWhiteSpace(result))
            {
                Launcher.GUIInterface.SetStatusText("Error checking for update");
                Launcher.GUIInterface.HideProgressBar();
                //make a short pause to let user see error before it proceeds to next task
                await Task.Delay(2500);
                TaskQueue.End();
                return;
            }

            
            //result = "OK"; //temp override to not update
            
            //OK || URL
            if (result == "OK")
            {
                //we good
                Launcher.GUIInterface.SetStatusText("No launcher updates found");
                Launcher.GUIInterface.HideProgressBar();
                TaskQueue.End();
                return;
            }

#if DEBUG
            Launcher.GUIInterface.SetStatusText("Not updating in debug mode");
            Launcher.GUIInterface.HideProgressBar();
            TaskQueue.End();
            return;
#else
            //do update from URL result unless we are in debugmode
            DownloadUpdate(result);
#endif
        }
        private async void DownloadUpdate(string fileUrl)
        {
            if (debug) Debug.Log("DownloadUpdate()");

            using (var httpClient = new HttpClient())
            {
                try
                {
                    // Get the file size to calculate progress
                    long fileSize = await Shared.GetFileSizeAsync(httpClient, fileUrl);

                    ProgressBar progressBar = Launcher.GUIInterface.InitializeProgressBar((int)fileSize);
                    Launcher.GUIInterface.SetStatusText("Downloading update..");

                    // since we are repairing, install directory should exist.
                    string destination = Program.GetDownloadExePath();
                    if (File.Exists(destination))
                    {
                        File.Delete(destination);
                    }
                    // Download the file asynchronously and update the progress bar
                    await Shared.DownloadFileAsync((string result) => Callback_DownloadUpdate(result), progressBar, httpClient, fileUrl, destination);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error: {ex.Message}", "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                    Launcher.GUIInterface.SetStatusText("Download error..");
                    Launcher.GUIInterface.HideProgressBar();
                    TaskQueue.End();
                }
            }
            if (debug) Debug.Log("DownloadUpdate() done");
        }
        private void Callback_DownloadUpdate(string result)
        {
            if (debug) Debug.Log("Callback_DownloadUpdate()");
            Launcher.GUIInterface.SetStatusText("Download complete!");
            Shared.InstallUpdate();
            //after install, this app will close. But we end the task regardless.
            Launcher.GUIInterface.HideProgressBar();
            TaskQueue.End();
        }
    }
}
