[CmdletBinding()]
param([string] $GameDllPath = 'D:\SteamLibrary\steamapps\common\Big Ambitions\Big Ambitions_Data\Managed\BigAmbitions.dll')
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'
$testRoot = Join-Path ([IO.Path]::GetTempPath()) ('BigHaxHappinessTests-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot
Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Scripts/Logic/BigHaxPlayerHaxService.cs') -Destination (Join-Path $testRoot 'BigHaxPlayerHaxService.cs')
@'
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><TargetFramework>net10.0</TargetFramework><OutputType>Exe</OutputType><Nullable>enable</Nullable></PropertyGroup>
</Project>
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Tests.csproj')
@'
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using BigHax;
using Helpers;
static class Program
{
    const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;
    static void Check(bool pass, string message) { if (!pass) throw new Exception(message); }
    static void Protect() => typeof(BigHaxPlayerHaxService).GetMethod("ProtectHappinessUpdate", BindingFlags.NonPublic | BindingFlags.Static)!.Invoke(null, null);
    static void InstallFixtureHook(BigHaxPlayerHaxService service)
    {
        var detour = new BigHaxMethodDetour(typeof(HappinessHelper).GetMethod("UpdateHappiness")!, typeof(Program).GetMethod("Protect", BindingFlags.NonPublic | BindingFlags.Static)!);
        detour.Apply(out _);
        typeof(BigHaxPlayerHaxService).GetField("happinessUpdateDetour", Private)!.SetValue(service, detour);
    }
    static void Main(string[] args)
    {
        // Check the production fingerprint against real installed IL without loading Unity.
        using var file = File.OpenRead(args[0]);
        using var pe = new PEReader(file);
        var metadata = pe.GetMetadataReader();
        var helper = metadata.TypeDefinitions.Select(metadata.GetTypeDefinition).Single(t => metadata.GetString(t.Name) == "HappinessHelper" && metadata.GetString(t.Namespace) == "Helpers");
        var method = helper.GetMethods().Select(metadata.GetMethodDefinition).Single(m => metadata.GetString(m.Name) == "UpdateHappiness");
        var hash = Convert.ToHexString(SHA256.HashData(pe.GetMethodBody(method.RelativeVirtualAddress).GetILBytes()!));
        var expected = (string)typeof(BigHaxPlayerHaxService).GetField("SupportedHappinessUpdateHash", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        Check(hash == expected, "installed game recalculation must match supported IL");
        var supported = typeof(BigHaxPlayerHaxService).GetMethod("IsSupportedHappinessUpdate", BindingFlags.NonPublic | BindingFlags.Static)!;
        Check(!(bool)supported.Invoke(null, new object[] { typeof(HappinessHelper).GetMethod("UpdateHappiness")! })!, "unknown method body must be rejected");
        var service = new BigHaxPlayerHaxService();
        var context = new BAModAPI.ModContext();
        var settings = new BigHaxSettings { DisablePlayerHappinessDecay = true };
        var save = new GameInstance { Happiness = 40 };
        save.happinessModifiers.Add(new HappinessModifierData { type = "boost", hoursLeft = 3, amount = 40 });
        SaveGameManager.Current = save;
        // Substitute only native patch transport; exercise the actual service.
        InstallFixtureHook(service);
        service.ApplyConfiguredBehavior(context, settings);
        Check(save.Happiness == 100, "enabling fills happiness");
        Check(!BigHaxLogger.DebugEnabled && !BigHaxLogger.HappinessDebugEnabled, "diagnostics remain off");
        HappinessHelper.RunHourly();
        Check(save.Happiness == 100 && save.happinessModifiers[0].hoursLeft == 2, "hourly expiry proceeds while protected");
        save.happinessModifiers.Add(new HappinessModifierData { type = "penalty", hoursLeft = -1, amount = -30 });
        HappinessHelper.UpdateHappiness();
        Check(save.Happiness == 100 && save.happinessModifiers.Count == 2, "modifier addition preserves protection and data");
        save.happinessModifiers.RemoveAt(0);
        HappinessHelper.UpdateHappiness();
        Check(save.Happiness == 100, "modifier removal stays protected");
        save.Happiness = 40;
        HappinessHelper.UpdateHappiness();
        Check(save.Happiness == 100, "recalculation corrects changed value without opening menu");
        int marks = SaveGameManager.Changes;
        HappinessHelper.UpdateHappiness();
        Check(SaveGameManager.Changes == marks, "unchanged value must not mark save again");
        settings.DisablePlayerHappinessDecay = false;
        service.ApplyConfiguredBehavior(context, settings);
        Check(save.Happiness == 0 && BigHaxMethodDetour.Active == null, "disable restores vanilla and recalculates immediately");
        save.happinessModifiers[0].amount = 60;
        HappinessHelper.UpdateHappiness();
        Check(save.Happiness == 60, "vanilla continues after disabling");
        settings.DisablePlayerHappinessDecay = true;
        InstallFixtureHook(service);
        service.ApplyConfiguredBehavior(context, settings);
        GameEvent.onGameEventTriggered = null;
        SaveGameManager.Current = new GameInstance { Happiness = 20 };
        service.ApplyConfiguredBehavior(context, settings);
        Check(SaveGameManager.Current.Happiness == 100 && GameEvent.onGameEventTriggered != null, "save reload fills and rebinds");
        HappinessHelper.UpdateHappiness();
        Check(SaveGameManager.Current.Happiness == 100, "new save stays protected");
        service.Shutdown();
        Check(BigHaxMethodDetour.Active == null && GameEvent.onGameEventTriggered == null && GlobalEvents.onFullMenuToggle == null, "unload releases hooks");
        HappinessHelper.UpdateHappiness();
        Check(SaveGameManager.Current.Happiness == 0, "unload restores vanilla");
        service.ApplyConfiguredBehavior(context, settings);
        Check(BigHaxLogger.Warnings.Count == 1 && BigHaxMethodDetour.Active == null, "unknown implementation warns and remains untouched");
        service.Shutdown();
        Console.WriteLine("PASS: actual Build 3682 IL, hourly/modifier recalculations, diagnostics off, enable/disable, save rebind, unload and unsupported-body fallback");
    }
}
namespace BAModAPI { class ModContext { public Logger Logger = new(); } class Logger { public void Warn(string text) { } } }
class GameInstance { public float Happiness, Energy = 0, Hunger = 0; public int Day = 1, Hour = 8; public List<HappinessModifierData> happinessModifiers = new(); }
static class SaveGameManager { public static GameInstance? Current; public static int Changes; public static void MarkChange() => Changes++; }
static class GameEvent { public static Action<string>? onGameEventTriggered; }
static class GlobalEvents { public static Action<bool>? onFullMenuToggle; }
class InstanceBehavior<T> { public static T? Instance => default; }
namespace UI.Smartphone { class FullMenu { } }
namespace UI { class UIs { public Smartphone.FullMenu? fullMenu => null; } }
namespace Helpers
{
    static class EnergyHelper { }
    class HappinessModifierData { public string type = ""; public int hoursLeft, amount; }
    static class HappinessHelper
    {
        public static void UpdateHappiness()
        {
            if (BigHaxMethodDetour.Active != null) { BigHaxMethodDetour.Active(); return; }
            SaveGameManager.Current!.Happiness = Math.Clamp(SaveGameManager.Current.happinessModifiers.Sum(m => m.amount), 0, 100);
        }
        public static void RunHourly()
        {
            foreach (var m in SaveGameManager.Current!.happinessModifiers) if (m.hoursLeft > 0) m.hoursLeft--;
            SaveGameManager.Current.happinessModifiers.RemoveAll(m => m.hoursLeft == 0);
            UpdateHappiness();
        }
    }
}
namespace BigHax
{
    class BigHaxSettings { public bool DisablePlayerHungerAndEnergyDecay { get; set; } public bool DisablePlayerHappinessDecay { get; set; } }
    class BigHaxMethodDetour
    {
        public static Action? Active;
        readonly MethodInfo replacement;
        public bool IsApplied { get; private set; }
        public BigHaxMethodDetour(MethodInfo target, MethodInfo replacement) { this.replacement = replacement; }
        public bool Apply(out string error) { error = ""; IsApplied = true; Active = () => replacement.Invoke(null, null); return true; }
        public bool Restore(out string error) { error = ""; IsApplied = false; Active = null; return true; }
    }
    static class BigHaxLogger
    {
        public static bool DebugEnabled => false;
        public static bool HappinessDebugEnabled => false;
        public static List<string> Warnings = new();
        public static void WarnOnce(BAModAPI.ModContext? c, string key, string text) { if (!Warnings.Contains(key)) Warnings.Add(key); }
        public static void Diagnostic(string text) { }
        public static void DiagnosticException(string text, Exception exception) { }
        public static void HappinessDiagnostic(BAModAPI.ModContext? c, string text) { }
    }
}
namespace UnityEngine
{
    class Object { public static void Destroy(Object o) { } }
    class GameObject { public string name = ""; public T AddComponent<T>() where T : new() => new(); }
    class MonoBehaviour : Object { public GameObject gameObject = new(); }
    class Transform : IEnumerable { public GameObject gameObject = new(); public T? GetComponent<T>() => default; public IEnumerator GetEnumerator() => Array.Empty<Transform>().GetEnumerator(); }
    static class Time { public static float unscaledTime => 0; }
    static class Mathf { public static bool Approximately(float a, float b) => Math.Abs(a - b) < 0.0001f; }
}
'@ | Set-Content -LiteralPath (Join-Path $testRoot 'Program.cs')
dotnet run --project (Join-Path $testRoot 'Tests.csproj') -- $GameDllPath
if ($LASTEXITCODE -ne 0) { throw 'Happiness regression tests failed.' }
