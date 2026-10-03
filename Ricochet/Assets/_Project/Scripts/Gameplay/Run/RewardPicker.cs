using System.Collections;
using System.Collections.Generic;
using Ricochet.Audio;
using Ricochet.Input;
using Ricochet.Room;
using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Pick 1 of 3 (CONCEPT section 3) with the game's one gesture (TECH_GUIDE section 6): three orbs of light float up
    /// from the sling on an arc at arm's reach. Pinch one, draw it toward you and let go to take it; let go early and
    /// it springs back. Each orb shows its glyph, its name above and one short line below. Hands, controllers and the
    /// desktop mouse all work through the sling's pinch inputs (the mouse, with no real reach, picks the orb nearest
    /// the gaze). Everything is built once and reused; no allocations while choosing.
    /// </summary>
    public sealed class RewardPicker : MonoBehaviour
    {
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int CoreColorId = Shader.PropertyToID("_CoreColor");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] PlayArea _playArea;
        [SerializeField] Sling _sling;
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] ShatterFx _fx;
        [SerializeField] Mesh _orbMesh;
        [SerializeField] Material _orbMaterial;
        [SerializeField] Material _haloMaterial;
        [SerializeField] Material _glyphMaterial;
        [SerializeField] Material _scrimMaterial;

        [Header("Layout (relative to the seated eye)")]
        [SerializeField] float _distance = 0.42f;     // the sling's reach
        [SerializeField] float _drop = 0.08f;         // below eye level, above the heart (TECH_GUIDE section 6)
        [SerializeField] float _spreadDeg = 22f;      // inside the +/-25 degree band (TECH_GUIDE section 7)
        [SerializeField] float _orbRadius = 0.03f;
        [SerializeField] Vector2 _cardSize = new(0.125f, 0.17f); // the dark card behind each orb's words
        [SerializeField] float _cardOpacity = 0.62f;

        [Header("Gesture")]
        [SerializeField] float _grabRadius = 0.07f;
        [SerializeField] float _hoverRadius = 0.14f;
        [SerializeField] float _takeDistance = 0.06f; // draw it this far out of its slot, then let go
        [SerializeField] float _maxDraw = 0.12f;
        [SerializeField] float _gazePickDeg = 12f;    // mouse (no reach): the orb nearest the gaze

        /// <summary>Automation: when >= 0, the next offer picks this orb by itself after a short beat.</summary>
        public static int AutoPick = -1;

        /// <summary>Index into the offer of the reward taken.</summary>
        public int Chosen { get; private set; } = -1;
        public bool IsChoosing => _state != State.Hidden;

        enum State { Hidden, Appearing, Open, Taking }

        sealed class Orb
        {
            public Transform Root, Core, Halo, Glyph, Card;
            public MeshRenderer CoreR, HaloR, GlyphR, CardR;
            public TextMeshPro Name, Blurb;
            public Vector3 Home;
            public Vector3 Offset;       // drawn out of the slot by the hand
            public float Appear, Hover, Charge, Fade = 1f;
            public Color Color;
            public float GlyphKind;
            public bool Ready;           // drawn far enough to take
            public int Tick;
        }

        readonly Orb[] _orbs = new Orb[3];
        readonly List<bool> _wasPinching = new();
        MaterialPropertyBlock _block;
        State _state = State.Hidden;
        int _count;
        int _held = -1;
        IPinchInput _holder;
        Vector3 _grabPoint;
        float _clock, _takeClock, _autoClock;
        Vector3 _takeFrom;
        bool _landed;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            for (int i = 0; i < _orbs.Length; i++) _orbs[i] = Build(i);
            SetVisible(false);
        }

        Orb Build(int index)
        {
            var o = new Orb();
            o.Root = new GameObject("Orb" + index).transform;
            o.Root.SetParent(transform, false);
            o.Core = Part("Core", o.Root, _orbMesh, _orbMaterial, out o.CoreR);
            o.Halo = Part("Halo", o.Root, Quad(), _haloMaterial, out o.HaloR);
            o.Glyph = Part("Glyph", o.Root, Quad(), _glyphMaterial, out o.GlyphR);
            if (_scrimMaterial != null)
            {
                // The card hangs in the slot (not on the orb), like the words, so a drawn orb leaves it behind.
                o.Card = Part("Card", o.Root, Quad(), _scrimMaterial, out o.CardR);
                o.Card.SetParent(transform, false);
            }
            o.Name = Text("Name", o.Root, 0.0185f, FontStyles.Bold);
            o.Blurb = Text("Blurb", o.Root, 0.0135f, FontStyles.Normal);
            return o;
        }

        static Mesh s_quad;
        /// <summary>A unit quad facing -z (the glyph and halo card), shared by the run UI.</summary>
        public static Mesh Quad()
        {
            if (s_quad != null) return s_quad;
            s_quad = new Mesh { name = "RewardQuad" };
            s_quad.SetVertices(new[] { new Vector3(-0.5f, -0.5f), new Vector3(0.5f, -0.5f), new Vector3(-0.5f, 0.5f), new Vector3(0.5f, 0.5f) });
            s_quad.SetUVs(0, new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(0, 1), new Vector2(1, 1) });
            s_quad.SetTriangles(new[] { 0, 2, 1, 2, 3, 1 }, 0);
            s_quad.RecalculateBounds();
            return s_quad;
        }

        static Transform Part(string name, Transform parent, Mesh mesh, Material material, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        static TextMeshPro Text(string name, Transform parent, float scale, FontStyles style)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * scale;
            var text = go.AddComponent<TextMeshPro>();
            text.fontSize = 10f;
            text.alignment = TextAlignmentOptions.Center;
            text.textWrappingMode = TextWrappingModes.NoWrap;
            text.fontStyle = style;
            text.rectTransform.sizeDelta = new Vector2(12f, 3f);
            text.outlineWidth = 0.2f;
            text.outlineColor = new Color32(16, 8, 32, 255);
            return text;
        }

        void SetVisible(bool on)
        {
            for (int i = 0; i < _orbs.Length; i++)
            {
                bool show = on && i < _count;
                _orbs[i].Root.gameObject.SetActive(show);
                if (_orbs[i].Card != null) _orbs[i].Card.gameObject.SetActive(show);
            }
        }

        /// <summary>Offers the rewards and waits until one is taken (read Chosen afterwards).</summary>
        public IEnumerator Choose(Reward[] offer, int count)
        {
            _count = Mathf.Clamp(count, 1, _orbs.Length);
            Chosen = -1;
            _held = -1;
            _holder = null;
            _clock = 0f;
            _autoClock = 0f;

            Vector3 eye = _playArea.Head.position;
            Vector3 fwd = Vector3.ProjectOnPlane(_playArea.Seat.forward, Vector3.up).normalized;
            for (int i = 0; i < _count; i++)
            {
                var o = _orbs[i];
                var r = offer[i];
                float yaw = (_count == 1 ? 0f : Mathf.Lerp(-_spreadDeg, _spreadDeg, i / (float)(_count - 1)));
                o.Home = eye + Quaternion.AngleAxis(yaw, Vector3.up) * fwd * _distance + Vector3.down * _drop;
                o.Offset = Vector3.zero;
                o.Appear = 0f;
                o.Hover = o.Charge = 0f;
                o.Fade = 1f;
                o.Ready = false;
                o.Tick = 0;
                o.Color = r.Color;
                o.GlyphKind = r.Glyph;
                o.Name.SetText(r.Name);
                o.Blurb.SetText(r.Blurb);
                o.Name.color = Color.Lerp(r.Color, Color.white, 0.35f);
                o.Blurb.color = new Color(0.86f, 0.82f, 0.95f);
                o.Root.SetPositionAndRotation(o.Home, Quaternion.LookRotation(o.Home - eye, Vector3.up));
            }
            // A pinch already held when the orbs appear never grabs one.
            var inputs = _sling.Inputs;
            _wasPinching.Clear();
            for (int i = 0; i < inputs.Count; i++) _wasPinching.Add(inputs[i].IsPinching);
            _state = State.Appearing;
            SetVisible(true);
            _sfx.PlaySwell(_sling.transform.position);

            while (_state != State.Hidden) yield return null;
        }

        void Update()
        {
            if (_state == State.Hidden) return;
            float dt = RealTime.DeltaTime;
            _clock += dt;

            switch (_state)
            {
                case State.Appearing:
                    bool all = true;
                    for (int i = 0; i < _count; i++)
                    {
                        // Staggered: each orb rises from the sling in turn.
                        float t = Mathf.Clamp01((_clock - 0.14f * i) / 0.55f);
                        _orbs[i].Appear = t;
                        all &= t >= 1f;
                    }
                    if (all) _state = State.Open;
                    break;
                case State.Open:
                    UpdateGesture(dt);
                    if (AutoPick >= 0 && (_autoClock += dt) > 0.8f) Take(Mathf.Clamp(AutoPick, 0, _count - 1));
                    break;
                case State.Taking:
                    UpdateTaking(dt);
                    break;
            }
            Render();
        }

        void UpdateGesture(float dt)
        {
            var inputs = _sling.Inputs;
            while (_wasPinching.Count < inputs.Count) _wasPinching.Add(true); // a pinch held from before doesn't grab

            if (_held >= 0)
            {
                var o = _orbs[_held];
                if (!_holder.IsTracked || !_holder.IsPinching)
                {
                    // Let go: taken if drawn far enough (and still tracked), otherwise it springs home.
                    if (o.Ready && _holder.IsTracked) Take(_held);
                    else
                    {
                        _sfx.PlayCancel(o.Root.position);
                        _held = -1;
                        _holder = null;
                    }
                    for (int i = 0; i < inputs.Count; i++) _wasPinching[i] = inputs[i].IsPinching;
                    return;
                }
                Vector3 draw = Vector3.ClampMagnitude(_holder.PinchPoint - _grabPoint, _maxDraw);
                o.Offset = Vector3.Lerp(o.Offset, draw, 1f - Mathf.Exp(-30f * dt));
                o.Charge = Mathf.Clamp01(o.Offset.magnitude / _takeDistance);
                int tick = Mathf.FloorToInt(o.Charge * 4f);
                if (tick > o.Tick) _sfx.PlayPullTick(tick, o.Root.position);
                o.Tick = tick;
                if (!o.Ready && o.Charge >= 1f)
                {
                    o.Ready = true;
                    _sfx.PlayGrab(o.Root.position);
                }
                else if (o.Ready && o.Charge < 0.8f) o.Ready = false;
                return;
            }

            // Hover and grab.
            for (int i = 0; i < _count; i++) _orbs[i].Hover = Mathf.MoveTowards(_orbs[i].Hover, 0f, dt * 4f);
            for (int k = 0; k < inputs.Count; k++)
            {
                var input = inputs[k];
                bool pinching = input.IsPinching;
                bool started = pinching && !_wasPinching[k];
                _wasPinching[k] = pinching;
                if (!input.IsTracked) continue;

                int target;
                float closeness;
                if (float.IsInfinity(input.ReachScale)) target = GazeTarget(out closeness);
                else target = Nearest(input.PinchPoint, out closeness);
                if (target < 0) continue;
                var o = _orbs[target];
                o.Hover = Mathf.Max(o.Hover, closeness);

                bool inReach = float.IsInfinity(input.ReachScale) ||
                               Vector3.Distance(input.PinchPoint, o.Root.position) <= _grabRadius * input.ReachScale;
                if (started && inReach)
                {
                    _held = target;
                    _holder = input;
                    _grabPoint = input.PinchPoint;
                    o.Charge = 0f;
                    o.Tick = 0;
                    o.Ready = false;
                    _sfx.PlayGrab(o.Root.position);
                    return;
                }
            }
        }

        int Nearest(Vector3 point, out float closeness)
        {
            int best = -1;
            float bestD = _hoverRadius;
            for (int i = 0; i < _count; i++)
            {
                float d = Vector3.Distance(point, _orbs[i].Home);
                if (d < bestD) { bestD = d; best = i; }
            }
            closeness = best < 0 ? 0f : 1f - Mathf.InverseLerp(_grabRadius, _hoverRadius, bestD);
            return best;
        }

        int GazeTarget(out float closeness)
        {
            Transform head = _playArea.Head;
            int best = -1;
            float bestA = _gazePickDeg;
            for (int i = 0; i < _count; i++)
            {
                float a = Vector3.Angle(head.forward, _orbs[i].Home - head.position);
                if (a < bestA) { bestA = a; best = i; }
            }
            closeness = best < 0 ? 0f : 1f;
            return best;
        }

        void Take(int index)
        {
            Chosen = index;
            _held = -1;
            _holder = null;
            _state = State.Taking;
            _takeClock = 0f;
            _landed = false;
            var o = _orbs[index];
            _takeFrom = o.Root.position;
            _sfx.PlayChord(_takeFrom);
            _sfx.PlayTwang(_takeFrom, 1f);
            if (_glow != null) _glow.Pulse(_takeFrom, o.Color * 1.8f, 1.2f, 0.6f);
        }

        void UpdateTaking(float dt)
        {
            _takeClock += dt;
            var chosen = _orbs[Chosen];
            // The taken orb flies into the sling (it becomes part of your Spark); the others fade away.
            float t = Mathf.Clamp01(_takeClock / 0.4f);
            float e = t * t * (3f - 2f * t);
            Vector3 target = _sling.transform.position;
            chosen.Offset = Vector3.Lerp(_takeFrom, target, e) - chosen.Home + Vector3.up * (Mathf.Sin(e * Mathf.PI) * 0.05f);
            for (int i = 0; i < _count; i++)
                if (i != Chosen) _orbs[i].Fade = Mathf.Max(0f, 1f - _takeClock / 0.3f);
            chosen.Fade = t < 1f ? 1f : 0f;
            if (t >= 1f && !_landed)
            {
                _landed = true;
                if (_fx != null) { _fx.Burst(target, chosen.Color); _fx.Burst(target, Color.white); }
                if (_glow != null) _glow.Pulse(target, chosen.Color * 2.2f, 1.6f, 0.8f);
                _sfx.PlaySwell(target);
            }
            if (_takeClock > 0.75f)
            {
                _state = State.Hidden;
                SetVisible(false);
            }
        }

        void Render()
        {
            Vector3 eye = _playArea.Head.position;
            float time = _clock;
            for (int i = 0; i < _count; i++)
            {
                var o = _orbs[i];
                float appear = OutBack(o.Appear);
                float grow = appear * o.Fade;
                // Rise out of the sling into the slot, with a gentle bob once there.
                Vector3 rise = Vector3.Lerp(_sling.transform.position, o.Home, Mathf.SmoothStep(0f, 1f, o.Appear));
                Vector3 bob = Vector3.up * (Mathf.Sin(time * 1.9f + i * 2.1f) * 0.004f * o.Appear);
                Vector3 pos = rise + o.Offset + bob;
                o.Root.SetPositionAndRotation(pos, Quaternion.LookRotation(pos - eye, Vector3.up));

                bool held = i == _held;
                float pulse = o.Ready ? 1f + 0.12f * Mathf.Sin(time * 18f) : 1f;
                float size = _orbRadius * 2f * grow * (1f + 0.18f * o.Hover + 0.25f * o.Charge) * pulse;
                o.Core.localScale = Vector3.one * size;
                o.Halo.localScale = Vector3.one * size * (3.2f + 1.5f * o.Charge + 0.8f * o.Hover);
                o.Glyph.localPosition = new Vector3(0f, 0f, -size * 0.62f); // just in front of the orb, toward you
                o.Glyph.localScale = Vector3.one * size * 0.85f;

                float lit = 0.8f + 0.5f * o.Hover + 1.2f * o.Charge + (held ? 0.2f : 0f);
                _block.Clear();
                _block.SetColor(RimColorId, o.Color * lit);
                _block.SetColor(CoreColorId, Color.Lerp(o.Color * 0.25f, Color.white, 0.25f * o.Charge));
                o.CoreR.SetPropertyBlock(_block);
                _block.Clear();
                _block.SetColor(ColorId, o.Color);
                _block.SetFloat(IntensityId, (0.55f + 0.6f * o.Hover + 1.1f * o.Charge) * o.Fade);
                o.HaloR.SetPropertyBlock(_block);
                _block.Clear();
                _block.SetFloat(KindId, o.GlyphKind);
                _block.SetColor(ColorId, Color.Lerp(o.Color, Color.white, 0.45f + 0.4f * o.Charge));
                _block.SetFloat(IntensityId, (1.2f + 0.6f * o.Hover + 0.8f * o.Charge) * o.Fade);
                o.GlyphR.SetPropertyBlock(_block);

                // The words stay put while the orb is drawn out, so they never swing through your hand.
                float textAlpha = Mathf.Clamp01(o.Appear * 1.5f - 0.5f) * (i == Chosen ? 1f - Mathf.Clamp01(_takeClock * 4f) : o.Fade);
                o.Name.transform.position = o.Home + bob + Vector3.up * (_orbRadius + 0.024f);
                o.Blurb.transform.position = o.Home + bob + Vector3.down * (_orbRadius + 0.03f);
                o.Name.transform.rotation = o.Blurb.transform.rotation = Quaternion.LookRotation(o.Home - eye, Vector3.up);
                o.Name.alpha = textAlpha * (0.9f + 0.1f * o.Hover);
                o.Blurb.alpha = textAlpha * (0.75f + 0.25f * o.Hover);

                if (o.CardR != null)
                {
                    // Centered on the words' span, a little behind the orb, so the orb and its glow sit on the card.
                    Vector3 away = (o.Home - eye).normalized;
                    o.Card.SetPositionAndRotation(o.Home + bob + Vector3.down * 0.012f + away * 0.02f, o.Name.transform.rotation);
                    o.Card.localScale = new Vector3(_cardSize.x, _cardSize.y, 1f) * (1f + 0.04f * o.Hover);
                    _block.Clear();
                    _block.SetFloat(IntensityId, textAlpha * _cardOpacity * (0.85f + 0.15f * o.Hover));
                    o.CardR.SetPropertyBlock(_block);
                }
            }
        }

        static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
