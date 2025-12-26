using Bloodcraft.Resources;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using ProjectM.Network;
using Stunlock.Core;
using Unity.Entities;
using static Bloodcraft.Patches.DeathEventListenerSystemPatch;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarBuffsManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarExperienceManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarUnlocksManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarRarityManager;
using static Bloodcraft.Utilities.Familiars;

namespace Bloodcraft.Systems.Familiars;
internal static class FamiliarUnlockSystem
{
    static EntityManager EntityManager => Core.EntityManager;

    static readonly Random _random = new();

    static readonly float _unitUnlockChance = ConfigService.UnitUnlockChance;
    static readonly float _vBloodUnlockChance = ConfigService.VBloodUnlockChance;
    static readonly float _shinyChance = ConfigService.ShinyChance;

    static readonly bool _shareUnlocks = ConfigService.ShareUnlocks;
    static readonly bool _allowVBloods = ConfigService.AllowVBloods;

    // static readonly SequenceGUID _unlockSequence = SequenceGUIDs.SEQ_Charm_Channel;
    // static readonly SequenceGUID _unlockSequence = SequenceGUIDs.SEQ_WarEvent_UnitKilled_Projectile;
    // static readonly SequenceGUID _unlockSequence = SequenceGUIDs.SEQ_GeneralTeleport_Cast;
    // static readonly SequenceGUID _shinySequence = SequenceGUIDs.SEQ_MagicSource_BloodKey_Active_Buff;

    static readonly SequenceGUID _unlockSequence = SequenceGUIDs.SEQ_Shared_Object_Destroy_1;
    static readonly SequenceGUID _shinySequence = SequenceGUIDs.SEQ_Shared_Ability_Cast_420;

    public static readonly HashSet<PrefabGUID> ConfiguredPrefabGuidBans = [];
    public static readonly HashSet<UnitCategory> ConfiguredCategoryBans = [];
    static SystemService SystemService => Core.SystemService;
    static PrefabCollectionSystem PrefabCollectionSystem => SystemService.PrefabCollectionSystem;

    static readonly HashSet<string> _defaultNameBans =
    [
        "trader",
        "carriage",
        "horse",
        "tombsummon",
        "servant"
    ];

    static readonly HashSet<PrefabGUID> _defaultPrefabGuidBans =
    [
        PrefabGUIDs.CHAR_Undead_ArenaChampion_VBlood, // absolutely not
        PrefabGUIDs.CHAR_Militia_Fabian_VBlood,       // yes, but need time
        PrefabGUIDs.CHAR_GoldGolem                    // nope
    ];

    public static readonly Dictionary<PrefabGUID, string> ShinyBuffColorHexes = new()
    {
        { new(348724578), "#A020F0" },   // ignite purple (Hex: A020F0)
        { new(-1576512627), "#FFD700" },  // static yellow (Hex: FFD700)
        { new(-1246704569), "#FF0000" },  // leech red (Hex: FF0000)
        { new(1723455773), "#008080" },   // weaken teal (Hex: 008080)
        { new(27300215), "#00FFFF" },     // chill cyan (Hex: 00FFFF)
        { new(-325758519), "#00FF00" }    // condemn green (Hex: 00FF00)
    };

    public static readonly Dictionary<PrefabGUID, string> ShinyBuffSpellSchools = new()
    {
        { new(348724578), "<color=#A020F0>Chaos</color>" },   // ignite purple (Hex: A020F0)
        { new(-1576512627), "<color=#FFD700>Storm</color>" },  // static yellow (Hex: FFD700)
        { new(-1246704569), "<color=#FF0000>Blood</color>" },  // leech red (Hex: FF0000)
        { new(1723455773), "<color=#008080>Illusion</color>" },   // weaken teal (Hex: 008080)
        { new(27300215), "<color=#00FFFF>Frost</color>" },       // chill cyan (Hex: 00FFFF)
        { new(-325758519), "<color=#00FF00>Unholy</color>" }    // condemn green (Hex: 00FF00)
    };

    public enum Rarity
    {
        N = 0,
        R = 1,
        SR = 2,
        SSR = 3,
        SS = 4,
        SSS = 5,
        EX = 6
    }

    // --- Rarity definitions ---
    public record RarityInfo(string ChineseName, string ColorHex, float PowerPercent, int AutoRemoveRewardQty);

    public static readonly Dictionary<Rarity, RarityInfo> RarityInfoMap = new()
    {
        { Rarity.N, new("普通", "#9E9E9E", 0.5f, 1) },
        { Rarity.R, new("稀有", "#4CAF50", 0.3f, 1) },
        { Rarity.SR, new("精良", "#2196F3", 1f, 2) },
        { Rarity.SSR, new("史詩", "#9C27B0", 1.1f, 2) },
        { Rarity.SS, new("傳說", "#FFC107", 1.3f, 5) },
        { Rarity.SSS, new("神話", "#FF5252", 1.5f, 10) },
        { Rarity.EX, new("永恆", "#B11226", 10f, 100) }
    };

    public static void OnUpdate(object sender, DeathEventArgs deathEvent)
    {
        if (!_shareUnlocks) ProcessUnlock(deathEvent.Source, deathEvent.Target);
        else if (_shareUnlocks) HandleGroupUnlock(deathEvent.Target, deathEvent.DeathParticipants);
    }
    public static void ProcessUnlock(Entity source, Entity target)
    {
        if (target.TryGetComponent(out PrefabGUID targetPrefabGuid) && target.TryGetComponent(out EntityCategory targetCategory))
        {
            string targetPrefabName = targetPrefabGuid.GetPrefabName();
            bool isVBloodOrGateBoss = target.IsVBloodOrGateBoss();

            if (!ValidTarget(targetPrefabName, targetPrefabGuid, targetCategory)) return;
            else if (!isVBloodOrGateBoss && (int)targetCategory.UnitCategory < 5)
            {
                HandleRoll(_unitUnlockChance, targetPrefabGuid, source, target);
            }
            else if (_allowVBloods && isVBloodOrGateBoss)
            {
                HandleRoll(_vBloodUnlockChance, targetPrefabGuid, source, target);
            }
        }
    }
    public static void HandleGroupUnlock(Entity target, HashSet<Entity> deathParticipants)
    {
        foreach (Entity player in deathParticipants)
        {
            ProcessUnlock(player, target);
        }
    }
    static bool ValidTarget(string targetPrefabName, PrefabGUID targetPrefabGuid, EntityCategory targetCategory)
    {
        if (_defaultNameBans.Any(part => targetPrefabName.Contains(part, StringComparison.CurrentCultureIgnoreCase)) || IsBannedPrefabGuid(targetPrefabGuid) || BannedCategory(targetCategory)) return false;
        return true;
    }
    public static bool IsBannedPrefabGuid(PrefabGUID prefabGuid)
    {
        // return (ConfiguredPrefabGuidBans.Contains(prefabGuid) || _defaultPrefabGUIDBans.Contains(prefabGuid));
        return ConfiguredPrefabGuidBans.Contains(prefabGuid) || _defaultPrefabGuidBans.Contains(prefabGuid);
    }
    static bool BannedCategory(EntityCategory category)
    {
        return ConfiguredCategoryBans.Contains(category.UnitCategory);
    }
    static void HandleRoll(float dropChance, PrefabGUID targetPrefabGuid, Entity playerCharacter, Entity target)
    {
        if (Misc.RollForChance(dropChance))
        {
            HandleUnlock(targetPrefabGuid, playerCharacter, target);
        }
    }
    public static string GetRarityName(Rarity rarity)
    {
        if (RarityInfoMap.TryGetValue(rarity, out var info))
        {
            return info.ChineseName;
        }
        return "";
    }

    public static string GetRarityHex(Rarity rarity)
    {
        if (RarityInfoMap.TryGetValue(rarity, out var info))
        {
            return info.ColorHex;
        }
        return "#FFFFFF";
    }
    static void HandleUnlock(PrefabGUID targetPrefabGuid, Entity playerCharacter, Entity target)
    {
        User user = playerCharacter.GetUser();
        ulong steamId = user.PlatformId;
        int famKey = targetPrefabGuid.GuidHash;

        // 取得或建立解鎖資料
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);
        string lastListName = data.FamiliarUnlocks.Keys.LastOrDefault();

        if (string.IsNullOrEmpty(lastListName) || data.FamiliarUnlocks[lastListName].Count >= 10)
        {
            lastListName = $"box{data.FamiliarUnlocks.Count + 1}";
            data.FamiliarUnlocks[lastListName] = new List<int>();

            if (steamId.TryGetFamiliarBox(out var box) && string.IsNullOrEmpty(box))
            {
                steamId.SetFamiliarBox(lastListName);
            }
        }

        bool isAlreadyUnlocked = false;
        bool isShiny = false;

        foreach (var list in data.FamiliarUnlocks.Values)
        {
            if (list.Contains(famKey))
            {
                isAlreadyUnlocked = true;

                if (_shinyChance > 0f)
                {
                    isShiny = HandleShiny(famKey, steamId, 1f);
                }

                break;
            }
        }

        // Roll rarity（不管是否已解鎖都 roll 一次）
        var rarityData = LoadFamiliarRarityData(steamId);
        Rarity newRarity = RollRarity(_random);

        bool isUpgraded = false;

        if (rarityData.FamiliarRarities.TryGetValue(famKey, out var currentRarity))
        {
            if (newRarity > currentRarity)
            {
                rarityData.FamiliarRarities[famKey] = newRarity;
                isUpgraded = true;
            }
        }
        else
        {
            // 尚未有 rarity，直接設定
            rarityData.FamiliarRarities[famKey] = newRarity;
            isUpgraded = true;
        }
        // 顯示訊息
        Rarity finalRarity = rarityData.FamiliarRarities[famKey];
        string rarityName = GetRarityName(finalRarity);
        string rarityHex = GetRarityHex(finalRarity);

        if (!isAlreadyUnlocked && GetAutoSellFamiliars(steamId) == 1 && newRarity < Rarity.SSS)
        {
            PrefabGUID familiarId = new(famKey);

            // Determine if this is a VBlood (boss) prefab - only non-VBlood get the auto-remove reward
            bool isVBlood = false;
            if (PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(familiarId, out Entity prefabEntity))
            {
                isVBlood = prefabEntity.Has<VBloodConsumeSource>() || prefabEntity.Has<VBloodUnit>();
            }

            // Give configured auto-remove reward if applicable
            PrefabGUID rewardItem = AutoRemoveRewardItem;
            int qty = AutoRemoveRewardQty * RarityInfoMap[finalRarity].AutoRemoveRewardQty;

            if (!isVBlood && !(rewardItem.Equals(new PrefabGUID(0)) || qty <= 0))
            {
                Misc.GiveOrDropItemOutMessage(user, playerCharacter, rewardItem, qty);

                if (!isShiny)
                {
                    LocalizationService.HandleServerReply(EntityManager, user,
                        $"自動販售抓到新寵物: <color=green>{targetPrefabGuid.GetLocalizedName()}</color> (<color={rarityHex}>{rarityName}</color>)，並獲得了 <color=white>{rewardItem.GetLocalizedName()}</color>x<color=white>{qty}</color>。");
                }
                else
                {
                    playerCharacter.PlaySequence(_shinySequence);
                    LocalizationService.HandleServerReply(EntityManager, user,
                        $"自動販售抓到新寵物: <color=#00FFFF>shiny</color> <color=green>{targetPrefabGuid.GetLocalizedName()}</color> (<color={rarityHex}>{rarityName}</color>)，並獲得了 <color=white>{rewardItem.GetLocalizedName()}</color>x<color=white>{qty}</color>。");
                }
                return;
            }
        }

        SaveFamiliarRarityData(steamId, rarityData);

        // 如果還沒解鎖，加入清單、設定經驗
        if (!isAlreadyUnlocked)
        {
            List<int> currentList = data.FamiliarUnlocks[lastListName];
            currentList.Add(famKey);
            SaveFamiliarUnlocksData(steamId, data);

            FamiliarExperienceData famData = LoadFamiliarExperienceData(steamId);
            famData.FamiliarExperience[famKey] = new(FamiliarBindingSystem.BASE_LEVEL, Progression.ConvertLevelToXp(FamiliarBindingSystem.BASE_LEVEL));
            SaveFamiliarExperienceData(steamId, famData);

            playerCharacter.PlaySequence(_unlockSequence);
        }

        // shiny 判定（如果尚未處理）
        if (!isShiny)
        {
            isShiny = HandleShiny(famKey, steamId, _shinyChance);
        }

        if (!isAlreadyUnlocked)
        {
            if (!isShiny)
            {
                LocalizationService.HandleServerReply(EntityManager, user,
                    $"抓到新寵物: <color=green>{targetPrefabGuid.GetLocalizedName()}</color> (<color={rarityHex}>{rarityName}</color>)");
            }
            else
            {
                playerCharacter.PlaySequence(_shinySequence);
                LocalizationService.HandleServerReply(EntityManager, user,
                    $"New <color=#00FFFF>shiny</color> unit unlocked: <color=green>{targetPrefabGuid.GetLocalizedName()}</color> (<color={rarityHex}>{rarityName}</color>)");
            }
        }
        else if (isUpgraded)
        {
            // 已解鎖但升級
            LocalizationService.HandleServerReply(EntityManager, user,
                    $"<color={rarityHex}>取得 {targetPrefabGuid.GetLocalizedName()}({rarityName})</color>");
        }
        else if (isShiny)
        {
            playerCharacter.PlaySequence(_shinySequence);
            LocalizationService.HandleServerReply(EntityManager, user,
                $"<color=#00FFFF>Shiny</color> unlocked: <color=green>{targetPrefabGuid.GetLocalizedName()}</color>");
        }
    }


    public static bool HandleShiny(int famKey, ulong steamId, float chance, int choice = -1)
    {
        FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);

        if (chance < 1f && Misc.RollForChance(chance))
        {
            if (!buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                List<int> famBuffs = [];
                famBuffs.Add(ShinyBuffColorHexes.ElementAt(_random.Next(ShinyBuffColorHexes.Count)).Key.GuidHash);
                buffsData.FamiliarBuffs[famKey] = famBuffs;
            }
            else if (buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                return false;
            }

            SaveFamiliarBuffsData(steamId, buffsData);
            return true;
        }
        else if (chance >= 1f && choice == -1)
        {
            if (!buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                List<int> famBuffs = [];
                famBuffs.Add(ShinyBuffColorHexes.ElementAt(_random.Next(ShinyBuffColorHexes.Count)).Key.GuidHash);
                buffsData.FamiliarBuffs[famKey] = famBuffs;
            }
            else if (buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                return false;
            }

            SaveFamiliarBuffsData(steamId, buffsData);
            return true;
        }
        else if (chance >= 1f && choice != -1)
        {
            if (!buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                List<int> famBuffs = [];
                famBuffs.Add(choice);
                buffsData.FamiliarBuffs[famKey] = famBuffs;
            }
            else if (buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                buffsData.FamiliarBuffs[famKey][0] = choice;
            }

            SaveFamiliarBuffsData(steamId, buffsData);
            return true;
        }

        return false;
    }
}