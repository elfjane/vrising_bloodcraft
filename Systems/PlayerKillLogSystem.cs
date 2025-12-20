using Bloodcraft.Patches;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using Unity.Entities;

namespace Bloodcraft.Systems
{
    internal static class PlayerKillLogSystem
    {
        public static void OnUpdate(object sender, DeathEventListenerSystemPatch.DeathEventArgs deathEvent)
        {
            try
            {
                if (deathEvent == null) return;
                Entity source = deathEvent.Source;
                Entity target = deathEvent.Target;

                if (!source.Exists()) return;
                if (!source.IsPlayer()) return; // only log when a player's character (or owned familiar) was the source

                ulong steamId = source.GetSteamId();

                int deathGuid = target.GetPrefabGuid().GuidHash;

                DataService.PlayerKillLogManager.IncrementPlayerKill(steamId, deathGuid);
            }
            catch (System.Exception e)
            {
                Core.Log.LogWarning($"[PlayerKillLogSystem] - Exception: {e}");
            }
        }
    }
}