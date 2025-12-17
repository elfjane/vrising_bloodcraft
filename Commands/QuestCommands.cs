using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;
using static Bloodcraft.Services.PlayerService;
using static Bloodcraft.Systems.Quests.QuestSystem;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;

namespace Bloodcraft.Commands;

[CommandGroup(name: "quest")]
internal static class QuestCommands
{
    static EntityManager EntityManager => Core.EntityManager;
    static ServerGameManager ServerGameManager => Core.ServerGameManager;

    [Command(name: "log", adminOnly: false, usage: ".quest log", description: "切換任務進度記錄顯示。")]
    public static void LogQuestCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        var steamId = ctx.Event.User.PlatformId;

        TogglePlayerBool(steamId, QUEST_LOG_KEY);
        LocalizationService.HandleReply(ctx, $"任務紀錄現在為 {(GetPlayerBool(steamId, QUEST_LOG_KEY) ? "<color=green>啟用</color>" : "<color=red>停用</color>")}。");
    }

    [Command(name: "progress", shortHand: "p", adminOnly: false, usage: ".quest p [QuestType]", description: "顯示目前任務進度。")]
    public static void DailyQuestProgressCommand(ChatCommandContext ctx, string questType)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        questType = questType.ToLower();
        if (!Enum.TryParse(questType, true, out QuestType typeEnum))
        {
            if (questType == "d")
            {
                typeEnum = QuestType.Daily;
            }
            else if (questType == "w")
            {
                typeEnum = QuestType.Weekly;
            }
            else
            {
                LocalizationService.HandleReply(ctx, "無效的任務種類。（daily/weekly 或 d/w）");
                return;
            }
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (steamId.TryGetPlayerQuests(out var questData))
        {
            Quests.QuestObjectiveReply(ctx, questData, typeEnum);
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你還沒有任何任務，稍後再查看。");
        }
    }

    [Command(name: "track", shortHand: "t", adminOnly: false, usage: ".quest t [QuestType]", description: "定位並追蹤任務目標。")]
    public static void LocateTargetCommand(ChatCommandContext ctx, string questType)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        questType = questType.ToLower();
        if (!Enum.TryParse(questType, true, out QuestType typeEnum))
        {
            if (questType == "d")
            {
                typeEnum = QuestType.Daily;
            }
            else if (questType == "w")
            {
                typeEnum = QuestType.Weekly;
            }
            else
            {
                LocalizationService.HandleReply(ctx, "無效的任務種類。（daily/weekly 或 d/w）");
                return;
            }
        }

        if (QuestService._lastUpdate == default)
        {
            LocalizationService.HandleReply(ctx, "任務目標快取尚未準備好，請稍後再試！");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (steamId.TryGetPlayerQuests(out var questData))
        {
            Quests.QuestTrackReply(ctx, questData, typeEnum);
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你還沒有任何任務，稍後再查看！");
        }
    }

    [Command(name: "refresh", shortHand: "rf", adminOnly: true, usage: ".quest rf [Name]", description: "重新整理玩家的每日與每週任務。")]
    public static void ForceRefreshQuests(ChatCommandContext ctx, string name)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply($"找不到該玩家。");
            return;
        }

        ulong steamId = playerInfo.User.PlatformId;

        // int level = (ConfigService.LevelingSystem && steamId.TryGetPlayerExperience(out var data)) ? data.Key : (int)playerInfo.CharEntity.Read<Equipment>().GetFullLevel();
        int level = (ConfigService.LevelingSystem && steamId.TryGetPlayerExperience(out var data)) ? data.Key : Progression.GetSimulatedLevel(playerInfo.UserEntity);
        ForceRefresh(steamId, level);

        LocalizationService.HandleReply(ctx, $"玩家 <color=green>{playerInfo.User.CharacterName.Value}</color> 的任務已重新整理。");
    }

    [Command(name: "reroll", shortHand: "r", adminOnly: false, usage: ".quest r [QuestType]", description: "重新抽取任務（目前僅支援每日任務）。")]
    public static void RerollQuestCommand(ChatCommandContext ctx, string questType)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        questType = questType.ToLower();
        if (questType == "d")
        {
            questType = "Daily";
        }
        else if (questType == "w")
        {
            questType = "Weekly";
        }

        if (!Enum.TryParse(questType, true, out QuestType type))
        {
            LocalizationService.HandleReply(ctx, "無效的任務種類。（Daily/Weekly）");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        if (type.Equals(QuestType.Daily))
        {
            if (steamId.TryGetPlayerQuests(out var questData) && questData[QuestType.Daily].Objective.Complete && !ConfigService.InfiniteDailies)
            {
                LocalizationService.HandleReply(ctx, "你今天的 <color=#00FFFF>每日任務</color> 已完成，請明天再來。");
                return;
            }
            else if (!ConfigService.RerollDailyPrefab.Equals(0))
            {
                PrefabGUID item = new(ConfigService.RerollDailyPrefab);
                int quantity = ConfigService.RerollDailyAmount;

                if (InventoryUtilities.TryGetInventoryEntity(EntityManager, ctx.User.LocalCharacter._Entity, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, item) >= quantity)
                {
                    if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, item, quantity))
                    {
                        // int level = (ConfigService.LevelingSystem && steamId.TryGetPlayerExperience(out var data)) ? data.Key : (int)ctx.Event.SenderCharacterEntity.Read<Equipment>().GetFullLevel();
                        int level = (ConfigService.LevelingSystem && steamId.TryGetPlayerExperience(out var data)) ? data.Key : Progression.GetSimulatedLevel(ctx.Event.SenderUserEntity);
                        ForceDaily(ctx.Event.User.PlatformId, level);

                        LocalizationService.HandleReply(ctx, $"你的 <color=#00FFFF>每日任務</color> 已重新抽取，消耗 <color=#C0C0C0>{item.GetLocalizedName()}</color> x<color=white>{quantity}</color>！");
                        Quests.QuestObjectiveReply(ctx, questData, type);
                    }
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"你無法負擔重新抽取每日任務的成本...（需要 <color=#C0C0C0>{item.GetLocalizedName()}</color> x<color=white>{quantity}</color>）");
                }
            }
            else
            {
                LocalizationService.HandleReply(ctx, "尚未設定每日任務重新抽取物品，或找不到玩家的每日任務資料。");
            }
        }
        else if (type.Equals(QuestType.Weekly))
        {
            if (steamId.TryGetPlayerQuests(out var questData) && questData[QuestType.Weekly].Objective.Complete)
            {
                LocalizationService.HandleReply(ctx, "你的 <color=#BF40BF>每週任務</color> 已完成，請下週再來。");
                return;
            }
            else if (!ConfigService.RerollWeeklyPrefab.Equals(0))
            {
                PrefabGUID item = new(ConfigService.RerollWeeklyPrefab);
                int quantity = ConfigService.RerollWeeklyAmount;

                if (InventoryUtilities.TryGetInventoryEntity(EntityManager, ctx.User.LocalCharacter._Entity, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, item) >= quantity)
                {
                    if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, item, quantity))
                    {
                        int level = (ConfigService.LevelingSystem && steamId.TryGetPlayerExperience(out var data)) ? data.Key : (int)ctx.Event.SenderCharacterEntity.Read<Equipment>().GetFullLevel();
                        ForceWeekly(ctx.Event.User.PlatformId, level);

                        LocalizationService.HandleReply(ctx, $"你的 <color=#BF40BF>每週任務</color> 已重新抽取，消耗 <color=#C0C0C0>{item.GetLocalizedName()}</color> x<color=white>{quantity}</color>！");
                        Quests.QuestObjectiveReply(ctx, questData, type);
                    }
                }
                else
                {
                    LocalizationService.HandleReply(ctx, $"你無法負擔重新抽取每週任務的成本...（需 <color=#C0C0C0>{item.GetLocalizedName()}</color> x<color=white>{quantity}</color>）");
                }
            }
            else
            {
                LocalizationService.HandleReply(ctx, "尚未設定每週任務重新抽取物品，或找不到玩家的每週任務資料。");
            }
        }
    }

    [Command(name: "complete", shortHand: "c", adminOnly: true, usage: ".quest c [Name] [QuestType]", description: "強制完成指定玩家的任務。")]
    public static void ForceCompleteQuest(ChatCommandContext ctx, string name, string questTypeName)
    {
        if (!ConfigService.QuestSystem)
        {
            LocalizationService.HandleReply(ctx, "任務系統尚未啟用。");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply("找不到該玩家...");
            return;
        }

        User user = playerInfo.User;
        ulong steamId = user.PlatformId;

        if (!steamId.TryGetPlayerQuests(out var questData))
        {
            ctx.Reply("玩家沒有任何進行中的任務！");
            return;
        }

        questTypeName = questTypeName.ToLower();
        if (questTypeName == "d")
        {
            questTypeName = "Daily";
        }
        else if (questTypeName == "w")
        {
            questTypeName = "Weekly";
        }

        if (!Enum.TryParse<QuestType>(questTypeName, true, out var questType))
        {
            ctx.Reply($"無效的任務種類 '{questTypeName}'。有效選項為：{string.Join(", ", Enum.GetNames(typeof(QuestType)))}");
            return;
        }

        if (!questData.ContainsKey(questType))
        {
            ctx.Reply($"玩家沒有進行中的 {questType} 任務可完成。");
            return;
        }

        var quest = questData[questType];
        if (quest.Objective.Complete)
        {
            ctx.Reply($"{playerInfo.User.CharacterName.Value} 的該 {questType} 任務已經完成。");
            return;
        }

        PrefabGUID target = quest.Objective.Target;

        int currentProgress = quest.Progress;
        int required = quest.Objective.RequiredAmount;

        int toAdd = required - currentProgress;
        if (toAdd <= 0) toAdd = required;

        ProcessQuestProgress(questData, target, toAdd, user);

        ctx.Reply($"已為玩家 <color=green>{playerInfo.User.CharacterName.Value}</color> 強制完成 {Quests.QuestTypeColor[questType]}！");
    }
}
