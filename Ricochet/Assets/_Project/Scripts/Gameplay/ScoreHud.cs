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
        [SerializeField] float _left = 0.19f;
        [SerializeField] float _down = 0.12f;

        [Header("Look")]
        [SerializeField] float _scoreScale = 0.056f;   // ~3.4 cm line at ~0.56 m: the number is the hero of the readout
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
        [SerializeField] RewardPicker _picker;          // the HUD steps aside while you choose a reward
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
        readonly MeshRenderer[] _relics = new MeshRenderer[10];
        readonly float[] _relicKinds = new float[10];
        int _relicCount;
        MaterialPropertyBlock _block;
        int _pipNowIndex = -1;
        float _pipDim = 1f;

        TextMeshPro _score, _line, _shield, _shieldLabel;
        float _shown, _rollSpeed;
        int _shownInt = -1;
        float _scorePunch, _linePunch, _shieldPunch;
        Color _shieldFlash;
        float _dim = 1f;

        void Awake()
        {
            _score = MakeText("Score", Vector3.zero, _scoreScale, TextAlignmentOptions.Right, 1f);
            _line = MakeText("Line", new Vector3(0f, -0.042f, 0f), _lineScale, TextAlignmentOptions.Right, 1f);
            // The HUD origin sits one sling-offset left of the sling, so the shield starts one offset to its right.
            _shield = MakeText("Shield", new Vector3(_left * 2f, 0f, 0f), _scoreScale, TextAlignmentOptions.Left, 0f);
            _shieldLabel = MakeText("ShieldLabel", new Vector3(_left * 2f, -0.042f, 0f), _lineScale, TextAlignmentOptions.Left, 0f);
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

            // Relics grow leftward from under the score's right edge (the edge nearest the sling).
            var relics = new GameObject("Relics").transform;
            relics.SetParent(transform, false);
            relics.localPosition = new Vector3(0f, -0.078f, 0f);
            for (int i = 0; i < _relics.Length; i++)
            {
                _relics[i] = GlyphQuad("Relic" + i, relics, new Vector3(-_relicSpacing * i - _relicSize * 0.5f, 0f, 0f), _relicSize);
                _relics[i].gameObject.SetActive(false);
            }
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
            _director.CrystalLit += OnCrystalLit;
            _director.ShotScored += OnShotScored;
            _director.BoardGenerated += ShowRemaining;
            if (_encounter != null)
            {
                _encounter.ShieldChanged += OnShieldChanged;
                _encounter.RunChanged += RefreshPips;
            }
        }

        void OnDisable()
        {
            _playArea.Ready -= OnReady;
            _director.CrystalLit -= OnCrystalLit;
            _director.ShotScored -= OnShotScored;
            _director.BoardGenerated -= ShowRemaining;
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

        TextMeshPro MakeText(string name, Vector3 localPos, float scale, TextAlignmentOptions align, float pivotX)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 10f;
            text.alignment = align;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = FontStyles.Bold;
            text.rectTransform.pivot = new Vector2(pivotX, 0.5f); // the edge nearest the sling anchors
            text.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            text.outlineWidth = 0.22f;
            text.outlineColor = new Color32(20, 10, 40, 255);
            return text;
        }

        void OnReady()
        {
            Pose seat = _playArea.Seat;
            Vector3 right = Vector3.Cross(Vector3.up, seat.forward).normalized;
            Vector3 pos = seat.position + seat.forward * _forward - right * _left + Vector3.down * _down;
            // Face the eye, upright: a readout you glance down at, like a watch on the table.
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - seat.position, Vector3.up));

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
            _line.SetText(left == 1 ? "{0} crystal left" : "{0} crystals left", left);
            _line.color = _lineColor;
        }

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
            float dimTarget = _picker != null && _picker.IsChoosing ? 0f : _director.ShotInProgress ? 0.55f : 1f;
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
        }

        // Fast overshoot that settles: sin bump weighted toward the start.
        static float Punch(float k) => Mathf.Sin(k * Mathf.PI) * k;
    }
}
