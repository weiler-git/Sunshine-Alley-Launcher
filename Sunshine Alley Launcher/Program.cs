using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Diagnostics;
using Microsoft.Win32;
using System.Globalization;
using System.IO;
using System.Security.Cryptography.Xml;
using System.Data.SqlTypes;
using IWshRuntimeLibrary;
using File = System.IO.File;
using System.Xml.Linq;
using System.Xml;

namespace SunshineAlley_Launcher
{
    public class Program
    {
        [DllImport("kernel32.dll")]
        public static extern bool AllocConsole();

        [DllImport("kernel32.dll")]
        public static extern bool FreeConsole();

        public static bool debug = true;

        public static string machineGuid = GetMachineGuid();
        //public static string registrySubKey = @"SOFTWARE\Sunshine Alley";
        public static RsaEncryptionService.RsaKeys rsaKeys = new RsaEncryptionService.RsaKeys();
        public static RSA rsa;

        //parameters
        public const string parameterUpdate = "update";
        public const string parameterUninstall = "uninstall";
        public const string parameterDebug = "debug";
        public static bool parameterUpdateEnabled = false;
        public static bool parameterUninstallEnabled = false;
        public static bool parameterDebugEnabled = false;

        //checked on startup
        public static bool isInstalled = false;
        public static string assemblyName = "";
        public static bool executeableHasMoved = false;
        public static string executeableMovedTo = "";

        public class SetupCallback
        {
            public static bool doInstall = false;
            public static bool doRepair = false;
            public static bool doUninstall = false;
            public static bool doCreateShortcuts = false;
            public static string installDirectory = "";
            public static string gameDirectory = "";
            public static bool createStartMenuShortcut = false;
            public static bool createDesktopShortcut = false;
        }

        [STAThread]
        public static void Main()
        {
#if DEBUG
            AllocConsole(); // close with FreeConsole();
#endif

            
            Config config = Config.Instance;




            // Get the command-line arguments
            string[] args = Environment.GetCommandLineArgs();

            // Display the executable name
            Console.WriteLine($"Executable: {args[0]}");

            // Display other command-line arguments
            for (int i = 1; i < args.Length; i++)
            {
                Console.WriteLine($"Argument {i}: {args[i]}");
                if (args[i].ToLower() == parameterUpdate) { parameterUpdateEnabled = true; break; }
                if (args[i].ToLower() == parameterUninstall) { parameterUninstallEnabled = true; break; }
                if (args[i].ToLower() == parameterDebug) { parameterDebugEnabled = true; break; }
            }
            if (parameterUpdateEnabled && parameterUninstallEnabled)
            {
                Debug.Log("Update and Uninstall was both requested, cancelling both requests.");
                parameterUpdateEnabled = false;
                parameterUninstallEnabled = false;
            }
            if (parameterDebugEnabled) debug = true;

            





            //test
            GetOrCreateKeypair();




            /* the procedure below is not neccessary if moving running .exe works fine. We can keep the parameters for future use.
             * 
             * if running outside programpath, or programdir undefined, run Setup()
             * if running outside programpath with parameter --update: copy self to programdir, start from programdir, close self. without instantiating Setup GUI.
             * if running outside programpath with parameter --repair: 
             * if running outside programpath with parameter --uninstall: delete all files and registry keys, close self. without instantiating Setup GUI.
             * 
             * Setup()
             * Install wizard:
             * 1) Location
             * 2) Gamefolder
             * 3) Optional shortcuts
             * Repair
             * - delete all files, config files and registry keys
             * - download latest exe
             * - start with --update param
             * - close self..
             * Create shortcut to desktop/start menu
             * Uninstall option
             * 
             * if running in programdir, run Launcher()
             * if update detected, download to temp executeable, run updated file with --update parameter, close self.
             * 
             * 
             * 
             * look for update before hashing files
             * 
             * 
             * 
             */
            //string startMenuProgramsPath = Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms);

            assemblyName = GetAssemblyName();


            if (debug) Debug.Log("Assembly Name: " + assemblyName);
            if (debug) Debug.Log("installDirectory: " + Config.SVars.InstallDirectory.stringValue);
            if (debug) Debug.Log("Process Directory: " + Path.GetDirectoryName(GetThisExecutable()));
            if (debug) Debug.Log("Assigned Location: " + GetInstallExePath());
            if (debug) Debug.Log("Process File: " + GetThisExecutable());


            //install directory defined?

            if (Config.SVars.InstallDirectory.stringValue.Length > 3 && Directory.Exists(Config.SVars.InstallDirectory.stringValue)) 
            {
                if (debug) Debug.Log("Install directory found");
                isInstalled = true; 
            }

            //but does executable exist?
            if (!File.Exists(GetInstallExePath()))
            {
                if (debug) Debug.Log("Executable missing");
                isInstalled = false;
            }


            //process running from designated .exe?
            if (IsRunningFromInstallExePath())
            {
                if (debug) Debug.Log("Running from designated location");
            }



            //before GUI
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

#if DEBUG
            TryRemoveTempExecutables();
            Application.Run(new Launcher());
            if (debug) Debug.Log("Launcher() finished running in Debug Mode. Terminating.");
            System.Environment.Exit(1);
#endif

            if (!isInstalled || !IsRunningFromInstallDirectory())
            {
                //do Setup
                if (debug) Debug.Log("Running Setup()");
                Application.Run(new Setup());
                Install install = new Install();
            }
            else if (IsRunningFromInstallExePath())
            {
                //remove temp executables (Repair temp exe might still be running and wont be removed until next time this app starts)
                TryRemoveTempExecutables();

                //start Launcher
                Application.Run(new Launcher());
            }
            else
            {
                MessageBox.Show("An unexpected error has occured, neither installer or launcher was able to run.");
            }


            OnApplicationQuit();
        }
        public static string GetDownloadExePath()
        {
            return Path.Combine(Config.SVars.InstallDirectory.stringValue, Program.assemblyName + ".update.exe");
        }
        public static string GetInstallExePath()
        {
            return Path.Combine(Config.SVars.InstallDirectory.stringValue, Program.assemblyName + ".exe");
        }
        
        public static bool IsRunningFromInstallExePath()
        {
            return GetInstallExePath() == GetThisExecutable();
        }
        public static bool IsRunningFromInstallDirectory()
        {
            return Path.GetDirectoryName(GetThisExecutable()) == Config.SVars.InstallDirectory.stringValue;
        }
        public static string GetThisExecutable()
        {
            if (executeableHasMoved)
                return executeableMovedTo;

            return Process.GetCurrentProcess().MainModule.FileName;
        }
        public static void RegisterMovedThisExecutable(string destination)
        {
            executeableHasMoved = true;
            executeableMovedTo = destination;
        }
        public static string GetStartMenuPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), Program.assemblyName + ".lnk");
        }
        public static string GetDesktopPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), Program.assemblyName + ".lnk");
        }

        public static void TryRemoveTempExecutables()
        {
            try
            {
                // Delete all files in the directory except the excluded ones
                string[] files = Directory.GetFiles(Config.SVars.InstallDirectory.stringValue);
                string deleteFilesStartingWith = Path.Combine(Config.SVars.InstallDirectory.stringValue, Program.assemblyName + ".temp");

                if (debug) Debug.Log("Deleting Temp Executables");
                foreach (var filePath in files)
                {
                    // Check if the file is one of the excluded files
                    if (filePath.ToLower().StartsWith(deleteFilesStartingWith.ToLower()))
                    {
                        // Delete the file
                        if (debug) Debug.Log(filePath);
                        File.Delete(filePath);
                    }
                }
            }
            catch
            {

            }
        }

        public static void OnApplicationQuit()
        {
            //if (debug) MessageBox.Show("end");
            if (debug) FreeConsole();
        }
        static string GetAssemblyName()
        {
            // Get the assembly of the current executing code
            Assembly assembly = Assembly.GetExecutingAssembly();

            // Get the AssemblyTitle attribute
            object[] attributes = assembly.GetCustomAttributes(typeof(AssemblyTitleAttribute), false);
            if (attributes.Length > 0 && attributes[0] is AssemblyTitleAttribute titleAttribute)
            {
                return titleAttribute.Title;
            }

            // If AssemblyTitle is not defined, use the default assembly full name
            return assembly.FullName.Split(',')[0];
        }
        public static void GetOrCreateKeypair()
        {
            //never reused and can be declared here
            string configPrivate = "RSA[" + machineGuid + "].Private";
            string configPublic = "RSA[" + machineGuid + "].Public";
            string configSignature = "RSA[" + machineGuid + "].Signature";
            //int configPrivateHash = configPrivate.GetHashCode();
            //int configPublicHash = configPublic.GetHashCode();
            //int configSignatureHash = configSignature.GetHashCode();


            bool foundRsaKeys = false;
            try
            {
                string privateKeyXml = Config.GetString(configPrivate);
                string publicKeyXml = Config.GetString(configPublic);
                string signature = Config.GetString(configSignature);

                rsa = RsaEncryptionService.ParseXMLString(privateKeyXml);
                if (rsa != null && RsaEncryptionService.VerifySignature(machineGuid, signature, rsa))
                {
                    rsaKeys.PrivateKey = privateKeyXml;
                    rsaKeys.PublicKey = publicKeyXml;
                    foundRsaKeys = true;
                    if (debug) Debug.Log("KeyPair approved");
                }
            }
            catch (Exception ex)
            {

            }
            
            if (!foundRsaKeys)
            {
                rsaKeys = RsaEncryptionService.GenerateKeys(out rsa);
                string signature = RsaEncryptionService.SignMessage(machineGuid, rsa);
                //RsaEncryptionService.SaveKeyPairToXml(rsaKeys.PublicKey, rsaKeys.PrivateKey, DeviceKeyPath);
                Config.Set(configPrivate, rsaKeys.PrivateKey);
                Config.Set(configPublic, rsaKeys.PublicKey);
                Config.Set(configSignature, signature);
                if (debug) Debug.Log("KeyPair created");
            }
        }
        static string GetMachineGuid()
        {
            try
            {
                using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64))
                using (RegistryKey subkey = key.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography"))
                {
                    if (subkey != null)
                    {
                        object value = subkey.GetValue("MachineGuid");
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error retrieving MachineGuid: {ex.Message}");
            }

            return null;
        }
    }
}