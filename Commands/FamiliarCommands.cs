using BepInEx;
using Bloodcraft.Patches;
using Bloodcraft.Resources;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;
using VampireCommandFramework;
using static Bloodcraft.Services.BattleService;
using static Bloodcraft.Services.DataService.FamiliarPersistence;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarBattleGroupsManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarBuffsManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarExperienceManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarPrestigeManager;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarUnlocksManager;
using static Bloodcraft.Services.PlayerService;
using static Bloodcraft.Systems.Familiars.FamiliarBindingSystem;
using static Bloodcraft.Systems.Familiars.FamiliarLevelingSystem;
using static Bloodcraft.Systems.Familiars.FamiliarUnlockSystem;
using static Bloodcraft.Utilities.Familiars;
using static Bloodcraft.Utilities.Familiars.ActiveFamiliarManager;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static Bloodcraft.Utilities.Progression;

namespace Bloodcraft.Commands;

[CommandGroup(name: "familiar", "cw")]
internal static class FamiliarCommands
{
    static EntityManager EntityManager => Core.EntityManager;
    static ServerGameManager ServerGameManager => Core.ServerGameManager;
    static SystemService SystemService => Core.SystemService;
    static PrefabCollectionSystem PrefabCollectionSystem => SystemService.PrefabCollectionSystem;

    const int BOX_SIZE = 10;
    const int BOX_CAP = 50;
    const int BOX_BATCH = 6;
    const int STAT_BATCH = 4;
    const int GROUP_BATCH = 5;

    const float SHINY_CHANGE_COST = 0.25f;
    const int SCHEMATICS_MIN = 500;
    const int SCHEMATICS_MAX = 2000;
    const int VAMPIRIC_DUST_MIN = 50;
    const int VAMPIRIC_DUST_MAX = 200;
    const int ECHOES_MIN = 1;
    const int ECHOES_MAX = 4;

    static readonly int _minLevel = PrefabCollectionSystem._PrefabGuidToEntityMap[PrefabGUIDs.CHAR_Forest_Wolf_VBlood].GetUnitLevel();
    static readonly int _maxLevel = PrefabCollectionSystem._PrefabGuidToEntityMap[PrefabGUIDs.CHAR_Vampire_Dracula_VBlood].GetUnitLevel();

    static readonly PrefabGUID _dominateBuff = new(-1447419822);
    static readonly PrefabGUID _takeFlightBuff = new(1205505492);
    static readonly PrefabGUID _tauntEmote = new(-158502505);
    static readonly PrefabGUID _pvpCombatBuff = new(697095869);
    static readonly PrefabGUID _pveCombatBuff = new(581443919);

    static readonly PrefabGUID _itemSchematic = new(2085163661);
    static readonly PrefabGUID _vampiricDust = new(805157024);

    static readonly Dictionary<string, Action<ChatCommandContext, ulong>> _familiarSettings = new()
    {
        {"VBloodEmotes", ToggleVBloodEmotes},
        {"Shiny", ToggleShinies}
    };

    [Command(name: "bind", shortHand: "b", adminOnly: false, usage: ".cw b [#]", description: "從當前清單啟用指定的寵物。")]
    public static void BindFamiliar(ChatCommandContext ctx, int boxIndex)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;

        Familiars.BindFamiliar(user, playerCharacter, boxIndex);
    }

    [Command(name: "unbind", shortHand: "ub", adminOnly: false, usage: ".cw ub", description: "解除招喚當前寵物。")]
    public static void UnbindFamiliar(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;

        Familiars.UnbindFamiliar(user, playerCharacter);
    }

    [Command(name: "list", shortHand: "l", adminOnly: false, usage: ".cw l", description: "列出已抓到的寵物。")]
    public static void ListFamiliars(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;

        FamiliarUnlocksData familiarUnlocksData = LoadFamiliarUnlocksData(steamId);
        FamiliarBuffsData familiarBuffsData = LoadFamiliarBuffsData(steamId);
        FamiliarExperienceData familiarExperienceData = LoadFamiliarExperienceData(steamId);
        FamiliarPrestigeData familiarPrestigeData_V2 = LoadFamiliarPrestigeData(steamId);

        string box = steamId.TryGetFamiliarBox(out box) ? box : string.Empty;

        if (!string.IsNullOrEmpty(box) && familiarUnlocksData.FamiliarUnlocks.TryGetValue(box, out var famKeys))
        {
            int count = 1;
            LocalizationService.HandleReply(ctx, $"<color=white>{box}</color>:");

            foreach (var famKey in famKeys)
            {
                PrefabGUID famPrefab = new(famKey);

                string famName = famPrefab.GetLocalizedName();
                string colorCode = "<color=#FF69B4>";

                if (familiarBuffsData.FamiliarBuffs.ContainsKey(famKey))
                {
                    if (ShinyBuffColorHexes.TryGetValue(new(familiarBuffsData.FamiliarBuffs[famKey][0]), out var hexColor))
                    {
                        colorCode = $"<color={hexColor}>";
                    }
                }

                int level = familiarExperienceData.FamiliarExperience.TryGetValue(famKey, out var experienceData) ? experienceData.Key : 1;
                int prestiges = familiarPrestigeData_V2.FamiliarPrestige.TryGetValue(famKey, out var prestigeData) ? prestigeData : 0;

                string levelAndPrestiges = prestiges > 0 ? $"[<color=white>{level}</color>][<color=#90EE90>{prestiges}</color>]" : $"[<color=white>{level}</color>]";
                LocalizationService.HandleReply(ctx, $"<color=yellow>{count}</color>| <color=green>{famName}</color>{(familiarBuffsData.FamiliarBuffs.ContainsKey(famKey) ? $"{colorCode}*</color> {levelAndPrestiges}" : $" {levelAndPrestiges}")}");
                count++;
            }
        }
        else if (string.IsNullOrEmpty(box))
        {
            // LocalizationService.HandleReply(ctx, "沒有活動的列表！如果你知道要找的寵物名稱，試試使用 <color=white>'.fam sb [名稱]'</color>。（名稱中有空格時請使用引號）");
            LocalizationService.HandleReply(ctx, "找不到活動的列表！");
        }
    }

    [Command(name: "listboxes", shortHand: "lb", adminOnly: false, usage: ".cw lb", description: "顯示可用的寵物列表。")]
    public static void ListFamiliarSets(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (data.FamiliarUnlocks.Keys.Count > 0)
        {
            List<string> sets = [.. data.FamiliarUnlocks.Keys];

            LocalizationService.HandleReply(ctx, $"寵物列表:");

            List<string> colorizedBoxes = [.. sets.Select(set => $"<color=white>{set}</color>")];

            foreach (var batch in colorizedBoxes.Batch(BOX_BATCH))
            {
                string fams = string.Join(", ", batch);
                LocalizationService.HandleReply(ctx, fams);
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你還沒有解鎖任何寵物！");
        }
    }

    [Command(name: "choosebox", shortHand: "cb", adminOnly: false, usage: ".cw cb [名稱]", description: "選擇寵物列表。")]
    public static void SelectBoxCommand(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (data.FamiliarUnlocks.TryGetValue(name, out var _))
        {
            steamId.SetFamiliarBox(name);
            LocalizationService.HandleReply(ctx, $"已選擇列表 - <color=white>{name}</color>");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到列表！");
        }
    }

    [Command(name: "renamebox", shortHand: "rb", adminOnly: false, usage: ".cw rb [目前名稱] [新名稱]", description: "重新命名一個列表。")]
    public static void RenameBoxCommand(ChatCommandContext ctx, string current, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (!data.FamiliarUnlocks.ContainsKey(name) && data.FamiliarUnlocks.TryGetValue(current, out var familiarBox))
        {
            // 移除舊的集合
            data.FamiliarUnlocks.Remove(current);

            // 使用新名稱新增集合
            data.FamiliarUnlocks[name] = familiarBox;

            if (steamId.TryGetFamiliarBox(out var set) && set.Equals(current)) // 如果舊名稱是活動列表，則將活動列表更改為新名稱
            {
                steamId.SetFamiliarBox(name);
            }

            // 將變更儲存回 FamiliarUnlocksManager
            SaveFamiliarUnlocksData(steamId, data);
            LocalizationService.HandleReply(ctx, $"列表 <color=white>{current}</color> 已重新命名 - <color=yellow>{name}</color>");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到要重新命名的列表，或者已經存在同名的列表！");
        }
    }

    [Command(name: "movebox", shortHand: "mb", adminOnly: false, usage: ".cw mb [#] [列表名稱]", description: "將當前清單中指定編號的寵物移到指定列表。")]
    public static void MoveFamiliar(ChatCommandContext ctx, int choice, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        // 檢查目標列表是否存在
        if (!data.FamiliarUnlocks.TryGetValue(name, out var targetSet))
        {
            LocalizationService.HandleReply(ctx, "找不到列表！");
            return;
        }

        // 檢查目標列表是否已滿
        if (targetSet.Count >= BOX_SIZE)
        {
            LocalizationService.HandleReply(ctx, "列表已滿！");
            return;
        }

        // 取得當前活動列表
        if (!steamId.TryGetFamiliarBox(out var activeBox) || !data.FamiliarUnlocks.TryGetValue(activeBox, out var sourceSet))
        {
            LocalizationService.HandleReply(ctx, "找不到活動的列表！");
            return;
        }

        if (string.Equals(activeBox, name, StringComparison.CurrentCultureIgnoreCase))
        {
            LocalizationService.HandleReply(ctx, "目標列表與當前列表相同！");
            return;
        }

        if (choice < 1 || choice > sourceSet.Count)
        {
            LocalizationService.HandleReply(ctx, $"無效的選擇，請使用 <color=white>1</color> 到 <color=white>{sourceSet.Count}</color> (當前清單:<color=yellow>{activeBox}</color>)");
            return;
        }

        int familiarId = sourceSet[choice - 1];
        sourceSet.RemoveAt(choice - 1);
        targetSet.Add(familiarId);

        SaveFamiliarUnlocksData(steamId, data);

        PrefabGUID prefab = new(familiarId);
        LocalizationService.HandleReply(ctx, $"<color=green>{prefab.GetLocalizedName()}</color> 已從 <color=white>{activeBox}</color> 移動到 <color=white>{name}</color>。");
    }

    [Command(name: "movetop", shortHand: "top", adminOnly: false, usage: ".cw top [#]", description: "將當前清單指定編號的寵物移到第一位，其它往後移動。")]
    public static void MoveFamiliarToTop(ChatCommandContext ctx, int choice)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        // 取得當前活動列表
        if (!steamId.TryGetFamiliarBox(out var activeBox) || !data.FamiliarUnlocks.TryGetValue(activeBox, out var sourceSet))
        {
            LocalizationService.HandleReply(ctx, "找不到活動的列表！");
            return;
        }

        if (choice < 1 || choice > sourceSet.Count)
        {
            LocalizationService.HandleReply(ctx, $"無效的選擇，請使用 <color=white>1</color> 到 <color=white>{sourceSet.Count}</color> (當前清單:<color=yellow>{activeBox}</color>)");
            return;
        }

        if (choice == 1)
        {
            LocalizationService.HandleReply(ctx, "該寵物已經位於第一位！");
            return;
        }

        int familiarId = sourceSet[choice - 1];
        sourceSet.RemoveAt(choice - 1);
        sourceSet.Insert(0, familiarId);

        SaveFamiliarUnlocksData(steamId, data);

        PrefabGUID movedPrefab = new(familiarId);
        LocalizationService.HandleReply(ctx, $"<color=green>{movedPrefab.GetLocalizedName()}</color> 已從 <color=white>{activeBox}</color> 移到第一位。");
    }

    [Command(name: "deletebox", shortHand: "db", adminOnly: false, usage: ".cw db [列表名稱]", description: "如果指定列表為空則刪除它。")]
    public static void DeleteBoxCommand(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (data.FamiliarUnlocks.TryGetValue(name, out var familiarSet) && familiarSet.Count == 0)
        {
            // 刪除列表
            data.FamiliarUnlocks.Remove(name);
            SaveFamiliarUnlocksData(steamId, data);

            LocalizationService.HandleReply(ctx, $"已刪除列表 - <color=white>{name}</color>");
        }
        else if (data.FamiliarUnlocks.ContainsKey(name))
        {
            LocalizationService.HandleReply(ctx, "列表不是空的！");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到列表！");
        }
    }

    [Command(name: "addbox", shortHand: "ab", adminOnly: false, usage: ".cw ab [列表名稱]", description: "新增一個指定名稱的空列表。")]
    public static void AddBoxCommand(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (data.FamiliarUnlocks.Count > 0 && data.FamiliarUnlocks.Count < BOX_CAP)
        {
            // 新增列表
            data.FamiliarUnlocks.Add(name, []);
            SaveFamiliarUnlocksData(steamId, data);

            LocalizationService.HandleReply(ctx, $"已新增列表 - <color=white>{name}</color>");
        }
        else
        {
            LocalizationService.HandleReply(ctx, $"必須至少解鎖一個單位才能開始新增列表。此外，列表總數不能超過 <color=yellow>{BOX_CAP}</color>。");
        }
    }

    [Command(name: "add", shortHand: "a", adminOnly: true, usage: ".cw a [玩家名稱] [PrefabGuid/CHAR_單位名稱]", description: "單位測試。")]
    public static void AddFamiliar(ChatCommandContext ctx, string name, string unit)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply($"找不到玩家。");
            return;
        }

        User foundUser = playerInfo.User;
        ulong steamId = foundUser.PlatformId;

        if (steamId.TryGetFamiliarBox(out string activeSet) && !string.IsNullOrEmpty(activeSet))
        {
            ParseAddedFamiliar(ctx, steamId, unit, activeSet);
        }
        else
        {
            FamiliarUnlocksData unlocksData = LoadFamiliarUnlocksData(steamId);
            string lastListName = unlocksData.FamiliarUnlocks.Keys.LastOrDefault();

            if (string.IsNullOrEmpty(lastListName))
            {
                lastListName = $"box{unlocksData.FamiliarUnlocks.Count + 1}";
                unlocksData.FamiliarUnlocks[lastListName] = [];

                SaveFamiliarUnlocksData(steamId, unlocksData);

                ParseAddedFamiliar(ctx, steamId, unit, lastListName);
            }
            else
            {
                ParseAddedFamiliar(ctx, steamId, unit, lastListName);
            }
        }
    }

    [Command(name: "echoes", adminOnly: false, usage: ".cw echoes [VBlood名稱]", description: "使用原始迴響購買 VBlood，數量根據單位階級調整。")] // 提醒我之後要處理狼人 >_>
    public static void PurchaseVBloodCommand(ChatCommandContext ctx, string vBlood)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }
        else if (!ConfigService.AllowVBloods)
        {
            LocalizationService.HandleReply(ctx, "VBlood 寵物未啟用。");
            return;
        }
        else if (!ConfigService.PrimalEchoes)
        {
            LocalizationService.HandleReply(ctx, "VBlood 購買未啟用。");
            return;
        }

        List<PrefabGUID> vBloodPrefabGuids = [..VBloodNamePrefabGuidMap
            .Where(kvp => kvp.Key.Contains(vBlood, StringComparison.CurrentCultureIgnoreCase))
            .Select(kvp => kvp.Value)];

        if (!vBloodPrefabGuids.Any())
        {
            LocalizationService.HandleReply(ctx, "找不到匹配的 vBlood！");
            return;
        }
        else if (vBloodPrefabGuids.Count > 1)
        {
            LocalizationService.HandleReply(ctx, "找到多個匹配項，請更明確一些！");
            return;
        }

        PrefabGUID vBloodPrefabGuid = vBloodPrefabGuids.First();

        if (IsBannedPrefabGuid(vBloodPrefabGuid))
        {
            LocalizationService.HandleReply(ctx, $"<color=white>{vBloodPrefabGuid.GetLocalizedName()}</color> 根據配置的寵物禁令不可用！");
            return;
        }
        else
        {
            if (PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(vBloodPrefabGuid, out Entity prefabEntity) && prefabEntity.TryGetBuffer<SpawnBuffElement>(out var buffer) && !buffer.IsEmpty)
            {
                ulong steamId = ctx.Event.User.PlatformId;
                FamiliarUnlocksData unlocksData = LoadFamiliarUnlocksData(steamId);

                if (unlocksData.FamiliarUnlocks.Values.Any(list => list.Contains(vBloodPrefabGuid.GuidHash)))
                {
                    LocalizationService.HandleReply(ctx, $"<color=white>{vBloodPrefabGuid.GetLocalizedName()}</color> 已經解鎖了！");
                    return;
                }

                int unitLevel = prefabEntity.GetUnitLevel();
                int scaledCostFactor = Mathf.RoundToInt(Mathf.Lerp(1, 25, (unitLevel - _minLevel) / (float)(_maxLevel - _minLevel)));

                PrefabGUID exoItem = new(ConfigService.ExoPrestigeReward);

                int baseCost = ConfigService.ExoPrestigeRewardQuantity * scaledCostFactor;
                int clampedFactor = Mathf.Clamp(ConfigService.EchoesFactor, ECHOES_MIN, ECHOES_MAX);

                int factoredCost = clampedFactor * baseCost;

                if (factoredCost <= 0)
                {
                    LocalizationService.HandleReply(ctx, $"無法驗證 {vBloodPrefabGuid.GetPrefabName()} 的成本！");
                }
                else if (!PrefabCollectionSystem._PrefabGuidToEntityMap.ContainsKey(exoItem))
                {
                    LocalizationService.HandleReply(ctx, $"無法驗證外骨骼聲望獎勵物品！ (<color=yellow>{exoItem}</color>)");
                }
                else if (InventoryUtilities.TryGetInventoryEntity(EntityManager, ctx.Event.SenderCharacterEntity, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, exoItem) >= factoredCost)
                {
                    if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, exoItem, factoredCost))
                    {
                        string lastBoxName = unlocksData.FamiliarUnlocks.Keys.LastOrDefault();

                        if (string.IsNullOrEmpty(lastBoxName) || (unlocksData.FamiliarUnlocks.TryGetValue(lastBoxName, out var box) && box.Count >= BOX_SIZE))
                        {
                            lastBoxName = $"box{unlocksData.FamiliarUnlocks.Count + 1}";

                            unlocksData.FamiliarUnlocks[lastBoxName] = [];
                            unlocksData.FamiliarUnlocks[lastBoxName].Add(vBloodPrefabGuid.GuidHash);

                            SaveFamiliarUnlocksData(steamId, unlocksData);
                            LocalizationService.HandleReply(ctx, $"抓到新寵物: <color=green>{vBloodPrefabGuid.GetLocalizedName()}</color>");
                        }
                        else if (unlocksData.FamiliarUnlocks.ContainsKey(lastBoxName))
                        {
                            unlocksData.FamiliarUnlocks[lastBoxName].Add(vBloodPrefabGuid.GuidHash);

                            SaveFamiliarUnlocksData(steamId, unlocksData);
                            LocalizationService.HandleReply(ctx, $"抓到新寵物: <color=green>{vBloodPrefabGuid.GetLocalizedName()}</color>");
                        }
                    }
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"{vBloodPrefabGuid.GetPrefabName()} 的 <color=#ffd9eb>{exoItem.GetLocalizedName()}</color>x<color=white>{factoredCost}</color> 不足！");
                }
            }
            else
            {
                LocalizationService.HandleReply(ctx, $"無法驗證 {vBloodPrefabGuid.GetPrefabName()} 的階級！到這一步不應該發生，可能需要通知開發者。");
                return;
            }
        }
    }

    [Command(name: "remove", shortHand: "r", adminOnly: false, usage: ".cw r [#]", description: "從當前集合中永久移除寵物。")]
    public static void DeleteFamiliarCommand(ChatCommandContext ctx, int choice)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (steamId.TryGetFamiliarBox(out var activeBox) && data.FamiliarUnlocks.TryGetValue(activeBox, out var familiarSet))
        {
            if (choice < 1 || choice > familiarSet.Count)
            {
                LocalizationService.HandleReply(ctx, $"無效的選擇，請使用 <color=white>1</color> 到 <color=white>{familiarSet.Count}</color> (當前清單:<color=yellow>{activeBox}</color>)");
                return;
            }

            PrefabGUID familiarId = new(familiarSet[choice - 1]);

            familiarSet.RemoveAt(choice - 1);
            data.OverflowFamiliars.Add(familiarId.GuidHash);
            SaveFamiliarUnlocksData(steamId, data);

            LocalizationService.HandleReply(ctx, $"<color=green>{familiarId.GetLocalizedName()}</color> 已從 <color=white>{activeBox}</color> 移動到溢位區。");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到要移除的活動寵物列表...");
        }
    }

    [Command(name: "removepet", shortHand: "rm", adminOnly: false, usage: ".cw rm [#]", description: "永久刪除寵物並獲得設定道具（非VBlood）。")]
    public static void RemoveFamiliarPermanentlyCommand(ChatCommandContext ctx, int choice)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (steamId.TryGetFamiliarBox(out var activeBox) && data.FamiliarUnlocks.TryGetValue(activeBox, out var familiarSet))
        {
            if (choice < 1 || choice > familiarSet.Count)
            {
                LocalizationService.HandleReply(ctx, $"無效的選擇，請使用 <color=white>1</color> 到 <color=white>{familiarSet.Count}</color> (當前清單:<color=yellow>{activeBox}</color>)");
                return;
            }

            int famKey = familiarSet[choice - 1];
            PrefabGUID familiarId = new(famKey);

            // Determine if this is a VBlood (boss) prefab - only non-VBlood get the auto-remove reward
            bool isVBlood = false;
            if (PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(familiarId, out Entity prefabEntity))
            {
                isVBlood = prefabEntity.Has<VBloodConsumeSource>() || prefabEntity.Has<VBloodUnit>();
            }

            // Give configured auto-remove reward if applicable
            PrefabGUID rewardItem = new(ConfigService.AutoRemoveItem);
            int qty = ConfigService.AutoRemoveItemQuantity;

            if (!isVBlood && !(rewardItem.Equals(new PrefabGUID(0)) || qty <= 0))
            {
                Misc.GiveOrDropItem(ctx.Event.User, ctx.Event.SenderCharacterEntity, rewardItem, qty);
                LocalizationService.HandleReply(ctx, $"<color=green>{familiarId.GetLocalizedName()}</color> 已被永久刪除，並獲得了 <color=white>{rewardItem.GetLocalizedName()}</color>x<color=white>{qty}</color>。");
            }
            else
            {
                LocalizationService.HandleReply(ctx, $"<color=green>{familiarId.GetLocalizedName()}</color> 已被永久刪除。");
            }

            // Remove from unlocks and clean up saved data
            familiarSet.RemoveAt(choice - 1);

            var xpData = LoadFamiliarExperienceData(steamId);
            if (xpData.FamiliarExperience.ContainsKey(famKey))
            {
                xpData.FamiliarExperience.Remove(famKey);
                SaveFamiliarExperienceData(steamId, xpData);
            }

            var prestigeData = LoadFamiliarPrestigeData(steamId);
            if (prestigeData.FamiliarPrestige.ContainsKey(famKey))
            {
                prestigeData.FamiliarPrestige.Remove(famKey);
                SaveFamiliarPrestigeData(steamId, prestigeData);
            }

            var buffsData = LoadFamiliarBuffsData(steamId);
            if (buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                buffsData.FamiliarBuffs.Remove(famKey);
                SaveFamiliarBuffsData(steamId, buffsData);
            }

            SaveFamiliarUnlocksData(steamId, data);
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到要移除的活動寵物列表...");
        }
    }

    [Command(name: "overflow", shortHand: "of", adminOnly: false, usage: ".cw of", description: "列出儲存在溢位區的寵物。")]
    public static void ListOverflowFamiliars(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;

        FamiliarUnlocksData familiarUnlocksData = LoadFamiliarUnlocksData(steamId);

        if (familiarUnlocksData.OverflowFamiliars.Count == 0)
        {
            LocalizationService.HandleReply(ctx, "溢位儲存區是空的。");
            return;
        }

        FamiliarBuffsData familiarBuffsData = LoadFamiliarBuffsData(steamId);
        FamiliarExperienceData familiarExperienceData = LoadFamiliarExperienceData(steamId);
        FamiliarPrestigeData familiarPrestigeData_V2 = LoadFamiliarPrestigeData(steamId);

        LocalizationService.HandleReply(ctx, "溢位區寵物:");

        int count = 1;

        foreach (int familiarKey in familiarUnlocksData.OverflowFamiliars)
        {
            PrefabGUID familiarPrefab = new(familiarKey);
            string familiarName = familiarPrefab.GetLocalizedName();
            string colorCode = "<color=#FF69B4>";

            if (familiarBuffsData.FamiliarBuffs.ContainsKey(familiarKey))
            {
                if (ShinyBuffColorHexes.TryGetValue(new(familiarBuffsData.FamiliarBuffs[familiarKey][0]), out var hexColor))
                {
                    colorCode = $"<color={hexColor}>";
                }
            }

            int level = familiarExperienceData.FamiliarExperience.TryGetValue(familiarKey, out var experienceData) ? experienceData.Key : 1;
            int prestiges = familiarPrestigeData_V2.FamiliarPrestige.TryGetValue(familiarKey, out var prestigeData) ? prestigeData : 0;

            string levelAndPrestiges = prestiges > 0 ? $"[<color=white>{level}</color>][<color=#90EE90>{prestiges}</color>]" : $"[<color=white>{level}</color>]";
            LocalizationService.HandleReply(ctx, $"<color=yellow>{count}</color>| <color=green>{familiarName}</color>{(familiarBuffsData.FamiliarBuffs.ContainsKey(familiarKey) ? $"{colorCode}*</color> {levelAndPrestiges}" : $" {levelAndPrestiges}")}");
            count++;
        }
    }

    [Command(name: "overflowmove", shortHand: "om", adminOnly: false, usage: ".cw om [#] [列表名稱]", description: "將寵物從溢位區移動到指定的列表。")]
    public static void MoveOverflowFamiliar(ChatCommandContext ctx, int choice, string boxName)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);

        if (data.OverflowFamiliars.Count == 0)
        {
            LocalizationService.HandleReply(ctx, "溢位儲存區是空的。");
            return;
        }

        if (choice < 1 || choice > data.OverflowFamiliars.Count)
        {
            LocalizationService.HandleReply(ctx, $"無效的選擇，請使用 <color=white>1</color> 到 <color=white>{data.OverflowFamiliars.Count}</color> (溢位清單)");
            return;
        }

        if (!data.FamiliarUnlocks.TryGetValue(boxName, out var familiarSet))
        {
            LocalizationService.HandleReply(ctx, "找不到列表！");
            return;
        }

        if (familiarSet.Count >= BOX_SIZE)
        {
            LocalizationService.HandleReply(ctx, "列表已滿！");
            return;
        }

        int familiarKey = data.OverflowFamiliars[choice - 1];
        data.OverflowFamiliars.RemoveAt(choice - 1);
        familiarSet.Add(familiarKey);

        SaveFamiliarUnlocksData(steamId, data);

        PrefabGUID familiarId = new(familiarKey);
        LocalizationService.HandleReply(ctx, $"<color=green>{familiarId.GetLocalizedName()}</color> 已移動到 <color=white>{boxName}</color>。");
    }

    [Command(name: "toggle", shortHand: "t", usage: ".cw t", description: "召喚或解散寵物。", adminOnly: false)]
    public static void ToggleFamiliarCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        Entity playerCharacter = ctx.Event.SenderCharacterEntity;

        if (ServerGameManager.HasBuff(playerCharacter, _dominateBuff.ToIdentifier()))
        {
            LocalizationService.HandleReply(ctx, "使用支配氣場時無法召喚寵物！");
            return;
        }
        else if (ServerGameManager.HasBuff(playerCharacter, _takeFlightBuff.ToIdentifier()))
        {
            LocalizationService.HandleReply(ctx, "使用蝙蝠形態時無法召喚寵物！");
            return;
        }

        EmoteSystemPatch.CallDismiss(ctx.Event.User, playerCharacter, steamId);
    }

    [Command(name: "togglecombat", shortHand: "c", usage: ".cw c", description: "啟用或停用寵物的戰鬥模式。", adminOnly: false)]
    public static void ToggleCombatCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;

        if (ServerGameManager.HasBuff(playerCharacter, _dominateBuff.ToIdentifier()))
        {
            LocalizationService.HandleReply(ctx, "使用支配氣場時無法切換寵物的戰鬥模式！");
            return;
        }
        else if (ServerGameManager.HasBuff(playerCharacter, _takeFlightBuff.ToIdentifier()))
        {
            LocalizationService.HandleReply(ctx, "使用蝙蝠形態時無法切換寵物的戰鬥模式！");
            return;
        }
        else if (playerCharacter.HasBuff(_pveCombatBuff) || playerCharacter.HasBuff(_pvpCombatBuff))
        {
            LocalizationService.HandleReply(ctx, "在 PvP/PvE 戰鬥中無法切換寵物的戰鬥模式！");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        EmoteSystemPatch.CombatMode(ctx.Event.User, playerCharacter, steamId);
    }

    [Command(name: "emotes", shortHand: "e", usage: ".cw e", description: "切換表情動作。", adminOnly: false)]
    public static void ToggleEmoteActionsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        TogglePlayerBool(steamId, EMOTE_ACTIONS_KEY);

        LocalizationService.HandleReply(ctx, $"表情動作 {(GetPlayerBool(steamId, EMOTE_ACTIONS_KEY) ? "<color=green>已啟用</color>" : "<color=red>已停用</color>")}！");
    }

    [Command(name: "emoteactions", shortHand: "actions", usage: ".cw actions", description: "顯示可用的表情動作。", adminOnly: false)]
    public static void ListEmoteActionsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        List<string> emoteInfoList = [];
        foreach (var emote in EmoteSystemPatch.EmoteActions)
        {
            if (emote.Key.Equals(_tauntEmote)) continue;

            string emoteName = emote.Key.GetLocalizedName();
            string actionName = emote.Value.Method.Name;
            emoteInfoList.Add($"<color=#FFC0CB>{emoteName}</color>: <color=yellow>{actionName}</color>");
        }

        string emotes = string.Join(", ", emoteInfoList);
        LocalizationService.HandleReply(ctx, emotes);
    }

    [Command(name: "getlevel", shortHand: "gl", adminOnly: false, usage: ".cw gl", description: "顯示當前寵物升級進度。")]
    public static void GetFamiliarLevelCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        if (steamId.HasActiveFamiliar())
        {
            ActiveFamiliarData activeFamiliar = GetActiveFamiliarData(steamId);
            int familiarId = activeFamiliar.FamiliarId;

            var xpData = GetFamiliarExperience(steamId, familiarId);
            int progress = (int)(xpData.Value - ConvertLevelToXp(xpData.Key));
            int percent = GetLevelProgress(steamId, familiarId);

            Entity familiar = GetActiveFamiliar(ctx.Event.SenderCharacterEntity);

            int prestigeLevel = 0;

            FamiliarPrestigeData prestigeData = LoadFamiliarPrestigeData(steamId);
            FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);

            if (!prestigeData.FamiliarPrestige.ContainsKey(familiarId))
            {
                prestigeData.FamiliarPrestige[familiarId] = 0;
                SaveFamiliarPrestigeData(steamId, prestigeData);
            }
            else
            {
                prestigeLevel = prestigeData.FamiliarPrestige[familiarId];
            }

            LocalizationService.HandleReply(ctx, $"你的寵物等級為 [<color=white>{xpData.Key}</color>][<color=#90EE90>{prestigeLevel}</color>]，擁有 <color=yellow>{progress}</color> 點 <color=#FFC0CB>經驗值</color> (<color=white>{percent}%</color>)！");

            if (familiar.Exists())
            {
                Health health = familiar.Read<Health>();
                UnitStats unitStats = familiar.Read<UnitStats>();
                AbilityBar_Shared abilityBar_Shared = familiar.Read<AbilityBar_Shared>();

                AiMoveSpeeds originalMoveSpeeds = familiar.GetPrefabEntity().Read<AiMoveSpeeds>();
                AiMoveSpeeds aiMoveSpeeds = familiar.Read<AiMoveSpeeds>();

                /*
                LifeLeech lifeLeech = new()
                {
                    PrimaryLeechFactor = new(0f),
                    PhysicalLifeLeechFactor = new(0f),
                    SpellLifeLeechFactor = new(0f),
                    AffectRecovery = false
                };

                if (familiar.Has<LifeLeech>())
                {
                    LifeLeech familiarLifeLeech = familiar.Read<LifeLeech>();

                    lifeLeech.PrimaryLeechFactor._Value = familiarLifeLeech.PrimaryLeechFactor._Value;
                    lifeLeech.PhysicalLifeLeechFactor._Value = familiarLifeLeech.PhysicalLifeLeechFactor._Value;
                    lifeLeech.SpellLifeLeechFactor._Value = familiarLifeLeech.SpellLifeLeechFactor._Value;
                }
                */

                List<KeyValuePair<string, string>> statPairs = [];

                foreach (FamiliarStatType statType in Enum.GetValues(typeof(FamiliarStatType)))
                {
                    string statName = statType.ToString();
                    string displayValue;

                    switch (statType)
                    {
                        case FamiliarStatType.MaxHealth:
                            displayValue = ((int)health.MaxHealth._Value).ToString();
                            break;
                        case FamiliarStatType.PhysicalPower:
                            displayValue = ((int)unitStats.PhysicalPower._Value).ToString();
                            break;
                        case FamiliarStatType.SpellPower:
                            displayValue = ((int)unitStats.SpellPower._Value).ToString();
                            break;
                        /*
                        case FamiliarStatType.PrimaryLifeLeech:
                            displayValue = lifeLeech.PrimaryLeechFactor._Value == 0f
                                ? string.Empty
                                : (lifeLeech.PrimaryLeechFactor._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.PhysicalLifeLeech:
                            displayValue = lifeLeech.PhysicalLifeLeechFactor._Value == 0f
                                ? string.Empty
                                : (lifeLeech.PhysicalLifeLeechFactor._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.SpellLifeLeech:
                            displayValue = lifeLeech.SpellLifeLeechFactor._Value == 0f
                                ? string.Empty
                                : (lifeLeech.SpellLifeLeechFactor._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.HealingReceived:
                            displayValue = unitStats.HealingReceived._Value == 0f
                                ? string.Empty
                                : (unitStats.HealingReceived._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.DamageReduction:
                            displayValue = unitStats.DamageReduction._Value == 0f
                                ? string.Empty
                                : (unitStats.DamageReduction._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.PhysicalResistance:
                            displayValue = unitStats.PhysicalResistance._Value == 0f
                                ? string.Empty
                                : (unitStats.PhysicalResistance._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.SpellResistance:
                            displayValue = unitStats.SpellResistance._Value == 0f
                                ? string.Empty
                                : (unitStats.SpellResistance._Value * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.MovementSpeed:
                            displayValue = aiMoveSpeeds.Walk._Value == originalMoveSpeeds.Walk._Value
                                ? string.Empty
                                : ((aiMoveSpeeds.Walk._Value / originalMoveSpeeds.Walk._Value) * 100).ToString("F1") + "%";
                            break;
                        case FamiliarStatType.CastSpeed:
                            displayValue = abilityBar_Shared.AbilityAttackSpeed._Value == 1f
                                ? string.Empty
                                : (abilityBar_Shared.AbilityAttackSpeed._Value * 100).ToString("F1") + "%";
                            break;
                        */
                        default:
                            continue;
                    }

                    if (!string.IsNullOrEmpty(displayValue)) statPairs.Add(new KeyValuePair<string, string>(statName, displayValue));
                }

                string shinyInfo = GetShinyInfo(buffsData, familiar, familiarId);
                string familiarName = GetFamiliarName(familiarId, buffsData);

                string infoHeader = string.IsNullOrEmpty(shinyInfo) ? $"{familiarName}:" : $"{familiarName} - {shinyInfo}";
                LocalizationService.HandleReply(ctx, infoHeader);

                foreach (var batch in statPairs.Batch(STAT_BATCH))
                {
                    string line = string.Join(
                        ", ",
                        batch.Select(stat => $"<color=#00FFFF>{stat.Key}</color>: <color=white>{stat.Value}</color>")
                    );

                    LocalizationService.HandleReply(ctx, line);
                }
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到活動的寵物！");
        }
    }

    [Command(name: "setlevel", shortHand: "sl", adminOnly: true, usage: ".cw sl [玩家] [等級]", description: "設定當前寵物等級。")]
    public static void SetFamiliarLevelCommand(ChatCommandContext ctx, string name, int level)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (level < 1 || level > ConfigService.MaxFamiliarLevel)
        {
            LocalizationService.HandleReply(ctx, $"等級必須在 1 到 {ConfigService.MaxFamiliarLevel} 之間");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply($"找不到玩家。");
            return;
        }

        User user = playerInfo.User;
        ulong steamId = user.PlatformId;

        if (steamId.HasActiveFamiliar())
        {
            Entity playerCharacter = playerInfo.CharEntity;
            Entity familiar = GetActiveFamiliar(playerCharacter);

            ActiveFamiliarData activeFamiliar = GetActiveFamiliarData(steamId);
            int familiarId = activeFamiliar.FamiliarId;

            KeyValuePair<int, float> newXP = new(level, ConvertLevelToXp(level));
            FamiliarExperienceData xpData = LoadFamiliarExperienceData(steamId);
            xpData.FamiliarExperience[familiarId] = newXP;
            SaveFamiliarExperienceData(steamId, xpData);

            if (ModifyFamiliar(user, steamId, familiarId, playerCharacter, familiar, level))
            {
                LocalizationService.HandleReply(ctx, $"<color=green>{user.CharacterName.Value}</color> 的活動寵物等級已設定為 <color=white>{level}</color>。");
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到活動的寵物....");
        }
    }

    [Command(name: "prestige", shortHand: "pr", adminOnly: false, usage: ".cw pr", description: "如果條件滿足，將寵物進行聲望晉升，按配置的倍率提升基礎屬性。")]
    public static void PrestigeFamiliarCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarPrestige)
        {
            LocalizationService.HandleReply(ctx, "寵物聲望系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;
        ulong steamId = user.PlatformId;

        if (steamId.HasActiveFamiliar())
        {
            FamiliarExperienceData xpData = LoadFamiliarExperienceData(ctx.Event.User.PlatformId);
            FamiliarPrestigeData prestigeData = LoadFamiliarPrestigeData(steamId);
            int clampedCost = Mathf.Clamp(ConfigService.PrestigeCostItemQuantity, SCHEMATICS_MIN, SCHEMATICS_MAX);

            var actives = ActiveFamiliarManager.GetActiveFamiliars(steamId)?.Where(x => x.Familiar.Exists()).ToList();
            if (actives == null || actives.Count == 0)
            {
                LocalizationService.HandleReply(ctx, "找不到要進行聲望晉升的活動寵物！");
                return;
            }

            // If player has enough schematics, use the inventory path which consumes schematics per familiar
            if (InventoryUtilities.TryGetInventoryEntity(EntityManager, playerCharacter, out Entity inventory) && ServerGameManager.GetInventoryItemCount(inventory, _itemSchematic) >= clampedCost)
            {
                HandleFamiliarPrestige(ctx, clampedCost);
                return;
            }

            // Otherwise try to prestige any active familiars that are at max level (no schematics consumed)
            bool anyPrestiged = false;

            foreach (var data in actives)
            {
                int familiarId = data.FamiliarId;

                if (!xpData.FamiliarExperience.ContainsKey(familiarId)) continue;
                if (xpData.FamiliarExperience[familiarId].Key < ConfigService.MaxFamiliarLevel) continue;

                if (!prestigeData.FamiliarPrestige.ContainsKey(familiarId))
                {
                    prestigeData.FamiliarPrestige[familiarId] = 0;
                    SaveFamiliarPrestigeData(steamId, prestigeData);
                    prestigeData = LoadFamiliarPrestigeData(steamId);
                }

                if (prestigeData.FamiliarPrestige[familiarId] >= ConfigService.MaxFamiliarPrestiges)
                {
                    LocalizationService.HandleReply(ctx, "寵物已達到最大聲望次數！");
                    continue;
                }

                KeyValuePair<int, float> newXP = new(1, ConvertLevelToXp(1)); // 重置等級為 1
                xpData.FamiliarExperience[familiarId] = newXP;
                SaveFamiliarExperienceData(steamId, xpData);

                int prestigeLevel = prestigeData.FamiliarPrestige[familiarId] + 1;
                prestigeData.FamiliarPrestige[familiarId] = prestigeLevel;
                SaveFamiliarPrestigeData(steamId, prestigeData);

                Entity familiar = data.Familiar;
                ModifyUnitStats(familiar, newXP.Key, steamId, familiarId);

                LocalizationService.HandleReply(ctx, $"你的寵物已進行聲望晉升 [<color=#90EE90>{prestigeLevel}</color>]！");
                anyPrestiged = true;
            }

            if (!anyPrestiged)
            {
                LocalizationService.HandleReply(ctx, $"嘗試進行聲望晉升的寵物必須達到最高等級 (<color=white>{ConfigService.MaxFamiliarLevel}</color>) 或需要 <color=#ffd9eb>{_itemSchematic.GetLocalizedName()}</color><color=yellow>x</color><color=white>{clampedCost}</color>。");
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到要進行聲望晉升的活動寵物！");
        }
    }

    [Command(name: "recallall", shortHand: "recall", adminOnly: false, usage: ".cw recallall", description: "重新呼叫所有活動寵物。")]
    public static void RecallAllFamiliarsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;

        int recalled = Utilities.Familiars.RecallActiveFamiliars(playerCharacter, user);
        if (recalled == 0) LocalizationService.HandleReply(ctx, "沒有要重新呼叫的活動寵物。");
        else LocalizationService.HandleReply(ctx, $"已重新呼叫 <color=green>{recalled}</color> 隻活動寵物。");
    }

    [Command(name: "actives", shortHand: "actv", adminOnly: false, usage: ".cw actives", description: "列出目前活動的寵物。")]
    public static void ListActiveFamiliarsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        User user = ctx.Event.User;
        ulong steamId = user.PlatformId;
        var actives = Utilities.Familiars.ActiveFamiliarManager.GetActiveFamiliars(steamId) ?? new List<Familiars.ActiveFamiliarData>();
        var existing = actives.Where(x => x.Familiar.Exists()).ToList();
        if (!existing.Any()) { LocalizationService.HandleReply(ctx, "你目前沒有活動寵物。"); return; }

        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"你目前有 <color=green>{existing.Count}</color> 隻活動寵物：");
        int idx = 1;
        foreach (var d in existing)
        {
            string name = d.Familiar.Exists() ? d.Familiar.GetPrefabGuid().GetLocalizedName() : "<unknown>";
            sb.AppendLine($"{idx++}: {name} (ID: {d.FamiliarId}){(d.Dismissed ? " [dismissed]" : "")}");
        }

        LocalizationService.HandleReply(ctx, sb.ToString());
    }

    [Command(name: "reset", adminOnly: false, usage: ".cw reset", description: "重置（摧毀）在追隨者緩衝區中找到的實體並清除寵物活動數據。")]
    public static void ResetFamiliarsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        Entity familiar = GetActiveFamiliar(playerCharacter);

        if (familiar.Exists())
        {
            ctx.Reply("看起來你的寵物仍然能被找到；如果它是被解散的，請在召喚後正常地解除綁定。");
            return;
        }

        User user = ctx.Event.User;
        ulong steamId = user.PlatformId;

        var buffer = ctx.Event.SenderCharacterEntity.ReadBuffer<FollowerBuffer>();

        for (int i = 0; i < buffer.Length; i++)
        {
            Entity follower = buffer[i].Entity.GetEntityOnServer();

            if (follower.Exists())
            {
                follower.Remove<Disabled>();
                follower.Destroy();
            }
        }

        ResetActiveFamiliarData(steamId);
        AutoCallMap.TryRemove(playerCharacter, out Entity _);

        LocalizationService.HandleReply(ctx, "寵物活動數據和追隨者已清除。");
    }

    [Command(name: "search", shortHand: "s", adminOnly: false, usage: ".cw s [名稱]", description: "在列表中搜尋具有匹配名稱的寵物。")]
    public static void FindFamiliarCommand(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;

        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);
        FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);
        int count = data.FamiliarUnlocks.Keys.Count;

        if (count > 0)
        {
            List<string> foundBoxNames = [];

            if (name.Equals("vblood", StringComparison.CurrentCultureIgnoreCase))
            {
                foreach (var box in data.FamiliarUnlocks)
                {
                    var matchingFamiliars = box.Value.Where(famKey =>
                    {
                        Entity prefabEntity = PrefabCollectionSystem._PrefabGuidToEntityMap.TryGetValue(new(famKey), out prefabEntity) ? prefabEntity : Entity.Null;
                        return (prefabEntity.Has<VBloodConsumeSource>() || prefabEntity.Has<VBloodUnit>());
                    }).ToList();

                    if (matchingFamiliars.Count > 0)
                    {
                        bool boxHasShiny = matchingFamiliars.Any(familiar => buffsData.FamiliarBuffs.ContainsKey(familiar));

                        if (boxHasShiny)
                        {
                            foundBoxNames.Add($"<color=white>{box.Key}</color><color=#AA336A>*</color>");
                        }
                        else
                        {
                            foundBoxNames.Add($"<color=white>{box.Key}</color>");
                        }
                    }
                }

                if (foundBoxNames.Count > 0)
                {
                    string foundBoxes = string.Join(", ", foundBoxNames);
                    string message = $"VBlood 寵物在以下列表中找到: {foundBoxes}";
                    LocalizationService.HandleReply(ctx, message);
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"在列表中找不到匹配的寵物。");
                }
            }
            else if (!name.IsNullOrWhiteSpace())
            {
                foreach (var box in data.FamiliarUnlocks)
                {
                    var matchingFamiliars = box.Value.Where(famKey =>
                    {
                        PrefabGUID famPrefab = new(famKey);
                        return famPrefab.GetLocalizedName().Contains(name, StringComparison.CurrentCultureIgnoreCase);
                    }).ToList();

                    if (matchingFamiliars.Count > 0)
                    {
                        bool boxHasShiny = matchingFamiliars.Any(familiar => buffsData.FamiliarBuffs.ContainsKey(familiar));

                        if (boxHasShiny)
                        {
                            foundBoxNames.Add($"<color=white>{box.Key}</color><color=#AA336A>*</color>");
                        }
                        else
                        {
                            foundBoxNames.Add($"<color=white>{box.Key}</color>");
                        }
                    }
                }

                if (foundBoxNames.Count > 0)
                {
                    string foundBoxes = string.Join(", ", foundBoxNames);
                    string message = $"匹配的寵物在以下列表中找到: {foundBoxes}";
                    LocalizationService.HandleReply(ctx, message);
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"找不到任何匹配...");
                }
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你還沒有解鎖任何寵物。");
        }
    }

    [Command(name: "smartbind", shortHand: "sb", adminOnly: false, usage: ".cw sb [名稱]", description: "搜尋並綁定一個寵物。如果找到多個匹配項，則返回一個清單以供澄清。")]
    public static void SmartBindFamiliarCommand(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;

        ulong steamId = user.PlatformId;

        FamiliarUnlocksData data = LoadFamiliarUnlocksData(steamId);
        FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);

        var shinyFamiliars = buffsData.FamiliarBuffs;
        Dictionary<string, Dictionary<string, int>> foundBoxMatches = [];

        if (data.FamiliarUnlocks.Count == 0)
        {
            LocalizationService.HandleReply(ctx, "你還沒有解鎖任何寵物！");
            return;
        }

        foreach (var box in data.FamiliarUnlocks)
        {
            var matchingFamiliars = box.Value
                .Select((famKey, index) => new { FamKey = famKey, Index = index })
                .Where(item =>
                {
                    PrefabGUID famPrefab = new(item.FamKey);
                    return famPrefab.GetLocalizedName().Contains(name, StringComparison.CurrentCultureIgnoreCase);
                })
                .ToDictionary(
                    item => item.FamKey,
                    item => item.Index + 1
                );

            if (matchingFamiliars.Any())
            {
                foreach (var keyValuePair in matchingFamiliars)
                {
                    if (!foundBoxMatches.ContainsKey(box.Key))
                    {
                        foundBoxMatches[box.Key] = [];
                    }

                    string familiarName = GetFamiliarName(keyValuePair.Key, buffsData);
                    foundBoxMatches[box.Key][familiarName] = keyValuePair.Value;
                }
            }
        }

        if (!foundBoxMatches.Any())
        {
            LocalizationService.HandleReply(ctx, $"找不到任何匹配...");
        }
        else if (foundBoxMatches.Count == 1)
        {
            Entity familiar = GetActiveFamiliar(playerCharacter);
            steamId.SetFamiliarBox(foundBoxMatches.Keys.First());

            if (familiar.Exists() && steamId.TryGetFamiliarBox(out string box) && foundBoxMatches.TryGetValue(box, out Dictionary<string, int> nameAndIndex))
            {
                int index = nameAndIndex.Any() ? nameAndIndex.First().Value : -1;
                Familiars.UnbindFamiliar(user, playerCharacter, true, index);
            }
            else if (steamId.TryGetFamiliarBox(out box) && foundBoxMatches.TryGetValue(box, out nameAndIndex))
            {
                int index = nameAndIndex.Any() ? nameAndIndex.First().Value : -1;
                Familiars.BindFamiliar(user, playerCharacter, index);
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找到多個匹配！智慧綁定目前還不支援這種情況...（開發中）");
        }
    }

    [Command(name: "shinybuff", shortHand: "shiny", adminOnly: false, usage: ".cw shiny [法術學派]", description: "花費吸血鬼塵埃讓你的寵物閃亮！")]
    public static void ShinyFamiliarCommand(ChatCommandContext ctx, string spellSchool = "")
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        PrefabGUID spellSchoolPrefabGuid = ShinyBuffColorHexes.Keys
                .SingleOrDefault(prefab => prefab.GetPrefabName().Contains(spellSchool, StringComparison.CurrentCultureIgnoreCase));

        if (!ShinyBuffColorHexes.ContainsKey(spellSchoolPrefabGuid))
        {
            LocalizationService.HandleReply(ctx, "從輸入的法術學派中找不到匹配的閃亮增益。(選項: blood, storm, unholy, chaos, frost, illusion)");
            return;
        }

        Entity character = ctx.Event.SenderCharacterEntity;
        ulong steamId = ctx.User.PlatformId;

        Entity familiar = GetActiveFamiliar(character);
        int famKey = familiar.GetGuidHash();

        int clampedCost = Mathf.Clamp(ConfigService.ShinyCostItemQuantity, VAMPIRIC_DUST_MIN, VAMPIRIC_DUST_MAX);

        if (familiar.Exists())
        {
            FamiliarBuffsData buffsData = LoadFamiliarBuffsData(steamId);

            if (!buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                if (InventoryUtilities.TryGetInventoryEntity(EntityManager, character, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, _vampiricDust) >= clampedCost)
                {
                    if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, _vampiricDust, clampedCost) && HandleShiny(famKey, steamId, 1f, spellSchoolPrefabGuid.GuidHash))
                    {
                        LocalizationService.HandleReply(ctx, "閃亮效果已新增！重新綁定寵物以查看效果。使用 '<color=white>.fam option shiny</color>' 來切換（如果閃亮增益被停用，則擊中時不會有機率施加法術學派減益效果）。");
                        return;
                    }
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"你沒有足夠的 <color=#ffd9eb>{_vampiricDust.GetLocalizedName()}</color>！(x<color=white>{clampedCost}</color>)");
                }
            }
            else if (buffsData.FamiliarBuffs.ContainsKey(famKey))
            {
                int changeQuantity = (int)(clampedCost * SHINY_CHANGE_COST);

                if (InventoryUtilities.TryGetInventoryEntity(EntityManager, character, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, _vampiricDust) >= changeQuantity)
                {
                    if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, _vampiricDust, changeQuantity) && HandleShiny(famKey, steamId, 1f, spellSchoolPrefabGuid.GuidHash))
                    {
                        LocalizationService.HandleReply(ctx, "閃亮效果已變更！重新綁定寵物以查看效果。使用 '<color=white>.fam option shiny</color>' 來切換（如果閃亮增益被停用，則擊中時不會有機率施加法術學派減益效果）。");
                        return;
                    }
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"你沒有足夠的 <color=#ffd9eb>{_vampiricDust.GetLocalizedName()}</color>！(x<color=white>{changeQuantity}</color>)");
                }
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到活動的寵物...");
        }
    }

    [Command(name: "toggleoption", shortHand: "option", adminOnly: false, usage: ".cw option [設定]", description: "切換各種寵物設定。")]
    public static void ToggleFamiliarSettingCommand(ChatCommandContext ctx, string option)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        ulong steamId = ctx.User.PlatformId;
        var action = _familiarSettings
            .Where(kvp => string.Equals(kvp.Key, option, StringComparison.CurrentCultureIgnoreCase))
            .Select(kvp => kvp.Value)
            .FirstOrDefault();

        if (action != null)
        {
            action(ctx, steamId);
        }
        else
        {
            string validOptions = string.Join(", ", _familiarSettings.Keys.Select(kvp => $"<color=white>{kvp}</color>"));
            LocalizationService.HandleReply(ctx, $"有效的選項: {validOptions}");
        }
    }

    [Command(name: "listbattlegroups", shortHand: "bgs", adminOnly: false, usage: ".cw bgs", description: "列出可用的戰鬥群組。")]
    public static void ListBattleGroupsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        FamiliarBattleGroupsData data = LoadFamiliarBattleGroupsData(steamId);

        if (data.BattleGroups.Count > 0)
        {
            List<string> battleGroupNames = [.. data.BattleGroups.Select(bg => bg.Name)];
            LocalizationService.HandleReply(ctx, "寵物戰鬥群組:");

            List<string> formattedGroups = [.. battleGroupNames.Select(bg => $"<color=white>{bg}</color>")];

            foreach (var batch in formattedGroups.Batch(GROUP_BATCH))
            {
                string groups = string.Join(", ", batch);
                LocalizationService.HandleReply(ctx, groups);
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你沒有戰鬥群組。");
        }
    }

    [Command(name: "listbattlegroup", shortHand: "bg", adminOnly: false, usage: ".cw bg [戰鬥群組]", description: "顯示指定戰鬥群組的詳細資訊，如果未指定則顯示活動群組。")]
    public static void ShowBattleGroupCommand(ChatCommandContext ctx, string groupName = "")
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        if (string.IsNullOrEmpty(groupName))
        {
            groupName = GetActiveBattleGroupName(steamId);
            if (string.IsNullOrEmpty(groupName))
            {
                LocalizationService.HandleReply(ctx, "沒有選擇活動的戰鬥群組！使用 <color=white>.fam cbg [名稱]</color> 來選擇一個。");
                return;
            }
        }

        var battleGroup = GetFamiliarBattleGroup(steamId, groupName);
        FamiliarBattleGroupsManager.HandleBattleGroupDetailsReply(ctx, steamId, battleGroup);
    }

    [Command(name: "choosebattlegroup", shortHand: "cbg", adminOnly: false, usage: ".cw cbg [戰鬥群組]", description: "設定活動戰鬥群組。")]
    public static void ChooseBattleGroupCommand(ChatCommandContext ctx, string groupName)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (SetActiveBattleGroup(ctx, steamId, groupName))
        {
            LocalizationService.HandleReply(ctx, $"活動戰鬥群組已設定為 <color=white>{groupName}</color>。");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到戰鬥群組。");
        }
    }

    [Command(name: "addbattlegroup", shortHand: "abg", adminOnly: false, usage: ".cw abg [戰鬥群組]", description: "建立新的戰鬥群組。")]
    public static void AddBattleGroupCommand(ChatCommandContext ctx, string groupName)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (CreateBattleGroup(ctx, steamId, groupName))
        {
            LocalizationService.HandleReply(ctx, $"戰鬥群組 <color=white>{groupName}</color> 已建立。");
        }
    }

    [Command(name: "slotbattlegroup", shortHand: "sbg", adminOnly: false, usage: ".cw sbg [戰鬥群組或欄位] [欄位]", description: "將活動寵物分配到戰鬥群組的欄位。如果未指定戰鬥群組，則分配到活動群組。")]
    public static void SetFamiliarInBattleGroupCommand(ChatCommandContext ctx, string groupOrSlot, int slotIndex = default)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        string groupName;

        if (int.TryParse(groupOrSlot, out int parsedSlot))
        {
            slotIndex = parsedSlot;
            groupName = GetActiveBattleGroupName(steamId);
        }
        else
        {
            groupName = groupOrSlot;
        }

        if (string.IsNullOrEmpty(groupName))
        {
            LocalizationService.HandleReply(ctx, "沒有選擇活動的戰鬥群組！使用 <color=white>.fam cbg [名稱]</color> 來選擇一個。");
            return;
        }

        if (slotIndex < 1 || slotIndex > 3)
        {
            LocalizationService.HandleReply(ctx, "欄位輸入超出範圍！(使用 <color=white>1, 2,</color> 或 <color=white>3</color>)");
            return;
        }

        if (AssignFamiliarToGroup(ctx, steamId, groupName, slotIndex))
        {
            LocalizationService.HandleReply(ctx, $"寵物已分配到 <color=white>{groupName}</color> 的欄位 {slotIndex}。");
        }
    }

    [Command(name: "deletebattlegroup", shortHand: "dbg", adminOnly: false, usage: ".cw dbg [戰鬥群組]", description: "刪除一個戰鬥群組。")]
    public static void DeleteBattleGroupCommand(ChatCommandContext ctx, string groupName)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (DeleteBattleGroup(ctx, steamId, groupName))
        {
            LocalizationService.HandleReply(ctx, $"已刪除戰鬥群組 <color=white>{groupName}</color>。");
        }
    }

    [Command(name: "challenge", adminOnly: false, usage: ".cw challenge [玩家名稱]", description: "挑戰玩家進行戰鬥或顯示佇列詳情。")]
    public static void ChallengePlayerCommand(ChatCommandContext ctx, string name = "")
    {
        if (!ConfigService.FamiliarSystem || !ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        bool isQueued = Matchmaker.QueuedPlayers.Contains(steamId);

        if (string.IsNullOrEmpty(name))
        {
            if (isQueued)
            {
                var (position, timeRemaining) = GetQueuePositionAndTime(steamId);
                LocalizationService.HandleReply(ctx, $"佇列中的位置: <color=white>{position}</color> (<color=yellow>{Misc.FormatTimespan(timeRemaining)}</color>)");
            }
            else
            {
                LocalizationService.HandleReply(ctx, "你目前沒有在戰鬥佇列中！使用 '<color=white>.fam challenge [玩家名稱]</color>' 來挑戰其他玩家。");
            }
            return;
        }

        if (isQueued)
        {
            var (position, timeRemaining) = GetQueuePositionAndTime(steamId);
            LocalizationService.HandleReply(ctx, "在佇列中等候戰鬥時不能挑戰其他玩家！佇列中的位置: <color=white>{position}</color> (<color=yellow>{Misc.FormatTimespan(timeRemaining)}</color>)");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply("找不到玩家。");
            return;
        }

        if (playerInfo.User.PlatformId == steamId)
        {
            ctx.Reply("你不能挑戰自己！");
            return;
        }

        if (Matchmaker.QueuedPlayers.Contains(playerInfo.User.PlatformId))
        {
            LocalizationService.HandleReply(ctx, $"<color=green>{playerInfo.User.CharacterName}</color> 已經在戰鬥佇列中了！");
            return;
        }

        if (EmoteSystemPatch.BattleChallenges.Any(challenge => challenge.Item1 == steamId || challenge.Item2 == steamId))
        {
            ctx.Reply("在現有的挑戰過期或被拒絕之前，不能挑戰其他玩家！");
            return;
        }

        if (EmoteSystemPatch.BattleChallenges.Any(challenge => challenge.Item1 == playerInfo.User.PlatformId || challenge.Item2 == playerInfo.User.PlatformId))
        {
            ctx.Reply($"<color=green>{playerInfo.User.CharacterName}</color> 已經有一個待處理的挑戰了！");
            return;
        }

        EmoteSystemPatch.BattleChallenges.Add((ctx.User.PlatformId, playerInfo.User.PlatformId));
        ctx.Reply($"已向 <color=white>{playerInfo.User.CharacterName.Value}</color> 發起戰鬥挑戰！(<color=yellow>30秒</color>後過期)");
        LocalizationService.HandleServerReply(EntityManager, playerInfo.User, $"<color=white>{ctx.User.CharacterName.Value}</color> 已向你發起戰鬥挑戰！(<color=yellow>30秒</color>後過期，使用表情 '<color=green>Yes</color>' 接受或 '<color=red>No</color>' 拒絕)");

        ChallengeExpiredRoutine((ctx.User.PlatformId, playerInfo.User.PlatformId)).Run();
    }

    [Command(name: "setbattlearena", shortHand: "sba", adminOnly: true, usage: ".cw sba", description: "將目前位置設定為寵物戰鬥競技場的中心。")]
    public static void SetBattleArenaCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.FamiliarSystem)
        {
            LocalizationService.HandleReply(ctx, "寵物系統未啟用。");
            return;
        }

        if (!ConfigService.FamiliarBattles)
        {
            LocalizationService.HandleReply(ctx, "寵物戰鬥未啟用。");
            return;
        }

        Entity character = ctx.Event.SenderCharacterEntity;

        float3 location = character.Read<Translation>().Value;
        List<float> floats = [location.x, location.y, location.z];

        DataService.PlayerDictionaries._familiarBattleCoords.Clear();
        DataService.PlayerDictionaries._familiarBattleCoords.Add(floats);
        DataService.PlayerPersistence.SaveFamiliarBattleCoords();

        if (_battlePosition.Equals(float3.zero))
        {
            Initialize();
            LocalizationService.HandleReply(ctx, "寵物競技場位置已設定，戰鬥服務已啟動！(目前僅允許一個競技場)");
        }
        else
        {
            FamiliarBattleCoords.Clear();
            FamiliarBattleCoords.Add(location);

            LocalizationService.HandleReply(ctx, "寵物競技場位置已更改！(目前僅允許一個競技場)");
        }
    }
}
