using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Godot;

namespace RandomStart;

internal static class ModConfigBridge
{
    private static bool _available;
    private static bool _registered;
    private static Type? _apiType;
    private static Type? _entryType;
    private static Type? _configTypeEnum;

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
        if (_available) Register();
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

            var registerMethod = _apiType!.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .First(m => m.Name == "Register");

            registerMethod.Invoke(null, new object[]
            {
                "RandomStart",
                "Random Start",
                entries
            });
        }
        catch (Exception e)
        {
            GD.PrintErr($"[RandomStart] ModConfig failed: {e}");
        }
    }

    internal static T GetValue<T>(string key, T fallback)
    {
        if (!_available) return fallback;

        try
        {
            var result = _apiType!.GetMethod("GetValue")
                ?.MakeGenericMethod(typeof(T))
                ?.Invoke(null, new object[] { "RandomStart", key });

            return result != null ? (T)result : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    // ─────────────────────────────────────────────

    private static Array BuildEntries()
{
    var list = new List<object>();

    
    list.Add(Entry(cfg => { Set(cfg, "Key", "useColorless");   Set(cfg, "Label", "Include Colorless");   Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)false); }));
    list.Add(Entry(cfg => { Set(cfg, "Key", "useCommon");   Set(cfg, "Label", "Include Common (and Basic)"); Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)true); }));
    list.Add(Entry(cfg => { Set(cfg, "Key", "useUncommon"); Set(cfg, "Label", "Include Uncommon");          Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)true); }));
    list.Add(Entry(cfg => { Set(cfg, "Key", "useRare");     Set(cfg, "Label", "Include Rare");              Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)true); }));
    list.Add(Entry(cfg => { Set(cfg, "Key", "useAncient");  Set(cfg, "Label", "Include Ancient");           Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)false); }));
    list.Add(Entry(cfg => { Set(cfg, "Key", "allowUnsafe"); Set(cfg, "Label", "Allow Unsafe Decks {Experimental}"); Set(cfg, "Type", EnumVal("Toggle")); Set(cfg, "DefaultValue", (object)false); }));
    
    // Debug logging
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