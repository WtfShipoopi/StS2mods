using System;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using Godot;


namespace StartWithOrobas;

[ModInitializer("Initialize")]
public static class StartWithOrobasMod
{
    public static void Initialize()
    {
        var harmony = new Harmony("StartWithOrobas");
        harmony.PatchAll();
        
        ModConfigBridge.DeferredRegister();

        GD.Print("[StartWithOrobas] Initialized");
    }
}

[HarmonyPatch(typeof(Neow), "GenerateInitialOptions")]
public static class PatchNeowStart
{

    [HarmonyPostfix]
    public static async void Postfix(Neow __instance)
    {
        try
        {
            if (!ModConfigBridge.GetValue("give_orobas", true))
                return;

            var player = __instance.Owner;
            if (player == null)
                return;

            if (player.GetRelic<TouchOfOrobas>() != null)
                return;

            var relic = ModelDb.Relic<TouchOfOrobas>().ToMutable();

            if (relic is not TouchOfOrobas oro)
                return;

            if (!oro.SetupForPlayer(player))
                return;

            await RelicCmd.Obtain(relic, player);
            
            GD.Print("[StartWithOrobas] Orobas applied (config enabled)");
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StartWithOrobas] Failed to give Orobas: {e}");
        }
    }
}

[HarmonyPatch(typeof(Orobas), "GenerateInitialOptions")]
public static class Orobas_ReplaceTouchOfOrobas
{
    public static void Postfix(Orobas __instance, ref IReadOnlyList<EventOption> __result)
    {
        var rng = __instance.Rng;

        var filtered = new List<EventOption>();
        bool removed = false;

        foreach (var opt in __result)
        {
            var relic = opt?.Relic;

            if (relic != null && relic.Id == ModelDb.Relic<TouchOfOrobas>().Id)
            {
                removed = true;
                continue;
            }

            filtered.Add(opt);
        }

        // Try to replace event option
        if (removed)
        {
            var replacementPool = __instance.AllPossibleOptions
                .Where(opt =>
                {
                    var relic = opt?.Relic;
                    return relic == null || relic.Id != ModelDb.Relic<TouchOfOrobas>().Id;
                })
                .ToList();

            if (replacementPool.Count > 0)
            {
                var newOption = rng.NextItem(replacementPool);
                filtered.Add(newOption);
            }
        }

        __result = filtered;
    }
}