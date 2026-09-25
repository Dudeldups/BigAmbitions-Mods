using System;
using System.IO;
using System.Reflection;

namespace MCG_Doom.Core
{
    internal static class DoomPaths
    {
        private static readonly string[] WadCandidates =
        {
            Path.Combine("Config", "Doom", "doom1.wad"),
            Path.Combine("Config", "Doom", "DOOM1.WAD")
        };

        private static readonly string[] SoundFontCandidates =
        {
            Path.Combine("Config", "Doom", "Audio", "TimGM6mb.sf2"),
            Path.Combine("Config", "Doom", "Audio", "timgm6mb.sf2")
        };

        public static string FindBundledSharewareWad()
        {
            return FindRequiredFile(WadCandidates,
                "Bundled doom1.wad was not found. Run tools/PrepareThirdParty.ps1 before building MCG_Doom.");
        }

        public static string FindBundledSoundFont()
        {
            return FindRequiredFile(SoundFontCandidates,
                "Bundled TimGM6mb.sf2 was not found. Run tools/PrepareThirdParty.ps1 before building MCG_Doom.");
        }

        private static string FindRequiredFile(string[] candidates, string error)
        {
            var assemblyDirectory = GetAssemblyDirectory();
            foreach (var relativePath in candidates)
            {
                var candidate = Path.Combine(assemblyDirectory, relativePath);
                if (File.Exists(candidate))
                {
                    return candidate;
                }
            }

            throw new FileNotFoundException(error);
        }

        private static string GetAssemblyDirectory()
        {
            var location = Assembly.GetExecutingAssembly().Location;
            if (string.IsNullOrEmpty(location))
            {
                throw new InvalidOperationException("Could not determine the MCG_Doom assembly directory.");
            }

            return Path.GetDirectoryName(location);
        }
    }
}
