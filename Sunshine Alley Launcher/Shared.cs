using Microsoft.VisualBasic.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SunshineAlley_Launcher
{
    public class Shared
    {
        public static bool debug = Program.debug;
        public static int AuthLevel = 0;
        public static bool IsAdmin = false;

        private const string steamSubkey = @"SOFTWARE\Valve\Steam";
        private const string steamKeyDirectory = "SteamPath";
        private const string steamKeyExe = "SteamExe";
        private const string steamKeyRunningAppID = "RunningAppID";
        public const string steamValheimInstallDirectory = @"\steamapps\common\Valheim";
        public const string valheimExecutable = "valheim.exe";
        public static int steamAppID = 892970;
        public static int SteamBuildID = 0;
        public const string webURL = "https://sunshinealley.games";

        public const string file_doorstop = "doorstop_config.ini";
        public const string file_doorstopBck = "doorstop_config.ini.bck";
        public const string dir_doorstop_libs = "doorstop_libs";
        public const string file_winHttp = "winhttp.dll";
        public const string dir_modpacks = "ModPacks";
        public const string dir_gameEssentials = "GameEssentials";
        public const string dir_defaultConfigs = "defaultconfig";
        public const string dir_bepinex = "bepinex";
        public const string dir_config = "config";
        public const string dir_unstripped_corlib = "unstripped_corlib";
        public const string dir_preloader = @"BepInEx\core\BepInEx.Preloader.dll";

        //public const string launcherDownloadURL = @"https://sunshinealley.games/ModPacks/Sunshine Alley Launcher.exe";

        /*
         * 
         * 
         * GAME/STEAM METHODS
         * 
         * 
         */
        public static string LocateGameDirectory(FolderBrowserDialog folderBrowserDialog, string current = "")
        {
            folderBrowserDialog.SelectedPath = "";
            if (Directory.Exists(current))
                folderBrowserDialog.SelectedPath = current;

            if (folderBrowserDialog.ShowDialog() == DialogResult.OK)
            {
                if (ValidateGameDirectory(folderBrowserDialog.SelectedPath))
                {
                    return folderBrowserDialog.SelectedPath;
                }
                else
                {
                    MessageBox.Show("Cannot find valheim.exe in the selected folder.", "valheim.exe not found!", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
            }
            return "";
        }
        public static bool ValidateGameDirectory(string directory)
        {
            if (!Directory.Exists(directory)) return false;
            if (!File.Exists(directory + @"\" + valheimExecutable)) return false;
            return true;
        }
        public static string GetSteamDirectory()
        {
            string steamFolder = "";
            RegistryKey key = Registry.CurrentUser.CreateSubKey(steamSubkey);
            if (!(key.GetValue(steamKeyDirectory) == null))
            {
                steamFolder = (string)key.GetValue(steamKeyDirectory);
            }
            key.Close();
            return steamFolder;
        }

        public static string FindSteamExe()
        {
            if(debug) Debug.Log("StartSteam()");
            string steamExe = null;

            RegistryKey key = Registry.CurrentUser.CreateSubKey(steamSubkey);
            if (!(key.GetValue(steamKeyExe) == null))
            {
                steamExe = (string)key.GetValue(steamKeyExe);
            }

            if (steamExe == null)
            {
                //steam not found.
                if (debug) Debug.Log("steam exe not found");
            }
            return steamExe;
        }
        public static int FindSteamRunningAppID()
        {
            int steamRunningAppID = 0;
            RegistryKey key = Registry.CurrentUser.CreateSubKey(steamSubkey);
            if (!(key.GetValue(steamKeyRunningAppID) == null))
            {
                steamRunningAppID = (int)key.GetValue(steamKeyRunningAppID);
            }
            key.Close();
            return steamRunningAppID;
        }
        public static void StartSteam()
        {
            if (debug) Debug.Log("StartSteam()");
            string steamExe = FindSteamExe();
            if (steamExe == null)
            {
                //steam not found.
                if (debug) Debug.Log("steam exe not found");
                return;
            }

            try
            {
                Process steam = new Process();
                steam.StartInfo.FileName = steamExe;
                steam.Start();
                steam.WaitForInputIdle();
            }
            catch
            {
                //start steam failed.
                if (debug) Debug.Log("failed to start steam exe");
            }
        }
        public static bool SteamIsRunning()
        {
            if (debug) Debug.Log("SteamIsRunning()");
            string steamExe = FindSteamExe();

            if (steamExe == null)
                return false;

            steamExe = steamExe
                .Replace(@"/", @"\")
                .ToLower();
            bool foundSteam = false;
            // Get all processes running on the local computer.
            Process[] localAll = Process.GetProcesses();
            foreach (var process in localAll)
            {
                try
                {
                    if (process.MainModule.FileName.ToLower() == steamExe)
                    {
                        foundSteam = true;
                        break;
                    }
                }
                catch
                {
                    //process without filename, ignore and continue..
                }
            }

            if (foundSteam)
            {
                if (debug) Debug.Log("found process steam exe");
                return true;
            }

            //steam not found.
            if (debug) Debug.Log("process steam exe not found");
            return false;
        }
        public static bool IsSteamBuildIDOK(string gameDirecotory, int serverBuildID,  bool forceUpdateMyBuildID = false)
        {
            // This check is for convenience, not security
            // If game directory is outside steam folder, or if something else fails when aquiring buildID, this should just auto approve.


            //bypass to always check for now, maybe we use the cahced value later..
            forceUpdateMyBuildID = true;

            //parse file first time or if forced
            if (SteamBuildID == 0 || forceUpdateMyBuildID)
            {
                SteamBuildID = GetSteamBuildID(gameDirecotory);
            }

            //auto approve if server buildID is unknown
            if (serverBuildID == 0) return true;
            if (debug) Debug.Log("Server Build ID: " + serverBuildID);

            //auto approve failed check
            if (SteamBuildID < 0) return true;

            //check if ID aquired:
            if (SteamBuildID == serverBuildID) return true;
            return false;
        }
        public static int GetSteamBuildID(string path) //path: C:\Program Files (x86)\Steam\steamapps\common\Valheim
        {
            path = Path.GetDirectoryName(path); //C:\Program Files (x86)\Steam\steamapps\common
            if (Path.GetFileName(path).ToLower() != "common")
            {
                if (debug) Debug.Log(@"Steam\steamapps\common not found");
                return -1;
            }
            path = Path.GetDirectoryName(path); //C:\Program Files (x86)\Steam\steamapps
            if (Path.GetFileName(path).ToLower() != "steamapps")
            {
                if (debug) Debug.Log(@"Steam\steamapps not found");
                return -2;
            }

            path = Path.Combine(path, "appmanifest_" + steamAppID + ".acf");
            if (!File.Exists(path))
            {
                if (debug) Debug.Log("appmanifest_" + steamAppID + ".acf not found in the file.");
                return -3;
            }

            // Read the content of the file
            string filePath = path; // Replace with the actual file path
            string fileContent = File.ReadAllText(filePath);

            // Extract the value of "buildid" using a regular expression
            string buildIdPattern = "\"buildid\"\\s+\"([^\"]+)\"";
            Match match = Regex.Match(fileContent, buildIdPattern);
            if (!match.Success) 
            {
                if (debug) Debug.Log("Build ID not found in the file.");
                return -4;
            }

            // Extract the captured value
            string buildIdValue = match.Groups[1].Value;
            if (debug) Debug.Log("Build ID: " + buildIdValue);

            int buildId = -10;
            _ = int.TryParse(buildIdValue, out buildId);

            return buildId;
        }








        public static bool InstallUpdate()
        {
            //Get updated/downloaded executable
            string executable = Program.GetDownloadExePath();
            if (File.Exists(executable) == false)
            {
                if (debug) Debug.Log("New executable not found!");
                return false;
            }


            //move executable to temp file :: should only happen if Setup() is manually started from launcher
            if (Program.IsRunningFromInstallExePath())
            {
                string temp = NameTemporaryExecutabel(Config.SVars.InstallDirectory.stringValue, Program.assemblyName);
                if (MoveExecutable(Program.GetThisExecutable(), temp) == false) return false;
            }

            try
            {
                File.Move(executable, Program.GetInstallExePath());
                if (debug) Debug.Log("Moved new executable");
            }
            catch
            {
                if (debug) Debug.Log("Unable to move new executable");
                MessageBox.Show("Failed to install "+Program.assemblyName+".exe during update\nYour installation may be corrupted.");
                return false;
            }

            //start new executable
            RunExecutable(Program.GetInstallExePath());

            //close this executable
            CloseThisExecutable();

            return true;
        }


        /*
         * 
         * 
         * 
         * EXECUTABLE METHODS
         * 
         * 
         * 
         */
        public static bool CopyExecutable(string source, string destination)
        {
            if (!File.Exists(source))
            {
                MessageBox.Show("Source file not found");
                return false;
            }
            if (File.Exists(destination))
            {
                string temp = NameTemporaryExecutabel(Config.SVars.InstallDirectory.stringValue, Program.assemblyName);
                try
                {
                    File.Move(destination, temp);
                }
                catch
                {
                    MessageBox.Show("File already exist, already running or otherwise unable to move.");
                    return false;
                }
                try
                {
                    File.Delete(temp);
                }
                catch
                {
                    MessageBox.Show("File already exist, already running or otherwise unable to delete.");
                    return false;
                }
                if (debug) Debug.Log("Existing file moved and deleted.");
            }
            try
            {
                File.Copy(source, destination, true);
                if (debug) Debug.Log("Executable copied to install directory.");
            }
            catch
            {
                MessageBox.Show("Unable to install executable at target location.");
                return false;
            }
            return true;
        }
        public static bool MoveExecutable(string source, string destination)
        {
            try
            {
                File.Move(source, destination);
            }
            catch
            {
                MessageBox.Show("Unable to move executable.");
                return false;
            }
            if (source == Program.GetThisExecutable())
            {
                Program.RegisterMovedThisExecutable(destination);
            }
            return true;
        }
        public static void RunExecutable(string executable, string arg = "")
        {
            if (debug) Debug.Log("Starting executable from new location.");

            // Concatenate arguments with space as the separator
            string combinedArguments;
            if (debug)
            {
                combinedArguments = string.Join(" ", arg, "debug");
            }
            else
            {
                combinedArguments = arg;
            }

            try
            {
                Process launcher = new Process();
                launcher.StartInfo.FileName = executable;
                launcher.StartInfo.Arguments = combinedArguments;
                launcher.Start();
            }
            catch
            {
                if (debug) Debug.Log("Unable to start executable: " + executable);
                MessageBox.Show("Unable to start application \n" + executable);
            }

        }
        public static void CloseThisExecutable()
        {
            if (debug) Debug.Log("Shutting down this executable.");
            Application.Exit();
            //System.Environment.Exit(1);
        }

        public static string NameTemporaryExecutabel(string directory, string name)
        {
            int i = 0;
            while (true)
            {
                i++;
                Guid guid = Guid.NewGuid();
                string shortGuid = guid.ToString("N").Substring(0, 8);
                string uniqueId = shortGuid + DateTime.Now.Ticks.ToString().Substring(0, 2);
                string destination = Path.Combine(directory, name + ".temp." + uniqueId + ".exe");
                if (!File.Exists(destination))
                {
                    return destination;
                }
                if (i > 1000) { break; }
            }
            return null;
        }

        public static string HashExecutable()
        {
            string fileLocation = Program.GetThisExecutable();
            string hashString = "";
            if (File.Exists(fileLocation))
            {
                byte[] assemblyByte = File.ReadAllBytes(fileLocation);
                byte[] hash = RsaEncryptionService.ComputeSHA256Hash(assemblyByte);
                hashString = BitConverter.ToString(hash).Replace("-", "").ToLower();
                if (debug) Debug.Log(fileLocation + ": " + hashString);
            }
            return hashString;
        }

        public static string AlphanumericsAndHyphen(string str)
        {
            char[] arr = str.ToCharArray();

            for (int i = 0; i < arr.Length; i++)
            {
                // Replace spaces with hyphens
                if (char.IsWhiteSpace(arr[i]))
                {
                    arr[i] = '-';
                }
                else if (!(char.IsLetterOrDigit(arr[i]) || arr[i] == '-'))
                {
                    // Remove characters that are not letters, digits, or hyphens
                    arr[i] = ' ';
                }
            }

            str = new string(arr);
            // Remove consecutive hyphens and trim leading/trailing hyphens
            str = System.Text.RegularExpressions.Regex.Replace(str, @"-+", "-").Trim('-');

            return str;
        }

        public static void CopyDirectory(string sourceDir, string destDir)
        {
            // Create the destination directory if it doesn't exist
            if (!Directory.Exists(destDir))
            {
                Directory.CreateDirectory(destDir);
            }

            // Copy files
            foreach (string filePath in Directory.GetFiles(sourceDir))
            {
                string destFilePath = Path.Combine(destDir, Path.GetFileName(filePath));
                File.Copy(filePath, destFilePath, true); // Set the last parameter to true to overwrite existing files
            }

            // Recursively copy subdirectories
            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }



        public static async Task PostData(Action<string> callback, string apiUrl, string jsonData, string sender, string signature)
        {
            
            Dictionary<string, string> formData = new Dictionary<string, string>
            {
                { "jsonData", jsonData },
                { "sender", sender },
                { "signature", signature },
            };

            string result = "";

            try
            {
               

                using (HttpClient httpClient = new HttpClient())
                {
                    // Set a timeout of 30 seconds
                    httpClient.Timeout = TimeSpan.FromSeconds(30);

                    using (HttpContent content = new FormUrlEncodedContent(formData))
                    {
                        try
                        {
                            HttpResponseMessage response = await httpClient.PostAsync(apiUrl, content);

                            if (response.IsSuccessStatusCode)
                            {
                                result = await response.Content.ReadAsStringAsync();
                                if (debug) Debug.Log($"Success! Response: {result}");
                            }
                            else
                            {
                                if (debug) Debug.Log($"PostData Error: {response.StatusCode} - {response.ReasonPhrase}");
                                result = await response.Content.ReadAsStringAsync();
                                if (debug) Debug.Log($"{result}");
                            }
                        }
                        catch (TaskCanceledException ex)
                        {
                            // Handle timeout exception
                            if (debug) Debug.Log($"Request timed out: {ex.Message}");
                        }
                    }
                }
                
            }
            catch (Exception ex)
            {
                if (debug) Debug.Log($"PostData Error: {ex}");
            }

            callback(result);

        }

        public static async Task<long> GetFileSizeAsync(HttpClient httpClient, string fileUrl)
        {
            using (var request = new HttpRequestMessage(HttpMethod.Head, fileUrl))
            using (var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead))
            {
                response.EnsureSuccessStatusCode();
                return response.Content.Headers.ContentLength ?? 0;
            }

            //using (var response = await httpClient.HeadAsync(fileUrl))
            //{
            //    response.EnsureSuccessStatusCode();
            //    return response.Content.Headers.ContentLength ?? 0;
            //}
        }

        public static async Task DownloadFileAsync(Action<string> callback, ProgressBar progressBar, HttpClient httpClient, string fileUrl, string destination)
        {
            using (var response = await httpClient.GetAsync(fileUrl, HttpCompletionOption.ResponseHeadersRead))
            {
                try
                {
                    response.EnsureSuccessStatusCode();
                    // Continue with download
                }
                catch (HttpRequestException ex)
                {
                    // Handle the exception (e.g., log, notify user, etc.)
                    callback($"Error: {ex.Message}");
                    return;
                }

                using (var fileStream = new FileStream(destination, FileMode.Create, FileAccess.Write))
                using (var stream = await response.Content.ReadAsStreamAsync())
                {
                    byte[] buffer = new byte[8192];
                    int bytesRead;
                    long totalBytesRead = 0;


                    while ((bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        try
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                        }
                        catch (Exception ex)
                        {
                            // Handle the exception (e.g., log, notify user, etc.)
                            callback($"Error writing to file: {ex.Message}");
                            return;
                        }

                        totalBytesRead += bytesRead;
                        if (progressBar.InvokeRequired)
                        {
                            progressBar.Invoke((Action)(() => progressBar.Value = (int)totalBytesRead));
                        }
                        else
                        {
                            progressBar.Value = (int)totalBytesRead;
                        }


                        if (debug) Debug.Log("DownloadFileAsync: " + progressBar.Value);
                    }
                    if (debug) Debug.Log("DownloadFileAsync while wend");
                }
                if (debug) Debug.Log("DownloadFileAsync ReadAsStreamAsync() end");
            }
            if (debug) Debug.Log("DownloadFileAsync GetAsync() end");

            callback("OK");
        }
    }
}
