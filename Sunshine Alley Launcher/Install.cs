using IWshRuntimeLibrary;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using File = System.IO.File;

namespace SunshineAlley_Launcher
{
    public class Install
    {
        //private bool canceled = false;
        public static bool debug = Program.debug;

        public Install()
        {
            if (Program.SetupCallback.doInstall) DoInstall();
            else if (Program.SetupCallback.doRepair) Repair();
            else if (Program.SetupCallback.doUninstall) Uninstall();
            else if (Program.SetupCallback.doCreateShortcuts) CreateShortcuts(Program.SetupCallback.createStartMenuShortcut, Program.SetupCallback.createDesktopShortcut);
        }

        /*
         * 
         * 
         * INSTALL METHODS
         * 
         * 
         */

        private void DoInstall()
        {
            Config.SVars.InstallDirectory.stringValue = Program.SetupCallback.installDirectory;
            Config.SVars.GameDirectory.stringValue = Program.SetupCallback.gameDirectory;
            string installExePath = Program.GetInstallExePath();

            //create install directory
            try
            {
                Directory.CreateDirectory(Config.SVars.InstallDirectory.stringValue);
                if (debug) Debug.Log("Install directory created");
            }
            catch
            {
                MessageBox.Show("Unable to create destination folder");
                return;
            }
            
            //copy exe to install directory
            if (Program.GetThisExecutable() != installExePath)
            {
                if (Shared.CopyExecutable(Program.GetThisExecutable(), installExePath) == false) return;
            }
            

            //create shortcuts
            CreateShortcuts(Program.SetupCallback.createStartMenuShortcut, Program.SetupCallback.createDesktopShortcut);

            //start executable :: close this
            Shared.RunExecutable(installExePath);

        }
        private void Repair()
        {
            //uninstall everything except for executable
            if (DeleteEverything(skipShortcuts: true, skipExecutables: true) == false) return;

            Shared.InstallUpdate();
        }
        
        private void Uninstall()
        {
            //move running exe to temp folder if run from inside install directory
            if (Program.IsRunningFromInstallDirectory())
            {
                string tempFolderPath = Path.GetTempPath();

                string temp = Shared.NameTemporaryExecutabel(tempFolderPath, Program.assemblyName);
                if (Shared.MoveExecutable(Program.GetThisExecutable(), temp) == false) return;
                //MessageBox.Show("??" + temp + "==" + Program.GetThisExecutable());
            }
            
            //uninstall everything
            if (DeleteEverything() == false) return;

            //close app
            MessageBox.Show(Program.assemblyName + " has been successfully uninstalled \nThis executable can be deleted from: " + Program.GetThisExecutable());
            Shared.CloseThisExecutable();
        }
        private void CreateShortcuts(bool createStartMenuShortcut, bool createDesktopShortcut)
        {
            //create shortcuts
            string executable = Program.GetInstallExePath();
            string startMenuPath = Program.GetStartMenuPath();
            string desktopPath = Program.GetDesktopPath();

            if (createStartMenuShortcut)
            {
                WshShell shell = new WshShell();
                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(startMenuPath);
                shortcut.Description = "Shortcut for "+Program.assemblyName;
                shortcut.TargetPath = executable;
                shortcut.Save();
                if (debug) Debug.Log("Start Menu shortcut created.");
            }
            
            if (createDesktopShortcut)
            {
                WshShell shell = new WshShell();
                IWshShortcut shortcut = (IWshShortcut)shell.CreateShortcut(desktopPath);
                shortcut.Description = "Shortcut for "+Program.assemblyName;
                shortcut.TargetPath = executable;
                shortcut.Save();
                if (debug) Debug.Log("Desktop shortcut created.");
            }

            if (Program.SetupCallback.doCreateShortcuts)
            {
                //shortcuts was requested as the main objective, provide feedback
                MessageBox.Show("Shortcut(s) has been successfully created.");

                //start the launcher properly if running as installer
                if (!Program.IsRunningFromInstallExePath())
                {
                    //start executable
                    Shared.RunExecutable(Program.GetInstallExePath());

                    //close this executable
                    Shared.CloseThisExecutable();
                }
            }

            
        }



        /*
         * 
         * 
         * HELPER METHODS
         * 
         * 
         */
        

        
        
        private bool DeleteEverything(bool skipExecutables = false, bool skipShortcuts = false)
        {

            string directoryPath = Config.SVars.InstallDirectory.stringValue;
            string excludedFilePath1 = Program.GetThisExecutable();
            string excludedFilePath2 = Program.GetDownloadExePath();

            if (skipExecutables)
            {
                if (debug) Debug.Log("Skipping file: " + excludedFilePath1);
                if (debug) Debug.Log("Skipping file: " + excludedFilePath2);
            }
            // Remove installed files
            try
            {
                // Delete all files in the directory except the excluded ones
                string[] files = Directory.GetFiles(directoryPath);

                if (debug) Debug.Log("Deleting files");
                foreach (var filePath in files)
                {
                    // Check if the file is one of the excluded files
                    if (string.Equals(filePath, excludedFilePath1, StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(filePath, excludedFilePath2, StringComparison.OrdinalIgnoreCase))
                    {
                        if (skipExecutables)
                        {
                            if (debug) Debug.Log($"Skipped deletion of file: {filePath}");
                            continue;
                        }
                    }

                    // Delete the file
                    if (debug) Debug.Log(filePath);
                    File.Delete(filePath);
                }

                // Delete all subdirectories and their contents
                string[] subdirectories = Directory.GetDirectories(directoryPath);
                foreach (var subdirectory in subdirectories)
                {
                    Directory.Delete(subdirectory, true);
                }

                if (debug) Debug.Log("All files and folders deleted successfully.");
            }
            catch (Exception ex)
            {
                if (debug) Debug.Log($"Error: {ex.Message}");
                MessageBox.Show("File(s) in use or inaccessible, unable to remove file(s) \n"+ex.Message);
                return false;
            }

            //Remove install directory
            if (!skipExecutables)
            {
                try
                {
                    if (Directory.Exists(directoryPath)) Directory.Delete(directoryPath, recursive: true); //recurse is redundant but no harm to clear it all should there be leftovers.
                    if (debug) Debug.Log("Deleted install directory");
                }
                catch (Exception ex)
                {
                    if (debug) Debug.Log($"Error: {ex.Message}");
                    MessageBox.Show("Unable to remove install directory. \n" + ex.Message);
                    //canceled = true;
                    //return;
                }
            }

            //Remove shortcuts
            if (!skipShortcuts)
            {
                try
                {
                    string startMenuPath = Program.GetStartMenuPath();
                    string desktopPath = Program.GetDesktopPath();
                    if (File.Exists(startMenuPath)) File.Delete(startMenuPath);
                    if (File.Exists(desktopPath)) File.Delete(desktopPath);
                    if (debug) Debug.Log("Deleted shortcuts");
                }
                catch (Exception ex)
                {
                    if (debug) Debug.Log($"Error: {ex.Message}");
                    MessageBox.Show("Shortcut file(s) inaccessible and are not removed. \n" + ex.Message);
                    //canceled = true;
                    //return;
                }
            }

            //Remove registry entries
            string targetSubkey = Config.IRegistry.registrySubKey;

            try
            {
                // Open the base key (e.g., Registry.CurrentUser)
                using (RegistryKey baseKey = Registry.CurrentUser)
                {
                    // Open the target subkey
                    using (RegistryKey targetKey = baseKey.OpenSubKey(targetSubkey, true))
                    {
                        if (targetKey != null)
                        {
                            // Delete all subkeys under the target subkey
                            string[] subKeyNames = targetKey.GetSubKeyNames();
                            foreach (var subKeyName in subKeyNames)
                            {
                                targetKey.DeleteSubKeyTree(subKeyName);
                                if (debug) Debug.Log($"Deleted subkey: {subKeyName}");
                            }

                            if (debug) Debug.Log("All subkeys deleted successfully.");
                        }
                        else
                        {
                            if (debug) Debug.Log("Target subkey not found.");
                        }
                    }
                    if (!skipExecutables)
                    {
                        baseKey.DeleteSubKey(targetSubkey);
                        if (debug) Debug.Log("Deleted root subkey");
                    } 
                }
            }
            catch (Exception ex)
            {
                if (debug) Debug.Log($"Error: {ex.Message}");
                MessageBox.Show("Unable to remove registry key(s). \n" + ex.Message);
            }

            return true;
        }
        
    }
}
