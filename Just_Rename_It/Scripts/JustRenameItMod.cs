#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using BAModAPI;
using BigAmbitions.Mods;
using UnityEngine;

[assembly: RegisterModClass(typeof(JustRenameIt.JustRenameItMod))]

namespace JustRenameIt
{
    [ModEntryOnInitializationLoad]
    public sealed class JustRenameItMod : IModBigAmbitions
    {
        private static JustRenameItRuntime? runtime;
        private static string? registeredModId;

        public string[] RelativeAssetBundlePaths => Array.Empty<string>();

        public Task OnLoadAsync(ModContext context)
        {
            JustRenameItLog.Initialize(context);
            runtime = JustRenameItRuntime.Initialize(context);
            OptionsService.RemoveModOptions(context.ModId);
            OptionsService.Register(context.ModId, new ModOptions()
                .AddHeader("justrenameit_options_header")
                .AddButton("justrenameit_open_manager", () => runtime?.OpenManager()));
            registeredModId = context.ModId;
            JustRenameItLog.General("Options and runtime initialized.");
            return Task.CompletedTask;
        }

        public Task OnUnloadAsync()
        {
            runtime?.Shutdown();
            runtime = null;
            if (registeredModId != null)
                OptionsService.RemoveModOptions(registeredModId);
            registeredModId = null;
            JustRenameItLog.Shutdown();
            return Task.CompletedTask;
        }
    }

    [Serializable]
    internal sealed class DebugSettings
    {
        public bool enabled = false;
        public bool nameGeneration = false;
        public bool rivalNames = false;
        public bool employeeUi = false;
        public bool renaming = false;
        public bool vehicleNames = false;
        public bool productNames = false;
        public bool businessNames = false;
    }

    internal static class JustRenameItLog
    {
        private static ModContext? context;
        private static DebugSettings settings = new DebugSettings();

        internal static void Initialize(ModContext modContext)
        {
            context = modContext;
            settings = new DebugSettings();
            var path = Path.Combine(modContext.ModRootPath, "Config", "debug.json");
            try
            {
                if (File.Exists(path))
                    settings = JsonUtility.FromJson<DebugSettings>(File.ReadAllText(path)) ?? new DebugSettings();
            }
            catch (Exception exception)
            {
                modContext.Logger.Warn("Just Rename It: could not read debug.json; diagnostics are disabled: " + exception.Message);
                settings = new DebugSettings();
            }
        }

        internal static void General(string message)
        {
            if (settings.enabled) context?.Logger.Info("Just Rename It: " + message);
        }

        internal static void Names(string message)
        {
            if (settings.enabled && settings.nameGeneration) context?.Logger.Info("Just Rename It [names]: " + message);
        }

        internal static void Rivals(string message)
        {
            if (settings.enabled && settings.rivalNames) context?.Logger.Info("Just Rename It [rivals]: " + message);
        }

        internal static void Ui(string message)
        {
            if (settings.enabled && settings.employeeUi) context?.Logger.Info("Just Rename It [employee UI]: " + message);
        }

        internal static void Rename(string message)
        {
            if (settings.enabled && settings.renaming) context?.Logger.Info("Just Rename It [rename]: " + message);
        }

        internal static void Vehicles(string message)
        {
            if (settings.enabled && settings.vehicleNames) context?.Logger.Info("Just Rename It [vehicles]: " + message);
        }

        internal static void Products(string message)
        {
            if (settings.enabled && settings.productNames) context?.Logger.Info("Just Rename It [products]: " + message);
        }

        internal static void Businesses(string message)
        {
            if (settings.enabled && settings.businessNames) context?.Logger.Info("Just Rename It [businesses]: " + message);
        }

        internal static void Warn(string message) => context?.Logger.Warn("Just Rename It: " + message);
        internal static void Error(Exception exception) => context?.Logger.Error(exception);

        internal static void Shutdown()
        {
            context = null;
            settings = new DebugSettings();
        }
    }
}
