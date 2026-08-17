using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Security.Cryptography;
using System.Runtime.CompilerServices;
using static SunshineAlley_Launcher.Launcher;
using static SunshineAlley_Launcher.Setup;

namespace SunshineAlley_Launcher
{
    public class Mods
    {
        public static bool debug = Program.debug;
        public static int RepeatFailure = 0;
        public class OptionalMod
        {
            public string name;
            public bool enabled;
            public static List<OptionalMod> all = new List<OptionalMod>();
            public static OptionalMod Add(string name)
            {
                foreach (OptionalMod mods in all)
                {
                    if (mods.name == name)
                    {
                        return mods;
                    }
                }
                OptionalMod mod = new OptionalMod { name = name };
                all.Add(mod);
                return mod;
            }
        }
        public static void VerifyModPack(int modPack, bool resetFailCounter = false)
        {
            if (resetFailCounter)
            {
                Mods.RepeatFailure = 0;
            }

            if (modPack > 0 && !Launcher.isGameLaunching && !Launcher.isGameRunning)
            {
                ModPack.PackNotVerified(modPack);
                API_VerifyMods.Execute(modPack);
            }
            
        }
        public static void InstallDefaultConfigIfMissing(int modPack)
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
                    File.Copy(sourceFile, destFile, true);
                }
            }
        }

        private class API_VerifyMods
        {
            public bool IsAdmin;
            public int ModPack;
            public List<FileHash> Files;
            public class FileHash
            {
                public string FilePath;
                public string Hash;
                public string Extenstion;
            }
            
            // Exclude this field from JSON serialization
            [JsonIgnore]
            public Launcher.TaskQueue TaskQueue;

            
            //public static List<string> ManagedExtensions = new List<string> { ".dll", ".mp3", ".png", ".yml", ".md", ".json" };
            public static async void Execute(int modpack)
            {
                if (debug) Debug.Log("VerifyModPack(" + modpack + ")");
                //int newProcessID = Launcher.TaskQueue.StartProcess("VerifyModPack" + modpack.ToString());
                //if (newProcessID == 0)
                //{
                //    if (debug) Debug.Log("Unable to start process");
                //    return;
                //}
                //processID = newProcessID;

                API_VerifyMods request = new API_VerifyMods();
                request.TaskQueue = await Launcher.TaskQueue.Start("VerifyModPack[" + modpack + "]");
                if (request.TaskQueue == null)
                    return;


                if(RepeatFailure>5)
                {
                    Launcher.GUIInterface.SetStatusText("Filescan failed too many times.");
                    request.TaskQueue.End();
                    return;
                }


                request.ModPack = modpack;
                request.IsAdmin = Shared.IsAdmin;

                await Task.Run(() =>
                {
                    request.Files = HashFiles(modpack.ToString());
                });


                //send signed hashlist to server
                string json = JsonConvert.SerializeObject(request, Formatting.None);
                string signature = RsaEncryptionService.SignMessage(json, Program.rsa);
                string url = Shared.webURL + "/valheimapi?Exec=LauncherVerifyFiles";
                if (debug) await Task.Delay(200);
                await Shared.PostData((string result) => API_FileValidationCallback.Callback_VerifyModPack(result, request.TaskQueue), url, json, "launcher:" + Program.machineGuid, signature);
                if (debug) Debug.Log("VerifyModPack(" + modpack + ") done");
            }
            private static List<FileHash> HashFiles(string modpack)
            {
                List<FileHash> fileList = new List<FileHash>();

                string directory = Path.Combine(Config.SVars.InstallDirectory.stringValue, Shared.dir_modpacks, modpack);

                if (Directory.Exists(directory))
                {
                    // Use Directory.GetFiles to get an array of files and subfiles
                    string[] filesArray = Directory.GetFiles(directory, "*.*", SearchOption.AllDirectories);

                    // Check if there are any files
                    if (filesArray.Length > 0)
                    {
                        ProgressBar progressBar = Launcher.GUIInterface.InitializeProgressBar(filesArray.Length);
                        Launcher.GUIInterface.SetStatusText("Scanning files..");


                        int i = 0;
                        foreach (string filePath in filesArray)
                        {
                            i++;
                            Launcher.GUIInterface.SetProgressValue(i);

                            if (debug) Debug.Log("filecheck:" + filePath);
                            string extension = Path.GetExtension(filePath);
                            fileList.Add(new API_VerifyMods.FileHash { FilePath = filePath, Hash = ComputeFileHash(filePath), Extenstion = Path.GetExtension(filePath) });
                            if (debug) Debug.Log("Hashed: " + filePath);

                        }
                    }
                }



                return fileList;

            }
            static string ComputeFileHash_(string filePath)
            {
                byte[] fileBytes = File.ReadAllBytes(filePath);
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(fileBytes);
                    return BitConverter.ToString(hash).Replace("-", "").ToLower();
                }
            }
            static string ComputeFileHash(string filePath)
            {
                try
                {
                    using (FileStream stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                    {
                        using (SHA256 sha256 = SHA256.Create())
                        {
                            byte[] hash = sha256.ComputeHash(stream);
                            return BitConverter.ToString(hash).Replace("-", "").ToLower();
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Handle or log the exception
                    if (debug) Debug.Log($"Error computing hash for {filePath}: {ex.Message}");
                    return null; // Or another appropriate value
                }
            }
        }
        
        private class API_FileValidationCallback
        {
            //public string rootURL; //http://sunshinealley.games/modpacks/1/
            public int modPack;
            public List<LocalFile> Illegal;
            public List<ServerFile> Missing;
            public List<ServerFile> Optional;

            // Exclude this field from JSON serialization
            [JsonIgnore]
            Launcher.TaskQueue TaskQueue;
            public bool DownloadFailed = false;

            public class LocalFile
            {
                public string File;
            }
            public class ServerFile
            {
                //public string RelativePath;
                public string DownloadUrl;
                public string ModName;
                public bool OK;
            }
            public static void Callback_VerifyModPack(string result, Launcher.TaskQueue taskQueue)
            {
                if (debug) Debug.Log("Callback_VerifyModPack()");
                if (result.ToLower().StartsWith("error") || string.IsNullOrWhiteSpace(result))
                {
                    Launcher.GUIInterface.SetStatusText("Error receiving mod data from server.");
                    Launcher.GUIInterface.HideProgressBar();
                    taskQueue.End();
                    return;
                }
                API_FileValidationCallback response = JsonConvert.DeserializeObject<API_FileValidationCallback>(result);
                response.TaskQueue = taskQueue;
                response.DeleteAndDownloadMods();
            }


            public async void DeleteAndDownloadMods()
            {
                //confirm pack as verified when no illegals or missing required
                if (Illegal.Count == 0 && Missing.Count == 0)
                {
                    ModPack.PackVerified(modPack);
                    RepeatFailure = 0;
                }
                else
                {
                    RepeatFailure++;
                    if (Launcher.isGameRunning == true || Launcher.isGameLaunching == true)
                    {
                        Launcher.GUIInterface.SetStatusText("Filedownload aborted, game is running.");
                        Launcher.GUIInterface.HideProgressBar();
                        TaskQueue.End();
                        return;
                    }
                }

                string rootURL = Shared.webURL;

                //delete files
                await Task.Run(() =>
                {
                    foreach (LocalFile file in Illegal)
                    {
                        try
                        {
                            File.Delete(file.File);
                            if (debug) Debug.Log("Deleted: " + file.File);
                        }
                        catch
                        {
                            if (debug) Debug.Log("Unable to delete: " + file.File);
                        }
                    }
                });

                //download required files
                foreach (ServerFile file in Missing)
                {
                    if (DownloadFailed)
                    {
                        Launcher.GUIInterface.SetStatusText("Error downloading mod.");
                        Launcher.GUIInterface.HideProgressBar();
                        TaskQueue.End();
                        return;
                    }
                    using (var httpClient = new HttpClient())
                    {
                        try
                        {
                            // Get the file size to calculate progress
                            long fileSize = await Shared.GetFileSizeAsync(httpClient, file.DownloadUrl);
                            ProgressBar progressBar = Launcher.GUIInterface.InitializeProgressBar((int)fileSize);
                            
                            if (file.DownloadUrl.StartsWith(rootURL) == false)
                                continue;

                            string relPath = file.DownloadUrl.Substring(rootURL.Length + 1).Replace("/", "\\");
                            string path = Path.Combine(Config.SVars.InstallDirectory.stringValue, relPath);
                            if (File.Exists(path))
                            {
                                File.Delete(path);
                            }
                            // Download the file asynchronously and update the progress bar
                            string directory = Path.GetDirectoryName(path);
                            Directory.CreateDirectory(directory);
                            if (debug) Debug.Log("Downloading: " + file.DownloadUrl);
                            Launcher.GUIInterface.SetStatusText("Downloading: " + Path.GetFileName(path));
                            await Shared.DownloadFileAsync((string result) => Callback_DownloadFile(result), progressBar, httpClient, file.DownloadUrl, path);
                        }
                        catch (Exception ex)
                        {
                            if (debug) Debug.Log($"Download Error {file.DownloadUrl}\n{ex.Message}");
                            //MessageBox.Show($"Error: {ex.Message}", "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }

                
                foreach (ServerFile file in Optional)
                {
                    file.ModName = Shared.AlphanumericsAndHyphen(file.ModName);
                    OptionalMod.Add(file.ModName);
                }
                ModPack.PackAvailableOptionalMods(modPack, OptionalMod.all.Count);
                bool newModDiscovered = false;
                int countEnabledMods = 0;
                foreach (OptionalMod mod in OptionalMod.all)
                {
                    bool known = Config.GetBool("Optional[" + modPack + "][" + mod.name + "].Known");
                    mod.enabled = Config.GetBool("Optional[" + modPack + "][" + mod.name + "].Enabled");
                    if (!known)
                    {
                        Config.Set("Optional[" + modPack + "][" + mod.name + "].Known", true);
                        newModDiscovered = true;
                    }
                    if (mod.enabled) countEnabledMods++;
                }
                ModPack.PackEnabledOptionalMods(modPack, countEnabledMods);



                //download optional files
                foreach (ServerFile file in Optional)
                {
                    bool enabled = false;
                    foreach (OptionalMod mod in OptionalMod.all)
                    {
                        if (mod.name != file.ModName)
                            continue;

                        if (mod.enabled)
                            enabled = true;

                        break;
                    }

                    bool shouldDownload = false;
                    bool shouldDelete = false;
                    if (enabled == true && file.OK == false)
                    {
                        shouldDownload = true;
                    }
                    if (enabled == false)
                    {
                        shouldDelete = true;
                    }
                    if (debug) Debug.Log($"Optional mod: {file.ModName}, GET:{shouldDownload}, REMOVE:{shouldDelete}");

                    using (var httpClient = new HttpClient())
                    {
                        if (DownloadFailed)
                        {
                            Launcher.GUIInterface.SetStatusText("Error downloading mod.");
                            Launcher.GUIInterface.HideProgressBar();
                            TaskQueue.End();
                            return;
                        }
                        try
                        {
                            

                            if (file.DownloadUrl.StartsWith(rootURL) == false)
                                continue;


                            string relPath = file.DownloadUrl.Substring(rootURL.Length + 1).Replace("/", "\\");
                            string path = Path.Combine(Config.SVars.InstallDirectory.stringValue, relPath);
                            


                            if (File.Exists(path) && (shouldDownload || shouldDelete))
                            {
                                File.Delete(path);
                            }
                            //Deleting optional mods method somewhat unintuitive, not ideal, but saves the need to carry over extra data.
                            //Renaming or moving optional modfiles will cause duplicates. Bepinex loads only first. If hash is invalid, the file is deleted in a previous step.

                            if (shouldDownload)
                            {
                                // Get the file size to calculate progress
                                long fileSize = await Shared.GetFileSizeAsync(httpClient, file.DownloadUrl);
                                ProgressBar progressBar = Launcher.GUIInterface.InitializeProgressBar((int)fileSize);

                                // Download the file asynchronously and update the progress bar
                                string directory = Path.GetDirectoryName(path);
                                Directory.CreateDirectory(directory);
                                if (debug) Debug.Log("Downloading: " + file.DownloadUrl);
                                Launcher.GUIInterface.SetStatusText("Downloading: " + Path.GetFileName(path));
                                await Shared.DownloadFileAsync((string result) => Callback_DownloadFile(result), progressBar, httpClient, file.DownloadUrl, path);
                            }
                            
                        }
                        catch (Exception ex)
                        {
                            if (debug) Debug.Log($"Download Error {file.DownloadUrl}\n{ex.Message}");
                            //MessageBox.Show($"Error: {ex.Message}", "Download Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }


                //

                if (newModDiscovered)
                {
                    Launcher.instance.NotifyNewOptionalMods();
                }

                Launcher.GUIInterface.SetStatusText("File scanning complete. Click here to rescan.");
                Launcher.GUIInterface.HideProgressBar();
                TaskQueue.End();

                //check if current selected is verified (may have been changed while busy hashing this pack), and run again validation if needed
                if (GameServer.selected != null && GameServer.selected.modPack > 0 && GameServer.selected.ModPack.verified == false)
                {
                    API_VerifyMods.Execute(GameServer.selected.ModPack.modPack);
                }

                //Install default config files if missing
                InstallDefaultConfigIfMissing(modPack);

                //enable play button
                Launcher.instance.UpdateButtonPlayState();

                //updated labelOptionalMods
                Launcher.instance.UpdateLabelOptionalMods();

                if (debug) Debug.Log("Callback_VerifyModPack() done");
            }
            private void Callback_DownloadFile(string result)
            {
                if (result.ToLower().StartsWith("error"))
                {
                    DownloadFailed = true;
                    RepeatFailure++;
                }
                
            }
        }
    }
}
