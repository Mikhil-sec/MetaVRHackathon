using TMPro;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// The void creature that comes through the rift (CONCEPT sections 3 and 6): an ink-and-light teardrop with
    /// glowing eyes and orbiting shards, an HP bar and its next move shown in advance. All motion is procedural
    /// and eased; it tints itself with the color of its intent so the telegraph reads without text.
    /// Visual children are built once in Awake from the serialized meshes and materials.
    /// </summary>
    public sealed class Creature : MonoBehaviour
    {
        static readonly int TintId = Shader.PropertyToID("_Tint");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int EnergyId = Shader.PropertyToID("_Energy");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RimColorId = Shader.PropertyToID("_RimColor");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int HurtId = Shader.PropertyToID("_Hurt");
        static readonly int CrackId = Shader.PropertyToID("_Crack");
        static readonly int DissolveId = Shader.PropertyToID("_Dissolve");

        [SerializeField] Mesh _bodyMesh;
        [SerializeField] Mesh _shardMesh;
        [SerializeField] Material _bodyMaterial;
        [SerializeField] Material _eyeMaterial;
        [SerializeField] Material _barMaterial;
        [SerializeField] Material _tendrilMaterial;
        [SerializeField] Material _glyphMaterial;
        [SerializeField] Color _armorColor = new(0.35f, 0.8f, 1f);
        [SerializeField] Color _hpColor = new(1f, 0.25f, 0.6f);
        [SerializeField] Color _chipColor = new(1f, 0.95f, 0.9f);
        [SerializeField] Color _barBackColor = new(0.06f, 0.02f, 0.08f);
        [SerializeField] float _seizeSeconds = 0.3f;      // death: it trembles, cracks blazing, before it breaks
        [SerializeField] float _bossSeizeSeconds = 0.45f;
        [SerializeField] float _breakSeconds = 0.45f;     // then it falls apart along its cracks
        [SerializeField] float _retreatSeconds = 0.9f;    // after a defeat it slips back into the rift

        Transform _body, _hud, _tendrils;
        MeshFilter _bodyFilter;
        const int MaxFeatures = 4;
        readonly Transform[] _features = new Transform[MaxFeatures];        // eyes and mouth, per CreatureDef.Face
        readonly MeshRenderer[] _featureRenderers = new MeshRenderer[MaxFeatures];
        readonly Vector3[] _featureHome = new Vector3[MaxFeatures];           // body-local, from the silhouette
        readonly Color[] _featureColor = new Color[MaxFeatures];
        Feature[] _face = Feature.TwoEyes;
        MaterialPropertyBlock _eyeBlock;
        readonly Transform[] _shards = new Transform[6]; // 3 orbit a creature; the boss stands all 6 up as a crown
        MeshRenderer _bodyRenderer;
        readonly MeshRenderer[] _shardRenderers = new MeshRenderer[6];
        bool _boss;
        Color _identity;
        Transform _fill, _chip;
        MeshRenderer _fillRenderer, _chipRenderer, _backRenderer;
        MeshRenderer _tendrilRenderer, _intentGlyph, _armorGlyph;
        MeshFilter _tendrilFilter;
        Transform _intentGlyphT;
        float _intentPop;
        TextMeshPro _intentText, _armorText;
        MaterialPropertyBlock _block;

        Vector3 _home, _from;
        Transform _viewer;
        float _size = 0.28f;
        float _distanceScale = 1f;
        float _emerge = 1f, _dying = -1f;
        float _flash, _punch, _recoil, _windup, _lunge;
        float _hpShown = 1f, _chipShown = 1f, _chipHold;
        float _blinkTimer = 2f, _blink;
        float _hurtShown;                    // cracks open (0 fresh .. 1 at 0 HP), easing after the HP
        float _jolt, _joltVel, _joltSide;    // damped-spring tilt from hits
        float _sputter, _sputterTimer = 1f;  // a badly hurt creature flickers now and then
        float _retreat = -1f;
        Color _tint = new(1f, 0.2f, 0.75f);

        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public int Armor { get; private set; }
        public bool Alive => Hp > 0 && _dying < 0f && _retreat < 0f;
        /// <summary>Dying, past the seizure: it is breaking apart now (the moment for the shatter burst).</summary>
        public bool Shattered { get; private set; }
        public bool Dying => _dying >= 0f;
        public Intent NextIntent { get; private set; }
        /// <summary>Damage needed to finish it this turn (HP plus armor).</summary>
        public int EffectiveHp => Hp + Armor;
        public Vector3 Center => _body != null ? _body.position : transform.position;
        /// <summary>Its drawn size (metres, distance scale included).</summary>
        public float Size => _size;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _body = Child("Body", _bodyMesh, _bodyMaterial, out _bodyRenderer);
            _bodyFilter = _body.GetComponent<MeshFilter>();
            _eyeBlock = new MaterialPropertyBlock();
            // Tendrils ride the body's transform, so they squash, swell and wobble with it.
            var tendrils = Child("Tendrils", null, _tendrilMaterial, out _tendrilRenderer);
            tendrils.SetParent(_body, false);
            _tendrils = tendrils;
            _tendrilFilter = tendrils.GetComponent<MeshFilter>();
            for (int i = 0; i < _shards.Length; i++) _shards[i] = Child("Shard" + i, _shardMesh, _bodyMaterial, out _shardRenderers[i]);
            for (int i = 0; i < MaxFeatures; i++) _features[i] = Quad("Feature" + i, _eyeMaterial, transform, out _featureRenderers[i]);

            _hud = new GameObject("Hud").transform;
            _hud.SetParent(transform, false);
            _hud.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads and text face -z; the creature's +z faces the viewer
            Quad("Back", _barMaterial, _hud, out _backRenderer).localScale = new Vector3(0.3f, 0.03f, 1f);
            _chip = Quad("Chip", _barMaterial, _hud, out _chipRenderer);
            _fill = Quad("Fill", _barMaterial, _hud, out _fillRenderer);
            SetColor(_backRenderer, _barBackColor);
            SetColor(_chipRenderer, _chipColor);
            SetColor(_fillRenderer, _hpColor);
            // Intent: an icon plus the number (zero text). Verified in a capture: HUD-local -x is the viewer's left,
            // so the glyph sits left of centre and its number to the right.
            _intentGlyphT = Quad("IntentGlyph", _glyphMaterial, _hud, out _intentGlyph);
            _intentGlyphT.localPosition = new Vector3(-0.05f, 0.09f, 0f);
            _intentText = Text("Intent", new Vector3(0.055f, 0.09f, 0f), 0.095f);
            var armorGlyph = Quad("ArmorGlyph", _glyphMaterial, _hud, out _armorGlyph);
            armorGlyph.localPosition = new Vector3(-0.03f, -0.045f, 0f);
            armorGlyph.localScale = Vector3.one * 0.035f;
            _armorText = Text("Armor", new Vector3(0.02f, -0.045f, 0f), 0.04f);
            SetGlyph(_armorGlyph, 1f, _armorColor, 1.2f);
            gameObject.SetActive(false);
        }

        Transform Child(string name, Mesh mesh, Material mat, out MeshRenderer renderer)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        void SetGlyph(Renderer r, float kind, Color color, float intensity)
        {
            _block.Clear();
            _block.SetFloat(KindId, kind);
            _block.SetColor(ColorId, color);
            _block.SetFloat(IntensityId, intensity);
            r.SetPropertyBlock(_block);
        }

        static Transform Quad(string name, Material mat, Transform parent, out MeshRenderer renderer)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            renderer = go.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = mat;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go.transform;
        }

        TextMeshPro Text(string name, Vector3 localPos, float scale)
        {
            var go = new GameObject(name);
            go.transform.SetParent(_hud, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = Vector3.one * scale;
            var t = go.AddComponent<TextMeshPro>();
            t.fontStyle = FontStyles.Bold;
            UiFonts.Use(t, true);
            t.fontSize = 10f;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.rectTransform.sizeDelta = new Vector2(8f, 1.2f);
            t.outlineWidth = 0.22f;
            t.outlineColor = new Color32(20, 10, 40, 255);
            return t;
        }

        void SetColor(Renderer r, Color c)
        {
            r.GetPropertyBlock(_block);
            _block.SetColor(BaseColorId, c);
            r.SetPropertyBlock(_block);
        }

        /// <summary>How much bigger a creature is drawn at this distance from the viewer (1x at 2.6 m, up to 2.4x).</summary>
        public static float DistanceScale(float distance) => Mathf.Clamp(distance / 2.6f, 1f, 2.4f);

        /// <summary>Comes out of the rift mouth and settles, facing the viewer.</summary>
        public void Emerge(CreatureDef def, Vector3 riftPosition, Vector3 home, Transform viewer)
        {
            MaxHp = Hp = def.Hp;
            Armor = 0;
            // Keep its presence (~5-6 degrees) and its telegraph readable on far walls: grow with distance.
            float distance = viewer != null ? Vector3.Distance(viewer.position, home) : 2.6f;
            _distanceScale = DistanceScale(distance);
            _size = def.Size * _distanceScale;
            _tendrilFilter.sharedMesh = EncounterMeshes.Tendrils(def.Tendrils, def.TendrilLength, def.TendrilWidth, def.Seed);
            _bodyFilter.sharedMesh = EncounterMeshes.Creature(def.Body);
            _face = def.Face ?? Feature.TwoEyes;
            for (int i = 0; i < MaxFeatures; i++)
            {
                bool on = i < _face.Length;
                _featureRenderers[i].enabled = on;
                if (!on) continue;
                // Just proud of the surface along the feature's direction, so the body never hides it.
                Vector3 dir = _face[i].Direction;
                _featureHome[i] = EncounterMeshes.SurfacePoint(dir, def.Body) + dir * 0.09f;
                _featureColor[i] = _face[i].Mouth ? def.MouthColor : def.EyeColor;
                _eyeBlock.Clear();
                _eyeBlock.SetColor(ColorId, _featureColor[i]);
                _featureRenderers[i].SetPropertyBlock(_eyeBlock);
            }
            _boss = def.Boss;
            _identity = def.Identity;
            for (int i = 3; i < _shardRenderers.Length; i++) _shardRenderers[i].enabled = _boss;
            _from = riftPosition;
            _home = home;
            _viewer = viewer;
            _emerge = 0f;
            _dying = _retreat = -1f;
            Shattered = false;
            _flash = _punch = _recoil = _windup = _lunge = 0f;
            _hurtShown = _jolt = _joltVel = _sputter = 0f;
            _hpShown = _chipShown = 1f;
            transform.position = riftPosition;
            gameObject.SetActive(true);
            _armorText.gameObject.SetActive(false);
            _armorGlyph.enabled = false;
        }

        public void ShowIntent(Intent intent)
        {
            NextIntent = intent;
            _tint = intent.Color;
            _intentText.color = intent.Color;
            _intentText.SetText("{0}", intent.Amount);
            float kind = intent.Kind switch { IntentKind.Attack => 0f, IntentKind.Guard => 1f, _ => 2f };
            SetGlyph(_intentGlyph, kind, intent.Color, 1.5f);
            _intentPop = 1f; // a new intent pops in so the change is noticed
        }

        /// <summary>Armor soaks damage first. Returns the damage that reached HP.</summary>
        public int TakeDamage(int amount)
        {
            if (!Alive || amount <= 0) return 0;
            int soaked = Mathf.Min(Armor, amount);
            Armor -= soaked;
            int dealt = Mathf.Min(Hp, amount - soaked);
            Hp -= dealt;
            _flash = 1f;
            _punch = 1f;
            _recoil = Mathf.Min(1f, 0.4f + 0.12f * amount);
            _chipHold = 0.35f;
            // It rocks back on a spring and winces; bigger hits rock it harder.
            _joltVel += Mathf.Min(1.6f, 0.55f + 0.12f * amount) * 9f;
            _joltSide = Random.Range(-1f, 1f);
            _blink = 1f;
            RefreshArmor();
            return dealt;
        }

        /// <summary>A resumed fight: set HP and armor straight away (right after Emerge), with the bar already there.</summary>
        public void Restore(int hp, int armor)
        {
            Hp = Mathf.Clamp(hp, 1, MaxHp);
            Armor = Mathf.Max(0, armor);
            _hpShown = _chipShown = (float)Hp / MaxHp;
            _hurtShown = 1f - _hpShown;
            RefreshArmor();
        }

        public void AddArmor(int amount)
        {
            Armor += amount;
            _punch = 0.6f;
            RefreshArmor();
        }

        void RefreshArmor()
        {
            _armorText.gameObject.SetActive(Armor > 0);
            _armorGlyph.enabled = Armor > 0;
            _armorText.color = _armorColor;
            _armorText.SetText("{0}", Armor);
        }

        /// <summary>Anticipation before a move: it draws back and burns brighter (0..1, eased by the caller's timing).</summary>
        public void SetWindup(float amount) => _windup = Mathf.Clamp01(amount);

        /// <summary>The strike itself: a short lunge toward the viewer that springs back.</summary>
        public void Lunge()
        {
            _windup = 0f;
            _lunge = 1f;
        }

        /// <summary>Death: a short seizure with its cracks blazing, then it breaks apart (<see cref="Shattered"/>).</summary>
        public void Die()
        {
            _dying = 0f;
            Shattered = false;
        }

        /// <summary>It won: it slips back into the rift it came from and the crack takes it.</summary>
        public void Retreat()
        {
            if (_dying < 0f && _retreat < 0f) _retreat = 0f;
        }

        void Update()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);
            float t = Time.time;
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            _punch = Mathf.Max(0f, _punch - dt * 3.5f);
            _recoil = Mathf.Max(0f, _recoil - dt * 3f);
            _lunge = Mathf.Max(0f, _lunge - dt * 2.8f);
            if (_emerge < 1f) _emerge = Mathf.Min(1f, _emerge + dt / 0.8f);

            Vector3 eye = _viewer != null ? _viewer.position : transform.position + Vector3.back;
            Vector3 toViewer = eye - _home;
            Vector3 flatToViewer = Vector3.ProjectOnPlane(toViewer, Vector3.up).normalized;

            // Hurt: the cracks follow the HP lost (they propagate over a moment, not in a frame); hits rock it on a
            // damped spring; below ~35% HP it droops and sputters (the cracks flicker), so "nearly done" reads.
            float hpFraction = MaxHp > 0 ? (float)Hp / MaxHp : 1f;
            _hurtShown = Mathf.MoveTowards(_hurtShown, 1f - hpFraction, dt * 1.5f);
            _joltVel += (-170f * _jolt - 9f * _joltVel) * dt;
            _jolt += _joltVel * dt;
            float wounded = Alive ? Mathf.Clamp01((0.35f - hpFraction) / 0.2f) : 0f;
            _sputter = Mathf.Max(0f, _sputter - dt * 6f);
            _sputterTimer -= dt;
            if (wounded > 0f && _sputterTimer <= 0f)
            {
                _sputter = 1f;
                _sputterTimer = Random.Range(0.5f, 1.4f) / (0.5f + wounded);
            }

            // Death: seize (tremble, cracks blaze, eyes wide), then break apart along the cracks.
            float crack = 0.5f * _sputter * wounded, dissolve = 0f, shake = 0f, swell = 0f, eyeOpen = 1f, fling = 0f;
            float hudShown = 1f, dead = 1f;
            if (_dying >= 0f)
            {
                _dying += dt;
                float seize = _boss ? _bossSeizeSeconds : _seizeSeconds;
                hudShown = Mathf.Clamp01(1f - _dying / 0.25f);
                if (_dying < seize)
                {
                    float k = _dying / seize;
                    crack = k * k;
                    shake = k;
                    swell = 0.1f * k;
                    eyeOpen = 1f + 0.6f * k;
                }
                else
                {
                    if (!Shattered) { Shattered = true; _flash = 0.35f; }
                    float k = Mathf.Clamp01((_dying - seize) / _breakSeconds);
                    crack = 1f;
                    dissolve = k;
                    fling = k;
                    swell = 0.1f + 0.1f * (1f - (1f - k) * (1f - k));
                    eyeOpen = 0f;
                    if (k >= 1f) gameObject.SetActive(false);
                }
            }

            // Position: glide out of the rift, bob, recoil from hits, lunge at the viewer.
            float glide = 1f - Mathf.Pow(1f - _emerge, 3f);
            Vector3 pos = Vector3.Lerp(_from, _home, glide);
            pos += Vector3.up * (0.02f * Mathf.Sin(t * (1.6f - 0.6f * wounded)) - 0.025f * wounded * _distanceScale);
            pos -= flatToViewer * (0.06f * _recoil + 0.05f * _windup);
            pos += toViewer.normalized * (0.14f * Mathf.Sin(_lunge * Mathf.PI));
            if (shake > 0f)
            {
                // A fast tremble in real time, so it buzzes even inside the slow motion of the kill.
                float rt = RealTime.Now;
                Vector3 side = Vector3.Cross(Vector3.up, flatToViewer);
                pos += (side * Mathf.Sin(rt * 71f) + Vector3.up * Mathf.Sin(rt * 53f + 1f)) * (0.012f * shake * _distanceScale);
            }
            if (_retreat >= 0f)
            {
                // Lost: it backs into the rift, shrinking into the crack.
                _retreat += dt / _retreatSeconds;
                float k = Mathf.Clamp01(_retreat);
                pos = Vector3.Lerp(pos, _from, k * k);
                dead = 1f - k * k * k;
                hudShown = 1f - k;
                if (k >= 1f) gameObject.SetActive(false);
            }
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flatToViewer, Vector3.up));

            // Scale: pop out with overshoot, squash on hits, swell on windup and in its death seizure.
            float appear = OutBack(_emerge);
            float squash = 0.25f * Mathf.Sin(_punch * Mathf.PI) * _punch;
            float s = _size * appear * dead * (1f + 0.12f * _windup + swell);
            _body.localScale = new Vector3(s * (1f + squash), s * (1f - squash), s * (1f + squash));
            _tendrils.localScale = Vector3.one * (1f - dissolve); // they wither as the body breaks
            _body.localRotation = Quaternion.Euler(8f * Mathf.Sin(t * 1.1f) - 12f * _windup - 14f * _jolt + 7f * wounded, 0f,
                6f * Mathf.Sin(t * 0.7f) + 10f * _jolt * _joltSide);

            if (_boss)
            {
                // The Queen's crown: six shards stand upright in a slowly turning ring above her, the front ones tallest
                // so it reads as a crown from the seat; the windup lifts and splays it.
                for (int i = 0; i < _shards.Length; i++)
                {
                    float a = t * (0.35f + 1.5f * _windup) + i * Mathf.PI * 2f / _shards.Length;
                    float front = 0.5f + 0.5f * Mathf.Sin(a);               // +z faces the viewer
                    var radial = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                    float lift = 0.46f + 0.02f * Mathf.Sin(t * 1.3f + i) + 0.08f * _windup;
                    _shards[i].localPosition = (radial * 0.27f + Vector3.up * lift) * (s * (1f + 4f * fling));
                    // Each point leans out from the head (and splays wider on the windup).
                    _shards[i].localRotation = Quaternion.AngleAxis(16f + 18f * _windup, Vector3.Cross(Vector3.up, radial));
                    _shards[i].localScale = new Vector3(0.11f, 0.25f + 0.09f * front, 0.11f) * (s * (1f - fling));
                }
            }
            else
            {
                // Shards orbit the crown; they spin up with the windup.
                for (int i = 0; i < 3; i++)
                {
                    float a = t * (1.2f + 3f * _windup) + i * Mathf.PI * 2f / 3f;
                    _shards[i].localPosition = new Vector3(Mathf.Cos(a), 0.9f + 0.1f * Mathf.Sin(a * 2f), Mathf.Sin(a)) * (s * 0.85f * (1f + 4f * fling));
                    _shards[i].localRotation = Quaternion.Euler(30f * Mathf.Sin(a), a * Mathf.Rad2Deg, 25f);
                    _shards[i].localScale = Vector3.one * (s * 0.28f * (1f - fling));
                }
            }

            // Eyes: two small lights on the front face that blink now and then.
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f) { _blink = 1f; _blinkTimer = 2.2f + 2.5f * Random.value; }
            _blink = Mathf.Max(0f, _blink - dt * 7f);
            float eyeK = s * (1f - 0.85f * Mathf.Sin(_blink * Mathf.PI)) * (1f + 0.4f * _windup)
                * eyeOpen * (1f - 0.5f * _sputter * wounded);
            // A mouth doesn't blink: it gapes as the creature winds up and strikes.
            float gape = s * (0.3f + 0.9f * Mathf.Max(_windup, _lunge) + 0.06f * Mathf.Sin(t * 2.3f)) * Mathf.Min(1f, eyeOpen);
            for (int i = 0; i < _face.Length && i < MaxFeatures; i++)
            {
                var feature = _face[i];
                _features[i].localPosition = _featureHome[i] * s;
                float size = feature.Size * (feature.Mouth ? s : eyeK);
                float height = feature.Mouth ? feature.Size * gape : size;
                _features[i].localScale = new Vector3(size * feature.Stretch, height, size);
                // The windup lights the face: eyes and visor burn toward the intent color, a mouth blazes in its own.
                Color fc = feature.Mouth ? _featureColor[i] : Color.Lerp(_featureColor[i], _tint, 0.5f * _windup);
                _eyeBlock.Clear();
                _eyeBlock.SetColor(ColorId, fc * (1f + 1.3f * _windup));
                _featureRenderers[i].SetPropertyBlock(_eyeBlock);
            }

            // Body shader: telegraph tint, white-hot flash on hits. Modest, so the creature's own hue survives its windup
            // (at 2.5x every creature became the same glowing blob in its intent color); the face carries the rest.
            float tintBoost = 1f + 0.6f * _windup;
            _block.Clear();
            _block.SetColor(TintId, _tint * tintBoost);
            _block.SetFloat(FlashId, _flash);
            _block.SetFloat(EnergyId, Mathf.Max(Mathf.Max(_windup, 0.6f * _lunge), crack));
            _block.SetFloat(HurtId, _hurtShown);
            _block.SetFloat(CrackId, crack);
            _block.SetFloat(DissolveId, dissolve);
            if (_identity.a > 0f)
            {
                _block.SetColor(RimColorId, _identity);
                _block.SetColor(GlowId, _identity);
            }
            _bodyRenderer.SetPropertyBlock(_block);
            _tendrilRenderer.SetPropertyBlock(_block);
            // The boss's crown burns pale gold (a standing half-flash), so it reads as light, not as dark horns.
            if (_boss) _block.SetFloat(FlashId, Mathf.Max(_flash, 0.42f));
            _block.SetFloat(HurtId, 0f); // shards stay whole: they fly off as the body breaks
            _block.SetFloat(CrackId, 0f);
            _block.SetFloat(DissolveId, 0f);
            for (int i = 0; i < _shardRenderers.Length; i++) _shardRenderers[i].SetPropertyBlock(_block);

            UpdateHud(dt, s, appear * dead * hudShown);
        }

        void UpdateHud(float dt, float s, float visible)
        {
            _hud.localPosition = new Vector3(0f, s * 1.15f + 0.07f * _distanceScale, 0f);
            _hud.localScale = Vector3.one * (visible * _distanceScale);
            float hp = MaxHp > 0 ? (float)Hp / MaxHp : 0f;
            _hpShown = Mathf.Lerp(_hpShown, hp, 1f - Mathf.Exp(-18f * dt));
            _chipHold -= dt;
            if (_chipHold <= 0f) _chipShown = Mathf.Lerp(_chipShown, _hpShown, 1f - Mathf.Exp(-5f * dt));
            SetBar(_fill, _hpShown, 0.002f);
            SetBar(_chip, _chipShown, 0.001f);

            // The intent glyph pops in on change and breathes with the windup, so the telegraph reads as "about to".
            _intentPop = Mathf.Max(0f, _intentPop - dt * 3f);
            float glyph = 0.11f * (1f + 0.5f * Mathf.Sin(_intentPop * Mathf.PI) + 0.25f * _windup * Mathf.Abs(Mathf.Sin(Time.time * 9f)));
            _intentGlyphT.localScale = Vector3.one * glyph;
        }

        // Left-anchored bar segment, nudged toward the viewer (HUD-local -z) in front of the back plate.
        static void SetBar(Transform bar, float fraction, float depth)
        {
            const float width = 0.29f, height = 0.02f;
            float w = Mathf.Max(0.0001f, width * Mathf.Clamp01(fraction));
            bar.localScale = new Vector3(w, height, 1f);
            bar.localPosition = new Vector3(-width * 0.5f + w * 0.5f, 0f, -depth);
        }

        static float OutBack(float t)
        {
            const float c1 = 1.70158f, c3 = c1 + 1f;
            float u = t - 1f;
            return 1f + c3 * u * u * u + c1 * u * u;
        }
    }
}
