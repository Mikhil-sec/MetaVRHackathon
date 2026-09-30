using System;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>Spark types (CONCEPT section 3). The run's bag cycles one per shot; the held Spark wears its color.</summary>
    public enum SparkKind { Plain, Splitter, Bomb, Ghost, Magnet, Heavy }

    /// <summary>Relics: passive rules for the rest of the run (CONCEPT section 3). One bit each, so a run saves as an int.</summary>
    [Flags]
    public enum Relic
    {
        None = 0,
        Carom = 1 << 0,       // each room bounce before your first hit raises the whole chain by 1
        Skylight = 1 << 1,    // a ceiling bounce makes the next hit a critical
        FirstLight = 1 << 2,  // the first hit of every shot is a critical
        GoldRush = 1 << 3,    // +3 Gold crystals on every board
        Aegis = 1 << 4,       // +10 max shield, fully restored
        Thornlight = 1 << 5,  // the creature takes 2 whenever it attacks you
        BigBang = 1 << 6,     // Bomb blasts reach 60% wider
        ThirdEye = 1 << 7,    // Focus criticals are x3
        SecondWind = 1 << 8,  // the first floor touch each shot bounces instead of ending it
        Resonance = 1 << 9,   // Amp crystals add +2 to the multiplier
    }

    public enum RewardType { Spark, Relic, Mend }

    /// <summary>One of the three orbs offered after an encounter.</summary>
    public readonly struct Reward
    {
        public readonly RewardType Type;
        public readonly SparkKind Spark;
        public readonly Relic Relic;

        public Reward(SparkKind spark) { Type = RewardType.Spark; Spark = spark; Relic = Relic.None; }
        public Reward(Relic relic) { Type = RewardType.Relic; Spark = SparkKind.Plain; Relic = relic; }
        Reward(RewardType type) { Type = type; Spark = SparkKind.Plain; Relic = Relic.None; }
        public static Reward Mend => new(RewardType.Mend);

        public string Name => Type switch
        {
            RewardType.Spark => Upgrades.Name(Spark),
            RewardType.Relic => Upgrades.Name(Relic),
            _ => "Mend",
        };

        public string Blurb => Type switch
        {
            RewardType.Spark => Upgrades.Blurb(Spark),
            RewardType.Relic => Upgrades.Blurb(Relic),
            _ => $"+{Upgrades.MendAmount} shield",
        };

        public Color Color => Type switch
        {
            RewardType.Spark => Upgrades.Color(Spark),
            RewardType.Relic => Upgrades.RelicColor,
            _ => Upgrades.MendColor,
        };

        /// <summary>IntentGlyph shape index (3.. are the reward glyphs).</summary>
        public float Glyph => Type switch
        {
            RewardType.Spark => Upgrades.Glyph(Spark),
            RewardType.Relic => Upgrades.GlyphRelic,
            _ => Upgrades.GlyphMend,
        };
    }

    /// <summary>Names, looks and the 1-of-3 offer. Tuning lives in code, like every other default in the project.</summary>
    public static class Upgrades
    {
        public const int MendAmount = 15;
        public const float GlyphRelic = 8f, GlyphMend = 9f, GlyphCrown = 10f, GlyphPip = 11f;
        public static readonly Color RelicColor = new(1f, 0.78f, 0.38f);
        public static readonly Color MendColor = new(0.45f, 0.85f, 1f);

        static readonly SparkKind[] SparkPool = { SparkKind.Splitter, SparkKind.Bomb, SparkKind.Ghost, SparkKind.Magnet, SparkKind.Heavy };
        static readonly Relic[] RelicPool =
        {
            Relic.Carom, Relic.Skylight, Relic.FirstLight, Relic.GoldRush, Relic.Aegis,
            Relic.Thornlight, Relic.BigBang, Relic.ThirdEye, Relic.SecondWind, Relic.Resonance,
        };

        public static string Name(SparkKind kind) => kind switch
        {
            SparkKind.Splitter => "Splitter",
            SparkKind.Bomb => "Bomb Spark",
            SparkKind.Ghost => "Ghost",
            SparkKind.Magnet => "Magnet",
            SparkKind.Heavy => "Heavy",
            _ => "Spark",
        };

        public static string Blurb(SparkKind kind) => kind switch
        {
            SparkKind.Splitter => "splits in three\non its first bounce",
            SparkKind.Bomb => "bursts on\nits third hit",
            SparkKind.Ghost => "passes through\nfurniture once",
            SparkKind.Magnet => "curves toward\ncrystals",
            SparkKind.Heavy => "plows through\ncrystals",
            _ => "",
        };

        public static string Name(Relic relic) => relic switch
        {
            Relic.Carom => "Carom",
            Relic.Skylight => "Skylight",
            Relic.FirstLight => "First Light",
            Relic.GoldRush => "Gold Rush",
            Relic.Aegis => "Aegis",
            Relic.Thornlight => "Thornlight",
            Relic.BigBang => "Big Bang",
            Relic.ThirdEye => "Third Eye",
            Relic.SecondWind => "Second Wind",
            Relic.Resonance => "Resonance",
            _ => "",
        };

        public static string Blurb(Relic relic) => relic switch
        {
            Relic.Carom => "bank shots raise\nthe whole chain",
            Relic.Skylight => "ceiling bounce:\nnext hit critical",
            Relic.FirstLight => "first hit each\nshot is critical",
            Relic.GoldRush => "+3 gold crystals\nevery board",
            Relic.Aegis => "+10 max shield,\nfully restored",
            Relic.Thornlight => "attackers take\n2 back",
            Relic.BigBang => "bombs blast\nmuch wider",
            Relic.ThirdEye => "focus hits\nx3 not x2",
            Relic.SecondWind => "first floor touch\nbounces instead",
            Relic.Resonance => "amp crystals\ngive +2",
            _ => "",
        };

        /// <summary>Each Spark type's light: the held Spark's rim and halo, and its reward orb.</summary>
        public static Color Color(SparkKind kind) => kind switch
        {
            SparkKind.Splitter => new Color(0.45f, 1f, 0.55f),
            SparkKind.Bomb => new Color(1f, 0.5f, 0.15f),
            SparkKind.Ghost => new Color(0.78f, 0.68f, 1f),
            SparkKind.Magnet => new Color(1f, 0.35f, 0.62f),
            SparkKind.Heavy => new Color(0.95f, 0.95f, 1f),
            _ => new Color(0.35f, 0.85f, 1f),
        };

        public static float Glyph(SparkKind kind) => kind switch
        {
            SparkKind.Splitter => 3f,
            SparkKind.Bomb => 4f,
            SparkKind.Ghost => 5f,
            SparkKind.Magnet => 6f,
            SparkKind.Heavy => 7f,
            _ => GlyphPip,
        };

        /// <summary>
        /// Fills three distinct rewards: at least one new Spark type and one relic while any are left, and a Mend
        /// in place of the third when the shield is low. Returns how many were filled.
        /// </summary>
        public static int Offer(RunState run, System.Random rng, Reward[] into)
        {
            int n = 0;
            int spark = PickSpark(run, rng, -1);
            if (spark >= 0) into[n++] = new Reward(SparkPool[spark]);
            int relic = PickRelic(run, rng, -1);
            if (relic >= 0) into[n++] = new Reward(RelicPool[relic]);

            bool lowShield = run.Shield < run.MaxShield * 0.6f;
            if (lowShield) into[n++] = Reward.Mend;
            else
            {
                // The third orb: another relic or Spark, whichever pool the coin favours and still has one.
                int another = rng.Next(2) == 0 ? PickRelic(run, rng, relic) : -1;
                if (another >= 0) into[n++] = new Reward(RelicPool[another]);
                else
                {
                    another = PickSpark(run, rng, spark);
                    if (another >= 0) into[n++] = new Reward(SparkPool[another]);
                    else if ((another = PickRelic(run, rng, relic)) >= 0) into[n++] = new Reward(RelicPool[another]);
                    else into[n++] = Reward.Mend;
                }
            }
            return n;
        }

        static int PickSpark(RunState run, System.Random rng, int except)
        {
            int count = 0, chosen = -1;
            for (int i = 0; i < SparkPool.Length; i++)
            {
                if (i == except || run.Bag.Contains(SparkPool[i])) continue;
                if (rng.Next(++count) == 0) chosen = i; // reservoir sampling: uniform, no list
            }
            return chosen;
        }

        static int PickRelic(RunState run, System.Random rng, int except)
        {
            int count = 0, chosen = -1;
            for (int i = 0; i < RelicPool.Length; i++)
            {
                if (i == except || run.Has(RelicPool[i])) continue;
                if (rng.Next(++count) == 0) chosen = i;
            }
            return chosen;
        }
    }
}
