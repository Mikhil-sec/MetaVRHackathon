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

        public static readonly CreatureDef[] Roster =
        {
            new() { Name = "Wisp", Hp = 16, Size = 0.24f, Cycle = new[] { A(3), A(4), G(4) } },
            new() { Name = "Shade", Hp = 24, Size = 0.28f, Cycle = new[] { A(4), H(3), A(5), G(5) } },
            new() { Name = "Maw", Hp = 32, Size = 0.32f, Cycle = new[] { G(6), A(6), H(4), A(7) } },
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
            return new CreatureDef { Name = baseDef.Name, Hp = Mathf.RoundToInt(baseDef.Hp * k), Size = baseDef.Size, Cycle = cycle };
        }

        static Intent A(int n) => new(IntentKind.Attack, n);
        static Intent G(int n) => new(IntentKind.Guard, n);
        static Intent H(int n) => new(IntentKind.Hex, n);
    }
}
