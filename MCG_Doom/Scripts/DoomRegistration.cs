using System;
using BAModAPI;
using Capisoft.Lib.BaComputerGames;

namespace MCG_Doom
{
    internal static class DoomRegistration
    {
        internal static IDisposable Register(ModContext context)
        {
            var definition = ComputerGameDefinition
                .Create<DoomGame>(
                    "dudeldups:doom",
                    "DOOM",
                    "The classic DOOM shareware episode, running on your Big Ambitions computer.",
                    version: "1.0.0",
                    descriptionKey: "mcg_doom_description",
                    ruleset: "doom-shareware-v1")
                .WithNativeRetroEffects(false);

            return ComputerGames.Register(
                context.ModId,
                context.ModRootPath,
                definition);
        }
    }
}
