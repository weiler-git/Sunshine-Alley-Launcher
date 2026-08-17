using Microsoft.Win32;
using System.Collections.Generic;
using static SunshineAlley_Launcher.Config;

namespace SunshineAlley_Launcher
{
    public class Config
    {
        
        /*
         * USER CONFIGURATIONS
         * 
         * Read from Registry on demand, cached in app, should never be changed externally, save changes to registry instantly.
         * 
         * Entry class for user configuration entires.
         * When read, if not exist (yet), try get from Registry, otherewise assign default value to new entry.
         * When set, overwrite if exist, otherwise new entry, then save in registry.
         * 
         * 
         * THIS FILE BUILT COMPATIBLE FOR COPYING TO GAME MOD
         * 
         */

        public static readonly Config Instance = new Config();
        
        public Config()
        {
            IRegistry iRegistry = IRegistry.Instance;
        }

        /*
         * 
         * Stored Variables
         * For use when accessing config values multipletimes
         * Caches HashCode for faster lookup
         * 
         * 
         */
        public class SVar
        {
            public string Key { get; private set; }
            private int hash = 0;
            public int Hash
            { 
                get 
                {
                    if (hash == 0)
                        hash = Key.GetHashCode();
                    return hash;
                } 
                set { hash = value; }
            }
            public string stringValue
            {
                get
                {
                    return GetString(this);
                }
                set
                {
                    Set(this, value);
                }
            }
            public bool boolValue
            {
                get
                {
                    return GetBool(this);
                }
                set
                {
                    Set(this, value);
                }
            }
            public static SVar Add(string key)
            {
                return new SVar { Key = key};
            }
            
            //private set to force all adding to be done with Add()
            //we only want to generate the hash when its first used.
        }
        public class SVars
        {
            public static SVar InstallDirectory = SVar.Add("InstallDirectory");
            public static SVar GameDirectory = SVar.Add("GameDirectory");
            public static SVar UseSteam = SVar.Add("UseSteam");
            public static SVar Persistent = SVar.Add("Persistent");
            public static SVar SelectedServer = SVar.Add("SelectedServer");
            public static SVar ModPackLaunched = SVar.Add("ModPackLaunched");
        }


        /*
         * 
         * Interface to registry
         * Get/Set forwarded from Entry
         * Reads/Saves from registry
         * 
         * 
         * 
         */

        public class IRegistry 
        {
            public static string registrySubKey = @"SOFTWARE\Sunshine Alley";

            private RegistryKey RootKey;

            public static readonly IRegistry Instance = new IRegistry();
            //PS: Static values is assigned AFTER constructor IRegistry()
            private static string packs = "ModPacks";

            public IRegistry()
            {
                RootKey = OpenSubKey();
            }
            public static RegistryKey OpenSubKey()
            {
                return Registry.CurrentUser.CreateSubKey(registrySubKey);
            }
            public static void CheckSubKey()
            {
                // Check if the RootKey is still open
                if (Instance.RootKey == null)
                {
                    Instance.RootKey = OpenSubKey();
                }
            }
            public static string GetString(string key)
            {
                CheckSubKey();
                return (string)Instance.RootKey.GetValue(key, null);
            }
            public static bool GetBool(string key)
            {
                CheckSubKey();
                if (Instance.RootKey.GetValue(key) == null)
                    return false;

                if ((string)Instance.RootKey.GetValue(key) == "true")
                {
                    return true;
                }
                return false;
            }
            public static void Set(string key, string value)
            {
                CheckSubKey();
                Instance.RootKey.SetValue(key, value, RegistryValueKind.String);
            }
            public static void Set(string key, bool value)
            {
                CheckSubKey();
                string _value = value ? "true" : "false";
                Instance.RootKey.SetValue(key, _value, RegistryValueKind.String);
            }

        }

        /*
         * 
         * ENTRIES
         * Stored entries, save on set, load on _first_ access
         * String key to get/set any values
         * Using IRegistry to save/load
         * 
         */

        public class Entry
        {
            public string key;
            public int hash;
            public string stringValue;
            public bool boolValue;

            public static Dictionary<int, Entry> instances = new Dictionary<int, Entry>();
        }

        //get and remember bool:
        public static bool GetBool(SVar sVar, string defaultValue = "")
        {
            return GetBool(sVar.Key, sVar.Hash, defaultValue);
        }
        //get and forget bool:
        public static bool GetBool(string key, int hash = 0, string defaultValue = "")
        {
            if (hash == 0) hash = key.GetHashCode();

            if (Entry.instances.TryGetValue(hash, out Entry instance))
            {
                return instance.boolValue;
            }

            bool value = IRegistry.GetBool(key);
            Entry.instances.Add(hash, new Entry { key = key, hash = hash, boolValue = value });
            return value;
        }
        //get and remember string:
        public static string GetString(SVar sVar, string defaultValue = "")
        {
            return GetString(sVar.Key, sVar.Hash, defaultValue);
        }
        //get and forget string:
        public static string GetString(string key, int hash = 0, string defaultValue = "")
        {
            if (hash == 0) hash = key.GetHashCode();

            if (Entry.instances.TryGetValue(hash, out Entry instance))
            {
                return instance.stringValue;
            }

            string value = IRegistry.GetString(key);
            if (value == null)
                value = defaultValue;

            Entry.instances.Add(hash, new Entry { key = key, hash = hash, stringValue = value });
            return value;
        }
        //set and remember string:
        public static void Set(SVar sVar, string value)
        {
            Set(sVar.Key, value, sVar.Hash);
        }
        //set and forget string:
        public static void Set(string key, string value, int hash = 0)
        {
            if (hash == 0) hash = key.GetHashCode(); 
            if (Entry.instances.TryGetValue(hash, out Entry instance))
            {
                instance.stringValue = value;
            }
            else
            {
                Entry.instances.Add(hash, new Entry { key = key, hash = hash, stringValue = value });
            }
            IRegistry.Set(key, value);
        }
        //set and remember bool:
        public static void Set(SVar sVar, bool value) 
        {
            Set(sVar.Key, value, sVar.Hash);
        }
        //set and forget bool:
        public static void Set(string key, bool value, int hash = 0) 
        {
            if (hash == 0) hash = key.GetHashCode();
            if (Entry.instances.TryGetValue(hash, out Entry instance))
            {
                instance.boolValue = value;
            }
            else
            {
                Entry.instances.Add(hash, new Entry { key = key, hash = hash, boolValue = value });
            }
            IRegistry.Set(key, value);
        }


    }
}
