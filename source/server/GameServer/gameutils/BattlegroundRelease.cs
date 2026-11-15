using System;

namespace DOL.GS
{
    /// <summary>
    /// A normal /release inside a battleground goes to that realm's portal keep.
    /// /release bind still uses the bind stone.
    /// </summary>
    public static class BattlegroundRelease
    {
        public const ushort NewFrontiersRegion = 163;

        public static eReleaseType ChooseNormalRelease(ushort regionId, bool zoneIsBattleground)
        {
            if (regionId == NewFrontiersRegion)
                return eReleaseType.NewFrontiers;
            if (zoneIsBattleground)
                return eReleaseType.Battleground;
            return eReleaseType.Bind;
        }

        public static bool IsFriendlyPortalKeep(bool isPortalKeep, string name, eRealm originalRealm, eRealm playerRealm)
        {
            if (originalRealm != playerRealm || playerRealm == eRealm.None)
                return false;
            if (isPortalKeep)
                return true;
            return !string.IsNullOrEmpty(name)
                && name.IndexOf("Portal Keep", StringComparison.OrdinalIgnoreCase) >= 0;
        }
    }
}
