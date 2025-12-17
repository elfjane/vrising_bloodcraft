using Bloodcraft.Interfaces;
using Bloodcraft.Services;
using Bloodcraft.Utilities;
using ProjectM;
using Stunlock.Core;
using Unity.Entities;
using VampireCommandFramework;
using static Bloodcraft.Systems.Leveling.ClassManager;
using static Bloodcraft.Utilities.Classes;
using static Bloodcraft.Utilities.Misc.PlayerBoolsManager;
using static VCF.Core.Basics.RoleCommands;
using User = ProjectM.Network.User;

namespace Bloodcraft.Commands;

[CommandGroup(name: "class")]
internal static class ClassCommands
{
    static EntityManager EntityManager => Core.EntityManager;

    static readonly bool _classes = ConfigService.ClassSystem;

    [Command(name: "select", shortHand: "s", adminOnly: false, usage: ".class s [Class]", description: "選擇職業.")]
    public static void SelectClassCommand(ChatCommandContext ctx, string input)
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;
        ulong steamId = ctx.Event.User.PlatformId;
        PlayerClass? nullablePlayerClass = ParseClassFromInput(ctx, input);

        if (nullablePlayerClass.HasValue)
        {
            PlayerClass playerClass = nullablePlayerClass.Value;

            if (!steamId.HasClass(out PlayerClass? currentClass) || !currentClass.HasValue)
            {
                UpdatePlayerClass(playerCharacter, playerClass, steamId);
                // ApplyClassBuffs(playerCharacter, steamId);

                LocalizationService.HandleReply(ctx, $"你選擇了 {FormatClassName(playerClass)}!");
            }
            else
            {
                LocalizationService.HandleReply(ctx, $"你已經選擇過 {FormatClassName(currentClass.Value)}，若要更換請使用 <color=white>'.class c [Class]'</color>（需要 <color=#ffd9eb>{new PrefabGUID(ConfigService.ChangeClassItem).GetLocalizedName()}</color>x<color=white>{ConfigService.ChangeClassQuantity}</color>）。");
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "無效的職業，用 '<color=white>.class l</color>' 查看所有選項。");
        }
    }

    [Command(name: "choosespell", shortHand: "csp", adminOnly: false, usage: ".class csp [#]", description: "選擇職業的 Shift 技能（需要足夠的轉生等級）。")]
    public static void ChooseClassSpell(ChatCommandContext ctx, int choice)
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        if (!ConfigService.ShiftSlot)
        {
            LocalizationService.HandleReply(ctx, "Shift 技能未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderCharacterEntity;

        if (!InventoryUtilities.TryGetInventoryEntity(EntityManager, playerCharacter, out Entity inventoryEntity) || InventoryUtilities.IsInventoryFull(EntityManager, inventoryEntity))
        {
            LocalizationService.HandleReply(ctx, "無法更換或啟用職業技能，背包必須至少保留一格空間以安全處理切換時的珠寶。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;

        if (GetPlayerBool(steamId, SHIFT_LOCK_KEY)
            && steamId.HasClass(out PlayerClass? playerClass)
            && playerClass.HasValue)
        {
            if (ConfigService.PrestigeSystem && steamId.TryGetPlayerPrestiges(out var prestigeData) && prestigeData.TryGetValue(PrestigeType.Experience, out var prestigeLevel))
            {
                List<int> spells = Configuration.ParseIntegersFromString(ClassSpellsMap[playerClass.Value]);

                if (spells.Count == 0)
                {
                    LocalizationService.HandleReply(ctx, $"{FormatClassName(playerClass.Value)} 沒有設定任何技能！");
                    return;
                }
                else if (choice < 0 || choice > spells.Count)
                {
                    LocalizationService.HandleReply(ctx, $"無效技能，用 '<color=white>.class lsp</color>' 查看職業技能列表。");
                    return;
                }

                if (choice == 0)
                {
                    if (ConfigService.DefaultClassSpell == 0)
                    {
                        LocalizationService.HandleReply(ctx, "預設技能未設定！");
                        return;
                    }
                    else if (prestigeLevel < Configuration.ParseIntegersFromString(ConfigService.PrestigeLevelsToUnlockClassSpells)[choice])
                    {
                        LocalizationService.HandleReply(ctx, "你的轉生等級不足以解鎖這個技能！");
                        return;
                    }
                    else if (steamId.TryGetPlayerSpells(out var data))
                    {
                        PrefabGUID spellPrefabGUID = new(ConfigService.DefaultClassSpell);
                        data.ClassSpell = ConfigService.DefaultClassSpell;

                        steamId.SetPlayerSpells(data);
                        UpdateShift(ctx, playerCharacter, spellPrefabGUID);

                        return;
                    }
                }
                else if (prestigeLevel < Configuration.ParseIntegersFromString(ConfigService.PrestigeLevelsToUnlockClassSpells)[choice])
                {
                    LocalizationService.HandleReply(ctx, "你的轉生等級不足以解鎖這個技能！");
                    return;
                }
                else if (steamId.TryGetPlayerSpells(out var spellsData))
                {
                    spellsData.ClassSpell = spells[choice - 1];
                    steamId.SetPlayerSpells(spellsData);

                    UpdateShift(ctx, ctx.Event.SenderCharacterEntity, new(spellsData.ClassSpell));
                }
            }
            else
            {
                List<int> spells = Configuration.ParseIntegersFromString(ClassSpellsMap[playerClass.Value]);

                if (spells.Count == 0)
                {
                    LocalizationService.HandleReply(ctx, $"{FormatClassName(playerClass.Value)} 沒有設定任何技能！");
                    return;
                }
                else if (choice < 0 || choice > spells.Count)
                {
                    LocalizationService.HandleReply(ctx, $"無效技能，用 '<color=white>.class lsp</color>' 查看技能列表。");
                    return;
                }

                if (choice == 0)
                {
                    if (steamId.TryGetPlayerSpells(out var data))
                    {
                        if (ConfigService.DefaultClassSpell == 0)
                        {
                            LocalizationService.HandleReply(ctx, "預設技能未設定！");
                            return;
                        }

                        PrefabGUID spellPrefabGUID = new(ConfigService.DefaultClassSpell);
                        data.ClassSpell = ConfigService.DefaultClassSpell;

                        steamId.SetPlayerSpells(data);
                        UpdateShift(ctx, ctx.Event.SenderCharacterEntity, spellPrefabGUID);

                        return;
                    }
                }

                if (steamId.TryGetPlayerSpells(out var spellsData))
                {
                    spellsData.ClassSpell = spells[choice - 1];
                    steamId.SetPlayerSpells(spellsData);

                    UpdateShift(ctx, ctx.Event.SenderCharacterEntity, new(spellsData.ClassSpell));
                }
            }
        }
        else
        {
            LocalizationService.HandleReply(ctx, "你尚未選擇職業或尚未啟用 Shift 技能！（<color=white>'.class s [Class]'</color> | <color=white>'.class shift'</color>）");
        }
    }

    [Command(name: "change", shortHand: "c", adminOnly: false, usage: ".class c [Class]", description: "更換職業.")]
    public static void ChangeClassCommand(ChatCommandContext ctx, string input)
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        Entity playerCharacter = ctx.Event.SenderUserEntity;
        ulong steamId = ctx.Event.User.PlatformId;

        PlayerClass? nullablePlayerClass = ParseClassFromInput(ctx, input);

        if (nullablePlayerClass.HasValue)
        {
            PlayerClass playerClass = nullablePlayerClass.Value;

            if (!steamId.HasClass(out PlayerClass? currentClass) || !currentClass.HasValue)
            {
                LocalizationService.HandleReply(ctx, "你尚未選擇職業，請先使用 <color=white>'.class s [Class]'</color> 選擇職業。");
                return;
            }

            if (GetPlayerBool(steamId, CLASS_BUFFS_KEY))
            {
                LocalizationService.HandleReply(ctx, "你目前啟用職業增益，被鎖定無法更換，請先使用 <color=white>'.class passives'</color> 關閉增益！");
                return;
            }

            if (ConfigService.ChangeClassItem != 0 && !HandleClassChangeItem(ctx))
            {
                return;
            }

            UpdatePlayerClass(playerCharacter, playerClass, steamId);
            LocalizationService.HandleReply(ctx, $"職業已更換為 {FormatClassName(playerClass)}!");
        }
        else
        {
            LocalizationService.HandleReply(ctx, "無效的職業，使用 '<color=white>.class l</color>' 查看選項。");
        }
    }

    [Command(name: "list", shortHand: "l", adminOnly: false, usage: ".class l", description: "列出所有可選職業.")]
    public static void ListClasses(ChatCommandContext ctx)
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        // 英文 → 中文 對照
        Dictionary<PlayerClass, string> chineseNames = new()
    {
        { PlayerClass.BloodKnight, "血之騎士" },
        { PlayerClass.DemonHunter, "惡魔獵人" },
        { PlayerClass.VampireLord, "吸血鬼領主" },
        { PlayerClass.ShadowBlade, "暗影之刃" },
        { PlayerClass.ArcaneSorcerer, "秘法術士" },
        { PlayerClass.DeathMage, "死亡法師" }
    };

        var classes = Enum.GetValues(typeof(PlayerClass))
            .Cast<PlayerClass>()
            .Select((playerClass, index) =>
            {
                string zh = chineseNames.ContainsKey(playerClass) ? chineseNames[playerClass] : playerClass.ToString();
                return $"<color=yellow>{index + 1}</color>| {zh}";
            })
            .ToList();

        string classTypes = string.Join(", ", classes);
        LocalizationService.HandleReply(ctx, $"可選職業：{classTypes}");
    }

    [Command(name: "listspells", shortHand: "lsp", adminOnly: false, usage: ".class lsp [Class]", description: "顯示該職業可獲得的技能.")]
    public static void ListClassSpellsCommand(ChatCommandContext ctx, string classType = "")
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        PlayerClass? nullablePlayerClass = ParseClassFromInput(ctx, classType);

        if (nullablePlayerClass.HasValue)
        {
            ReplyClassSpells(ctx, nullablePlayerClass.Value);
        }
        else if (string.IsNullOrEmpty(classType) && steamId.HasClass(out PlayerClass? currentClass) && currentClass.HasValue)
        {
            ReplyClassSpells(ctx, currentClass.Value);
        }

        /*
        else
        {
            LocalizationService.HandleReply(ctx, "Invalid class, use '<color=white>.class l</color>' to see options.");
        }

        ulong steamId = ctx.Event.User.PlatformId;

        if (steamId.HasClass(out PlayerClass? playerClass)
            && playerClass.HasValue)
        {
            if (!string.IsNullOrEmpty(classType) && TryParseClass(classType, out PlayerClass requestedClass))
            {
                playerClass = requestedClass;
            }

            ReplyClassSpells(ctx, playerClass.Value);
        }
        else
        {
            if (!string.IsNullOrEmpty(classType) && TryParseClass(classType, out PlayerClass requestedClass))
            {
                ReplyClassSpells(ctx, requestedClass);
            }
            else
            {
                LocalizationService.HandleReply(ctx, "Invalid class, use <color=white>'.class l'</color> to see options.");
            }
        }
        */
    }

    [Command(name: "liststats", shortHand: "lst", adminOnly: false, usage: ".class lst [Class]", description: "顯示職業的武器與血液加成屬性.")]
    public static void ListClassStatsCommand(ChatCommandContext ctx, string classType = "")
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用。");
            return;
        }

        ulong steamId = ctx.Event.User.PlatformId;
        PlayerClass? nullablePlayerClass = ParseClassFromInput(ctx, classType);

        if (nullablePlayerClass.HasValue)
        {
            ReplyClassSynergies(ctx, nullablePlayerClass.Value);
        }
        else if (string.IsNullOrEmpty(classType) && steamId.HasClass(out PlayerClass? currentClass) && currentClass.HasValue)
        {
            ReplyClassSynergies(ctx, currentClass.Value);
        }
        else
        {
            LocalizationService.HandleReply(ctx, "無效的職業，用 '<color=white>.class l</color>' 查看選項。");
        }

        /*
        ulong steamId = ctx.Event.User.PlatformId;

        if (steamId.HasClass(out PlayerClass? playerClass)
            && playerClass.HasValue)
        {
            if (!string.IsNullOrEmpty(classType) && TryParseClass(classType, out PlayerClass requestedClass))
            {
                playerClass = requestedClass;
            }

            ReplyClassSynergies(ctx, playerClass.Value);
        }
        else
        {
            if (!string.IsNullOrEmpty(classType) && TryParseClass(classType, out PlayerClass requestedClass))
            {
                ReplyClassSynergies(ctx, requestedClass);
            }
            else
            {
                LocalizationService.HandleReply(ctx, "Invalid class, use <color=white>'.class l'</color> to see options.");
            }
        }
        */
    }

    [Command(name: "lockshift", shortHand: "shift", adminOnly: false, usage: ".class shift", description: "切換 Shift 技能啟用狀態.")]
    public static void ShiftSlotToggleCommand(ChatCommandContext ctx)
    {
        if (!_classes)
        {
            LocalizationService.HandleReply(ctx, "職業系統未啟用，無法使用 Shift 技能。");
            return;
        }

        if (!ConfigService.ShiftSlot)
        {
            LocalizationService.HandleReply(ctx, "Shift 技能未啟用。");
            return;
        }

        Entity character = ctx.Event.SenderCharacterEntity;
        User user = ctx.Event.User;

        ulong steamId = user.PlatformId;

        if (!InventoryUtilities.TryGetInventoryEntity(EntityManager, character, out Entity inventoryEntity) || InventoryUtilities.IsInventoryFull(EntityManager, inventoryEntity))
        {
            LocalizationService.HandleReply(ctx, "無法更換或啟用職業技能，背包至少需要 1 格空間。");
            return;
        }

        TogglePlayerBool(steamId, SHIFT_LOCK_KEY);
        if (GetPlayerBool(steamId, SHIFT_LOCK_KEY))
        {
            if (steamId.TryGetPlayerSpells(out var spellsData))
            {
                PrefabGUID spellPrefabGUID = new(spellsData.ClassSpell);

                if (spellPrefabGUID.HasValue())
                {
                    UpdateShift(ctx, ctx.Event.SenderCharacterEntity, spellPrefabGUID);
                }
            }

            LocalizationService.HandleReply(ctx, "Shift 技能 <color=green>已啟用</color>！");
        }
        else
        {
            RemoveShift(ctx.Event.SenderCharacterEntity);

            LocalizationService.HandleReply(ctx, "Shift 技能 <color=red>已關閉</color>！");
        }
    }
}
