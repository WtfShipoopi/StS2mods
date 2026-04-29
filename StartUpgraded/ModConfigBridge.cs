using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace StartUpgraded;

internal static class ModConfigBridge
{
    private static bool _available;
    private static bool _registered;
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configTypeEnum;

    internal static bool IsAvailable => _available;


    internal static void DeferredRegister()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame += OnNextFrame;
    }

    private static void OnNextFrame()
    {
        var tree = (SceneTree)Engine.GetMainLoop();
        tree.ProcessFrame -= OnNextFrame;

        Detect();

        if (_available)
            Register();
    }


    private static void Detect()
    {
        try
        {
            var allTypes = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a =>
                {
                    try { return a.GetTypes(); }
                    catch { return Type.EmptyTypes; }
                })
                .ToArray();

            _apiType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ModConfigApi");
            _entryType = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigEntry");
            _configTypeEnum = allTypes.FirstOrDefault(t => t.FullName == "ModConfig.ConfigType");

            _available = _apiType != null && _entryType != null && _configTypeEnum != null;
        }
        catch
        {
            _available = false;
        }
    }


    private static void Register()
    {
        if (_registered) return;
        _registered = true;

        try
        {
            var entries = BuildEntries();

            var displayNames = new Dictionary<string, string>
            {
                ["en"] = "Start Upgraded"
            };

            var registerMethod = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.Name == "Register")
                .OrderByDescending(m => m.GetParameters().Length)
                .First();

            if (registerMethod.GetParameters().Length == 4)
            {
                registerMethod.Invoke(null, new object[]
                {
                    "StartUpgraded",
                    displayNames["en"],
                    displayNames,
                    entries
                });
            }
            else
            {
                registerMethod.Invoke(null, new object[]
                {
                    "StartUpgraded",
                    displayNames["en"],
                    entries
                });
            }
        }
        catch (Exception e)
        {
            GD.PrintErr($"[StartUpgraded] ModConfig registration failed: {e}");
        }
    }


    internal static T GetValue<T>(string key, T fallback)
    {
        if (!_available) return fallback;

        try
        {
            var result = _apiType!.GetMethod("GetValue", BindingFlags.Public | BindingFlags.Static)
                ?.MakeGenericMethod(typeof(T))
                ?.Invoke(null, new object[] { "StartUpgraded", key });

            return result != null ? (T)result : fallback;
        }
        catch
        {
            return fallback;
        }
    }


    private static Array BuildEntries()
    {
        var list = new List<object>();


        list.Add(Entry(cfg =>
        {
            Set(cfg, "Label", "Start Upgraded");
            Set(cfg, "Type", EnumVal("Header"));
        }));

        // Upgrade Mode
        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "upgradeMode");
            Set(cfg, "Label", "Upgrade Mode");
            Set(cfg, "Type", EnumVal("Dropdown"));

            Set(cfg, "DefaultValue", (object)"Upgrade All Cards");

            Set(cfg, "Options", new string[]
            {
                "Upgrade All Cards",
                "Only Strike/Defend",
                "Exclude Strike/Defend",
                "Upgrade Random Cards",
                "Upgrade No Cards"
            });

            Set(cfg, "Description", "Choose how your starting cards are upgraded");
        }));
        
        // Random upgrade Count
        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "randomCount");
            Set(cfg, "Label", "Random Card Count");
            Set(cfg, "Type", EnumVal("Slider"));

            Set(cfg, "DefaultValue", (object)3);
            Set(cfg, "Min", 1);
            Set(cfg, "Max", 10);
            Set(cfg, "Step", 1);
            Set(cfg, "Format", "F0");

            Set(cfg, "Description", "Number of cards to upgrade in Random mode");
        }));

        // Debug 
        list.Add(Entry(cfg =>
        {
            Set(cfg, "Key", "debug");
            Set(cfg, "Label", "Enable Debug Logging");
            Set(cfg, "Type", EnumVal("Toggle"));
            Set(cfg, "DefaultValue", (object)false);
            Set(cfg, "Description", "Enable Logging for mod support.");
        }));

        var result = Array.CreateInstance(_entryType!, list.Count);
        for (int i = 0; i < list.Count; i++)
            result.SetValue(list[i], i);

        return result;
    }


    private static object Entry(Action<object> configure)
    {
        var inst = Activator.CreateInstance(_entryType!)!;
        configure(inst);
        return inst;
    }

    private static void Set(object obj, string name, object value)
        => obj.GetType().GetProperty(name)?.SetValue(obj, value);

    private static object EnumVal(string name)
        => Enum.Parse(_configTypeEnum!, name);
}