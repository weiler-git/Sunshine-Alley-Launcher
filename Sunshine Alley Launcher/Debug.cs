using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SunshineAlley_Launcher
{
    public class Debug
    {
        private static Logger instance;

        public static void Log(string message)
        {
            if (instance == null)
            {
                instance = new Logger();
            }
            
            instance.Log(message);
        }

        private class Logger
        {
            private string logOutputPath;
            private bool isFirstWrite = true;

            public Logger()
            {
                if (Directory.Exists(Config.SVars.InstallDirectory.stringValue))
                {
                    logOutputPath = Path.Combine(Config.SVars.InstallDirectory.stringValue, "LogOutput.log");                    
                }
            }
            public void Log(string message) 
            {
                ConsoleLog(message);
                LogOutput(message);
            }

            private void ConsoleLog(string message)
            {
                Console.WriteLine(message);
            }
            private async void LogOutput(string message)
            {
                if (string.IsNullOrEmpty(logOutputPath)) return;
                

                bool success = false;
                int retries = 0;
                string messageWithTimeStamp = $"\n[{DateTime.UtcNow.ToString()}] {message}";
                while (!success && retries < 10) {
                    try
                    {
                        if (isFirstWrite)
                        {
                            File.WriteAllText(logOutputPath, $"Launcher Log Output - UTC: {DateTime.UtcNow.ToString()}");
                            isFirstWrite = false;
                        }
                        if (File.Exists(logOutputPath))
                        {
                            File.AppendAllText(logOutputPath, messageWithTimeStamp);
                        }
                        success = true;
                    }
                    catch
                    {
                        await Task.Delay(100);
                        retries++;
                    }
                }
                
            }
        }
    }
}
