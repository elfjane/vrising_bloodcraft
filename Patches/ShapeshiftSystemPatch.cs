using Bloodcraft.Resources;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using HarmonyLib;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Collections;
using Unity.Entities;
using EnterShapeshiftEvent = ProjectM.Network.EnterShapeshiftEvent;
using User = ProjectM.Network.User;

namespace Bloodcraft.Patches;

[HarmonyPatch]
internal static class ShapeshiftSystemPatch
{
    static EntityManager EntityManager => Core.EntityManager;

    static readonly bool _familiars = ConfigService.FamiliarSystem;

    static readonly PrefabGUID _psychicForm = PrefabGUIDs.AB_Shapeshift_DominatingPresence_PsychicForm_Group;
    static readonly PrefabGUID _batForm = PrefabGUIDs.AB_Shapeshift_Bat_Group;

    static readonly PrefabGUID _vanishBuff = Buffs.VanishBuff;

    [HarmonyPatch(typeof(ShapeshiftSystem), nameof(ShapeshiftSystem.OnUpdate))]
    [HarmonyPrefix]
    static void OnUpdatePrefix(ShapeshiftSystem __instance)
    {
        if (!Core.IsReady) return;
        else if (!_familiars) return;

        NativeArray<Entity> entities = __instance._Query.ToEntityArray(Allocator.Temp);

        try
        {
            foreach (Entity entity in entities)
            {
                if (!entity.TryGetComponent(out FromCharacter fromCharacter)) continue;
                EnterShapeshiftEvent enterShapeshiftEvent = entity.Read<EnterShapeshiftEvent>();

                Entity playerCharacter = fromCharacter.Character;
                User user = playerCharacter.GetUser();
                ulong steamId = user.PlatformId;

                if (enterShapeshiftEvent.Shapeshift.Equals(_psychicForm))
                {
                    bool hasActive = steamId.HasActiveFamiliar();
                    bool isDismissed = steamId.HasDismissedFamiliar();

                    if (hasActive && !isDismissed)
                    {
                        var actives = Familiars.ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList();
                        if (actives == null) continue;

                        foreach (var a in actives)
                        {
                            var fam = a.Familiar;
                            if (fam.HasBuff(_vanishBuff)) continue;

                            Familiars.DismissFamiliar(playerCharacter, fam, user, steamId);
                        }
                    }
                }
                else if (enterShapeshiftEvent.Shapeshift.Equals(_batForm))
                {
                    bool hasActive = steamId.HasActiveFamiliar();
                    bool isDismissed = steamId.HasDismissedFamiliar();

                    if (hasActive && !isDismissed)
                    {
                        var actives = Familiars.ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList();
                        if (actives == null) continue;

                        foreach (var a in actives)
                        {
                            var fam = a.Familiar;
                            if (fam.HasBuff(_vanishBuff)) continue;

                            Familiars.AutoCallMap[fromCharacter.Character] = fam;
                            Familiars.DismissFamiliar(playerCharacter, fam, user, steamId);
                        }
                    }
                }
            }
        }
        finally
        {
            entities.Dispose();
        }
    }
}
