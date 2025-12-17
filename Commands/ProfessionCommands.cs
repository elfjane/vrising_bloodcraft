using Bloodcraft.Interfaces;
using Bloodcraft.Services;
using Bloodcraft.Systems.Professions;
using VampireCommandFramework;
using static Bloodcraft.Services.PlayerService;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static Bloodcraft.Utilities.Progression;

namespace Bloodcraft.Commands;

[CommandGroup(name: "profession", "prof")]
internal static class ProfessionCommands
{
    const int MAX_PROFESSION_LEVEL = 100;

    [Command(name: "log", adminOnly: false, usage: ".prof log", description: "切換專精進度紀錄開關.")]
    public static void LogProgessionCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ProfessionSystem)
        {
            LocalizationService.HandleReply(ctx, "專精系統尚未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        TogglePlayerBool(steamId, PROFESSION_LOG_KEY);
        LocalizationService.HandleReply(ctx, $"專精紀錄目前為 {(GetPlayerBool(steamId, PROFESSION_LOG_KEY) ? "<color=green>啟用</color>" : "<color=red>停用</color>")}。");
    }

    [Command(name: "get", adminOnly: false, usage: ".prof get [Profession]", description: "顯示你目前的專精進度.")]
    public static void GetProfessionCommand(ChatCommandContext ctx, string profession)
    {
        if (!ConfigService.ProfessionSystem)
        {
            LocalizationService.HandleReply(ctx, "專精系統尚未啟用。");
            return;
        }

        if (!Enum.TryParse(profession, true, out Profession professionType))
        {
            LocalizationService.HandleReply(ctx, $"可用專精：{ProfessionFactory.GetProfessionNames()}");
            return;
        }

        if (professionType.IsDisabled())
        {
            var handler = ProfessionFactory.GetProfession(professionType);
            LocalizationService.HandleReply(ctx, $"{handler.GetProfessionName()} 已在設定中停用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        IProfession professionHandler = ProfessionFactory.GetProfession(professionType);
        if (professionHandler == null)
        {
            LocalizationService.HandleReply(ctx, "無效的專精。");
            return;
        }

        KeyValuePair<int, float> data = professionHandler.GetProfessionData(steamId);
        if (data.Key > 0)
        {
            int progress = (int)(data.Value - ConvertLevelToXp(data.Key));
            LocalizationService.HandleReply(ctx,
                $"你的專精等級為 [<color=white>{data.Key}</color>]，擁有 <color=yellow>{progress}</color> <color=#FFC0CB>熟練度</color> (<color=white>{ProfessionSystem.GetLevelProgress(steamId, professionHandler)}%</color>)，專精：{professionHandler.GetProfessionName()}");
        }
        else
        {
            LocalizationService.HandleReply(ctx, $"你尚未在 {professionHandler.GetProfessionName()} 中獲得任何進度！");
        }
    }

    [Command(name: "set", adminOnly: true, usage: ".prof set [Name] [Profession] [Level]", description: "設定玩家專精等級.")]
    public static void SetProfessionCommand(ChatCommandContext ctx, string name, string profession, int level)
    {
        if (!ConfigService.ProfessionSystem)
        {
            LocalizationService.HandleReply(ctx, "專精系統尚未啟用。");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply($"找不到玩家。");
            return;
        }

        if (level < 0 || level > MAX_PROFESSION_LEVEL)
        {
            LocalizationService.HandleReply(ctx, $"等級必須介於 0 到 {MAX_PROFESSION_LEVEL} 之間。");
            return;
        }

        if (!Enum.TryParse(profession, true, out Profession professionType))
        {
            LocalizationService.HandleReply(ctx, $"可用專精：{ProfessionFactory.GetProfessionNames()}");
            return;
        }

        IProfession professionHandler = ProfessionFactory.GetProfession(professionType);
        if (professionHandler == null)
        {
            LocalizationService.HandleReply(ctx, "無效的專精。");
            return;
        }

        ulong steamId = playerInfo.User.PlatformId;

        float xp = ConvertLevelToXp(level);
        professionHandler.SetProfessionData(steamId, new KeyValuePair<int, float>(level, xp));

        LocalizationService.HandleReply(ctx,
            $"{professionHandler.GetProfessionName()} 等級已設定為 [<color=white>{level}</color>]，玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>");
    }

    [Command(name: "list", shortHand: "l", adminOnly: false, usage: ".prof l", description: "列出可用專精.")]
    public static void ListProfessionsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ProfessionSystem)
        {
            LocalizationService.HandleReply(ctx, "專精系統尚未啟用。");
            return;
        }

        LocalizationService.HandleReply(ctx, $"可用專精：{ProfessionFactory.GetProfessionNames()}");
    }
}
