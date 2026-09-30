using System;
using System.Collections;
using System.Collections.Generic;
using Ricochet.Audio;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The encounter loop and the run around it (CONCEPT section 3). A rift opens on a real wall and a creature comes
    /// through, showing its next move. Each shot's popped crystals send their light into it as damage (chain value per
    /// crystal, so combos matter); then it acts: attacks your shield, guards, or hexes crystals. Seal the rift and the
    /// room fills with light (Fever), then you pinch one of three rewards before the next rift opens elsewhere.
    /// Five creatures, then the boss; lose your shield and the run starts over. The run saves after every turn.
    /// Hooks into ShotDirector's Intro and TurnGate, so the shot loop itself stays unchanged.
    /// </summary>
    public sealed class EncounterDirector : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] ShotDirector _director;
        [SerializeField] BoardGenerator _board;
        [SerializeField] Sling _sling;
        [SerializeField] Rift _rift;
        [SerializeField] Creature _creature;
        [SerializeField] LightMotes _motes;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] TimeWarp _warp;
        [SerializeField] ShatterFx _fx;
        [SerializeField] ScorePopups _popups;
        [SerializeField] RewardPicker _rewards;
        [SerializeField] Banner _banner;

        [Header("Rules")]
        [SerializeField] int _maxShield = 30;
        [SerializeField] int _shieldRestore = 12;
        [SerializeField] int _refreshBelow = 12;         // fewer crystals left than this: a fresh board next turn
        [SerializeField] int _feverPoints = 50;
        [SerializeField] int _aegisShield = 10;
        [SerializeField] int _thornDamage = 2;
        [SerializeField] int _goldRushExtra = 3;

        [Header("Light")]
        [SerializeField] Color _damageGlow = new(1.6f, 1.1f, 0.4f);
        [SerializeField] Color _shieldHitGlow = new(2f, 0.25f, 0.2f);
        [SerializeField] Color _guardGlow = new(0.4f, 1.2f, 2f);
        [SerializeField] Color _victoryGlow = new(2.6f, 2f, 1.1f);
        [SerializeField] Color _feverWave = new(1.6f, 1.15f, 0.5f);
        [SerializeField] Color _runWave = new(0.9f, 0.6f, 1.8f);
        [SerializeField] float _feverWaveSpeed = 3.2f; // m/s: a 5 m room sweeps in ~1.5 s
        [SerializeField] Color _burstColor = new(1f, 0.35f, 0.8f);
        [SerializeField] Color _shardColor = new(1f, 0.85f, 0.45f);

        readonly List<Crystal> _scratch = new();
        readonly Reward[] _offer = new Reward[3];
        CreatureDef _def;
        int _intentIndex;
        int _tick;
        Vector3? _lastRift;
        bool _placeRift;
        RiftPlacer.Placement _placement;
        RunState _run;
        System.Random _rng;

        public RunState Run => _run;
        public int Encounter => _run.Encounter;
        public int Shield => _run.Shield;
        public int MaxShield => _run.MaxShield;
        public Creature Creature => _creature;

        /// <summary>Shield changed: (new value, delta).</summary>
        public event Action<int, int> ShieldChanged;
        /// <summary>The run moved on (a new encounter, a reward, a new run): the HUD redraws its progress.</summary>
        public event Action RunChanged;

        /// <summary>This shot has already banked enough light to finish the creature.</summary>
        public bool ShotLethal => _creature.Alive && _director.ShotInProgress && _director.ShotLight >= _creature.EffectiveHp;

        /// <summary>The next crystal hit would bank the finishing light (Peggle's "this peg wins" moment).</summary>
        public bool NextHitLethal => _creature.Alive && _director.ShotInProgress && !ShotLethal &&
                                     _director.ShotLight + _director.NextHitValue >= _creature.EffectiveHp;

        /// <summary>
        /// Runs resume on device. In the Editor they start fresh (the desktop test scripts expect encounter 1) unless
        /// PlayerPrefs "Ricochet.ResumeInEditor" is 1 (DevHooks.ResumeInEditor).
        /// </summary>
        static bool ResumeEnabled => !Application.isEditor || PlayerPrefs.GetInt("Ricochet.ResumeInEditor", 0) == 1;

        void Awake()
        {
            var saved = ResumeEnabled ? RunState.Load() : null;
            StartRun(saved);
            if (saved != null) Debug.Log($"[Ricochet] Run resumed: {saved}");
            _director.Intro = OpenEncounter;
            // A killing shot opens the next encounter, which generates its own board: a Prism need not.
            _director.WillRenewBoard = () => _creature.Alive && _director.ShotLight >= _creature.EffectiveHp;
            _director.TurnGate = Turn;
            _director.BoardShaper = ShapeBoard;
        }

        void StartRun(RunState saved)
        {
            _run = saved ?? RunState.New(_maxShield);
            _rng = new System.Random(_run.Seed + 7919 * _run.Encounter);
            _director.Run = _run;
            if (saved != null) _director.RestoreScore(saved.Score, saved.BestCombo);
            else _director.ResetScore();
            ApplyRelicsToBoard();
            ShieldChanged?.Invoke(_run.Shield, 0);
            RunChanged?.Invoke();
        }

        void ApplyRelicsToBoard() => _board.ExtraGold = _run.Has(Relic.GoldRush) ? _goldRushExtra : 0;

        void OnEnable()
        {
            _director.CrystalPopped += OnCrystalPopped;
            _motes.Arrived += OnMoteArrived;
        }

        void OnDisable()
        {
            _director.CrystalPopped -= OnCrystalPopped;
            _motes.Arrived -= OnMoteArrived;
        }

        // Quitting or taking the headset off mid-run: keep what the last turn saved, plus the score so far.
        void OnApplicationPause(bool paused)
        {
            if (paused && _run != null && !_director.ShotInProgress) SaveRun();
        }

        void SaveRun()
        {
            bool mid = _creature.Alive && _creature.gameObject.activeSelf;
            _run.CreatureHp = mid ? _creature.Hp : -1;
            _run.CreatureArmor = mid ? _creature.Armor : 0;
            _run.IntentIndex = _intentIndex;
            _run.Score = _director.Score;
            _run.BestCombo = _director.BestCombo;
            _run.Save();
        }

        /// <summary>
        /// Where the creature settles. Its intent is critical info, so it must sit inside the Glasses' +/-25 degree
        /// band (TECH_GUIDE section 7) even when the rift uses the +/-30 degree tier: past 20 degrees it glides out of
        /// the rift toward the centre of view (rotated about the eye) and a little nearer.
        /// </summary>
        Vector3 CreatureHome(Vector3 mouth)
        {
            const float maxYaw = 20f;
            Vector3 eye = _playArea.Head.position;
            Vector3 forward = Vector3.ProjectOnPlane(_playArea.Seat.forward, Vector3.up);
            Vector3 to = mouth - eye;
            float yaw = Vector3.SignedAngle(forward, Vector3.ProjectOnPlane(to, Vector3.up), Vector3.up);
            if (Mathf.Abs(yaw) <= maxYaw) return mouth;
            Quaternion swing = Quaternion.AngleAxis(Mathf.Clamp(yaw, -maxYaw, maxYaw) - yaw, Vector3.up);
            return eye + swing * to * 0.85f;
        }

        IEnumerator OpenEncounter()
        {
            _def = CreatureDef.ForEncounter(_run.Encounter);
            RunChanged?.Invoke();
            // The board comes first so the rift can keep clear of the hero cluster (the guaranteed first shot);
            // PlaceRift runs as the board shaper, then clears crystals from the rift's patch of wall.
            _board.SetExclusion(Vector3.zero, 0f);
            _placeRift = true;
            _director.RegenerateBoard(); // crystals cascade in while the crack spreads
            var place = _placement;
            Debug.Log($"[Ricochet] Encounter {_run.Encounter + 1}/{RunState.EncountersPerRun}: {_def.Name} (HP {_def.Hp}), rift {place.Info}");
            _rift.Open(place.Position, place.Normal);
            _sfx.PlayRiftOpen(place.Position);
            if (_def.Boss && _banner != null) _banner.Show(_def.Name, "the last rift", _burstColor, 3.2f);
            yield return Wait(_def.Boss ? 1.6f : 0.9f);

            _creature.Emerge(_def, place.Position, CreatureHome(_rift.Mouth), _playArea.Head);
            _intentIndex = 0;
            if (_run.CreatureHp > 0)
            {
                // A resumed fight: the creature comes back as hurt as you left it.
                _creature.Restore(Mathf.Min(_run.CreatureHp, _def.Hp), _run.CreatureArmor);
                _intentIndex = _run.IntentIndex % _def.Cycle.Length;
            }
            yield return Wait(0.8f);
            _creature.ShowIntent(_def.Cycle[_intentIndex]);
            SaveRun();
        }

        int ShapeBoard()
        {
            if (_placeRift)
            {
                _placeRift = false;
                _placement = RiftPlacer.Place(_playArea.Room, _playArea.Seat, _lastRift, _board.HeroPoint);
                _lastRift = _placement.Position;
                _board.SetExclusion(_placement.ExclusionCenter, _placement.ExclusionRadius);
                _board.ClearExclusion();
            }
            return _board.Active.Count;
        }

        void OnCrystalPopped(Vector3 at, int light)
        {
            if (light <= 0 || !_creature.Alive) return;
            // Brighter chains fly a touch slower, so a big combo lands as a visible volley.
            _motes.Launch(LightMotes.Kind.Damage, light, at, _creature.Center, 0.05f, 0.5f + 0.02f * light);
        }

        void OnMoteArrived(LightMotes.Kind kind, int amount, Vector3 at)
        {
            switch (kind)
            {
                case LightMotes.Kind.Damage:
                    bool armored = _creature.Armor > 0;
                    _creature.TakeDamage(amount);
                    _sfx.PlayTick(_tick++, at);
                    if (armored) _sfx.PlayGuard(at); // light spent on armor rings hollow
                    if (_popups != null) _popups.Show(at + Vector3.up * 0.12f, amount, _tick, true);
                    _glow.Pulse(at, armored ? _guardGlow : _damageGlow, 0.6f, 0.3f);
                    break;

                case LightMotes.Kind.Bolt:
                    SetShield(_run.Shield - amount);
                    _sfx.PlayShieldHit(at);
                    _glow.Pulse(at, _shieldHitGlow, 0.8f, 0.5f);
                    if (_fx != null) _fx.Burst(at, _burstColor);
                    if (_popups != null) _popups.Show(at + Vector3.up * 0.08f, amount, 0, true);
                    if (_run.Has(Relic.Thornlight) && _creature.Alive)
                    {
                        // Thornlight: the shield throws some of the blow back as light.
                        _motes.Launch(LightMotes.Kind.Damage, _thornDamage, at, _creature.Center, 0.1f, 0.45f);
                        _glow.Pulse(at, Upgrades.RelicColor * 1.3f, 0.6f, 0.4f);
                    }
                    break;

                case LightMotes.Kind.Hex:
                    var active = _board.Active;
                    if (amount >= 0 && amount < active.Count) active[amount].Corrupt();
                    _glow.Pulse(at, _burstColor * 0.6f, 0.4f, 0.3f);
                    break;
            }
        }

        void SetShield(int value)
        {
            value = Mathf.Clamp(value, 0, _run.MaxShield);
            int delta = value - _run.Shield;
            _run.Shield = value;
            ShieldChanged?.Invoke(value, delta);
        }

        IEnumerator Turn()
        {
            // Let the light land first: the damage ticks are the payoff of the shot.
            while (_motes.InFlight > 0) yield return null;
            _tick = 0;
            yield return Wait(0.2f);

            if (!_creature.Alive)
            {
                yield return Victory();
                yield break;
            }

            yield return CreatureAct(_creature.NextIntent);
            if (!_creature.Alive)
            {
                yield return Victory(); // Thornlight finished it on its own attack
                yield break;
            }
            if (_run.Shield <= 0)
            {
                yield return Defeat();
                yield break;
            }

            _intentIndex = (_intentIndex + 1) % _def.Cycle.Length;
            _creature.ShowIntent(_def.Cycle[_intentIndex]);

            if (_board.RemainingCount() < _refreshBelow)
            {
                _director.RegenerateBoard();
                yield return Wait(0.5f);
            }
            SaveRun();
        }

        IEnumerator CreatureAct(Intent intent)
        {
            // Anticipation: it draws back and burns in the color of its move.
            for (float t = 0f; t < 0.45f; t += Time.deltaTime)
            {
                _creature.SetWindup(t / 0.45f);
                yield return null;
            }
            _creature.Lunge();
            Vector3 from = _creature.Center;

            switch (intent.Kind)
            {
                case IntentKind.Attack:
                    _sfx.PlayBolt(from);
                    // Aim at your shield: just below the sling, never at the face.
                    _motes.Launch(LightMotes.Kind.Bolt, intent.Amount, from, _sling.transform.position + Vector3.down * 0.06f, 0f, 0.55f);
                    break;

                case IntentKind.Guard:
                    _creature.AddArmor(intent.Amount);
                    _sfx.PlayGuard(from);
                    _glow.Pulse(from, _guardGlow, 0.9f, 0.6f);
                    break;

                case IntentKind.Hex:
                    int n = HexTargets(intent.Amount);
                    for (int i = 0; i < n; i++)
                    {
                        int index = BoardIndex(_scratch[i]);
                        _motes.Launch(LightMotes.Kind.Hex, index, from, _scratch[i].transform.position, i * 0.08f, 0.5f);
                    }
                    if (n > 0) _sfx.PlayBolt(from);
                    break;
            }
            while (_motes.InFlight > 0) yield return null;
            yield return Wait(0.3f);
        }

        int BoardIndex(Crystal crystal)
        {
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
                if (active[i] == crystal) return i;
            return -1;
        }

        /// <summary>Picks up to n unlit, clean crystals to hex, favouring the ones nearest the view center.</summary>
        int HexTargets(int n)
        {
            _scratch.Clear();
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
            {
                var c = active[i];
                if (!c.IsPopped && !c.IsLit && !c.IsCorrupt) _scratch.Add(c);
            }
            Vector3 eye = _playArea.Seat.position, fwd = _playArea.Seat.forward;
            _scratch.Sort((a, b) => Vector3.Angle(fwd, a.transform.position - eye).CompareTo(Vector3.Angle(fwd, b.transform.position - eye)));
            // Spread the hex through the central group instead of always the same crystals.
            int pool = Mathf.Min(_scratch.Count, n * 3);
            for (int i = 0; i < Mathf.Min(n, pool); i++)
            {
                int j = UnityEngine.Random.Range(i, pool);
                (_scratch[i], _scratch[j]) = (_scratch[j], _scratch[i]);
            }
            return Mathf.Min(n, pool);
        }

        IEnumerator Victory()
        {
            Vector3 at = _creature.Center;
            bool boss = _def.Boss;
            Debug.Log($"[Ricochet] Encounter {_run.Encounter + 1} won: {_def.Name} sealed");
            _creature.Die();
            _sfx.PlayCrash(at);
            _sfx.PlayChord(at);
            _glow.Pulse(at, _victoryGlow, 2.2f, 1.6f);
            if (_fx != null) { _fx.Burst(at, _burstColor); _fx.Burst(at, _shardColor); }
            _warp.Hold(boss ? 0.2f : 0.3f, boss ? 1.6f : 0.9f);
            yield return WaitReal(boss ? 1.6f : 0.9f);
            _rift.Seal();
            yield return WaitReal(0.4f);

            yield return Fever(at);

            if (boss)
            {
                yield return RunComplete();
                yield break;
            }

            SetShield(_run.Shield + _shieldRestore);
            yield return WaitReal(1.2f);

            // Between rifts: three rewards float up in front of you; pinch one, draw it in, let go.
            _run.Encounter++;
            _run.CreatureHp = -1;
            _rng = new System.Random(_run.Seed + 7919 * _run.Encounter);
            _run.Save(); // an encounter won is never lost, even if you quit at the reward
            RunChanged?.Invoke();
            if (_rewards != null)
            {
                int count = Upgrades.Offer(_run, _rng, _offer);
                yield return _rewards.Choose(_offer, count);
                Apply(_offer[Mathf.Clamp(_rewards.Chosen, 0, count - 1)]);
            }
            yield return OpenEncounter();
        }

        /// <summary>
        /// Fever: a front of light races out from where the creature fell, across every real wall, and each crystal left
        /// shatters the moment the front reaches it. A rising swell carries it.
        /// </summary>
        IEnumerator Fever(Vector3 at)
        {
            _glow.Wave(at, _feverWave, _feverWaveSpeed, 0.16f, 9f);
            _sfx.PlaySwell(at);
            _scratch.Clear();
            var active = _board.Active;
            for (int i = 0; i < active.Count; i++)
                if (!active[i].IsPopped) _scratch.Add(active[i]);
            _scratch.Sort((a, b) => (a.transform.position - at).sqrMagnitude.CompareTo((b.transform.position - at).sqrMagnitude));
            float front = 0f; // distance the wave has covered so far
            for (int i = 0; i < _scratch.Count; i++)
            {
                var c = _scratch[i];
                Vector3 p = c.transform.position;
                float d = Vector3.Distance(p, at);
                if (d > front)
                {
                    yield return WaitReal((d - front) / _feverWaveSpeed);
                    front = d;
                }
                c.Light();
                c.Pop();
                if (_fx != null) _fx.Burst(p, _shardColor);
                _glow.Pulse(p, _victoryGlow * 0.6f, 0.8f, 0.5f);
                if (i % 2 == 0) _sfx.PlayComboNote(i / 2 % 15, p);
                _director.AddScore(_feverPoints);
                if (_popups != null && i % 3 == 0) _popups.Show(p, _feverPoints, Mathf.Min(i, 8));
            }
            Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
            _glow.Pulse(center, _victoryGlow, 4f, 1.8f);
            _sfx.PlayChord(center);
        }

        void Apply(Reward reward)
        {
            switch (reward.Type)
            {
                case RewardType.Spark:
                    int slot = _run.Bag.IndexOf(reward.Spark);
                    if (slot < 0) { _run.Bag.Add(reward.Spark); slot = _run.Bag.Count - 1; }
                    _run.BagIndex = slot; // try the new Spark on the very next shot
                    break;
                case RewardType.Relic:
                    _run.Add(reward.Relic);
                    if (reward.Relic == Relic.Aegis)
                    {
                        _run.MaxShield += _aegisShield;
                        SetShield(_run.MaxShield);
                    }
                    ApplyRelicsToBoard();
                    break;
                case RewardType.Mend:
                    SetShield(_run.Shield + Upgrades.MendAmount);
                    break;
            }
            _director.RefreshArmedKind();
            SaveRun();
            Debug.Log($"[Ricochet] Reward: {reward.Name}. Run: {_run}");
            RunChanged?.Invoke();
        }

        /// <summary>The boss is sealed: the whole room answers in waves of light, then a new run begins.</summary>
        IEnumerator RunComplete()
        {
            Debug.Log($"[Ricochet] Run complete! score {_director.Score}, best chain {_director.BestCombo}");
            RunState.Clear();
            Vector3 center = _playArea.Seat.position + _playArea.Seat.forward * 2f;
            for (int i = 0; i < 3; i++)
            {
                _glow.Wave(center + UnityEngine.Random.insideUnitSphere * 0.8f, i == 1 ? _runWave : _feverWave,
                           _feverWaveSpeed * 0.8f, 0.2f, 9f);
                _sfx.PlayChord(center);
                if (_fx != null) { _fx.Burst(center, _shardColor); _fx.Burst(center, _burstColor); }
                yield return WaitReal(0.7f);
            }
            if (_banner != null) _banner.Show("Rift sealed", $"{_director.Score:N0}", _shardColor, 4.5f);
            yield return WaitReal(5f);

            _lastRift = null;
            StartRun(null);
            yield return OpenEncounter();
        }

        IEnumerator Defeat()
        {
            Debug.Log($"[Ricochet] Run lost at encounter {_run.Encounter + 1} (score {_director.Score})");
            RunState.Clear();
            Vector3 heart = _sling.transform.position;
            _glow.Pulse(heart, _shieldHitGlow * 1.5f, 3f, 2f);
            _sfx.PlayShieldHit(heart);
            yield return WaitReal(1.2f);
            _creature.Die();
            _rift.Seal();
            yield return WaitReal(1f);

            _lastRift = null;
            StartRun(null);
            yield return OpenEncounter();
        }

        /// <summary>Dev: pick up a Spark type or relic without winning an encounter.</summary>
        public void Grant(Reward reward) => Apply(reward);

        static IEnumerator Wait(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.deltaTime) yield return null;
        }

        static IEnumerator WaitReal(float seconds)
        {
            for (float t = 0f; t < seconds; t += Time.unscaledDeltaTime) yield return null;
        }
    }
}
