using UnityEngine;

namespace Ricochet.Gameplay
{
    public enum IntentKind { Attack, Guard, Hex }

    /// <summary>What the creature will do after your next shot, shown in advance (Slay-the-Spire-style intent).</summary>
    public readonly struct Intent
    {
        public readonly IntentKind Kind;
        public readonly int Amount;

        public Intent(IntentKind kind, int amount) { Kind = kind; Amount = amount; }

        public Color Color => Kind switch
        {
            IntentKind.Attack => new Color(1f, 0.35f, 0.22f),
            IntentKind.Guard => new Color(0.35f, 0.8f, 1f),
            _ => new Color(0.75f, 0.35f, 1f),
        };
    }

    /// <summary>
    /// A creature's stats and its move cycle. Tuning lives in code (like every other default in the project).
    /// Damage math: each popped crystal deals its chain value (1, 2, 3...), so a shot deals score / 10.
    /// The sweep's average aimed shot lights ~2.4 crystals, about 4 damage.
    /// </summary>
    public sealed class CreatureDef
    {
        public string Name;
        public int Hp;
        public float Size;
        public Intent[] Cycle;
        // Silhouette: the tendril skirt is what tells the roster apart at a glance (body-local units, body ~1 tall).
        public int Tendrils = 5;
        public float TendrilLength = 0.9f;
        public float TendrilWidth = 0.07f;
        public int Seed = 1;
        /// <summary>The run's last rift: bigger, announced, and sealing it ends the run.</summary>
        public bool Boss;
        /// <summary>Rim and tendril glow; alpha 0 keeps the material's magenta (the boss wears royal gold).</summary>
        public Color Identity;

        /// <summary>One run: five creatures that escalate, then the boss (RunState.EncountersPerRun).</summary>
        public static readonly CreatureDef[] Roster =
        {
            // Wisp: a few short, fine wisps. Shade: many long streamers. Maw: a heavy, stubby fringe.
            // Lurker: a spindly thicket that hexes. Warden: a squat armored bell. The Hollow Queen: a vast long-trailed crown.
            // Balance (session 6, sweep aimedDmg ~5.5 per shot before crits): an average player takes about
            // 3/4/6/6/7/10 shots, and loses about 15-25% of the shield early, ~45% mid-run and ~65% to the Queen.
            new() { Name = "Wisp", Hp = 14, Size = 0.24f, Cycle = new[] { A(2), A(3), G(3) },
                    Tendrils = 5, TendrilLength = 0.8f, TendrilWidth = 0.11f, Seed = 3 },
            new() { Name = "Shade", Hp = 20, Size = 0.28f, Cycle = new[] { A(3), H(3), A(4), G(4) },
                    Tendrils = 7, TendrilLength = 1.35f, TendrilWidth = 0.09f, Seed = 5 },
            new() { Name = "Maw", Hp = 26, Size = 0.32f, Cycle = new[] { G(5), A(4), H(3), A(5) },
                    Tendrils = 4, TendrilLength = 0.65f, TendrilWidth = 0.18f, Seed = 8 },
            new() { Name = "Lurker", Hp = 28, Size = 0.27f, Cycle = new[] { H(3), A(4), H(4), A(5) },
                    Tendrils = 10, TendrilLength = 1.1f, TendrilWidth = 0.055f, Seed = 11 },
            new() { Name = "Warden", Hp = 30, Size = 0.34f, Cycle = new[] { G(6), A(4), G(5), A(6) },
                    Tendrils = 6, TendrilLength = 0.45f, TendrilWidth = 0.24f, Seed = 13 },
            new() { Name = "The Hollow Queen", Hp = 50, Size = 0.42f, Boss = true, Identity = new Color(1.5f, 1.12f, 0.5f, 1f),
                    Cycle = new[] { A(4), H(4), G(6), A(5), H(5), A(7) },
                    Tendrils = 12, TendrilLength = 1.6f, TendrilWidth = 0.1f, Seed = 17 },
        };

        /// <summary>Encounter n of a run (0-based): cycles the roster and toughens each lap.</summary>
        public static CreatureDef ForEncounter(int n)
        {
            var baseDef = Roster[n % Roster.Length];
            int lap = n / Roster.Length;
            if (lap == 0) return baseDef;
            float k = 1f + 0.35f * lap;
            var cycle = new Intent[baseDef.Cycle.Length];
            for (int i = 0; i < cycle.Length; i++)
                cycle[i] = new Intent(baseDef.Cycle[i].Kind, Mathf.RoundToInt(baseDef.Cycle[i].Amount * k));
            return new CreatureDef
            {
                Name = baseDef.Name, Hp = Mathf.RoundToInt(baseDef.Hp * k), Size = baseDef.Size, Cycle = cycle,
                Tendrils = baseDef.Tendrils, TendrilLength = baseDef.TendrilLength, TendrilWidth = baseDef.TendrilWidth,
                Seed = baseDef.Seed, Boss = baseDef.Boss, Identity = baseDef.Identity,
            };
        }

        static Intent A(int n) => new(IntentKind.Attack, n);
        static Intent G(int n) => new(IntentKind.Guard, n);
        static Intent H(int n) => new(IntentKind.Hex, n);
    }
}
