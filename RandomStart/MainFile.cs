using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models.CardPools;
using Godot;

namespace RandomStart;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    internal const string ModId = "RandomStart";

    public static void Initialize()
    {
        var harmony = new Harmony(ModId);
        harmony.PatchAll(Assembly.GetExecutingAssembly());

        ModConfigBridge.DeferredRegister();

        GD.Print("[RandomStart] Initialized");
    }
}

[HarmonyPatch]
class PatchAllStartingDecks
{
    private static readonly Random _rng = new();


    private static List<CardModel> GetAllCards(object poolModel)
    {
        try
        {
            var method = poolModel.GetType().GetMethod(
                "GenerateAllCards",
                BindingFlags.NonPublic | BindingFlags.Instance
            );

            if (method == null)
                return new List<CardModel>();

            var result = method.Invoke(poolModel, null) as IEnumerable<CardModel>;
            return result?.ToList() ?? new List<CardModel>();
        }
        catch
        {
            return new List<CardModel>();
        }
    }

    static IEnumerable<MethodBase> TargetMethods()
    {
        return AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(a =>
            {
                try { return a.GetTypes(); }
                catch { return Array.Empty<Type>(); }
            })
            .Where(t => typeof(CharacterModel).IsAssignableFrom(t) && !t.IsAbstract)
            .Select(t => AccessTools.PropertyGetter(t, "StartingDeck"))
            .Where(m => m != null);
    }

    static void Postfix(CharacterModel __instance, ref IEnumerable<CardModel> __result)
    {
        try
        {
            var originalDeck = __result?.ToList() ?? new List<CardModel>();

            bool debug = ModConfigBridge.GetValue<bool>("debug", false);

            if (debug)
                GD.Print($"[RandomStart] ===== START ===== ({__instance.GetType().Name})");

            // Split Deck
            var lockedCards = new List<CardModel>();
            var replaceableCards = new List<CardModel>();

            foreach (var card in originalDeck)
            {
                bool isSafe =
                    card.CanBeGeneratedInCombat &&
                    card.CanBeGeneratedByModifiers &&
                    card.IsRemovable;

                if (isSafe)
                    replaceableCards.Add(card);
                else
                    lockedCards.Add(card);
            }

            if (debug)
            {
                GD.Print($"[RandomStart] Original Deck: {originalDeck.Count}");
                GD.Print($"[RandomStart] Replaceable: {replaceableCards.Count}");
                GD.Print($"[RandomStart] Locked: {lockedCards.Count}");
            }

            if (replaceableCards.Count == 0)
            {
                if (debug)
                    GD.Print("[RandomStart] No replaceable cards.");
                return;
            }

            // Build Pool
            var allCards = GetAllCards(__instance.CardPool);

            bool useColorless = ModConfigBridge.GetValue<bool>("useColorless", false);

            List<CardModel> colorlessCards = new();

            if (useColorless)
            {
                try
                {
                    var colorlessPool = ModelDb.CardPool<ColorlessCardPool>();
                    colorlessCards = GetAllCards(colorlessPool);

                    allCards.AddRange(colorlessCards);

                    if (debug)
                    {
                        GD.Print($"[RandomStart] Colorless Pool Size: {colorlessCards.Count}");
                        foreach (var c in colorlessCards.Take(5))
                            GD.Print($"[RandomStart] Colorless Sample: {c.Id}");
                    }
                }
                catch (Exception e)
                {
                    GD.PrintErr($"[RandomStart] Failed to load colorless pool: {e}");
                }
            }

            if (debug)
            {
                GD.Print($"[RandomStart] Total Pool Size: {allCards.Count}");
            }

            // Config
            bool useCommon = ModConfigBridge.GetValue<bool>("useCommon", true);
            bool useUncommon = ModConfigBridge.GetValue<bool>("useUncommon", true);
            bool useRare = ModConfigBridge.GetValue<bool>("useRare", true);
            bool useAncient = ModConfigBridge.GetValue<bool>("useAncient", false);
            bool allowUnsafe = ModConfigBridge.GetValue<bool>("allowUnsafe", false);

            if (debug)
            {
                GD.Print("[RandomStart] ===== CONFIG =====");
                GD.Print($"useColorless: {useColorless}");
                GD.Print($"useCommon: {useCommon}");
                GD.Print($"useUncommon: {useUncommon}");
                GD.Print($"useRare: {useRare}");
                GD.Print($"useAncient: {useAncient}");
                GD.Print($"allowUnsafe: {allowUnsafe}");
            }

            // Filter out cards
            int failRarity = 0;
            int failSafety = 0;
            int passColorless = 0;
            int passNormal = 0;

            var validCards = allCards.Where(c =>
            {
                string rarity = c.Rarity.ToString();
                bool isColorless = colorlessCards.Contains(c);

                if (useColorless && isColorless)
                {
                    if (allowUnsafe || c.IsRemovable)
                    {
                        passColorless++;
                        return true;
                    }

                    failSafety++;
                    return false;
                }

                bool allowedByRarity =
                    (useCommon && (rarity == "Common" || rarity == "Basic")) ||
                    (useUncommon && rarity == "Uncommon") ||
                    (useRare && rarity == "Rare") ||
                    (useAncient && rarity == "Ancient");

                if (!allowedByRarity)
                {
                    failRarity++;
                    return false;
                }

                if (allowUnsafe)
                {
                    passNormal++;
                    return true;
                }

                bool safe =
                    c.CanBeGeneratedInCombat &&
                    c.CanBeGeneratedByModifiers &&
                    c.IsRemovable;

                if (safe)
                {
                    passNormal++;
                    return true;
                }

                failSafety++;
                return false;

            }).ToList();

            if (debug)
            {
                GD.Print("[RandomStart] ===== FILTER RESULTS =====");
                GD.Print($"Valid: {validCards.Count}");
                GD.Print($"Pass Normal: {passNormal}");
                GD.Print($"Pass Colorless: {passColorless}");
                GD.Print($"Fail Rarity: {failRarity}");
                GD.Print($"Fail Safety: {failSafety}");
            }

            if (validCards.Count == 0)
            {
                if (debug)
                    GD.Print("[RandomStart] No valid cards after filtering.");
                return;
            }

            // Randomizing
            int targetCount = replaceableCards.Count;

            var newCards = new List<CardModel>();
            var counts = new Dictionary<ModelId, int>();

            int safety = 0;

            while (newCards.Count < targetCount && safety++ < 5000)
            {
                var card = validCards[_rng.Next(validCards.Count)];

                if (!counts.ContainsKey(card.Id))
                    counts[card.Id] = 0;

                if (counts[card.Id] >= 2)
                    continue;

                counts[card.Id]++;
                newCards.Add(card);

                if (debug && newCards.Count <= 10)
                    GD.Print($"[RandomStart] PICKED: {card.Id} ({card.Rarity})");
            }

            // Finalize Deck
            var finalDeck = new List<CardModel>();
            finalDeck.AddRange(lockedCards);
            finalDeck.AddRange(newCards);

            if (debug)
            {
                GD.Print($"[RandomStart] Final Deck Size: {finalDeck.Count}");
                GD.Print("[RandomStart] ===== END =====");
            }

            __result = finalDeck;
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RandomStart] Error: {e}");
        }
    }
}    