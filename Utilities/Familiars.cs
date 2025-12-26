using Bloodcraft.Patches;
using Bloodcraft.Resources;
using Bloodcraft.Services;
using ProjectM;
using ProjectM.Behaviours;
using ProjectM.Gameplay.Scripting;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using System.Collections;
using System.Collections.Concurrent;
using System.Linq;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VampireCommandFramework;
using static Bloodcraft.Patches.LinkMinionToOwnerOnSpawnSystemPatch;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarBuffsManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarEquipmentManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarExperienceManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarPrestigeManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarUnlocksManager;
using static Bloodcraft.Services.PlayerService;
using static Bloodcraft.Systems.Familiars.FamiliarBindingSystem;
using static Bloodcraft.Systems.Familiars.FamiliarUnlockSystem;
using static Bloodcraft.Utilities.Familiars.ActiveFamiliarManager;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarRarityManager;

namespace Bloodcraft.Utilities;

internal static class Familiars
{
    static EntityManager EntityManager => Core.EntityManager;
    static ServerGameManager ServerGameManager => Core.ServerGameManager;
    static SystemService SystemService => Core.SystemService;
    static PrefabCollectionSystem PrefabCollectionSystem => SystemService.PrefabCollectionSystem;

    static readonly bool _familiarCombat = ConfigService.FamiliarCombat;

    static readonly WaitForSeconds _bindingDelay = new(0.25f);
    static readonly WaitForSeconds _delay = new(2f);

    static readonly PrefabGUID _bonusStatsBuff = Buffs.BonusPlayerStatsBuff;
    static readonly PrefabGUID _defaultEmoteBuff = Buffs.DefaultEmoteBuff;
    static readonly PrefabGUID _pveCombatBuff = Buffs.PvECombatBuff;
    static readonly PrefabGUID _pvpCombatBuff = Buffs.PvPCombatBuff;
    static readonly PrefabGUID _dominateBuff = Buffs.DominateBuff;
    static readonly PrefabGUID _takeFlightBuff = Buffs.TakeFlightBuff;
    static readonly PrefabGUID _inkCrawlerDeathBuff = Buffs.InkCrawlerDeathBuff;
    static readonly PrefabGUID _invulnerableBuff = Buffs.AdminInvulnerableBuff;
    static readonly PrefabGUID _disableAggroBuff = Buffs.DisableAggroBuff;
    static readonly PrefabGUID _vanishBuff = Buffs.VanishBuff;
    static readonly PrefabGUID _interactModeBuff = Buffs.InteractModeBuff;

    static readonly PrefabGUID _spiritDouble = PrefabGUIDs.CHAR_Cursed_MountainBeast_SpiritDouble;
    static readonly PrefabGUID _highlordGroundSword = PrefabGUIDs.CHAR_Legion_HighLord_GroundSword;
    static readonly PrefabGUID _enchantedCross = PrefabGUIDs.CHAR_ChurchOfLight_EnchantedCross;

    static readonly PrefabGUID _itemSchematic = PrefabGUIDs.Item_Ingredient_Research_Schematic;

    // Cached auto-remove reward (initialized once to avoid repeated allocations)
    public static PrefabGUID AutoRemoveRewardItem { get; } = new(ConfigService.AutoRemoveItem);
    public static int AutoRemoveRewardQty { get; } = ConfigService.AutoRemoveItemQuantity;

    static readonly float3 _southFloat3 = new(0f, 0f, -1f);

    const float BLOOD_QUALITY_IGNORE = 90f;
    public enum FamiliarEquipmentType
    {
        Chest,
        Weapon,
        MagicSource,
        Footgear,
        Legs,
        Gloves
    }

    static readonly Dictionary<FamiliarEquipmentType, EquipmentType> _familiarEquipmentMap = new()
    {
        { FamiliarEquipmentType.Chest, EquipmentType.Chest },
        { FamiliarEquipmentType.Weapon, EquipmentType.Weapon },
        { FamiliarEquipmentType.MagicSource, EquipmentType.MagicSource },
        { FamiliarEquipmentType.Footgear, EquipmentType.Footgear },
        { FamiliarEquipmentType.Legs, EquipmentType.Legs },
        { FamiliarEquipmentType.Gloves, EquipmentType.Gloves }
    };
    public static IReadOnlyDictionary<FamiliarEquipmentType, EquipmentType> FamiliarEquipmentMap => _familiarEquipmentMap;
    public class ActiveFamiliarData
    {
        public Entity Familiar { get; set; } = Entity.Null;
        public Entity Servant { get; set; } = Entity.Null;
        public int FamiliarId { get; set; } = 0;
        public bool Dismissed { get; set; } = false;
        public bool IsBinding { get; set; } = false;
    }
    public const string PLAYER_MAX_ACTIVE_FAMILIARS_KEY = "MaxActiveFamiliars";

    public static int GetPlayerMaxActiveFamiliars(ulong steamId)
    {
        var defaults = new Dictionary<string, int>() { [PLAYER_MAX_ACTIVE_FAMILIARS_KEY] = ConfigService.MaxActiveFamiliars };
        var settings = DataService.PlayerSettingsManager.GetOrInitializePlayerSettings(steamId, defaults);
        return settings.TryGetValue(PLAYER_MAX_ACTIVE_FAMILIARS_KEY, out var val) ? val : ConfigService.MaxActiveFamiliars;
    }

    public static void SetPlayerMaxActiveFamiliars(ulong steamId, int value)
    {
        var settings = DataService.PlayerSettingsManager.LoadPlayerSettings(steamId);
        settings[PLAYER_MAX_ACTIVE_FAMILIARS_KEY] = value;
        DataService.PlayerSettingsManager.SavePlayerSettings(steamId, settings);
    }
    public const string PLAYER_AUTO_SELL_FAMILIARS_KEY = "AutoSellFamiliars";
    public static int GetAutoSellFamiliars(ulong steamId)
    {
        var defaults = new Dictionary<string, int>() { [PLAYER_AUTO_SELL_FAMILIARS_KEY] = 0 };
        var settings = DataService.PlayerSettingsManager.GetOrInitializePlayerSettings(steamId, defaults);
        return settings.TryGetValue(PLAYER_AUTO_SELL_FAMILIARS_KEY, out var val) ? val : 0;
    }
    public static void SetAutoSellFamiliars(ulong steamId, int value)
    {
        var settings = DataService.PlayerSettingsManager.LoadPlayerSettings(steamId);
        settings[PLAYER_AUTO_SELL_FAMILIARS_KEY] = value;
        DataService.PlayerSettingsManager.SavePlayerSettings(steamId, settings);
    }

    public static class ActiveFamiliarManager
    {
        // Support multiple active familiars per player
        static readonly ConcurrentDictionary<ulong, List<ActiveFamiliarData>> _familiarActives = new();
        static readonly ConcurrentDictionary<ulong, bool> _bindingStates = new();

        public static IReadOnlyDictionary<ulong, List<ActiveFamiliarData>> ActiveFamiliars => _familiarActives;

        public static List<ActiveFamiliarData> GetActiveFamiliars(ulong steamId)
        {
            if (!_familiarActives.TryGetValue(steamId, out var list))
            {
                list = CreateActiveFamiliarList(steamId);
            }

            return list;
        }

        // Backwards compatible: return the first active familiar (if any)
        public static ActiveFamiliarData GetActiveFamiliarData(ulong steamId)
        {
            var list = GetActiveFamiliars(steamId);
            if (list == null || list.Count == 0)
            {
                var empty = new ActiveFamiliarData();
                _familiarActives[steamId] = new List<ActiveFamiliarData>() { empty };
                return empty;
            }

            return list[0];
        }

        public static void UpdateActiveFamiliarData(ulong steamId, Entity familiar, Entity servant, int familiarId, bool isDismissed = false)
        {
            var list = GetActiveFamiliars(steamId);

            var existing = list.Find(x => x.FamiliarId == familiarId || x.Familiar == familiar);
            if (existing != null)
            {
                existing.Familiar = familiar;
                existing.Servant = servant;
                existing.FamiliarId = familiarId;
                existing.Dismissed = isDismissed;
            }
            else
            {
                var data = new ActiveFamiliarData()
                {
                    Familiar = familiar,
                    Servant = servant,
                    FamiliarId = familiarId,
                    Dismissed = isDismissed
                };

                list.Add(data);
            }

            _familiarActives[steamId] = list;
        }

        static List<ActiveFamiliarData> CreateActiveFamiliarList(ulong steamId)
        {
            var list = new List<ActiveFamiliarData>();

            if (steamId.TryGetPlayerInfo(out PlayerInfo playerInfo))
            {
                Entity familiar = FindActiveFamiliar(playerInfo.CharEntity);
                if (familiar.Exists())
                {
                    Entity servant = FindFamiliarServant(familiar);
                    var data = new ActiveFamiliarData()
                    {
                        Familiar = familiar,
                        Servant = servant,
                        FamiliarId = familiar.GetPrefabGuid().GuidHash
                    };

                    list.Add(data);
                }
            }

            _familiarActives[steamId] = list;
            return list;
        }

        public static void UpdateActiveFamiliarDismissed(ulong steamId, bool dismissed)
        {
            if (_familiarActives.TryGetValue(steamId, out var list))
            {
                foreach (var data in list) data.Dismissed = dismissed;
                _familiarActives[steamId] = list;
            }
        }

        public static void UpdateActiveFamiliarDismissed(ulong steamId, Entity familiar, bool dismissed)
        {
            if (_familiarActives.TryGetValue(steamId, out var list))
            {
                var existing = list.Find(x => x.Familiar == familiar || x.FamiliarId == familiar.GetGuidHash());
                if (existing != null)
                {
                    existing.Dismissed = dismissed;
                    _familiarActives[steamId] = list;
                }
            }
        }

        public static void RemoveActiveFamiliar(ulong steamId, Entity familiar)
        {
            if (_familiarActives.TryGetValue(steamId, out var list))
            {
                list.RemoveAll(x => x.Familiar == familiar || x.FamiliarId == familiar.GetGuidHash());
                _familiarActives[steamId] = list;
            }
        }

        public static void UpdateActiveFamiliarBinding(ulong steamId, bool isBinding)
        {
            _bindingStates[steamId] = isBinding;
        }

        public static bool IsBinding(ulong steamId)
        {
            return _bindingStates.TryGetValue(steamId, out var v) && v;
        }

        public static bool HasActiveFamiliar(ulong steamId)
        {
            var list = GetActiveFamiliars(steamId);
            return list != null && list.Any(x => x.Familiar.Exists());
        }

        public static bool HasDismissedFamiliar(ulong steamId)
        {
            var list = GetActiveFamiliars(steamId);
            return list != null && list.Any(x => x.Familiar.Exists() && x.Dismissed);
        }

        public static void ResetActiveFamiliarData(ulong steamId)
        {
            _familiarActives[steamId] = new List<ActiveFamiliarData>();
        }
    }

    public static readonly Dictionary<string, PrefabGUID> VBloodNamePrefabGuidMap = new()
    {
        { "Mairwyn the Elementalist", new(-2013903325) },
        { "Clive the Firestarter", new(1896428751) },
        { "Rufus the Foreman", new(2122229952) },
        { "Grayson the Armourer", new(1106149033) },
        { "Errol the Stonebreaker", new(-2025101517) },
        { "Quincey the Bandit King", new(-1659822956) },
        { "Lord Styx the Night Champion", new(1112948824) },
        { "Gorecrusher the Behemoth", new(-1936575244) },
        { "Albert the Duke of Balaton", new(-203043163) },
        { "Matka the Curse Weaver", new(-910296704) },
        { "Alpha the White Wolf", new(-1905691330) },
        { "Terah the Geomancer", new(-1065970933) },
        { "Morian the Stormwing Matriarch", new(685266977) },
        { "Talzur the Winged Horror", new(-393555055) },
        { "Raziel the Shepherd", new(-680831417) },
        { "Vincent the Frostbringer", new(-29797003) },
        { "Octavian the Militia Captain", new(1688478381) },
        { "Meredith the Bright Archer", new(850622034) },
        { "Ungora the Spider Queen", new(-548489519) },
        { "Goreswine the Ravager", new(577478542) },
        { "Leandra the Shadow Priestess", new(939467639) },
        { "Cyril the Cursed Smith", new(326378955) },
        { "Bane the Shadowblade", new(613251918) },
        { "Kriig the Undead General", new(-1365931036) },
        { "Nicholaus the Fallen", new(153390636) },
        { "Foulrot the Soultaker", new(-1208888966) },
        { "Putrid Rat", new(-2039908510) },
        { "Jade the Vampire Hunter", new(-1968372384) },
        { "Tristan the Vampire Hunter", new(-1449631170) },
        { "Ben the Old Wanderer", new(109969450) },
        { "Beatrice the Tailor", new(-1942352521) },
        { "Frostmaw the Mountain Terror", new(24378719) },
        { "Terrorclaw the Ogre", new(-1347412392) },
        { "Keely the Frost Archer", new(1124739990)},
        { "Lidia the Chaos Archer", new(763273073)},
        { "Finn the Fisherman", new(-2122682556)},
        { "Azariel the Sunbringer", new(114912615)},
        { "Sir Magnus the Overseer", new(-26105228)},
        { "Baron du Bouchon the Sommelier", new(192051202)},
        { "Solarus the Immaculate", new(-740796338)},
        { "Kodia the Ferocious Bear", new(-1391546313)},
        { "Ziva the Engineer", new(172235178)},
        { "Adam the Firstborn", new(1233988687)},
        { "Angram the Purifier", new(106480588)},
        { "Voltatia the Power Master", new(2054432370)},
        { "Henry Blackbrew the Doctor", new(814083983)},
        { "Domina the Blade Dancer", new(-1101874342)},
        { "Grethel the Glassblower", new(910988233)},
        { "Christina the Sun Priestess", new(-99012450)},
        { "Maja the Dark Savant", new(1945956671)},
        { "Polora the Feywalker", new(-484556888)},
        { "Simon Belmont the Vampire Hunter", new(336560131)},
        { "General Valencia the Depraved", new(495971434)},
        { "Dracula the Immortal King", new(-327335305)},
        { "General Cassius the Betrayer", new(-496360395)},
        { "General Elena the Hollow", PrefabGUIDs.CHAR_Vampire_IceRanger_VBlood},
        { "Willfred the Village Elder", PrefabGUIDs.CHAR_WerewolfChieftain_Human},
        { "Sir Erwin the Gallant Cavalier", PrefabGUIDs.CHAR_Militia_Fabian_VBlood},
        { "Gaius the Cursed Champion", PrefabGUIDs.CHAR_Undead_ArenaChampion_VBlood},
        { "Stavros the Carver", PrefabGUIDs.CHAR_Blackfang_CarverBoss_VBlood},
        { "Dantos the Forgebinder", PrefabGUIDs.CHAR_Blackfang_Valyr_VBlood},
        { "Lucile the Venom Alchemist", PrefabGUIDs.CHAR_Blackfang_Lucie_VBlood},
        { "Jakira the Shadow Huntress", PrefabGUIDs.CHAR_Blackfang_Livith_VBlood},
        { "Megara the Serpent Queen", PrefabGUIDs.CHAR_Blackfang_Morgana_VBlood}
    };

    public static readonly ConcurrentDictionary<Entity, Entity> AutoCallMap = [];
    public static bool HasActiveFamiliar(this ulong steamId)
    {
        return ActiveFamiliarManager.HasActiveFamiliar(steamId);
    }
    public static bool HasDismissedFamiliar(this ulong steamId)
    {
        return ActiveFamiliarManager.HasDismissedFamiliar(steamId);
    }
    public static bool IsBinding(this ulong steamId)
    {
        return ActiveFamiliarManager.IsBinding(steamId);
    }
    public static Entity FindActiveFamiliar(Entity playerCharacter)
    {
        if (playerCharacter.TryGetBuffer<FollowerBuffer>(out var followers) && !followers.IsEmpty)
        {
            foreach (FollowerBuffer follower in followers)
            {
                Entity familiar = follower.Entity._Entity;
                if (familiar.Has<BlockFeedBuff>()) return familiar;
            }
        }

        return Entity.Null;
    }
    public static Entity FindFamiliarServant(Entity familiar)
    {
        if (familiar.TryGetBuffer<FollowerBuffer>(out var followers) && !followers.IsEmpty)
        {
            foreach (FollowerBuffer follower in followers)
            {
                Entity servant = follower.Entity._Entity;
                if (servant.Has<BlockFeedBuff>()) return servant;
            }
        }

        return Entity.Null;
    }
    public static Entity GetActiveFamiliar(Entity playerCharacter)
    {
        if (!playerCharacter.Exists()) return Entity.Null;

        ulong steamId = playerCharacter.GetSteamId();

        // Prefer any active familiar stored in ActiveFamiliarManager that exists and is not dismissed
        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId);
        if (actives != null && actives.Count > 0)
        {
            var preferred = actives.FirstOrDefault(x => x.Familiar.Exists() && !x.Dismissed);
            if (preferred != null && preferred.Familiar.Exists()) return preferred.Familiar;

            var firstExisting = actives.FirstOrDefault(x => x.Familiar.Exists());
            if (firstExisting != null && firstExisting.Familiar.Exists()) return firstExisting.Familiar;
        }

        // Fall back to checking the follower buffer on the character (legacy behavior)
        Entity found = FindActiveFamiliar(playerCharacter);
        if (found.Exists()) return found;

        // Ensure we return something consistent with the ActiveFamiliarData API
        return GetActiveFamiliarData(steamId).Familiar;
    }

    public static Entity GetFamiliarServant(Entity playerCharacter)
    {
        if (!playerCharacter.Exists()) return Entity.Null;

        ulong steamId = playerCharacter.GetSteamId();

        // Try to pick the servant associated with the preferred active familiar
        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId);
        if (actives != null && actives.Count > 0)
        {
            var preferred = actives.FirstOrDefault(x => x.Familiar.Exists() && !x.Dismissed);
            if (preferred != null && preferred.Servant.Exists()) return preferred.Servant;

            var firstExistingServant = actives.FirstOrDefault(x => x.Servant.Exists());
            if (firstExistingServant != null && firstExistingServant.Servant.Exists()) return firstExistingServant.Servant;
        }

        // Fallback: derive servant from what FindActiveFamiliar returns
        Entity fam = GetActiveFamiliar(playerCharacter);
        if (fam.Exists())
        {
            Entity servant = FindFamiliarServant(fam);
            if (servant.Exists()) return servant;
        }

        return Entity.Null;
    }
    public static Entity GetServantFamiliar(Entity servant)
    {
        if (servant.TryGetComponent(out Follower follower))
        {
            Entity familiar = follower.Followed._Value;
            if (familiar.Has<BlockFeedBuff>()) return familiar;
        }

        return Entity.Null;
    }
    public static Entity GetServantCoffin(Entity servant)
    {
        Entity coffin = Entity.Null;

        if (!servant.TryGetComponent(out ServantConnectedCoffin servantConnectedCoffin)) return coffin;
        else coffin = servantConnectedCoffin.CoffinEntity.GetEntityOnServer();

        return coffin;
    }
    public static void SyncFamiliarServant(Entity familiar, Entity servant)
    {
        float familiarHealth = familiar.GetMaxHealth();
        int familiarLevel = familiar.GetUnitLevel();
        (float physicalPower, float spellPower) = familiar.GetPowerTuple();

        servant.With((ref Health health) =>
        {
            health.MaxHealth._Value = familiarHealth;
            health.Value = familiarHealth;
        });

        servant.With((ref ServantPower servantPower) =>
        {
            servantPower.GearLevel = familiarLevel;
            servantPower.Power = physicalPower;
            servantPower.Expertise = 0f;
        });
    }
    public static IEnumerator FamiliarSyncDelayRoutine(Entity familiar, Entity servant)
    {
        yield return _bindingDelay;

        if (!familiar.Exists() || !servant.Exists()) yield break;

        float familiarHealth = familiar.GetMaxHealth();
        int familiarLevel = familiar.GetUnitLevel();
        (float physicalPower, float spellPower) = familiar.GetPowerTuple();

        servant.With((ref Health health) =>
        {
            health.MaxHealth._Value = familiarHealth;
            health.Value = familiarHealth;
        });

        servant.With((ref ServantPower servantPower) =>
        {
            servantPower.GearLevel = familiarLevel;
            servantPower.Power = physicalPower;
            servantPower.Expertise = 0f;
        });
    }
    public static void HandleFamiliarMinions(Entity familiar)
    {
        if (FamiliarMinions.TryRemove(familiar, out HashSet<Entity> familiarMinions))
        {
            foreach (Entity minion in familiarMinions)
            {
                minion.Destroy();
            }
        }
    }
    public static void ParseAddedFamiliar(ChatCommandContext ctx, ulong steamId, string unit, string activeBox = "")
    {
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (int.TryParse(unit, out int prefabHash) && PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(new(prefabHash), out Entity prefabEntity))
        {
            // Add to set if valid
            if (!prefabEntity.Read<PrefabGUID>().GetPrefabName().StartsWith("CHAR"))
            {
                LocalizationService.HandleReply(ctx, "Invalid unit prefab (match found but does not start with CHAR/char).");
                return;
            }

            data.FamiliarUnlocks[activeBox].Add(prefabHash);
            SaveFamiliarUnlocksData(steamId, data);

            LocalizationService.HandleReply(ctx, $"<color=green>{new PrefabGUID(prefabHash).GetLocalizedName()}</color> added to <color=white>{activeBox}</color>.");
        }
        else if (unit.StartsWith("char", StringComparison.CurrentCultureIgnoreCase)) // search for full and/or partial name match
        {
            // Try using TryGetValue for an exact match (case-sensitive)
            if (!PrefabCollectionSystem.SpawnableNameToPrefabGuidDictionary.TryGetValue(unit, out PrefabGUID match))
            {
                // If exact match is not found, do a case-insensitive search for full or partial matches
                foreach (var kvp in LocalizationService.PrefabGuidNames)
                {
                    // Check for a case-insensitive full match
                    if (kvp.Value.Equals(unit, System.StringComparison.CurrentCultureIgnoreCase))
                    {
                        match = kvp.Key; // Full match found
                        break;
                    }
                }
            }

            // verify prefab is a char unit
            if (!match.IsEmpty() && PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(match, out prefabEntity))
            {
                if (!prefabEntity.Read<PrefabGUID>().GetPrefabName().StartsWith("CHAR"))
                {
                    LocalizationService.HandleReply(ctx, "Invalid unit name (match found but does not start with CHAR/char).");
                    return;
                }

                data.FamiliarUnlocks[activeBox].Add(match.GuidHash);
                SaveFamiliarUnlocksData(steamId, data);

                LocalizationService.HandleReply(ctx, $"<color=green>{match.GetLocalizedName()}</color> (<color=yellow>{match.GuidHash}</color>) added to <color=white>{activeBox}</color>.");
            }
            else
            {
                LocalizationService.HandleReply(ctx, "Invalid unit name (no full or partial matches).");
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "Invalid prefab (not an integer) or name (does not start with CHAR/char).");
        }
    }
    public static void TryReturnFamiliar(Entity playerCharacter, Entity familiar)
    {
        float3 playerPosition = playerCharacter.GetPosition();
        float distance = Vector3.Distance(familiar.GetPosition(), playerPosition);

        if (distance >= 25f)
        {
            PreventDisabled(familiar);
            ReturnFamiliar(playerPosition, familiar);
        }

        /*
        if (familiar.ShouldRelocate())
        {
            Core.Log.LogWarning($"Familiar {familiar.GetPrefabGuid().GetLocalizedName()} stuck, relocating!");
            PreventDisableFamiliar(familiar);
            ReturnFamiliar(playerPosition, familiar);
        }
        */
    }
    static bool ShouldRelocate(this Entity familiar)
    {
        if (familiar.TryGetComponent(out BehaviourTreeState behaviourTreeState) && behaviourTreeState.Value.Equals(GenericEnemyState.Relocate_Unstuck))
        {
            return true;
        }

        return false;
    }
    public static void SetPreCombatPosition(Entity playerCharacter, Entity familiar)
    {
        familiar.With((ref AggroConsumer aggroConsumer) => aggroConsumer.PreCombatPosition = playerCharacter.GetPosition());
    }
    public static void HandleFamiliarEnteringCombat(Entity playerCharacter, Entity familiar)
    {
        if (familiar.HasBuff(_interactModeBuff))
        {
            User user = playerCharacter.GetUser();
            ulong steamId = user.PlatformId;

            EmoteSystemPatch.InteractMode(user, playerCharacter, steamId);
        }

        familiar.With((ref Follower follower) => follower.ModeModifiable._Value = 1);

        SetPreCombatPosition(playerCharacter, familiar);
        TryReturnFamiliar(playerCharacter, familiar);
    }

    /// <summary>
    /// Apply the normal "entering combat" logic to all active familiars for a player.
    /// This ensures additional familiars also auto-enter combat and sync aggro.
    /// </summary>
    public static void HandlePlayerEnteringCombat(Entity playerCharacter)
    {
        if (!playerCharacter.Exists()) return;

        User user = playerCharacter.GetUser();

        ulong steamId = user.PlatformId;
        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId);
        if (actives == null) return;

        foreach (var data in actives)
        {
            var fam = data.Familiar;
            if (!fam.Exists()) continue;

            HandleFamiliarEnteringCombat(playerCharacter, fam);
            SyncAggro(playerCharacter, fam);
        }
    }
    public static void ReturnFamiliar(float3 position, Entity familiar)
    {
        familiar.With((ref LastTranslation lastTranslation) => lastTranslation.Value = position);

        familiar.With((ref Translation translation) => translation.Value = position);

        familiar.With((ref AggroConsumer aggroConsumer) => aggroConsumer.PreCombatPosition = position);
    }

    // mmm these seem redundant? note to remove or otherwise rethink
    public static void ToggleShinies(ChatCommandContext ctx, ulong steamId)
    {
        TogglePlayerBool(steamId, SHINY_FAMILIARS_KEY);
        LocalizationService.HandleReply(ctx, GetPlayerBool(steamId, SHINY_FAMILIARS_KEY) ? "Shiny familiars <color=green>enabled</color>." : "Shiny familiars <color=red>disabled</color>.");
    }
    public static void ToggleVBloodEmotes(ChatCommandContext ctx, ulong steamId)
    {
        TogglePlayerBool(steamId, VBLOOD_EMOTES_KEY);
        LocalizationService.HandleReply(ctx, GetPlayerBool(steamId, VBLOOD_EMOTES_KEY) ? "VBlood emotes <color=green>enabled</color>." : "VBlood emotes <color=red>disabled</color>.");
    }

    public static void ToggleAutoRemove(ChatCommandContext ctx, ulong steamId)
    {
        TogglePlayerBool(steamId, AUTOREMOVE_KEY);
        bool current = GetPlayerBool(steamId, AUTOREMOVE_KEY);

        if (current)
        {
            // When AutoRemove is enabled, ensure AutoSellFamiliars is set to 1
            SetAutoSellFamiliars(steamId, 1);
            LocalizationService.HandleReply(ctx, "自動賣出寵物已 <color=green>啟用</color>。");
        }
        else
        {
            SetAutoSellFamiliars(steamId, 0);
            LocalizationService.HandleReply(ctx, "自動賣出寵物已 <color=red>停用</color>。");
        }
    }
    public static void CallFamiliar(Entity playerCharacter, Entity familiar, User user, ulong steamId)
    {
        familiar.Remove<Disabled>();
        PreventDisabled(familiar);

        float3 position = playerCharacter.GetPosition();
        ReturnFamiliar(position, familiar);

        familiar.With((ref Follower follower) =>
        {
            follower.Followed._Value = playerCharacter;
            follower.ModeModifiable._Value = 0; // leash until combat again, if still in combat see if this works to clear previous target? seems maybe to be doing that, noting for further... uh, notes
        });

        if (_familiarCombat && !familiar.HasBuff(_invulnerableBuff))
        {
            familiar.TryRemoveBuff(buffPrefabGuid: _disableAggroBuff);
        }

        UpdateActiveFamiliarDismissed(steamId, familiar, false);

        string message = "<color=yellow>Familiar</color> <color=green>enabled</color>!";
        LocalizationService.HandleServerReply(EntityManager, user, message);
    }
    public static void DismissFamiliar(Entity playerCharacter, Entity familiar, User user, ulong steamId)
    {
        if (familiar.HasBuff(_vanishBuff))
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Can't dismiss familiar when binding/unbinding!");
            return;
        }

        HandleFamiliarMinions(familiar);

        familiar.With((ref Follower follower) => follower.Followed._Value = Entity.Null);

        var buffer = playerCharacter.ReadBuffer<FollowerBuffer>();
        for (int i = 0; i < buffer.Length; i++)
        {
            if (buffer[i].Entity._Entity.Equals(familiar))
            {
                buffer.RemoveAt(i);
                break;
            }
        }

        PreventDisabled(familiar);
        familiar.Add<Disabled>();

        UpdateActiveFamiliarDismissed(steamId, familiar, true);

        string message = "<color=yellow>Familiar</color> <color=red>disabled</color>!";
        LocalizationService.HandleServerReply(EntityManager, user, message);
    }

    /// <summary>
    /// Recall all currently active familiars for the player (calls each familiar and clears dismissed state).
    /// Returns the number of familiars recalled.
    /// </summary>
    public static int RecallActiveFamiliars(Entity playerCharacter, User user)
    {
        if (!playerCharacter.Exists()) return 0;

        ulong steamId = user.PlatformId;
        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList();
        if (actives == null || actives.Count == 0) return 0;

        int recalled = 0;
        foreach (var data in actives)
        {
            var fam = data.Familiar;
            if (!fam.Exists()) continue;

            CallFamiliar(playerCharacter, fam, user, steamId);
            ActiveFamiliarManager.UpdateActiveFamiliarDismissed(steamId, fam, false);
            recalled++;
        }

        return recalled;
    }
    public static string GetShinyInfo(FamiliarBuffsData buffsData, Entity familiar, int familiarId)
    {
        if (buffsData.FamiliarBuffs.ContainsKey(familiarId))
        {
            PrefabGUID shinyBuff = new(buffsData.FamiliarBuffs[familiarId].FirstOrDefault());

            if (ShinyBuffSpellSchools.TryGetValue(shinyBuff, out string spellSchool) && familiar.TryGetBuffStacks(shinyBuff, out Entity _, out int stacks))
            {
                return $"{spellSchool}[<color=white>{stacks}</color>]";
            }
        }

        return string.Empty;
    }
    public static void NothingLivesForever(this Entity unit, float duration = FAMILIAR_LIFETIME)
    {
        if (unit.TryApplyAndGetBuff(_inkCrawlerDeathBuff, out Entity buffEntity))
        {
            buffEntity.With((ref LifeTime lifeTime) => lifeTime.Duration = duration);

            PrefabGUID unitPrefabGuid = unit.GetPrefabGuid();

            if ((unitPrefabGuid.Equals(_spiritDouble) || unitPrefabGuid.Equals(_highlordGroundSword)) && unit.Has<Immortal>())
            {
                unit.With((ref Immortal immortal) => immortal.IsImmortal = false);
            }
        }
    }
    public static void DisableAggro(Entity familiar)
    {
        if (familiar.Has<AggroConsumer>())
        {
            familiar.With((ref AggroConsumer aggroConsumer) => aggroConsumer.Active._Value = false);
        }
    }
    public static void EnableAggro(Entity familiar)
    {
        if (familiar.Has<AggroConsumer>())
        {
            familiar.With((ref AggroConsumer aggroConsumer) => aggroConsumer.Active._Value = true);
        }
    }
    public static void EnableAggroable(this Entity entity)
    {
        if (entity.Has<Aggroable>())
        {
            entity.With((ref Aggroable aggroable) =>
            {
                aggroable.Value._Value = true;
                aggroable.DistanceFactor._Value = 1f;
                aggroable.AggroFactor._Value = 1f;
            });
        }
    }
    public static void DisableAggroable(this Entity entity)
    {
        if (entity.Has<Aggroable>())
        {
            entity.With((ref Aggroable aggroable) =>
            {
                aggroable.Value._Value = false;
                aggroable.DistanceFactor._Value = 0f;
                aggroable.AggroFactor._Value = 0f;
            });
        }
    }
    public static void BindFamiliar(User user, Entity playerCharacter, int boxIndex = -1)
    {
        ulong steamId = user.PlatformId;
        if (steamId.IsBinding())
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Familiar binding already in progress!");
            return;
        }

        var currentActives = ActiveFamiliarManager.GetActiveFamiliars(steamId).Where(x => x.Familiar.Exists()).ToList();
        int maxForPlayer = GetPlayerMaxActiveFamiliars(steamId);
        if (currentActives.Count >= maxForPlayer)
        {
            LocalizationService.HandleServerReply(EntityManager, user, $"You already have {currentActives.Count} active familiar(s) (limit: {maxForPlayer}). Dismiss/unbind one first.");
            return;
        }
        else if (playerCharacter.HasBuff(_pveCombatBuff) || playerCharacter.HasBuff(_dominateBuff) || playerCharacter.HasBuff(_takeFlightBuff) || playerCharacter.HasBuff(_pvpCombatBuff))
        {
            LocalizationService.HandleServerReply(EntityManager, user, "You can't bind in combat or when using certain forms! (dominating presence, bat)");
            return;
        }

        string box = steamId.TryGetFamiliarBox(out box) ? box : string.Empty;

        if (string.IsNullOrEmpty(box))
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Couldn't find active box! Use '<color=white>.fam listboxes</color>' and select one with '<color=white>.fam cb [BoxName]</color>");
            return;
        }
        else if (LoadFamiliarUnlocksData(steamId).FamiliarUnlocks.TryGetValue(box, out var famKeys))
        {
            if (boxIndex == -1 && steamId.TryGetBindingIndex(out boxIndex))
            {
                if (boxIndex < 1 || boxIndex > famKeys.Count)
                {
                    LocalizationService.HandleServerReply(EntityManager, user, $"Invalid index for active box, try binding or smartbind via command.");
                    return;
                }

                int famKey = famKeys[boxIndex - 1];
                var allActives = ActiveFamiliarManager.GetActiveFamiliars(steamId);
                if (allActives != null && allActives.Any(x => x.FamiliarId == famKey))
                {
                    var existing = allActives.FirstOrDefault(x => x.FamiliarId == famKey);
                    if (existing != null && existing.Familiar.Exists())
                    {
                        CallFamiliar(playerCharacter, existing.Familiar, user, steamId);
                        LocalizationService.HandleServerReply(EntityManager, user, "Familiar recalled!");
                        return;
                    }
                    // If there's a matching entry but no entity present (stale), allow binding to proceed
                }

                ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, true);
                InstantiateFamiliarRoutine(user, playerCharacter, famKey).Start();
            }
            else if (boxIndex == -1)
            {
                LocalizationService.HandleServerReply(EntityManager, user, $"Couldn't find binding preset, try binding or smartbind via command.");
            }
            else if (boxIndex < 1 || boxIndex > famKeys.Count)
            {
                LocalizationService.HandleServerReply(EntityManager, user, $"Invalid index, use <color=white>1</color>-<color=white>{famKeys.Count}</color>! (Active Box - <color=yellow>{box}</color>)");
            }
            else
            {
                int famKey = famKeys[boxIndex - 1];
                var allActives = ActiveFamiliarManager.GetActiveFamiliars(steamId);
                if (allActives != null && allActives.Any(x => x.FamiliarId == famKey))
                {
                    var existing = allActives.FirstOrDefault(x => x.FamiliarId == famKey);
                    if (existing != null && existing.Familiar.Exists())
                    {
                        CallFamiliar(playerCharacter, existing.Familiar, user, steamId);
                        LocalizationService.HandleServerReply(EntityManager, user, "Familiar recalled!");
                        return;
                    }
                    // If there's a matching entry but no entity present (stale), allow binding to proceed
                }

                steamId.SetBindingIndex(boxIndex);
                ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, true);
                InstantiateFamiliarRoutine(user, playerCharacter, famKey).Start();
            }
        }
        else
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Couldn't find familiar actives or familiar already active! If this doesn't seem right try using '<color=white>.fam reset</color>'.");
        }
    }
    public static void UnbindFamiliar(User user, Entity playerCharacter, bool smartBind = false, int index = -1)
    {
        ulong steamId = user.PlatformId;

        if (steamId.IsBinding())
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Cannot unbind while a binding is in progress!");
            return;
        }

        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList() ?? new List<ActiveFamiliarData>();

        if (!actives.Any())
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Couldn't find familiar to unbind! If this doesn't seem right try using '<color=white>.fam reset</color>'.");
            return;
        }

        // allow unbind to proceed even if some familiars are dismissed; we want .cw ub to clear them as well

        // don't allow unbind if any active familiar is interacting
        if (actives.Any(a => a.Familiar.HasBuff(_interactModeBuff)))
        {
            LocalizationService.HandleServerReply(EntityManager, user, "Can't unbind familiar right now! (interacting)");
            return;
        }

        // If an index is provided, target only the familiar corresponding to that index (smart unbind)
        if (index != -1)
        {
            // resolve player's active box and selected prefab
            if (!steamId.TryGetFamiliarBox(out var box) || !LoadFamiliarUnlocksData(steamId).FamiliarUnlocks.TryGetValue(box, out var famKeys))
            {
                LocalizationService.HandleServerReply(EntityManager, user, "Couldn't find active box or binding preset for the requested index.");
                return;
            }

            if (index < 1 || index > famKeys.Count)
            {
                LocalizationService.HandleServerReply(EntityManager, user, "Invalid index for unbind.");
                return;
            }

            int famKey = famKeys[index - 1];
            var target = actives.Find(a => a.FamiliarId == famKey);
            if (target == null || !target.Familiar.Exists())
            {
                LocalizationService.HandleServerReply(EntityManager, user, "Couldn't find a summoned familiar matching that index.");
                return;
            }

            if (target.Familiar.HasBuff(_interactModeBuff))
            {
                LocalizationService.HandleServerReply(EntityManager, user, "Can't unbind familiar right now! (interacting)");
                return;
            }

            ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, true);

            var familiar = target.Familiar;
            familiar.TryApplyBuff(_vanishBuff);
            familiar.TryApplyBuff(_disableAggroBuff);
            familiar.TryRemoveBuff(buffPrefabGuid: _bonusStatsBuff);

            UnbindFamiliarDelayRoutine(user, playerCharacter, familiar, smartBind, index).Start();
            return;
        }

        // mark binding state and start unbind routines for each active familiar
        ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, true);

        foreach (var data in actives)
        {
            var familiar = data.Familiar;

            familiar.TryApplyBuff(_vanishBuff);
            familiar.TryApplyBuff(_disableAggroBuff);
            familiar.TryRemoveBuff(buffPrefabGuid: _bonusStatsBuff);

            UnbindFamiliarDelayRoutine(user, playerCharacter, familiar, smartBind, index).Start();
        }
    }
    static IEnumerator UnbindFamiliarDelayRoutine(User user, Entity playerCharacter, Entity familiar,
        bool smartBind = false, int index = -1)
    {
        yield return _delay;

        PrefabGUID prefabGuid = familiar.GetPrefabGuid();
        ulong steamId = user.PlatformId;
        if (prefabGuid.IsEmpty())
        {
            ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, false);
            yield break;
        }

        int famKey = prefabGuid.GuidHash;

        FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);
        string shinyHexColor = "";

        if (buffsData.FamiliarBuffs.ContainsKey(famKey))
        {
            if (ShinyBuffColorHexes.TryGetValue(new(buffsData.FamiliarBuffs[famKey].First()), out var hexColor))
            {
                shinyHexColor = $"<color={hexColor}>";
            }
        }

        HandleFamiliarMinions(familiar);
        SaveFamiliarEquipment(steamId, famKey, UnequipFamiliar(playerCharacter));

        familiar.Remove<Disabled>();
        if (AutoCallMap.ContainsKey(playerCharacter)) AutoCallMap.TryRemove(playerCharacter, out var _);

        familiar.Destroy();
        ActiveFamiliarManager.RemoveActiveFamiliar(steamId, familiar);
        ActiveFamiliarManager.UpdateActiveFamiliarBinding(steamId, false);

        string message = !string.IsNullOrEmpty(shinyHexColor) ? $"<color=green>{prefabGuid.GetLocalizedName()}</color>{shinyHexColor}*</color> <color=#FFC0CB>離開</color>!" : $"<color=green>{prefabGuid.GetLocalizedName()}</color> <color=#FFC0CB>離開</color>!";
        LocalizationService.HandleServerReply(EntityManager, user, message);

        if (smartBind)
        {
            yield return _bindingDelay;

            BindFamiliar(user, playerCharacter, index);
        }
    }

    const float MAX_AGGRO_RANGE = 25f;
    const float DISTANCE_AGGRO_BASE = 100f;
    public static void SyncAggro(Entity playerCharacter, Entity familiar)
    {
        if (!playerCharacter.TryGetBuffer<InverseAggroBufferElement>(out var inverseAggroBuffer) || inverseAggroBuffer.IsEmpty) return;

        List<Entity> targets = [];

        foreach (InverseAggroBufferElement aggroBufferElement in inverseAggroBuffer)
        {
            Entity target = aggroBufferElement.Entity;
            if (target.Exists()) targets.Add(target);
        }

        AddToFamiliarAggroBuffer(playerCharacter, familiar, targets);
    }
    public static void AddToFamiliarAggroBuffer(Entity playerCharacter, Entity familiar, List<Entity> targets)
    {
        if (!familiar.TryGetBuffer<AggroBuffer>(out var buffer)) return;

        // Core.Log.LogWarning($"Adding to aggro buffer for {familiar.GetPrefabGuid().GetLocalizedName()}");

        List<Entity> entities = [];
        foreach (AggroBuffer aggroBufferEntry in buffer)
        {
            entities.Add(aggroBufferEntry.Entity);
        }

        foreach (Entity target in targets)
        {
            if (entities.Contains(target)) continue;
            else if (target.GetPrefabGuid().Equals(_enchantedCross)) continue;
            else if (target.TryGetComponent(out BloodConsumeSource bloodConsumeSource)
                && bloodConsumeSource.BloodQuality >= BLOOD_QUALITY_IGNORE) continue;

            float distance = Vector3.Distance(playerCharacter.GetPosition(), target.GetPosition());
            float distanceFactor = Mathf.Clamp01(1f - (distance / MAX_AGGRO_RANGE));

            float baseAggro = target.IsVBloodOrGateBoss() ? 400f : 100f;
            float aggroValue = baseAggro + (DISTANCE_AGGRO_BASE * distanceFactor);

            AggroBuffer aggroBufferElement = new()
            {
                DamageValue = aggroValue,
                Entity = target,
                Weight = 1f
            };

            buffer.Add(aggroBufferElement);
        }

        /*
        if (target.GetPrefabGuid().Equals(_enchantedCross)) return; // see if works to ignore

        bool targetInBuffer = false;

        foreach (AggroBuffer aggroBufferEntry in buffer)
        {
            if (aggroBufferEntry.Entity.Equals(target))
            {
                targetInBuffer = true;
                break;
            }
        }

        if (targetInBuffer) return;
        else if (target.TryGetComponent(out BloodConsumeSource bloodConsumeSource)
            && bloodConsumeSource.BloodQuality >= BLOOD_QUALITY_IGNORE) return; // make sure this doesn't have unintended effects on targeting vBloods or something | seems good?

        float distance = Vector3.Distance(playerCharacter.GetPosition(), target.GetPosition());
        float distanceFactor = Mathf.Clamp01(1f - (distance / MAX_AGGRO_RANGE)); // Closer = 1, Far = 0

        float baseAggro = target.IsVBloodOrGateBoss() ? 400f : 100f;
        float aggroValue = baseAggro + (DISTANCE_AGGRO_BASE * distanceFactor);

        AggroBuffer aggroBufferElement = new()
        {
            DamageValue = aggroValue,
            Entity = target,
            Weight = 1f
        };

        buffer.Add(aggroBufferElement);
        */
    }
    public static void FaceYourEnemy(Entity familiar, Entity target)
    {
        if (familiar.Has<EntityInput>())
        {
            familiar.With((ref EntityInput entityInput) => entityInput.AimDirection = _southFloat3);
        }

        if (familiar.Has<TargetDirection>())
        {
            familiar.With((ref TargetDirection targetDirection) => targetDirection.AimDirection = _southFloat3);
        }

        if (familiar.TryApplyBuff(_defaultEmoteBuff) && familiar.TryGetBuff(_defaultEmoteBuff, out Entity buffEntity))
        {
            buffEntity.With((ref EntityOwner entityOwner) => entityOwner.Owner = target);
        }
    }
    public static bool EligibleForCombat(this Entity familiar)
    {
        return familiar.Exists() && !familiar.IsDisabled() && !familiar.HasBuff(_invulnerableBuff);
    }
    public static string GetFamiliarName(int familiarId, FamiliarBuffsData buffsData)
    {
        if (buffsData.FamiliarBuffs.ContainsKey(familiarId))
        {
            if (ShinyBuffColorHexes.TryGetValue(new(buffsData.FamiliarBuffs[familiarId].FirstOrDefault()), out string hexColor))
            {
                string colorCode = string.IsNullOrEmpty(hexColor) ? $"<color={hexColor}>" : string.Empty;
                return $"<color=green>{new PrefabGUID(familiarId).GetLocalizedName()}</color>{colorCode}*</color>";
            }
        }

        return $"<color=green>{new PrefabGUID(familiarId).GetLocalizedName()}</color>";
    }
    public static void HandleFamiliarPrestige(ChatCommandContext ctx, int clampedCost) // now supports prestige for multiple active familiars (consumes schematics per familiar)
    {
        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.User;

        ulong steamId = user.PlatformId;

        var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList();
        if (actives == null || actives.Count == 0)
        {
            LocalizationService.HandleReply(ctx, "找不到活動的寵物...");
            return;
        }

        FamiliarExperienceData xpData = LoadFamiliarExperienceData(steamId);
        FamiliarPrestigeData prestigeData = LoadFamiliarPrestigeData(steamId);

        bool anyPrestiged = false;

        foreach (var data in actives)
        {
            int familiarId = data.FamiliarId;

            if (!prestigeData.FamiliarPrestige.ContainsKey(familiarId))
            {
                prestigeData.FamiliarPrestige[familiarId] = 0;
                SaveFamiliarPrestigeData(steamId, prestigeData);
                prestigeData = LoadFamiliarPrestigeData(steamId);
            }

            if (prestigeData.FamiliarPrestige[familiarId] >= ConfigService.MaxFamiliarPrestiges)
            {
                LocalizationService.HandleReply(ctx, $"{GetFamiliarName(familiarId, LoadFamiliarBuffsData(steamId))} 已達到最大聲望次數！ (<color=white>{ConfigService.MaxFamiliarPrestiges}</color>)");
                continue;
            }

            // Attempt to remove schematics for this familiar's prestige; if removal fails, stop processing further familiars
            if (!ServerGameManager.TryRemoveInventoryItem(playerCharacter, _itemSchematic, clampedCost))
            {
                LocalizationService.HandleReply(ctx, "無法從你的背包中移除所需的圖紙（或資源不足）！");
                break;
            }

            int prestigeLevel = prestigeData.FamiliarPrestige[familiarId] + 1;
            prestigeData.FamiliarPrestige[familiarId] = prestigeLevel;
            SaveFamiliarPrestigeData(steamId, prestigeData);

            if (xpData.FamiliarExperience.ContainsKey(familiarId))
            {
                Entity familiar = data.Familiar;
                ModifyUnitStats(familiar, xpData.FamiliarExperience[familiarId].Key, steamId, familiarId);
            }

            LocalizationService.HandleReply(ctx, $"{GetFamiliarName(familiarId, LoadFamiliarBuffsData(steamId))} 已進行聲望晉升 [<color=#90EE90>{prestigeLevel}</color>]！");
            anyPrestiged = true;
        }

        if (!anyPrestiged)
        {
            // If nothing was prestiged, give a helpful message
            LocalizationService.HandleReply(ctx, "沒有寵物可以進行聲望晉升（可能是資源不足或已達到最大聲望），請確認條件或攜帶足夠的圖紙。");
        }
    }
    public static IEnumerator HandleFamiliarShapeshiftRoutine(User user, Entity playerCharacter, Entity familiar)
    {
        yield return _delay;

        try
        {
            HandleModifications(user, playerCharacter, familiar);
        }
        catch (Exception ex)
        {
            Core.Log.LogWarning(ex);
        }
    }
    public static void HandleFamiliarCastleMan(Entity buffEntity)
    {
        buffEntity.Remove<ScriptSpawn>();
        buffEntity.Remove<ScriptUpdate>();
        buffEntity.Remove<ScriptDestroy>();
        buffEntity.Remove<Script_Buff_ModifyDynamicCollision_DataServer>();
        buffEntity.Remove<Script_Castleman_AdaptLevel_DataShared>();
    }
    public static void DestroyFamiliarServant(Entity servant)
    {
        // Entity familiarServant = GetFamiliarServant(playerCharacter);
        // Entity servantCoffin = familiarServant.TryGetComponent(out ServantConnectedCoffin connectedCoffin) ? connectedCoffin.CoffinEntity.GetEntityOnServer() : Entity.Null;
        Entity coffin = GetServantCoffin(servant);

        /*
        if (servantCoffin.Exists())
        {
            servantCoffin.With((ref ServantCoffinstation coffinStation) =>
            {
                coffinStation.ConnectedServant._Entity = Entity.Null;
            });

            servantCoffin.Remove<Disabled>();
            servantCoffin.Destroy();
        }
        */

        // servant.Remove<Disabled>();
        StatChangeUtility.KillOrDestroyEntity(EntityManager, servant, Entity.Null, Entity.Null, Core.ServerTime, StatChangeReason.Default, true);
        // servant.Destroy();

        // servant.DropInventory();
        // servant.Destroy(VExtensions.DestroyMode.Delayed);

        if (coffin.Exists())
        {
            /*
            coffin.With((ref ServantCoffinstation coffinStation) =>
            {
                coffinStation.ConnectedServant._Entity = Entity.Null;
            });
            */

            coffin.Remove<Disabled>();
            coffin.Destroy();
        }
    }

    public static void UpFamiliarRarity(ulong steamId, int famKey, Rarity rarity)
    {
        var rarityData = LoadFamiliarRarityData(steamId);
        rarityData.FamiliarRarities[famKey] = rarity;
        SaveFamiliarRarityData(steamId, rarityData);
    }
}