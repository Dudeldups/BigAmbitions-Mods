#nullable enable
using System;
using BAModAPI;
using BigAmbitions.Mods;

internal static class AudiRS6ROptions
{
    private const string PopsKey = "audirs6r_exhaust_pops";
    private static ModContext? context;
    private static bool registered;
    internal static bool ExhaustPopsEnabled { get; private set; } = true;
    internal static event Action? Changed;

    // Same key used by the game's ModOptionPrefs. Read at mod load so a saved
    // preference applies even when the player never opens the options screen.
    private static string SavedKey => "m:" + context!.ModId + ":" + PopsKey;

    internal static void Initialize(ModContext modContext)
    {
        Shutdown();
        context = modContext;
        ExhaustPopsEnabled = UnityEngine.PlayerPrefs.GetInt(SavedKey, 1) != 0;
        try
        {
            OptionsService.Register(context.ModId, new ModOptions()
                .AddHeader("audirs6r_options_header")
                // Keep the actual default true so native Reset to Defaults works.
                .AddToggle(PopsKey, "audirs6r_exhaust_pops_label", true, SetExhaustPops));
            registered = true;
        }
        catch (Exception ex)
        {
            context.Logger.Warn($"AudiRS6R: could not register exhaust pop option: {ex.Message}");
        }
    }

    private static void SetExhaustPops(bool enabled)
    {
        if (context == null || ExhaustPopsEnabled == enabled) return;
        ExhaustPopsEnabled = enabled;
        // Notify immediately, including in the paused options menu. Controllers
        // discard queued bursts and stop one-shot tails before the next frame.
        Changed?.Invoke();
        UnityEngine.PlayerPrefs.SetInt(SavedKey, enabled ? 1 : 0);
        UnityEngine.PlayerPrefs.Save();
    }

    internal static void Shutdown()
    {
        if (registered && context != null) OptionsService.RemoveModOptions(context.ModId);
        registered = false;
        context = null;
    }
}
