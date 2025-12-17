using Bloodcraft.Interfaces;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using ProjectM.Scripting;
using Stunlock.Core;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using VampireCommandFramework;
using static Bloodcraft.Services.PlayerService;
using static Bloodcraft.Systems.Expertise.WeaponManager;
using static Bloodcraft.Systems.Expertise.WeaponManager.WeaponStats;
using static Bloodcraft.Systems.Expertise.WeaponSystem;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static Bloodcraft.Utilities.Progression;
using static Bloodcraft.Utilities.Progression.ModifyUnitStatBuffSettings;
using static VCF.Core.Basics.RoleCommands;
using User = ProjectM.Network.User;
using WeaponType = Bloodcraft.Interfaces.WeaponType;

namespace Bloodcraft.Commands;

[CommandGroup(name: "weapon", "wep")]
internal static class WeaponCommands
{
    static EntityManager EntityManager => Core.EntityManager;
    static ServerGameManager ServerGameManager => Core.ServerGameManager;

    static readonly PrefabGUID _exoFormBuff = new(-31099041);

    private const int WEAPON_STAT_BATCH = 6;
    private const int STAT_LIST_BATCH = 4;

    [Command(name: "get", adminOnly: false, usage: ".wep get", description: "顯示目前武器專精資訊.")]
    public static void GetExpertiseCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        WeaponType weaponType = GetCurrentWeaponType(playerCharacter);

        IWeaponExpertise handler = WeaponExpertiseFactory.GetExpertise(weaponType);
        if (handler == null)
        {
            LocalizationService.HandleReply(ctx, "無法取得此武器的專精處理器；這不應該發生，你可能需要回報給開發者。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        var ExpertiseData = handler.GetExpertiseData(steamId);

        int progress = (int)(ExpertiseData.Value - ConvertLevelToXp(ExpertiseData.Key));
        int prestigeLevel = steamId.TryGetPlayerPrestiges(out var prestiges) ? prestiges[WeaponPrestigeTypes[weaponType]] : 0;

        if (ExpertiseData.Key > 0 || ExpertiseData.Value > 0)
        {
            LocalizationService.HandleReply(ctx, $"你的武器專精等級為 [<color=white>{ExpertiseData.Key}</color>][<color=#90EE90>{prestigeLevel}</color>]，目前擁有 <color=yellow>{progress}</color> <color=#FFC0CB>專精值</color> (<color=white>{GetLevelProgress(steamId, handler)}%</color>)，武器種類：<color=#c0c0c0>{weaponType}</color>！");

            if (steamId.TryGetPlayerWeaponStats(out var weaponTypeStats) && weaponTypeStats.TryGetValue(weaponType, out var weaponStatTypes))
            {
                List<KeyValuePair<WeaponStatType, string>> weaponExpertiseStats = [];
                foreach (WeaponStatType weaponStatType in weaponStatTypes)
                {
                    if (!TryGetScaledModifyUnitExpertiseStat(handler, playerCharacter, steamId, weaponType,
                        weaponStatType, out float statValue, out ModifyUnitStatBuff modifyUnitStatBuff)) continue;

                    string weaponStatString = Misc.FormatWeaponStatValue(weaponStatType, statValue);
                    weaponExpertiseStats.Add(new KeyValuePair<WeaponStatType, string>(weaponStatType, weaponStatString));
                }
                foreach (var batch in weaponExpertiseStats.Batch(WEAPON_STAT_BATCH))
                {
                    string bonuses = string.Join(", ", batch.Select(stat => $"<color=#00FFFF>{stat.Key}</color>: <color=white>{stat.Value}</color>"));
                    LocalizationService.HandleReply(ctx, $"<color=#c0c0c0>{weaponType}</color> 屬性加成：{bonuses}");
                }
            }
            else
            {
                LocalizationService.HandleReply(ctx, "你目前裝備的武器沒有任何加成。");
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, $"你尚未獲得 <color=#c0c0c0>{weaponType}</color> 的任何專精值！");
        }
    }

    [Command(name: "log", adminOnly: false, usage: ".wep log", description: "切換專精增益紀錄開關.")]
    public static void LogExpertiseCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        var steamId = ctx.Event.User.PlatformId;
        TogglePlayerBool(steamId, WEAPON_LOG_KEY);

        LocalizationService.HandleReply(ctx, $"專精紀錄目前為 {(GetPlayerBool(steamId, WEAPON_LOG_KEY) ? "<color=green>啟用</color>" : "<color=red>停用</color>")}。");
    }

    [Command(name: "choosestat", shortHand: "cst", adminOnly: false, usage: ".wep cst [WeaponOrStat] [WeaponStat]", description: "選擇要增強的武器屬性.")]
    public static void ChooseWeaponStat(ChatCommandContext ctx, string weaponOrStat, int statType = default)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        ulong steamId = ctx.Event.User.PlatformId;

        WeaponType finalWeaponType;
        WeaponStats.WeaponStatType finalWeaponStat;

        if (int.TryParse(weaponOrStat, out int numericStat))
        {
            numericStat--;

            if (!Enum.IsDefined(typeof(WeaponStats.WeaponStatType), numericStat))
            {
                LocalizationService.HandleReply(ctx,
                    "無效的屬性，請使用 '<color=white>.wep lst</color>' 查看可用選項。");
                return;
            }

            finalWeaponStat = (WeaponStats.WeaponStatType)numericStat;
            finalWeaponType = GetCurrentWeaponType(playerCharacter);

            if (ChooseStat(steamId, finalWeaponType, finalWeaponStat))
            {
                Buffs.RefreshStats(playerCharacter);
                LocalizationService.HandleReply(ctx,
                    $"已為 <color=#c0c0c0>{finalWeaponType}</color> 選擇屬性：<color=#00FFFF>{finalWeaponStat}</color>！");
            }
        }
        else
        {
            if (!Enum.TryParse(weaponOrStat, true, out finalWeaponType))
            {
                LocalizationService.HandleReply(ctx,
                    "無效的武器種類，請使用 '<color=white>.wep lst</color>' 查看可用選項。");
                return;
            }

            if (statType <= 0)
            {
                LocalizationService.HandleReply(ctx,
                    "無效的屬性，請使用 '<color=white>.wep lst</color>' 查看可用選項。");
                return;
            }

            int typedStat = --statType;

            if (!Enum.IsDefined(typeof(WeaponStats.WeaponStatType), typedStat))
            {
                LocalizationService.HandleReply(ctx,
                    "無效的屬性，請使用 '<color=white>.wep lst</color>' 查看可用選項。");
                return;
            }

            finalWeaponStat = (WeaponStats.WeaponStatType)typedStat;

            if (ChooseStat(steamId, finalWeaponType, finalWeaponStat))
            {
                Buffs.RefreshStats(playerCharacter);
                LocalizationService.HandleReply(ctx,
                    $"已為 <color=#c0c0c0>{finalWeaponType}</color> 選擇屬性：<color=#00FFFF>{finalWeaponStat}</color>！");
            }
        }
    }

    [Command(name: "resetstats", shortHand: "rst", adminOnly: false, usage: ".wep rst", description: "重置目前武器的屬性加點.")]
    public static void ResetWeaponStats(ChatCommandContext ctx)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        ulong steamId = ctx.Event.User.PlatformId;

        WeaponType weaponType = GetCurrentWeaponType(playerCharacter);

        if (!ConfigService.ResetExpertiseItem.Equals(0))
        {
            PrefabGUID item = new(ConfigService.ResetExpertiseItem);
            int quantity = ConfigService.ResetExpertiseItemQuantity;

            if (InventoryUtilities.TryGetInventoryEntity(EntityManager, ctx.User.LocalCharacter._Entity, out Entity inventoryEntity) && ServerGameManager.GetInventoryItemCount(inventoryEntity, item) >= quantity)
            {
                if (ServerGameManager.TryRemoveInventoryItem(inventoryEntity, item, quantity))
                {
                    ResetStats(steamId, weaponType);
                    Buffs.RefreshStats(playerCharacter);

                    LocalizationService.HandleReply(ctx,$"你的 <color=#c0c0c0>{weaponType}</color> 屬性已重置！");
                    return;
                }
            }
            else
            {
                LocalizationService.HandleReply(ctx, $"你沒有重置武器屬性所需的物品！(<color=#ffd9eb>{item.GetLocalizedName()}</color>x<color=white>{quantity}</color>)");
                return;
            }

        }

        ResetStats(steamId, weaponType);
        Buffs.RefreshStats(playerCharacter);

        LocalizationService.HandleReply(ctx, $"你的 <color=#c0c0c0>{weaponType}</color> 屬性已重置！");
    }

    [Command(name: "set", adminOnly: true, usage: ".wep set [Name] [Weapon] [Level]", description: "設定玩家的武器專精等級.")]
    public static void SetExpertiseCommand(ChatCommandContext ctx, string name, string weapon, int level)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        PlayerInfo playerInfo = GetPlayerInfo(name);
        if (!playerInfo.UserEntity.Exists())
        {
            ctx.Reply($"找不到玩家。");
            return;
        }

        if (level < 0 || level > ConfigService.MaxExpertiseLevel)
        {
            string message = $"等級必須介於 0 到 {ConfigService.MaxExpertiseLevel} 之間。";
            LocalizationService.HandleReply(ctx, message);
            return;
        }

        if (!Enum.TryParse<WeaponType>(weapon, true, out var weaponType))
        {
            LocalizationService.HandleReply(ctx, $"無效的武器種類。");
            return;
        }

        IWeaponExpertise expertiseHandler = WeaponExpertiseFactory.GetExpertise(weaponType);
        if (expertiseHandler == null)
        {
            LocalizationService.HandleReply(ctx, "無效的武器種類。");
            return;
        }

        ulong steamId = playerInfo.User.PlatformId;

        var xpData = new KeyValuePair<int, float>(level, ConvertLevelToXp(level));
        if (SetExtensionMap.TryGetValue(weaponType, out var setFunc))
        {
            setFunc(steamId, xpData);
            Buffs.RefreshStats(playerInfo.CharEntity);

            LocalizationService.HandleReply(ctx, $"<color=#c0c0c0>{expertiseHandler.GetWeaponType()}</color> 專精等級已設定為 <color=white>{level}</color>，玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "找不到對應的武器資料保存方式...");
        }
    }

    [Command(name: "liststats", shortHand: "lst", adminOnly: false, usage: ".wep lst", description: "列出所有可用的武器屬性.")]
    public static void ListWeaponStatsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        var weaponStatsWithCaps = Enum.GetValues(typeof(WeaponStats.WeaponStatType))
            .Cast<WeaponStats.WeaponStatType>()
            .Select((stat, index) =>
                $"<color=yellow>{index + 1}</color>| <color=#00FFFF>{stat}</color>: <color=white>{Misc.FormatWeaponStatValue(stat, WeaponStats.WeaponStatBaseCaps[stat])}</color>")
            .ToList();

        if (weaponStatsWithCaps.Count == 0)
        {
            LocalizationService.HandleReply(ctx, "目前沒有可用的武器屬性。");
        }
        else
        {
            foreach (var batch in weaponStatsWithCaps.Batch(STAT_LIST_BATCH))
            {
                string replyMessage = string.Join(", ", batch);
                LocalizationService.HandleReply(ctx, replyMessage);
            }
        }
    }

    [Command(name: "list", shortHand: "l", adminOnly: false, usage: ".wep l", description: "列出所有可用的武器專精種類.")]
    public static void ListWeaponsCommand(ChatCommandContext ctx)
    {
        if (!ConfigService.ExpertiseSystem)
        {
            LocalizationService.HandleReply(ctx, "武器專精系統尚未啟用。");
            return;
        }

        string weaponTypes = string.Join(", ", Enum.GetNames(typeof(WeaponType)));
        LocalizationService.HandleReply(ctx, $"可用的武器專精種類：<color=#c0c0c0>{weaponTypes}</color>");
    }

    [Command(name: "setspells", shortHand: "spell", adminOnly: true, usage: ".wep spell [Name] [Slot] [PrefabGuid] [Radius]", description: "手動設定玩家的技能（可用於測試；若指定半徑則會套用到範圍內所有玩家）。")]
    public static void SetSpellCommand(ChatCommandContext ctx, string name, int slot, int ability, float radius = 0f)
    {
        if (!ConfigService.UnarmedSlots)
        {
            LocalizationService.HandleReply(ctx, "額外的徒手技能欄位尚未啟用。");
            return;
        }

        if (slot < 1 || slot > 7)
        {
            LocalizationService.HandleReply(ctx, "無效的欄位（<color=white>1</color> 代表 Q, <color=white>2</color> 代表 E）");
            return;
        }

        if (radius > 0f)
        {
            Entity character = ctx.Event.SenderCharacterEntity;
            float3 charPosition = character.Read<Translation>().Value;

            HashSet<PlayerInfo> processed = [];

            foreach (PlayerInfo playerInfo in SteamIdOnlinePlayerInfoCache.Values)
            {
                if (processed.Contains(playerInfo)) continue;
                else if (playerInfo.CharEntity.TryGetComponent(out Translation translation) && math.distance(charPosition, translation.Value) <= radius)
                {
                    ulong steamId = playerInfo.User.PlatformId;

                    if (steamId.TryGetPlayerSpells(out var spells))
                    {
                        if (slot == 1)
                        {
                            spells.FirstUnarmed = ability;
                            LocalizationService.HandleReply(ctx, $"第一個徒手技能設定為 <color=white>{new PrefabGUID(ability).GetPrefabName()}</color>（玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>）。");
                        }
                        else if (slot == 2)
                        {
                            spells.SecondUnarmed = ability;
                            LocalizationService.HandleReply(ctx, $"第二個徒手技能設定為 <color=white>{new PrefabGUID(ability).GetPrefabName()}</color>（玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>）。");
                        }

                        steamId.SetPlayerSpells(spells);
                    }

                    processed.Add(playerInfo);
                }
            }
        }
        else if (radius < 0f)
        {
            LocalizationService.HandleReply(ctx, "半徑必須大於 0！");
            return;
        }
        else
        {
            PlayerInfo playerInfo = GetPlayerInfo(name);
            if (!playerInfo.UserEntity.Exists())
            {
                ctx.Reply($"找不到玩家。");
                return;
            }

            ulong steamId = playerInfo.User.PlatformId;

            if (steamId.TryGetPlayerSpells(out var spells))
            {
                if (slot == 1)
                {
                    spells.FirstUnarmed = ability;
                    LocalizationService.HandleReply(ctx, $"第一個徒手技能已設定為 <color=white>{new PrefabGUID(ability).GetPrefabName()}</color>（玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>）。");
                }
                else if (slot == 2)
                {
                    spells.SecondUnarmed = ability;
                    LocalizationService.HandleReply(ctx, $"第二個徒手技能已設定為 <color=white>{new PrefabGUID(ability).GetPrefabName()}</color>（玩家：<color=green>{playerInfo.User.CharacterName.Value}</color>）。");
                }

                steamId.SetPlayerSpells(spells);
            }
        }
    }

    [Command(name: "lockspells", shortHand: "locksp", adminOnly: false, usage: ".wep locksp", description: "鎖定下一次裝備的徒手技能.")]
    public static void LockPlayerSpells(ChatCommandContext ctx)
    {
        if (!ConfigService.UnarmedSlots)
        {
            LocalizationService.HandleReply(ctx, "額外徒手技能欄位尚未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;
        ulong SteamID = user.PlatformId;

        if (playerCharacter.HasBuff(_exoFormBuff))
        {
            LocalizationService.HandleReply(ctx, "使用異形形態時無法鎖定技能。");
            return;
        }

        TogglePlayerBool(SteamID, SPELL_LOCK_KEY);

        if (GetPlayerBool(SteamID, SPELL_LOCK_KEY))
        {
            LocalizationService.HandleReply(ctx, "請切換成你想要鎖定到徒手欄位的技能。完成後再次使用此指令解鎖。");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "徒手技能已鎖定。");
        }
    }
}
