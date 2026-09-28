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

        [SerializeField] Mesh _bodyMesh;
        [SerializeField] Mesh _shardMesh;
        [SerializeField] Material _bodyMaterial;
        [SerializeField] Material _eyeMaterial;
        [SerializeField] Material _barMaterial;
        [SerializeField] Color _hpColor = new(1f, 0.25f, 0.6f);
        [SerializeField] Color _chipColor = new(1f, 0.95f, 0.9f);
        [SerializeField] Color _barBackColor = new(0.06f, 0.02f, 0.08f);

        Transform _body, _eyeL, _eyeR, _hud;
        readonly Transform[] _shards = new Transform[3];
        MeshRenderer _bodyRenderer;
        readonly MeshRenderer[] _shardRenderers = new MeshRenderer[3];
        Transform _fill, _chip;
        MeshRenderer _fillRenderer, _chipRenderer, _backRenderer;
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
        Color _tint = new(1f, 0.2f, 0.75f);

        public int Hp { get; private set; }
        public int MaxHp { get; private set; }
        public int Armor { get; private set; }
        public bool Alive => Hp > 0 && _dying < 0f;
        public Intent NextIntent { get; private set; }
        /// <summary>Damage needed to finish it this turn (HP plus armor).</summary>
        public int EffectiveHp => Hp + Armor;
        public Vector3 Center => _body != null ? _body.position : transform.position;

        void Awake()
        {
            _block = new MaterialPropertyBlock();
            _body = Child("Body", _bodyMesh, _bodyMaterial, out _bodyRenderer);
            for (int i = 0; i < _shards.Length; i++) _shards[i] = Child("Shard" + i, _shardMesh, _bodyMaterial, out _shardRenderers[i]);
            _eyeL = Quad("EyeL", _eyeMaterial, transform, out _);
            _eyeR = Quad("EyeR", _eyeMaterial, transform, out _);

            _hud = new GameObject("Hud").transform;
            _hud.SetParent(transform, false);
            _hud.localRotation = Quaternion.Euler(0f, 180f, 0f); // quads and text face -z; the creature's +z faces the viewer
            Quad("Back", _barMaterial, _hud, out _backRenderer).localScale = new Vector3(0.3f, 0.03f, 1f);
            _chip = Quad("Chip", _barMaterial, _hud, out _chipRenderer);
            _fill = Quad("Fill", _barMaterial, _hud, out _fillRenderer);
            SetColor(_backRenderer, _barBackColor);
            SetColor(_chipRenderer, _chipColor);
            SetColor(_fillRenderer, _hpColor);
            _intentText = Text("Intent", new Vector3(0f, 0.06f, 0f), 0.05f);
            _armorText = Text("Armor", new Vector3(0f, -0.045f, 0f), 0.035f);
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
            t.fontSize = 10f;
            t.alignment = TextAlignmentOptions.Center;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.fontStyle = FontStyles.Bold;
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

        /// <summary>Comes out of the rift mouth and settles, facing the viewer.</summary>
        public void Emerge(CreatureDef def, Vector3 riftPosition, Vector3 home, Transform viewer)
        {
            MaxHp = Hp = def.Hp;
            Armor = 0;
            // Keep its presence (~5-6 degrees) and its telegraph readable on far walls: grow with distance.
            float distance = viewer != null ? Vector3.Distance(viewer.position, home) : 2.6f;
            _distanceScale = Mathf.Clamp(distance / 2.6f, 1f, 2f);
            _size = def.Size * _distanceScale;
            _from = riftPosition;
            _home = home;
            _viewer = viewer;
            _emerge = 0f;
            _dying = -1f;
            _flash = _punch = _recoil = _windup = _lunge = 0f;
            _hpShown = _chipShown = 1f;
            transform.position = riftPosition;
            gameObject.SetActive(true);
            _armorText.gameObject.SetActive(false);
        }

        public void ShowIntent(Intent intent)
        {
            NextIntent = intent;
            _tint = intent.Color;
            _intentText.color = intent.Color;
            switch (intent.Kind)
            {
                case IntentKind.Attack: _intentText.SetText("ATTACK {0}", intent.Amount); break;
                case IntentKind.Guard: _intentText.SetText("GUARD {0}", intent.Amount); break;
                default: _intentText.SetText("HEX {0}", intent.Amount); break;
            }
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
            RefreshArmor();
            return dealt;
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
            _armorText.color = new Color(0.35f, 0.8f, 1f);
            _armorText.SetText("ARMOR {0}", Armor);
        }

        /// <summary>Anticipation before a move: it draws back and burns brighter (0..1, eased by the caller's timing).</summary>
        public void SetWindup(float amount) => _windup = Mathf.Clamp01(amount);

        /// <summary>The strike itself: a short lunge toward the viewer that springs back.</summary>
        public void Lunge()
        {
            _windup = 0f;
            _lunge = 1f;
        }

        public void Die() => _dying = 0f;

        void Update()
        {
            float dt = Time.deltaTime;
            float t = Time.time;
            _flash = Mathf.Max(0f, _flash - dt * 4f);
            _punch = Mathf.Max(0f, _punch - dt * 3.5f);
            _recoil = Mathf.Max(0f, _recoil - dt * 3f);
            _lunge = Mathf.Max(0f, _lunge - dt * 2.8f);
            if (_emerge < 1f) _emerge = Mathf.Min(1f, _emerge + dt / 0.8f);

            Vector3 eye = _viewer != null ? _viewer.position : transform.position + Vector3.back;
            Vector3 toViewer = eye - _home;
            Vector3 flatToViewer = Vector3.ProjectOnPlane(toViewer, Vector3.up).normalized;

            // Position: glide out of the rift, bob, recoil from hits, lunge at the viewer.
            float glide = 1f - Mathf.Pow(1f - _emerge, 3f);
            Vector3 pos = Vector3.Lerp(_from, _home, glide);
            pos += Vector3.up * (0.02f * Mathf.Sin(t * 1.6f));
            pos -= flatToViewer * (0.06f * _recoil + 0.05f * _windup);
            pos += toViewer.normalized * (0.14f * Mathf.Sin(_lunge * Mathf.PI));
            transform.SetPositionAndRotation(pos, Quaternion.LookRotation(flatToViewer, Vector3.up));

            // Scale: pop out with overshoot, squash on hits, swell on windup, shrink away on death.
            float appear = OutBack(_emerge);
            float dead = 1f;
            if (_dying >= 0f)
            {
                _dying += dt / 0.7f;
                _flash = Mathf.Max(_flash, 1f - _dying);
                dead = Mathf.Max(0f, 1f - _dying * _dying);
                if (_dying >= 1f) gameObject.SetActive(false);
            }
            float squash = 0.25f * Mathf.Sin(_punch * Mathf.PI) * _punch;
            float s = _size * appear * dead * (1f + 0.12f * _windup);
            _body.localScale = new Vector3(s * (1f + squash), s * (1f - squash), s * (1f + squash));
            _body.localRotation = Quaternion.Euler(8f * Mathf.Sin(t * 1.1f) - 12f * _windup, 0f, 6f * Mathf.Sin(t * 0.7f));

            // Shards orbit the crown; they spin up with the windup.
            for (int i = 0; i < _shards.Length; i++)
            {
                float a = t * (1.2f + 3f * _windup) + i * Mathf.PI * 2f / _shards.Length;
                _shards[i].localPosition = new Vector3(Mathf.Cos(a), 0.9f + 0.1f * Mathf.Sin(a * 2f), Mathf.Sin(a)) * s * 0.85f;
                _shards[i].localRotation = Quaternion.Euler(30f * Mathf.Sin(a), a * Mathf.Rad2Deg, 25f);
                _shards[i].localScale = Vector3.one * s * 0.28f;
            }

            // Eyes: two small lights on the front face that blink now and then.
            _blinkTimer -= dt;
            if (_blinkTimer <= 0f) { _blink = 1f; _blinkTimer = 2.2f + 2.5f * Random.value; }
            _blink = Mathf.Max(0f, _blink - dt * 7f);
            float eyeSize = s * 0.34f * (1f - 0.85f * Mathf.Sin(_blink * Mathf.PI)) * (1f + 0.4f * _windup);
            _eyeL.localPosition = new Vector3(-0.16f * s, 0.12f * s, 0.46f * s);
            _eyeR.localPosition = new Vector3(0.16f * s, 0.12f * s, 0.46f * s);
            _eyeL.localScale = _eyeR.localScale = Vector3.one * eyeSize;

            // Body shader: telegraph tint, white-hot flash on hits.
            float tintBoost = 1f + 1.5f * _windup;
            _block.Clear();
            _block.SetColor(TintId, _tint * tintBoost);
            _block.SetFloat(FlashId, _flash);
            _bodyRenderer.SetPropertyBlock(_block);
            for (int i = 0; i < _shardRenderers.Length; i++) _shardRenderers[i].SetPropertyBlock(_block);

            UpdateHud(dt, s, appear * dead);
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
