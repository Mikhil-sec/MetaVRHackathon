using Ricochet.Room;
using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The running score and chain readout (docs/TECH_GUIDE.md section 7). World-locked beside the sling, the player's
    /// home position, well inside the +/-25 x +/-20 degree band of the seated forward view, and clear of the shot path.
    /// During a shot it shows the chain (x N) punching up with each hit; afterwards the score rolls up to the new total
    /// and the chain line becomes the crystals left on the board. Pooled text, SetText with numbers: no allocations.
    /// </summary>
    public sealed class ScoreHud : MonoBehaviour
    {
        [SerializeField] PlayArea _playArea;
        [SerializeField] ShotDirector _director;
        [SerializeField] BoardGenerator _board;

        [Header("Placement relative to the seated head")]
        [SerializeField] float _forward = 0.52f;
        [SerializeField] float _left = 0.12f;   // ~13 deg each side of the sling: a 4-digit score stays inside +/-25 deg
        [SerializeField] float _down = 0.12f;

        [Header("Look")]
        [SerializeField] float _scoreScale = 0.033f;   // Unbounded digits are big and wide: a 4-digit score spans ~11 deg
        [SerializeField] float _lineScale = 0.021f;
        [SerializeField] float _rollSeconds = 0.6f;
        [SerializeField] Color _scoreColor = new(0.94f, 0.9f, 1f);
        [SerializeField] Color _lineColor = new(0.72f, 0.6f, 1f);
        [SerializeField] Color _hotColor = new(1f, 0.85f, 0.25f);
        [SerializeField] int _comboForHot = 6;

        [Header("Shield (right of the sling, mirroring the score)")]
        [SerializeField] EncounterDirector _encounter;
        [SerializeField] Color _shieldColor = new(0.45f, 0.85f, 1f);
        [SerializeField] Color _hurtColor = new(1f, 0.3f, 0.25f);

        [Header("Run progress (a row of pips under the shield: five creatures, then the crown)")]
        [SerializeField] Material _glyphMaterial;
        [SerializeField] Material _scrimMaterial;       // a dark card behind each relic pop: it reads over any flash
        [SerializeField] RewardPicker _picker;          // the HUD steps aside while you choose a reward
        [SerializeField] Sling _sling;                  // hidden until the first Spark has arrived (the cold open)
        [SerializeField] float _pipSize = 0.016f;
        [SerializeField] float _pipSpacing = 0.022f;
        [SerializeField] float _relicSize = 0.019f;     // owned relics: a row of their glyphs under the score
        [SerializeField] float _relicSpacing = 0.024f;
        [SerializeField] Color _pipDone = new(1f, 0.8f, 0.4f);
        [SerializeField] Color _pipAhead = new(0.6f, 0.5f, 0.85f);
        [SerializeField] Color _pipNow = new(1f, 0.45f, 0.85f);

        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        readonly MeshRenderer[] _pips = new MeshRenderer[RunState.EncountersPerRun];
        readonly MeshRenderer[] _ascension = new MeshRenderer[Ascension.Max]; // a chevron per tier, above the shield
        static readonly Color AscensionColor = new(1f, 0.5f, 0.3f);
        readonly MeshRenderer[] _relics = new MeshRenderer[10];
        readonly float[] _relicKinds = new float[10];
        readonly float[] _relicPunch = new float[10];   // a relic acting: its glyph in the row flares and swells
        int _relicCount;
        // ...and its glyph pops at the spot where it acted, so you see which relic did what.
        const int PopCount = 4;
        const float PopSeconds = 0.9f;
        readonly MeshRenderer[] _pops = new MeshRenderer[PopCount];
        readonly MeshRenderer[] _popCards = new MeshRenderer[PopCount];
        readonly Vector3[] _popAt = new Vector3[PopCount];
        readonly float[] _popAge = new float[PopCount];
        readonly float[] _popSize = new float[PopCount];
        readonly float[] _popKind = new float[PopCount];
        int _nextPop;
        MaterialPropertyBlock _block;
        int _pipNowIndex = -1;
        float _pipDim = 1f;

        TextMeshPro _score, _line, _shield, _shieldLabel;
        float _shown, _rollSpeed;
        int _shownInt = -1;
        float _scorePunch, _linePunch, _shieldPunch;
        Color _shieldFlash;
        float _dim;                                    // starts hidden: fades in with the first Spark

        void Awake()
        {
            _score = MakeText("Score", Vector3.zero, _scoreScale, TextAlignmentOptions.Right, 1f, true);
            _line = MakeText("Line", new Vector3(0f, -0.034f, 0f), _lineScale, TextAlignmentOptions.Right, 1f, false);
            // The HUD origin sits one sling-offset left of the sling, so the shield starts one offset to its right.
            _shield = MakeText("Shield", new Vector3(_left * 2f, 0f, 0f), _scoreScale, TextAlignmentOptions.Left, 0f, true);
            _shieldLabel = MakeText("ShieldLabel", new Vector3(_left * 2f, -0.034f, 0f), _lineScale, TextAlignmentOptions.Left, 0f, false);
            _shieldLabel.SetText("shield");
            _score.gameObject.SetActive(false);
            _line.gameObject.SetActive(false);
            _shield.gameObject.SetActive(false);
            _shieldLabel.gameObject.SetActive(false);
            if (_glyphMaterial != null) BuildPips();
        }

        void BuildPips()
        {
            _block = new MaterialPropertyBlock();
            var root = new GameObject("RunPips").transform;
            root.SetParent(transform, false);
            root.localPosition = new Vector3(_left * 2f, -0.074f, 0f);
            for (int i = 0; i < _pips.Length; i++)
            {
                bool boss = i == _pips.Length - 1;
                float size = _pipSize * (boss ? 1.6f : 1f);
                _pips[i] = GlyphQuad("Pip" + i, root, new Vector3(_pipSpacing * i + size * 0.5f + (boss ? _pipSize * 0.3f : 0f), 0f, 0f), size);
            }
            root.gameObject.SetActive(false);
            var climb = new GameObject("Ascension").transform;
            climb.SetParent(transform, false);
            climb.localPosition = new Vector3(_left * 2f, 0.038f, 0f);
            for (int i = 0; i < _ascension.Length; i++)
            {
                // Spaced like the run pips, a little bigger: a row of chevrons above the shield number.
                _ascension[i] = GlyphQuad("Tier" + i, climb, new Vector3(_pipSize * 1.25f * i + _pipSize * 0.6f, 0f, 0f), _pipSize * 1.2f);
                _ascension[i].gameObject.SetActive(false);
            }

            // Relics grow leftward from under the score's right edge (the edge nearest the sling).
            var relics = new GameObject("Relics").transform;
            relics.SetParent(transform, false);
            relics.localPosition = new Vector3(0f, -0.078f, 0f);
            for (int i = 0; i < _relics.Length; i++)
            {
                _relics[i] = GlyphQuad("Relic" + i, relics, new Vector3(-_relicSpacing * i - _relicSize * 0.5f, 0f, 0f), _relicSize);
                _relics[i].gameObject.SetActive(false);
            }

            var pops = new GameObject("RelicPops").transform;
            pops.SetParent(transform, false);
            for (int i = 0; i < PopCount; i++)
            {
                _pops[i] = GlyphQuad("Pop" + i, pops, Vector3.zero, 1f);
                _pops[i].gameObject.SetActive(false);
                if (_scrimMaterial != null)
                {
                    _popCards[i] = GlyphQuad("PopCard" + i, pops, Vector3.zero, 1f);
                    _popCards[i].sharedMaterial = _scrimMaterial;
                    _popCards[i].gameObject.SetActive(false);
                }
                _popAge[i] = PopSeconds;
            }
        }

        void OnRelicTriggered(Relic relic, Vector3 at)
        {
            if (_block == null) return;
            float kind = Upgrades.Glyph(relic);
            for (int i = 0; i < _relicCount; i++)
                if (Mathf.Approximately(_relicKinds[i], kind)) _relicPunch[i] = 1f;

            int p = _nextPop;
            _nextPop = (_nextPop + 1) % PopCount;
            _popAt[p] = at;
            _popAge[p] = 0f;
            // About 2.6 degrees across wherever it happens (a crystal 4 m away or the shield at arm's length).
            float distance = Vector3.Distance(_playArea.Head != null ? _playArea.Head.position : _playArea.Seat.position, at);
            _popSize[p] = Mathf.Clamp(0.045f * distance, 0.035f, 0.24f);
            _popKind[p] = kind;
            _pops[p].transform.localScale = Vector3.zero;
            _pops[p].gameObject.SetActive(true);
            if (_popCards[p] != null)
            {
                _popCards[p].transform.localScale = Vector3.zero;
                _popCards[p].gameObject.SetActive(true);
            }
        }

        void AnimateRelics(float dt)
        {
            for (int i = 0; i < _relicCount; i++)
            {
                if (_relicPunch[i] <= 0f) continue;
                _relicPunch[i] = Mathf.Max(0f, _relicPunch[i] - dt * 1.6f);
                float k = _relicPunch[i];
                _relics[i].transform.localScale = Vector3.one * (_relicSize * (1f + 0.9f * Punch(k) + 0.25f * k));
                // Bright even while the readout is stepped back for the shot: this is the moment it is about.
                SetGlyph(_relics[i], _relicKinds[i], Color.Lerp(Upgrades.RelicColor, Color.white, 0.45f * k),
                         (1.3f + 3f * k) * Mathf.Max(_dim, k));
            }

            // The pops belong to the moment in the room, not to the readout: game time, so they linger in slow motion.
            Vector3 eye = _playArea.Head != null ? _playArea.Head.position : _playArea.Seat.position;
            for (int i = 0; i < PopCount; i++)
            {
                if (_popAge[i] >= PopSeconds) continue;
                _popAge[i] += Time.deltaTime;
                float t = _popAge[i] / PopSeconds;
                if (t >= 1f)
                {
                    _pops[i].gameObject.SetActive(false);
                    if (_popCards[i] != null) _popCards[i].gameObject.SetActive(false);
                    continue;
                }
                // Pops out with an overshoot, drifts up a little, fades. It stands beside the spot, a little toward the
                // eye (out of the wall), clear of the score popup that rises straight up from the same crystal.
                Vector3 toEye = (eye - _popAt[i]).normalized;
                Vector3 side = Vector3.Cross(Vector3.up, toEye).normalized;
                Vector3 pos = _popAt[i] + toEye * 0.15f + side * (_popSize[i] * 1.9f) + Vector3.up * (_popSize[i] * (0.4f + 0.4f * t));
                var facing = Quaternion.LookRotation(pos - eye, Vector3.up);
                _pops[i].transform.SetPositionAndRotation(pos, facing);
                float grow = OutBack(Mathf.Clamp01(t * 4f));
                _pops[i].transform.localScale = Vector3.one * (_popSize[i] * grow);
                float fade = 1f - Mathf.SmoothStep(0f, 1f, (t - 0.55f) / 0.45f);
                SetGlyph(_pops[i], _popKind[i], Upgrades.RelicColor, 2.6f * fade);
                if (_popCards[i] == null) continue;
                _popCards[i].transform.SetPositionAndRotation(pos - toEye * 0.004f, facing);
                _popCards[i].transform.localScale = Vector3.one * (_popSize[i] * 1.9f * grow);
                _block.Clear();
                _block.SetFloat(IntensityId, 0.7f * fade);
                _popCards[i].SetPropertyBlock(_block);
            }
        }

        static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }

        MeshRenderer GlyphQuad(string name, Transform parent, Vector3 localPos, float size)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = RewardPicker.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _glyphMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            return r;
        }

        void RefreshPips()
        {
            if (_block == null || _encounter == null || _encounter.Run == null) return;
            int now = _encounter.Encounter;
            _pipNowIndex = now;
            _pipDim = _dim;
            for (int i = 0; i < _pips.Length; i++)
            {
                bool boss = i == _pips.Length - 1;
                float kind = boss ? Upgrades.GlyphCrown : i < now ? Upgrades.GlyphPip + 1f : Upgrades.GlyphPip;
                Color c = i < now ? _pipDone : i == now ? _pipNow : _pipAhead;
                SetGlyph(_pips[i], kind, c, (i < now ? 1.4f : i == now ? 1.6f : 0.7f) * _dim);
            }

            int tier = _encounter.Run.Ascension;
            for (int i = 0; i < _ascension.Length; i++)
            {
                bool show = i < tier;
                _ascension[i].gameObject.SetActive(show);
                if (show) SetGlyph(_ascension[i], Upgrades.GlyphAscension, AscensionColor, 1.4f * _dim);
            }

            // Owned relics, in bit order (the order of the glyph kinds).
            int owned = _encounter.Run.Relics;
            _relicCount = 0;
            for (int bit = 0; bit < _relics.Length; bit++)
                if ((owned & (1 << bit)) != 0) _relicKinds[_relicCount++] = 13f + bit;
            for (int i = 0; i < _relics.Length; i++)
            {
                bool show = i < _relicCount;
                _relics[i].gameObject.SetActive(show);
                if (show) SetGlyph(_relics[i], _relicKinds[i], Upgrades.RelicColor, 1.3f * _dim);
            }
        }

        void SetGlyph(MeshRenderer r, float kind, Color color, float intensity)
        {
            _block.Clear();
            _block.SetFloat(KindId, kind);
            _block.SetColor(ColorId, color);
            _block.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(_block);
        }

        void OnEnable()
        {
            _playArea.Ready += OnReady;
            _playArea.Reseated += Layout;
            _director.CrystalLit += OnCrystalLit;
            _director.ShotScored += OnShotScored;
            _director.BoardGenerated += ShowRemaining;
            _director.RelicTriggered += OnRelicTriggered;
            if (_encounter != null)
            {
                _encounter.ShieldChanged += OnShieldChanged;
                _encounter.RunChanged += RefreshPips;
            }
        }

        void OnDisable()
        {
            _playArea.Ready -= OnReady;
            _playArea.Reseated -= Layout;
            _director.CrystalLit -= OnCrystalLit;
            _director.ShotScored -= OnShotScored;
            _director.BoardGenerated -= ShowRemaining;
            _director.RelicTriggered -= OnRelicTriggered;
            if (_encounter != null)
            {
                _encounter.ShieldChanged -= OnShieldChanged;
                _encounter.RunChanged -= RefreshPips;
            }
        }

        void OnShieldChanged(int value, int delta)
        {
            _shield.SetText("{0}", value);
            _shieldPunch = 1f;
            _shieldFlash = delta < 0 ? _hurtColor : Color.white;
        }

        TextMeshPro MakeText(string name, Vector3 localPos, float scale, TextAlignmentOptions align, float pivotX, bool display)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            var text = go.AddComponent<TextMeshPro>();
            text.fontStyle = FontStyles.Bold;
            UiFonts.Use(text, display); // numbers in the display face, labels in the body face
            text.fontSize = 10f;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.rectTransform.pivot = new Vector2(pivotX, 0.5f); // the edge nearest the sling anchors
            text.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(20, 10, 40, 255);
            return text;
        }

        void Layout()
        {
            Pose seat = _playArea.Seat;
            Vector3 right = Vector3.Cross(Vector3.up, seat.forward).normalized;
            Vector3 pos = seat.position + seat.forward * _forward - right * _left + Vector3.down * _down;
            // Face the eye, upright: a readout you glance down at, like a watch on the table.
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - seat.position, Vector3.up));
        }

        void OnReady()
        {
            Layout();

            _shown = _director.Score;
            _shownInt = -1;
            _score.gameObject.SetActive(true);
            _line.gameObject.SetActive(true);
            if (_encounter != null)
            {
                _shield.gameObject.SetActive(true);
                _shieldLabel.gameObject.SetActive(true);
                _shield.SetText("{0}", _encounter.Shield);
                _shieldFlash = _shieldColor;
                if (_block != null)
                {
                    _pips[0].transform.parent.gameObject.SetActive(true);
                    RefreshPips();
                }
            }
            ShowRemaining();
        }

        void OnCrystalLit(Crystal crystal, int combo)
        {
            int chain = combo + 1;
            if (chain < 2) return; // a single hit is not a chain yet
            _line.SetText("×{0} chain", chain);
            _line.color = Color.Lerp(_lineColor, _hotColor, Mathf.Clamp01(combo / (float)_comboForHot));
            _linePunch = 1f;
        }

        void OnShotScored(int crystals, int points)
        {
            _rollSpeed = Mathf.Max(1f, (_director.Score - _shown) / _rollSeconds);
            ShowRemaining();
        }

        void ShowRemaining()
        {
            int left = _board.RemainingCount();
            _line.SetText("{0} left", left); // short: the "crystals left" line sat out at -28 deg (TECH_GUIDE section 7)
            _line.color = _lineColor;
        }

        bool _seenSpark;

        void LateUpdate()
        {
            if (!_score.gameObject.activeSelf) return;
            float dt = RealTime.DeltaTime; // the readout is UI: it ignores slow motion

            if (_shown > _director.Score) _shown = _director.Score; // a new run
            if (_shown < _director.Score && !_director.ShotInProgress)
            {
                // Keep up with points that arrive between shots too (the Fever bonus).
                _rollSpeed = Mathf.Max(_rollSpeed, (_director.Score - _shown) / _rollSeconds);
                _shown = Mathf.MoveTowards(_shown, _director.Score, _rollSpeed * dt);
                _scorePunch = Mathf.Max(_scorePunch, 0.5f);
            }
            int shownInt = Mathf.RoundToInt(_shown);
            if (shownInt != _shownInt)
            {
                _shownInt = shownInt;
                _score.SetText("{0}", shownInt);
            }

            _scorePunch = Mathf.Max(0f, _scorePunch - dt * 3f);
            _linePunch = Mathf.Max(0f, _linePunch - dt * 4f);
            // While the Spark flies, the score steps back so the room and the chain carry the moment; while a reward
            // is offered it steps aside entirely (the orbs sit in the same band of the view).
            // The cold open stays clean (zero text): no readout until the first Spark is in the sling.
            if (!_seenSpark) _seenSpark = _sling == null || _sling.IsReady || _sling.IsPulling;
            float dimTarget = !_seenSpark || (_picker != null && _picker.IsChoosing) ? 0f : _director.ShotInProgress ? 0.55f : 1f;
            _dim = Mathf.Lerp(_dim, dimTarget, 1f - Mathf.Exp(-(dimTarget < _dim ? 10f : 6f) * dt));
            Color lineC = _line.color;
            lineC.a = _dim;
            _line.color = lineC;

            _score.transform.localScale = Vector3.one * (_scoreScale * (1f + 0.12f * _scorePunch));
            _line.transform.localScale = Vector3.one * (_lineScale * (1f + 0.35f * Punch(_linePunch)));
            Color sc = Color.Lerp(_scoreColor, _hotColor, _scorePunch * 0.8f);
            sc.a = _dim;
            _score.color = sc;

            if (_shield.gameObject.activeSelf)
            {
                _shieldPunch = Mathf.Max(0f, _shieldPunch - dt * 2.5f);
                _shield.transform.localScale = Vector3.one * (_scoreScale * (1f + 0.3f * Punch(_shieldPunch)));
                Color c = Color.Lerp(_shieldColor, _shieldFlash, _shieldPunch);
                c.a = _dim;
                _shield.color = c;
                Color lc = _shieldColor * 0.8f;
                lc.a = _dim;
                _shieldLabel.color = lc;
                // The current encounter's pip breathes.
                int now = _pipNowIndex;
                if (_block != null && now >= 0 && now < _pips.Length)
                {
                    bool boss = now == _pips.Length - 1;
                    SetGlyph(_pips[now], boss ? Upgrades.GlyphCrown : Upgrades.GlyphPip, _pipNow,
                             (1.3f + 0.5f * Mathf.Sin(RealTime.Now * 3f)) * _dim);
                }
                if (Mathf.Abs(_dim - _pipDim) > 0.02f) RefreshPips(); // fade the rest of the row and the relics too
            }
            if (_block != null) AnimateRelics(dt);
        }

        // Fast overshoot that settles: sin bump weighted toward the start.
        static float Punch(float k) => Mathf.Sin(k * Mathf.PI) * k;
    }
}
