using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SunshineAlley_Launcher
{
    public class ModPack
    {
        public int modPack;
        public bool verified = false;
        public int availableOptionalMods = 0;
        public int enabledOptionalMods = 0;
        public static List<ModPack> all = new List<ModPack>();
        public override string ToString()
        {
            return modPack.ToString();
        }
        public static ModPack Add(int modPack)
        {
            if (modPack < 1)
                return null;

            foreach (ModPack packs in all)
            {
                if (packs.modPack == modPack)
                {
                    return packs;
                }
            }
            ModPack pack = new ModPack { modPack = modPack };
            all.Add(pack);
            return pack;
        }
        public static void PackVerified(int modPack)
        {
            foreach (ModPack pack in all)
            {
                if (pack.modPack == modPack)
                {
                    pack.verified = true;
                }
            }
        }
        public static void PackNotVerified(int modPack)
        {
            foreach (ModPack pack in all)
            {
                if (pack.modPack == modPack)
                {
                    pack.verified = false;
                    pack.enabledOptionalMods = 0;
                }
            }
        }
        public static void PackAvailableOptionalMods(int modPack, int count)
        {
            foreach (ModPack pack in all)
            {
                if (pack.modPack == modPack)
                {
                    pack.availableOptionalMods = count;
                }
            }
        }
        public static void PackEnabledOptionalMods(int modPack, int count)
        {
            foreach (ModPack pack in all)
            {
                if (pack.modPack == modPack)
                {
                    pack.enabledOptionalMods = count;
                }
            }
        }
    }
}
