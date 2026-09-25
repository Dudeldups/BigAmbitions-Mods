#nullable enable
using System;
using UnityEngine;

namespace BigHax
{
    internal static class BigHaxFeedbackLinks
    {
        public const string SteamWorkshopUrl = "https://steamcommunity.com/sharedfiles/filedetails/?id=3744259108";

        private const string DiscordUserId = "576848055882612754";

        private const string SteamIconPng =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAABmJLR0QA/wD/AP+gvaeTAAAE10lEQVR4nO2abWiWVRjH/9d0reVyZYFo5htLCymlXBZBWRB9qUyMWiiUFVghEX4yqCiMIsKiMuyN6AWiISSR2JfIdEojwSx61TJti1azpjNnW9t+fThn7Onsfl7u52X3pPsH4zw7b/f1P+c5L9d1P1JKSkpKSkpKSkrK/xJL2oBsACZpjv+rl9Qn6XdJe83sSJK2VRRgDvAC0EE0A8CnwCrg1KTtLRtAHbAB6M8iPIqfgRuTtr1kgFnANzGEhzyNWzKxSXwPAKZL2inp3IjiY5J2S+qUNF5Sg6QLJVVF1N1oZvdVys6KANQAeyJm9AdgOVAT0Waan/HeiHYn3QA8GiGiGTjNl1cDVwF3+QGZm9H2YqA9aHvcf6PGPsBZwF+BgM3AOF9+J/BrxABtB+b5OnOBrqD8tWSVFQiwJjD8N+BMX/ZUhPBMuoHLfd07grITwBnJqisAoCUwfK3PvyGP+CHacEdnFbAvKLstaX05Acb7mcpkpi9rLXAAAFb7NuuC/OcLtSXqOBkNpkjKvMUdNrODuCVwaYx+rvPp7iB/dqEdJDUA4Ro97NPJinc3merTziB/YqEdJDUADwb/1/k0rpPT5dPTg/y/C+1g1AcAuFVSuEmdA0wysw5J+2N0t8OnFwX5h4q1r6IAU4E/smxoK32dewvcAI8BU3ybnUHZqmSVRgAYsDWHoK9wp0MVsCWP+EFgue/3yqBsgLF4GwTuKWBW1/m6NbiYQJRr3AEs9fXqge+D8q1xDVsB1FZAc+YzGhh57c02s2sy2s0CHgBeBNYDTQz7CZMYeZkaABrjGjeIW5frgfPKrF3AOGBXAeIz2Qw0ZOnPgGXAoYh2z8W1zwAy+5f0kaSNkj4ws/7iZP/H4LWSniyi6aCkFrlYQbukGkkXyF1+ZkbUb5F0rZn1xjWwL8sstAGP4HfaYgDmE+23l5ttQH2xRobuZEgfsAm4hhhhJ9xG9mVFZTvbngCqixLvDT0Q44HfAveTx90Easnv0pZCD/AGcH7Rwj0GtEpaFLNdj6R35OJwe3Dh6RWSmiQ1KsZdvEC2yN3ufpG0V9IOMztelp6Bd0ucjVbgYJAXurrF0oW7OleMKknfldjHIkkz/Of9kq4ws1pJ1ZIukfSM3DcmLi2SFphZc4n25QZYUqbZGmIA+BAX2RmK7zXgrrqF8A/w0FDbigOc7Y2uBAeA6/1zJuOO1lz8CFw2KsKDQdhUoQEAN7g3++cszVHvTaDcm2esQZgPvIRzM8tNJzABd439KSg7wlgKYgITgdXA12UehGW+/9cz8lqAGflsqiQjIkJm1m1mG8xsnqTFkprl3s2XylCgst2nzZIWm1mi0ZucITEz225mTXLH3MOS2kp41p8+neDTz81soIT+Rh+ca7skYh3nox8fpQE+9nk3Ja2naBh+qXmU7DG+TJ717abjzvle/GuwkxIvpNfP7NXASuCzLOJfwXtrDB+3byWtoWQY9vba8ZEkYCHwOPCyTxdm1H/M1+9mLAYt4wKcAnziRXUBdxPhlwOzgfcy9oIxtfZL+okMUCfpbUlDojolbZNzXeskLZBzlqokHZV0u5m9X8ozxyTALcAXWfaAHuBVYFrSdkZR1h9J4X7C0ij39veEpH2SdpUteJGSkpKSkpKSkpKSUi7+BdT7CZKTUB4kAAAAAElFTkSuQmCC";

        private const string DiscordIconPng =
            "iVBORw0KGgoAAAANSUhEUgAAAEAAAABACAYAAACqaXHeAAAAAXNSR0IArs4c6QAAAARnQU1BAACxjwv8YQUAAAAJcEhZcwAADsMAAA7DAcdvqGQAAAT+SURBVHhe7Zs/aNVXFMfP+ZEhQ4ZALQQqNAWHlDpkUCq0UKEODhkyCHXIEGhBoYulFQot2GJLBweHFhwsaCcHh1JKcbTQQYpCLAoKChEMaGmhQgMKhu+nQ+7v9Zfj+3N/7yW27+X3gZC8c8+573fPPb/759wbs4aGhoaGhobNAJgC5iWdlrQE7I86dQFmJd2QdEbSYUk7o85/BjAJLEo6K+kWAUmXo01dJH3fpt5lSeeARWBHtNlyJO2TdE7S4/hwbeg7CiTtkvQ0VlhF0mNJFyQdjPY5eBR0IvX2gpkdcffdsbwTwHV3/wF4wcwmzGw6/d5hZlNmNmZmK2b2KP2smNmqu/8OvOXu2Q4E7rn7d8C3RVGsxPK+Se91Tm//L5D0VNLR2I529IwAYAK45e7/n8EnA+CRu7/i7o9iWZUiCiLAiWFrvJmZu08CJ6I80jUCJO02syV3H4tlwwCwZmavFkVxN5aV9IqAM8PaeFuPgjEzOx3lVTo6AJh39zejfNhw9zngQJSXdHwFJC25+2yUDyNpKt7r7muxrG0EpN4ficbbehTMAotRbp0iYJR6vwR4mKbFJ1X5MxEgaW7UGm/rUTBlZoej/BkHmNmRKBgVgPejbMMrkLaz94d56usFsLcoimvl5xgBi6Pc+MS71Q8bIkDSsrtPV2WjBrDq7i+Wg2ErAoD9o954Wx8MJ8ystcCrOuDtltaIA7xT/l0dA/rKqESAVeAasGG+HQRgLdXZdWtbg7kNn9LoPxCSrkjaU6lzDNgv6UbUzUXSA0lzwHil3pl2ecK6SNrXcoCkhahQh5SgbDt7ABOSLkabXki6CkzF+kqAj6NNHSQda1Um6euokIukO9UeagcwKelBtO1ESoTOxHoiki5F21wkXaxWdDUq5CJp4/vUAUlHo20nJJ2K9u0AZqJtLpKWy0rG+k14pp5qG/oRYDradyE7EyxpORrXYEcBzLh71xDuwu12e+x2uPs9YDXKO3AzCrpQR3cDwJ7CzLJz/G14GAU9+DMKIsCau/fUq1BHNzJdmNmuKK1BtvOA8ZyVpruP5QyAFeroRl4uzOylKM3F3adyz+aAOs7K0k3jzyAO2FWk46m+AT6Msg7k6lmuLrDo7pNRXoPpgaZA/j2G6tpjkg5Gu170Otqqu7Zoh6TlQacRSEvWTlOXpEOS/o42vUiO/SjWZ+nAZpAldomkB75ZOYB0CnPezH5197vArJm94e6Hom4dgJ/N7Cd3v25mO4HXzWwhbWsHZtMcMKzElNi2o3FAFGw3trUDgCeFmd2LBduIhwNFAPAFcBy4Hcu2GmDFzD4HPkhTcH9IOtpPPkDSfWC+rCfl/y70s+jJJS2OLkk6VOYh+s07plzGZ+XDz0i6EpVySDdCq44YBw5IOtXu8mRd0qXIs6nRrcVPavjlqJ+DpFtlUrR1MpQ8+inwST/HY8Bdd//S3c8H+aSZ7U4rw9cqu7fxykasvCNoaUz6zd1vmtn1mBtIKbiT/Z5gA9+4+/F4TN4irbP7SjZKOhvr22wknYzfm0OK8PwrPymMl2JFnZD0V25uYBCA8TobOEl3JD1zLyCLdLixmOOIXtvXzQSYj98fSQ0/1itln02KiB/jF5HCK+pvNZ1eU0m/JAfVHseySGPEV2kaLKem1pHY8yLdJH+cnuGP9D8Kz/c5UlS8F+XPC0kLKdu0Nb3d0NDQ0NAw2vwDXgp5a0i46gEAAAAASUVORK5CYII=";

        private static Sprite? steamIcon;
        private static Sprite? discordIcon;
        private static bool steamIconInitialized;
        private static bool discordIconInitialized;

        public static bool HasDiscordProfile => DiscordUserId.Length > 0;
        public static Sprite? SteamIcon
        {
            get
            {
                if (!steamIconInitialized)
                {
                    steamIconInitialized = true;
                    steamIcon = CreateIcon("BigHax Steam Icon", SteamIconPng);
                }

                return steamIcon;
            }
        }

        public static Sprite? DiscordIcon
        {
            get
            {
                if (!discordIconInitialized)
                {
                    discordIconInitialized = true;
                    discordIcon = CreateIcon("BigHax Discord Icon", DiscordIconPng);
                }

                return discordIcon;
            }
        }

        public static void OpenSteam() => Application.OpenURL(SteamWorkshopUrl);

        public static void OpenDiscord()
        {
            if (HasDiscordProfile)
                Application.OpenURL("https://discord.com/users/" + DiscordUserId);
        }

        private static Sprite? CreateIcon(string name, string png)
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, mipChain: false)
            {
                name = name + " Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            try
            {
                if (!ImageConversion.LoadImage(texture, Convert.FromBase64String(png), markNonReadable: true))
                {
                    UnityEngine.Object.Destroy(texture);
                    return null;
                }
            }
            catch (Exception)
            {
                UnityEngine.Object.Destroy(texture);
                return null;
            }

            var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height), new Vector2(0.5f, 0.5f), 64f);
            sprite.name = name;
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }
    }
}
