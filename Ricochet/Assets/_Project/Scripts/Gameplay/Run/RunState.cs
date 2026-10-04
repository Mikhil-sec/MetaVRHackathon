using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Everything a run needs to resume exactly where you left it (TECH_GUIDE section 9, the bus-stop test): saved at
    /// the end of every turn, after every reward, and when the app pauses. The board itself is not saved: a fresh one
    /// is generated on resume, since the room may have changed anyway.
    /// </summary>
    [Serializable]
    public sealed class RunState
    {
        public const int EncountersPerRun = 6; // five creatures, then the boss
        const int FormatVersion = 1;

        public int Version = FormatVersion;
        public int Seed;
        public int Daily;     // the Daily Rift's date (yyyymmdd), 0 for an ordinary run
        public int Ascension; // 0..Ascension.Max: how much stronger this run's creatures are
        public int Encounter;
        public int Shield;
        public int MaxShield;
        public int Score;
        public int BestCombo;
        public List<SparkKind> Bag = new() { SparkKind.Plain };
        public int BagIndex;
        public int Relics;
        // The creature mid-fight (-1: the encounter hasn't started, spawn it fresh).
        public int CreatureHp = -1;
        public int CreatureArmor;
        public int IntentIndex;

        public bool IsBoss => Encounter == EncountersPerRun - 1;
        public bool Has(Relic relic) => (Relics & (int)relic) != 0;
        public void Add(Relic relic) => Relics |= (int)relic;
        public SparkKind NextSpark => Bag.Count > 0 ? Bag[BagIndex % Bag.Count] : SparkKind.Plain;
        public void AdvanceBag() => BagIndex = Bag.Count > 0 ? (BagIndex + 1) % Bag.Count : 0;

        public static RunState New(int maxShield, int daily = 0) => new()
        {
            Seed = daily != 0 ? DailyRift.Seed(daily) : Environment.TickCount & 0x7fffffff,
            Daily = daily,
            Ascension = daily != 0 ? 0 : Gameplay.Ascension.ForNewRun,
            Shield = maxShield,
            MaxShield = maxShield,
        };

        static string SavePath => Path.Combine(Application.persistentDataPath, "run.json");

        public void Save()
        {
            try { File.WriteAllText(SavePath, JsonUtility.ToJson(this)); }
            catch (Exception e) { Debug.LogWarning($"[Ricochet] Run save failed: {e.Message}"); }
        }

        /// <summary>The saved run, or null when there is none (or it is unreadable or from an older format).</summary>
        public static RunState Load()
        {
            try
            {
                if (!File.Exists(SavePath)) return null;
                var run = JsonUtility.FromJson<RunState>(File.ReadAllText(SavePath));
                if (run == null || run.Version != FormatVersion || run.Shield <= 0 || run.MaxShield <= 0) return null;
                if (run.Bag == null || run.Bag.Count == 0) run.Bag = new List<SparkKind> { SparkKind.Plain };
                run.Encounter = Mathf.Clamp(run.Encounter, 0, EncountersPerRun - 1);
                return run;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ricochet] Run load failed: {e.Message}");
                return null;
            }
        }

        public static void Clear()
        {
            try { if (File.Exists(SavePath)) File.Delete(SavePath); }
            catch (Exception e) { Debug.LogWarning($"[Ricochet] Run clear failed: {e.Message}"); }
        }

        public override string ToString() =>
            $"{(Daily != 0 ? "daily " + Daily + ", " : "")}{(Ascension > 0 ? "ascension " + Ascension + ", " : "")}encounter {Encounter + 1}/{EncountersPerRun}, shield {Shield}/{MaxShield}, score {Score}, " +
            $"bag [{string.Join(",", Bag)}] next {NextSpark}, relics {(Relic)Relics}";
    }
}
