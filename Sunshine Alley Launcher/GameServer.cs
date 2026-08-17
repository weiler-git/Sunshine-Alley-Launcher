using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SunshineAlley_Launcher
{
    public class GameServer
    {
        public static bool debug = Program.debug;

        public string WorldName;
        public int modPack = 0;
        public int SteamBuildID;
        public ModPack ModPack;
        public DateTime ReleaseWorld;
        public DateTime LastOnline;
        public static List<GameServer> all = new List<GameServer>();
        public static GameServer selected = null;
        private static bool isRequestServerList = false;

        public static void Refresh()
        {
            API_ServerListRequest.RequestServerList();
        }
        private static void Updated()
        {
            selected = null;
            AddLocalLaunchOptions();
            PopulateModPacks();
            Launcher.instance.RedrawServerListCombo();
        }
        private static void AddLocalLaunchOptions()
        {
            all.Add(new GameServer
            {
                WorldName = "Private World",
                modPack = -1,
                ReleaseWorld = DateTime.MinValue,
                LastOnline = DateTime.UtcNow,
            });
            all.Add(new GameServer
            {
                WorldName = "Vanilla / No mods",
                modPack = -99,
                ReleaseWorld = DateTime.MinValue,
                LastOnline = DateTime.UtcNow,
            });
        }
        private static void PopulateModPacks()
        {
            foreach (GameServer server in all)
            {
                server.ModPack = ModPack.Add(server.modPack);
            }
        }


        private class API_ServerListRequest
        {
            public string SessionGuid;
            public string PublicKey;
            public static string _SessionGuid;
            public static async void RequestServerList()
            {
                if (debug) Debug.Log("RequestServerList()");
                if (isRequestServerList) return;
                isRequestServerList = true;

                API_ServerListRequest request = new API_ServerListRequest();
                if (string.IsNullOrEmpty(_SessionGuid))
                {
                    Guid SessionId = Guid.NewGuid();
                    _SessionGuid = SessionId.ToString();
                }
                request.SessionGuid = _SessionGuid;

                if (Shared.AuthLevel < 1)
                {
                    request.PublicKey = Program.rsaKeys.PublicKey;
                }

                string json = JsonConvert.SerializeObject(request, Formatting.None);
                string signature = RsaEncryptionService.SignMessage(json, Program.rsa);
                string url = Shared.webURL + "/valheimapi?Exec=LauncherServerList";
                if (debug) await Task.Delay(200);
                await Shared.PostData((string result) => API_ServerListCallback.Callback(result), url, json, "launcher:" + Program.machineGuid, signature);
                isRequestServerList = false;
                if (debug) Debug.Log("RequestServerList() done");
            }
        }
        private class API_ServerListCallback
        {
            public int AuthLevel = 0;
            public bool IsAdmin = false;
            public List<Server> servers;
            public class Server
            {
                public string WorldName;
                public int ModPack;
                public int SteamBuildID = 0;
                public bool AllowNewCharacters;
                public DateTime ReleaseWorld;
                public DateTime LastOnline;
            }

            public static void Callback(string json)
            {
                if (debug) Debug.Log("API_ServerListCallback()");
                if (json.StartsWith("ERROR") || string.IsNullOrWhiteSpace(json))
                {
                    if (json == "ERROR_BADSIGNATURE")
                    {
                        //make new keypair ?
                        //reset AuthLevel ?
                    }
                    if (debug) Debug.Log($"ERROR: {json}");
                    return;
                }   

                API_ServerListCallback result;

                try
                {
                    result = JsonConvert.DeserializeObject<API_ServerListCallback>(json);
                }
                catch (Exception ex)
                {
                    if (debug) Debug.Log($"Undeserializable response: {json}");
                    return;
                }
                if (result == null)
                    return;

                Shared.AuthLevel = result.AuthLevel;
                Shared.IsAdmin = result.IsAdmin;
                //if (debug)
                //{
                //  Shared.AuthLevel = 2;
                //  Shared.IsAdmin = true;
                //}
                
                all.Clear();
                foreach (Server server in result.servers)
                {
                    if (Shared.AuthLevel < 2 && server.AllowNewCharacters == false)
                        continue;
                
                    all.Add(new GameServer
                    {
                        WorldName = server.WorldName,
                        modPack = server.ModPack,
                        SteamBuildID = server.SteamBuildID,
                        ReleaseWorld = server.ReleaseWorld,
                        LastOnline = server.LastOnline
                    });
                }
                Updated();
            }
        }
    }
}
