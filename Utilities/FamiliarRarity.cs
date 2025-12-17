using System;
using System.Collections.Generic;
using System.Linq;
using Bloodcraft.Services;

namespace Bloodcraft.Utilities
{
    public enum FamiliarRarity
    {
        N,
        R,
        SR,
        SSR,
        SS,
        SSS
    }

    public static class FamiliarRarityInfo
    {
        static readonly Random _rng = new();

        public static IReadOnlyDictionary<FamiliarRarity, (string Eng, string Chi, string Hex)> RarityDisplayMap = new Dictionary<FamiliarRarity, (string, string, string)>
        {
            [FamiliarRarity.N] = ("N", "普通", "#9E9E9E"),
            [FamiliarRarity.R] = ("R", "稀有", "#4CAF50"),
            [FamiliarRarity.SR] = ("SR", "精良", "#2196F3"),
            [FamiliarRarity.SSR] = ("SSR", "史詩", "#9C27B0"),
            [FamiliarRarity.SS] = ("SS", "傳說", "#FFC107"),
            [FamiliarRarity.SSS] = ("SSS", "神話", "#FF5252")
        };

        public static FamiliarRarity ChooseRandomRarity()
        {
            if (!ConfigService.FamiliarRaritySystem) return FamiliarRarity.N;

            double pN = ConfigService.FamiliarRarityProbabilityN;
            double pR = ConfigService.FamiliarRarityProbabilityR;
            double pSR = ConfigService.FamiliarRarityProbabilitySR;
            double pSSR = ConfigService.FamiliarRarityProbabilitySSR;
            double pSS = ConfigService.FamiliarRarityProbabilitySS;
            double pSSS = ConfigService.FamiliarRarityProbabilitySSS;

            double t = pN + pR + pSR + pSSR + pSS + pSSS;
            if (t <= 0) return FamiliarRarity.N;

            // Normalize in case fractions aren't exact
            pN /= t; pR /= t; pSR /= t; pSSR /= t; pSS /= t; pSSS /= t;

            double r = _rng.NextDouble();
            double cumulative = 0;

            cumulative += pN; if (r < cumulative) return FamiliarRarity.N;
            cumulative += pR; if (r < cumulative) return FamiliarRarity.R;
            cumulative += pSR; if (r < cumulative) return FamiliarRarity.SR;
            cumulative += pSSR; if (r < cumulative) return FamiliarRarity.SSR;
            cumulative += pSS; if (r < cumulative) return FamiliarRarity.SS;
            return FamiliarRarity.SSS;
        }

        public static float GetMultiplier(FamiliarRarity rarity)
        {
            return rarity switch
            {
                FamiliarRarity.N => ConfigService.FamiliarRarityMultiplierN,
                FamiliarRarity.R => ConfigService.FamiliarRarityMultiplierR,
                FamiliarRarity.SR => ConfigService.FamiliarRarityMultiplierSR,
                FamiliarRarity.SSR => ConfigService.FamiliarRarityMultiplierSSR,
                FamiliarRarity.SS => ConfigService.FamiliarRarityMultiplierSS,
                FamiliarRarity.SSS => ConfigService.FamiliarRarityMultiplierSSS,
                _ => 0.3f
            };
        }

        public static string GetHex(FamiliarRarity rarity) => RarityDisplayMap.TryGetValue(rarity, out var v) ? v.Hex : "#9E9E9E";
        public static string GetChinese(FamiliarRarity rarity) => RarityDisplayMap.TryGetValue(rarity, out var v) ? v.Chi : "普通";
        public static string GetTag(FamiliarRarity rarity) => RarityDisplayMap.TryGetValue(rarity, out var v) ? v.Eng : "N";
    }
}
