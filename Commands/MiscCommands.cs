using Bloodcraft.Services;
using ProjectM;
using ProjectM.Gameplay.Systems;
using ProjectM.Network;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;
using static Bloodcraft.Services.DataService.FamiliarPersistence.FamiliarUnlocksManager;
using static Bloodcraft.Utilities.Misc;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static VCF.Core.Basics.RoleCommands;

namespace Bloodcraft.Commands;

[CommandGroup(name: "miscellaneous", "misc")]
internal static class MiscCommands
{
    static ServerGameManager ServerGameManager => Core.ServerGameManager;
    static SystemService SystemService => Core.SystemService;
    static CombatMusicSystem_Server CombatMusicSystemServer => SystemService.CombatMusicSystem_Server;
    static ClaimAchievementSystem ClaimAchievementSystem => SystemService.ClaimAchievementSystem;
    static EntityCommandBufferSystem EntityCommandBufferSystem => SystemService.EntityCommandBufferSystem;

    static readonly PrefabGUID _combatBuff = new(581443919);

    public static readonly Dictionary<PrefabGUID, int> StarterKitItemPrefabGUIDs = [];

    private const int MAX_PER_MESSAGE = 6;

    [Command(name: "reminders", shortHand: "remindme", adminOnly: false, usage: ".misc remindme", description: "切換模組相關的提示功能。")]
    public static void LogExperienceCommand(ChatCommandContext ctx)
    {
        ulong steamId = ctx.Event.User.PlatformId;

        TogglePlayerBool(steamId, REMINDERS_KEY);
        LocalizationService.HandleReply(ctx, $"提示功能已{(GetPlayerBool(steamId, REMINDERS_KEY) ? "<color=green>啟用</color>" : "<color=red>關閉</color>")}。");
    }

    [Command(name: "sct", adminOnly: false, usage: ".misc sct [Type]", description: "切換各種捲動文字顯示。")]
    public static void ToggleScrollingText(ChatCommandContext ctx, string input = "")
    {
        ulong steamId = ctx.Event.User.PlatformId;

        if (string.IsNullOrWhiteSpace(input))
        {
            ReplySCTDetails(ctx);
            return;
        }
        else if (int.TryParse(input, out int sctEnum))
        {
            sctEnum--;

            if (!Enum.IsDefined(typeof(ScrollingTextMessage), sctEnum))
            {
                ReplySCTDetails(ctx);
                return;
            }

            ScrollingTextMessage sctType = (ScrollingTextMessage)sctEnum;

            if (!ScrollingTextBoolKeyMap.TryGetValue(sctType, out var boolKey))
            {
                LocalizationService.HandleReply(ctx, "找不到對應的捲動文字設定...");
                return;
            }

            TogglePlayerBool(steamId, boolKey);
            bool currentState = GetPlayerBool(steamId, boolKey);

            LocalizationService.HandleReply(ctx, $"<color=white>{sctType}</color> 捲動文字已{(currentState ? "<color=green>啟用</color>" : "<color=red>關閉</color>")}。");
        }
        else
        {
            if (!ScrollingTextNameMap.TryGetValue(input, out var sctType))
            {
                ReplySCTDetails(ctx);
                return;
            }

            if (!ScrollingTextBoolKeyMap.TryGetValue(sctType, out var boolKey))
            {
                LocalizationService.HandleReply(ctx, "找不到對應的捲動文字設定...");
                return;
            }

            TogglePlayerBool(steamId, boolKey);
            bool currentState = GetPlayerBool(steamId, boolKey);

            LocalizationService.HandleReply(ctx, $"<color=white>{sctType}</color> 捲動文字已{(currentState ? "<color=green>啟用</color>" : "<color=red>關閉</color>")}。");
        }
    }

    [Command(name: "starterkit", shortHand: "kitme", adminOnly: false, usage: ".misc kitme", description: "給予新手禮包。")]
    public static void KitMe(ChatCommandContext ctx)
    {
        if (!ConfigService.StarterKit)
        {
            LocalizationService.HandleReply(ctx, "新手禮包尚未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        if (!GetPlayerBool(steamId, STARTER_KIT_KEY))
        {
            SetPlayerBool(steamId, STARTER_KIT_KEY, true);
            Entity character = ctx.Event.SenderCharacterEntity;

            foreach (var item in StarterKitItemPrefabGUIDs)
            {
                ServerGameManager.TryAddInventoryItem(character, item.Key, item.Value);
            }

            string kitFamiliarName = string.Empty;
            PrefabGUID familiarPrefabGuid = new(ConfigService.KitFamiliar);

            if (familiarPrefabGuid.HasValue()
                && familiarPrefabGuid.IsCharacter())
            {
                FamiliarUnlocksData unlocksData = LoadFamiliarUnlocksData(steamId);
                string boxName = steamId.TryGetFamiliarBox(out var currentBox) ? currentBox : string.Empty;

                if (string.IsNullOrEmpty(boxName) || !unlocksData.FamiliarUnlocks.ContainsKey(boxName))
                {
                    boxName = unlocksData.FamiliarUnlocks.Count == 0 ? "box1" : unlocksData.FamiliarUnlocks.Keys.First();
                    if (!unlocksData.FamiliarUnlocks.ContainsKey(boxName))
                    {
                        unlocksData.FamiliarUnlocks[boxName] = [];
                    }

                    steamId.SetFamiliarBox(boxName);
                }

                if (!unlocksData.FamiliarUnlocks.TryGetValue(boxName, out var familiars))
                {
                    familiars = [];
                    unlocksData.FamiliarUnlocks[boxName] = familiars;
                }

                int familiarGuid = familiarPrefabGuid.GuidHash;

                if (!familiars.Contains(familiarGuid))
                {
                    familiars.Add(familiarGuid);
                    SaveFamiliarUnlocksData(steamId, unlocksData);
                    kitFamiliarName = new PrefabGUID(familiarGuid).GetLocalizedName();
                }
            }

            List<string> kitItems = [.. StarterKitItemPrefabGUIDs.Select(x => $"<color=#ffd9eb>{x.Key.GetLocalizedName()}</color>x<color=white>{x.Value}</color>")];

            LocalizationService.HandleReply(ctx, "你獲得了 <color=yellow>新手禮包</color>：");
            foreach (var batch in kitItems.Batch(MAX_PER_MESSAGE))
            {
                string items = string.Join(", ", batch);
                LocalizationService.HandleReply(ctx, items);
            }

            if (!string.IsNullOrEmpty(kitFamiliarName))
            {
                LocalizationService.HandleReply(ctx, $"並解鎖寵物：<color=green>{kitFamiliarName}</color>");
            }
        }
        else
        {
            ctx.Reply("你已使用過 <color=white>新手禮包</color>！");
        }
    }

    [Command(name: "prepareforthehunt", shortHand: "prepare", adminOnly: false, usage: ".misc prepare", description: "完成「狩獵準備」任務（若尚未完成）。")]
    public static void QuickStartCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.LevelingSystem)
        {
            LocalizationService.HandleReply(ctx, "等級系統尚未啟用。");
            return;
        }

        EntityCommandBuffer entityCommandBuffer = EntityCommandBufferSystem.CreateCommandBuffer();
        PrefabGUID achievementPrefabGUID = new(560247139); // Journal_GettingReadyForTheHunt

        Entity userEntity = ctx.Event.SenderUserEntity;
        Entity characterEntity = ctx.Event.SenderCharacterEntity;
        Entity achievementOwnerEntity = userEntity.Read<AchievementOwner>().Entity._Entity;

        ClaimAchievementSystem.CompleteAchievement(entityCommandBuffer, achievementPrefabGUID, userEntity, characterEntity, achievementOwnerEntity, false, true);
        LocalizationService.HandleReply(ctx, "你已準備好開始狩獵！");
    }

    [Command(name: "userstats", adminOnly: false, usage: ".misc userstats", description: "顯示玩家各項統計資料。")]
    public static void GetUserStats(ChatCommandContext ctx)
    {
        Entity userEntity = ctx.Event.SenderUserEntity;
        UserStats userStats = userEntity.Read<UserStats>();

        int VBloodKills = userStats.VBloodKills;
        int UnitKills = userStats.UnitKills;
        int Deaths = userStats.Deaths;

        float OnlineTime = userStats.OnlineTime;
        OnlineTime = (int)OnlineTime / 3600;

        float DistanceTraveled = userStats.DistanceTravelled;
        DistanceTraveled = (int)DistanceTraveled / 1000;

        float LitresBloodConsumed = userStats.LitresBloodConsumed;
        LitresBloodConsumed = (int)LitresBloodConsumed;

        LocalizationService.HandleReply(ctx,
            $"<color=white>擊殺首領</color>: <color=#FF5733>{VBloodKills}</color> | " +
            $"<color=white>擊殺單位</color>: <color=#FFD700>{UnitKills}</color> | " +
            $"<color=white>死亡次數</color>: <color=#808080>{Deaths}</color> | " +
            $"<color=white>上線時間</color>: <color=#1E90FF>{OnlineTime}</color> 小時 | " +
            $"<color=white>行走距離</color>: <color=#32CD32>{DistanceTraveled}</color> 公里 | " +
            $"<color=white>飲用血量</color>: <color=red>{LitresBloodConsumed}</color> L");
    }

    [Command(name: "silence", adminOnly: false, usage: ".misc silence", description: "重置卡住的戰鬥音樂。")]
    public static void ResetMusicCommand(ChatCommandContext ctx)
    {
        Entity character = ctx.Event.SenderCharacterEntity;

        if (ServerGameManager.HasBuff(character, _combatBuff.ToIdentifier()))
        {
            LocalizationService.HandleReply(ctx, "此指令僅應在需要時使用，且不可在戰鬥中使用。");
            return;
        }

        CombatMusicListener_Shared combatMusicListener_Shared = character.Read<CombatMusicListener_Shared>();
        combatMusicListener_Shared.UnitPrefabGuid = new PrefabGUID(0);
        character.Write(combatMusicListener_Shared);

        CombatMusicSystemServer.OnUpdate();
        ctx.Reply("戰鬥音樂已重置！");
    }
}
