using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Modding;
using Godot;

namespace StartUpgraded;

[ModInitializer("Initialize")]
public static class StartUpgradedMod
{
    public static void Initialize()
    {
        var harmony = new Harmony("StartUpgraded");
        harmony.PatchAll(typeof(StartUpgradedMod).Assembly);

        ModConfigBridge.DeferredRegister();
    }
}

public enum UpgradeMode
{
    All,
    OnlyStrikeDefend,
    ExcludeStrikeDefend,
    RandomX,
    None
}

[HarmonyPatch(typeof(Player), "PopulateStartingDeck")]
public static class PatchUpgradeStartingDeck
{
    private static readonly Dictionary<Type, CardReflectionMeta> _cache = new();

    private class CardReflectionMeta
    {
        public PropertyInfo? IsUpgradable;
        public PropertyInfo? UpgradeLevel;
        public PropertyInfo? Id;
        public PropertyInfo? IsBasicSD;
        public MethodInfo? Upgrade;
        public MethodInfo? Finalize;
    }

    [HarmonyPostfix]
    public static void Postfix(Player __instance)
    {
        try
        {
            string modeStr = ModConfigBridge.GetValue("upgradeMode", "Upgrade All Cards");
            int randomCount = ModConfigBridge.GetValue("randomCount", 3);
            bool debug = ModConfigBridge.GetValue("debug", false);

            UpgradeMode mode = ParseMode(modeStr);

            if (debug)
            {
                GD.Print("======================================");
                GD.Print($"[StartUpgraded] New Run - Mode: {modeStr}");
                GD.Print("======================================");
            }

            if (mode == UpgradeMode.None)
                return;

            var validCards = new List<object>();

            foreach (var card in __instance.Deck.Cards)
            {
                try
                {
                    var type = card.GetType();

                    if (!_cache.TryGetValue(type, out var meta))
                    {
                        meta = new CardReflectionMeta
                        {
                            IsUpgradable = type.GetProperty("IsUpgradable"),
                            UpgradeLevel = type.GetProperty("UpgradeLevel"),
                            Id = type.GetProperty("Id"),
                            IsBasicSD = type.GetProperty("IsBasicStrikeOrDefend"),
                            Upgrade = type.GetMethod("UpgradeInternal"),
                            Finalize = type.GetMethod("FinalizeUpgradeInternal")
                        };

                        _cache[type] = meta;
                    }

                    // Skip already upgraded
                    if (meta.UpgradeLevel != null)
                    {
                        int level = (int)(meta.UpgradeLevel.GetValue(card) ?? 0);
                        if (level > 0)
                            continue;
                    }

                    // Detect Sword Defend
                    bool isSD = false;

                    if (meta.IsBasicSD != null)
                        isSD = (bool)(meta.IsBasicSD.GetValue(card) ?? false);

                    if (!isSD && meta.Id != null)
                    {
                        var id = meta.Id.GetValue(card)?.ToString();
                        if (id != null && (id.Contains("Strike") || id.Contains("Defend")))
                            isSD = true;
                    }

                    // Mode filtering
                    if (mode == UpgradeMode.OnlyStrikeDefend && !isSD)
                        continue;

                    if (mode == UpgradeMode.ExcludeStrikeDefend && isSD)
                        continue;

                    bool isUpgradable = (bool)(meta.IsUpgradable?.GetValue(card) ?? false);
                    if (!isUpgradable)
                        continue;

                    validCards.Add(card);
                }
                catch (Exception inner)
                {
                    GD.PrintErr($"[StartUpgraded] Card scan error: {inner}");
                }
            }

            if (mode == UpgradeMode.RandomX)
            {
                var rng = new Random();
                validCards = validCards.OrderBy(_ => rng.Next()).Take(randomCount).ToList();
            }

            // Apply upgrades
            foreach (var card in validCards)
            {
                try
                {
                    var type = card.GetType();
                    var meta = _cache[type];

                    meta.Upgrade?.Invoke(card, null);
                    meta.Finalize?.Invoke(card, null);

                    if (debug)
                    {
                        var id = meta.Id?.GetValue(card);
                        GD.Print($"[StartUpgraded] Upgraded: {id}");
                    }
                }
                catch (Exception inner)
                {
                    GD.PrintErr($"[StartUpgraded] Upgrade error: {inner}");
                }
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StartUpgraded] Fatal error: {e}");
        }
    }
    
    private static UpgradeMode ParseMode(string modeStr)
    {
        return modeStr switch
        {
            "Only Strike/Defend" => UpgradeMode.OnlyStrikeDefend,
            "Exclude Strike/Defend" => UpgradeMode.ExcludeStrikeDefend,
            "Upgrade Random Cards" => UpgradeMode.RandomX,
            "Upgrade No Cards" => UpgradeMode.None,
            _ => UpgradeMode.All
        };
    }
}