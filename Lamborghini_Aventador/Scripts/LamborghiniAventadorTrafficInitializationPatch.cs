#nullable enable
using System;
using System.Linq;
using System.Reflection;
using BAModAPI;
using GleyTrafficSystem;

/// <summary>
/// Adds the Aventador to the exact VehiclePool passed into Gley's native
/// initializer, before TrafficVehicles snapshots and allocates that pool.
/// </summary>
internal sealed class LamborghiniAventadorTrafficInitializationPatch
{
    private const string HarmonyId = "Dudeldups.Lamborghini_Aventador.TrafficInitialization";
    private const BindingFlags InstanceMethods = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private static readonly MethodInfo? InitializeMethod = FindInitializeMethod();
    private static readonly MethodInfo? InitializePrefix = typeof(LamborghiniAventadorTrafficInitializationPatch)
        .GetMethod(nameof(BeforeTrafficManagerInitialize), BindingFlags.Static | BindingFlags.NonPublic);

    private readonly object harmony;
    private readonly Type harmonyType;
    private readonly ModContext context;

    private LamborghiniAventadorTrafficInitializationPatch(object harmony, Type harmonyType, ModContext context)
    {
        this.harmony = harmony;
        this.harmonyType = harmonyType;
        this.context = context;
    }

    internal static LamborghiniAventadorTrafficInitializationPatch? Install(ModContext context)
    {
        if (InitializeMethod == null || InitializePrefix == null)
        {
            context.Logger.Warn(
                "LamborghiniAventador traffic: native TrafficManager.Initialize(VehiclePool) was not found; " +
                "NPC traffic registration cannot be guaranteed before Gley allocates its vehicle list.");
            return null;
        }

        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(candidate =>
                               string.Equals(candidate.GetName().Name, "0Harmony", StringComparison.OrdinalIgnoreCase))
                           ?? Assembly.Load(new AssemblyName("0Harmony"));
            var harmonyType = assembly.GetType("HarmonyLib.Harmony", throwOnError: true)!;
            var harmonyMethodType = assembly.GetType("HarmonyLib.HarmonyMethod", throwOnError: true)!;
            var harmonyConstructor = harmonyType.GetConstructor(new[] { typeof(string) });
            var harmonyMethodConstructor = harmonyMethodType.GetConstructor(new[] { typeof(MethodInfo) });
            var patchMethod = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .FirstOrDefault(candidate =>
                {
                    if (!string.Equals(candidate.Name, "Patch", StringComparison.Ordinal)) return false;
                    var parameters = candidate.GetParameters();
                    return parameters.Length == 5 && parameters[0].ParameterType == typeof(MethodBase);
                });
            if (harmonyConstructor == null || harmonyMethodConstructor == null || patchMethod == null)
                throw new MissingMethodException("Harmony 2.x Harmony/Patch API signatures were not found.");

            var harmony = harmonyConstructor.Invoke(new object[] { HarmonyId });
            var prefix = harmonyMethodConstructor.Invoke(new object[] { InitializePrefix });
            patchMethod.Invoke(harmony, new object?[] { InitializeMethod, prefix, null, null, null });
            LamborghiniAventadorDiagnostics.Info(context,
                "LamborghiniAventador traffic: installed pre-allocation hook " +
                $"method={InitializeMethod.DeclaringType?.FullName}.{InitializeMethod.Name}.");
            return new LamborghiniAventadorTrafficInitializationPatch(harmony, harmonyType, context);
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                "LamborghiniAventador traffic: failed to install native pool initialization hook: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
            return null;
        }
    }

    internal void Shutdown()
    {
        try
        {
            var unpatchAll = harmonyType.GetMethod("UnpatchAll",
                BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public,
                binder: null, types: new[] { typeof(string) }, modifiers: null);
            if (unpatchAll == null)
                throw new MissingMethodException("Harmony.UnpatchAll(string) is unavailable.");
            unpatchAll.Invoke(unpatchAll.IsStatic ? null : harmony, new object[] { HarmonyId });
        }
        catch (Exception exception)
        {
            context.Logger.Warn(
                "LamborghiniAventador traffic hook cleanup failed: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
        }
    }

    private static MethodInfo? FindInitializeMethod()
    {
        return typeof(TrafficManager).GetMethods(InstanceMethods).FirstOrDefault(method =>
        {
            if (!string.Equals(method.Name, "Initialize", StringComparison.Ordinal) ||
                method.ReturnType != typeof(void))
                return false;

            var parameters = method.GetParameters();
            return parameters.Length == 11 &&
                   parameters[0].ParameterType == typeof(UnityEngine.Transform[]) &&
                   parameters[2].ParameterType == typeof(VehiclePool);
        });
    }

    private static void BeforeTrafficManagerInitialize(object[] __args)
    {
        var context = LamborghiniAventadorTrafficSupport.CurrentContext;
        var playerPrefab = LamborghiniAventadorTrafficSupport.PlayerPrefab;
        if (playerPrefab == null || __args == null)
            return;

        VehiclePool? pool = null;
        foreach (var argument in __args)
        {
            if (argument is VehiclePool candidate)
            {
                pool = candidate;
                break;
            }
        }

        if (pool == null)
        {
            context?.Logger.Warn(
                "LamborghiniAventador traffic: Gley initialization hook did not receive its VehiclePool; " +
                "NPC traffic registration was skipped.");
            return;
        }

        try
        {
            LamborghiniAventadorTrafficSupport.PrepareTrafficPool(
                playerPrefab, pool, "traffic-manager-initialize");
        }
        catch (Exception exception)
        {
            context?.Logger.Warn(
                "LamborghiniAventador traffic: pre-allocation pool preparation failed: " +
                $"{exception.GetBaseException().GetType().Name}: {exception.GetBaseException().Message}.");
        }
    }
}
