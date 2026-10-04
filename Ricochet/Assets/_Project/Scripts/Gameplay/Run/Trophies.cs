using System;
using System.Collections.Generic;
using System.IO;
using Ricochet.Audio;
using Ricochet.Room;
using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Trophies in your home (CONCEPT section 3, Meta): every sealed boss leaves a small crown of gold crystals on the
    /// real wall where its rift was, held there by a spatial anchor, so over weeks the room becomes a record of your
    /// runs. The anchors' UUIDs (plus score, chain and date) live in persistentDataPath/trophies.json; on the next
    /// launch they are loaded, localized and bound, and the crowns grow back where they were.
    /// Without anchors (desktop Play) the stored pose is used instead, which is only meaningful in the same room.
    /// The Pocket Arena (no scan) moves with the player, so its crowns are kept in the seat's frame, unanchored, and
    /// grow back on the arena's walls; anchored crowns from scanned-room runs still restore in the real room.
    /// Visual only: no colliders, so the board, the Spark and the sweep never see them.
    /// </summary>
    public sealed class Trophies : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        // Shelf slots around the rift spot, in wall-plane metres (along the wall, up): a row first, then a row above.
        static readonly Vector2[] Slots =
        {
            new(0f, 0f), new(0.22f, 0f), new(-0.22f, 0f), new(0.44f, 0f), new(-0.44f, 0f),
            new(0.11f, 0.21f), new(-0.11f, 0.21f), new(0.33f, 0.21f), new(-0.33f, 0.21f),
            new(0.66f, 0f), new(-0.66f, 0f), new(0f, -0.21f), new(0.22f, -0.21f), new(-0.22f, -0.21f),
        };
        const float SlotClearance = 0.18f;

        [SerializeField] PlayArea _playArea;
        [SerializeField] Mesh _crystalMesh;
        [SerializeField] Material _crystalMaterial;
        [SerializeField] Material _haloMaterial;       // gold, breathing (material values, so crowns can batch)
        [SerializeField] Material _glintMaterial;      // four-point star that flashes now and then
        [SerializeField] SfxPlayer _sfx;
        [SerializeField] ShatterFx _fx;
        [SerializeField] RoomGlow _glow;
        [SerializeField] int _max = 12;                 // the oldest crown makes way (its anchor is erased)
        [SerializeField] float _crystalSize = 0.1f;     // ~16 cm crown: findable on a wall 3-5 m away
        [SerializeField] Color _gold = new(1f, 0.8f, 0.38f);

        [Serializable]
        sealed class Record
        {
            public string Uuid = "";
            public int Score;
            public int Chain;
            public string Date = "";
            public string Room = "";                    // the stored pose only means something in the same room
                                                        // (PocketArena.RoomKey: the pose is in the seat's frame)
            public Vector3 Position;
            public Quaternion Rotation = Quaternion.identity;
        }

        [Serializable]
        sealed class Shelf { public List<Record> Items = new(); }

        sealed class Shown
        {
            public Record Record;
            public Transform Root;
            public OVRSpatialAnchor Anchor;
            public float Grow = 1f;
            public bool Hidden;                         // stepped aside for a rift opening on top of it
        }

        Shelf _shelf = new();
        readonly List<Shown> _shown = new();
        readonly List<OVRSpatialAnchor.UnboundAnchor> _unbound = new();
        MaterialPropertyBlock _block;
        bool _restored;
        Vector3 _yieldAt;
        float _yieldRadius;                             // > 0 while a rift is open on the shelf

        public int Count => _shelf.Items.Count;
        static string FilePath => Path.Combine(Application.persistentDataPath, "trophies.json");
        static bool AnchorsAvailable => !PlayArea.IsDesktop && !PlayArea.IsPocket;
        string RoomName => _playArea.Room != null ? _playArea.Room.name : "";

        void Awake() => _block = new MaterialPropertyBlock();
        void OnEnable() => _playArea.Ready += OnReady;
        void OnDisable() => _playArea.Ready -= OnReady;

        void OnReady()
        {
            if (_restored) return;
            _restored = true;
            Load();
            Restore();
        }

        void Load()
        {
            try
            {
                _shelf = File.Exists(FilePath) ? JsonUtility.FromJson<Shelf>(File.ReadAllText(FilePath)) ?? new Shelf() : new Shelf();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Ricochet] Trophies unreadable, starting a new shelf: {e.Message}");
                _shelf = new Shelf();
            }
        }

        void Save()
        {
            try { File.WriteAllText(FilePath, JsonUtility.ToJson(_shelf)); }
            catch (Exception e) { Debug.LogWarning($"[Ricochet] Trophies not saved: {e.Message}"); }
        }

        async void Restore()
        {
            if (_shelf.Items.Count == 0) return;
            var uuids = new List<Guid>();
            foreach (var r in _shelf.Items)
            {
                if (r.Room == PocketArena.RoomKey)
                {
                    if (PlayArea.IsPocket) Show(r, null, PocketArena.ToWorld(r.Position), PocketArena.ToWorld(r.Rotation), 0f);
                }
                else if (!PlayArea.IsDesktop && Guid.TryParse(r.Uuid, out Guid id)) uuids.Add(id);
                else if (PlayArea.IsDesktop && r.Room == RoomName) Show(r, null, r.Position, r.Rotation, 0f); // the stored pose
            }
            if (uuids.Count == 0) return;

            var result = await OVRSpatialAnchor.LoadUnboundAnchorsAsync(uuids, _unbound);
            if (!result.Success)
            {
                Debug.LogWarning($"[Ricochet] Trophies: loading {uuids.Count} anchors failed ({result.Status})");
                return;
            }
            int bound = 0;
            foreach (var unbound in _unbound)
            {
                if (!await unbound.LocalizeAsync()) continue;
                var record = _shelf.Items.Find(r => r.Uuid == unbound.Uuid.ToString());
                if (record == null) continue;
                var shown = Show(record, null, Vector3.zero, Quaternion.identity, 0f);
                shown.Anchor = shown.Root.gameObject.AddComponent<OVRSpatialAnchor>();
                try
                {
                    unbound.BindTo(shown.Anchor); // the anchor now drives the crown's pose
                    bound++;
                }
                catch (InvalidOperationException e)
                {
                    // Already bound: in the Editor (no domain reload) OVRSpatialAnchor's static registry can outlive
                    // the previous Play session. Drop this copy rather than show an unanchored crown.
                    Debug.LogWarning($"[Ricochet] Trophy anchor {unbound.Uuid} not bound: {e.Message}");
                    _shown.Remove(shown);
                    Destroy(shown.Root.gameObject);
                }
            }
            Debug.Log($"[Ricochet] Trophies restored: {bound}/{uuids.Count} anchored crowns");
        }

        /// <summary>A sealed boss: a crown grows out of the wall where the rift was, and is anchored there.</summary>
        public async void Place(Vector3 position, Vector3 wallNormal, int score, int chain)
        {
            position = FreeSlot(position, wallNormal);
            var record = new Record
            {
                Score = score,
                Chain = chain,
                Date = DateTime.Now.ToString("yyyy-MM-dd"),
                Room = RoomName,
                Position = position + wallNormal * 0.03f,
                Rotation = Quaternion.LookRotation(wallNormal, Vector3.up), // +z out of the wall
            };
            _shelf.Items.Add(record);
            var shown = Show(record, null, record.Position, record.Rotation, 0f);
            if (PlayArea.IsPocket)
            {
                record.Room = PocketArena.RoomKey;
                record.Position = PocketArena.ToLocal(record.Position);
                record.Rotation = PocketArena.ToLocal(record.Rotation);
            }
            if (_fx != null) { _fx.Burst(shown.Root.position, _gold, 1.6f); _fx.Burst(shown.Root.position, Color.white); }
            if (_glow != null) _glow.Pulse(shown.Root.position, _gold * 2f, 1.4f, 1.2f);
            if (_sfx != null) _sfx.PlayChord(shown.Root.position);

            while (_shelf.Items.Count > _max) await Retire(_shelf.Items[0]);

            if (AnchorsAvailable)
            {
                shown.Anchor = shown.Root.gameObject.AddComponent<OVRSpatialAnchor>();
                if (await shown.Anchor.WhenCreatedAsync())
                {
                    var saved = await shown.Anchor.SaveAnchorAsync();
                    if (saved.Success) record.Uuid = shown.Anchor.Uuid.ToString();
                    else Debug.LogWarning($"[Ricochet] Trophy anchor not saved ({saved.Status})");
                }
                else Debug.LogWarning("[Ricochet] Trophy anchor not created");
            }
            Save();
            Debug.Log($"[Ricochet] Trophy placed ({_shelf.Items.Count} on the shelf){(record.Uuid.Length > 0 ? ", anchor " + record.Uuid : ", no anchor")}");
        }

        /// <summary>World positions of the crowns on show (the rift keeps away from them when it can).</summary>
        public void GetPositions(List<Vector3> into)
        {
            into.Clear();
            foreach (var s in _shown) if (s.Root != null) into.Add(s.Root.position);
        }

        /// <summary>A rift opens on the shelf: crowns within the radius sink into the wall until <see cref="Return"/>.</summary>
        public void Yield(Vector3 at, float radius)
        {
            _yieldAt = at;
            _yieldRadius = radius;
            foreach (var s in _shown)
            {
                if (s.Root == null || s.Hidden || (s.Root.position - at).sqrMagnitude > radius * radius) continue;
                s.Hidden = true;
                if (_fx != null) _fx.Burst(s.Root.position, _gold, 0.5f);
            }
        }

        /// <summary>The rift sealed: the crowns that stepped aside grow back.</summary>
        public void Return()
        {
            _yieldRadius = 0f;
            foreach (var s in _shown)
            {
                if (!s.Hidden) continue;
                s.Hidden = false;
                s.Grow = 0f;
            }
        }

        /// <summary>The nearest shelf slot on the same wall that no crown holds yet, so repeat wins form a row.</summary>
        Vector3 FreeSlot(Vector3 position, Vector3 normal)
        {
            Vector3 along = Vector3.Cross(Vector3.up, normal);
            if (along.sqrMagnitude < 0.25f) return position; // not a vertical surface
            along.Normalize();
            var room = _playArea.Room;
            foreach (var slot in Slots)
            {
                Vector3 p = position + along * slot.x + Vector3.up * slot.y;
                bool taken = false;
                foreach (var s in _shown)
                    if (s.Root != null && (s.Root.position - normal * 0.03f - p).sqrMagnitude < SlotClearance * SlotClearance) { taken = true; break; }
                if (taken) continue;
                if (slot != Vector2.zero && room != null)
                {
                    // The slot must still be on a wall, flush with this one (not past a corner, not in a doorway).
                    if (!room.Raycast(new Ray(p + normal * 0.3f, -normal), 0.5f, out RaycastHit hit, out var anchor) || anchor == null ||
                        (anchor.Label & Meta.XR.MRUtilityKit.MRUKAnchor.SceneLabels.WALL_FACE) == 0 ||
                        Mathf.Abs(hit.distance - 0.3f) > 0.05f || Vector3.Dot(hit.normal, normal) < 0.9f) continue;
                }
                return p;
            }
            return position;
        }

        async System.Threading.Tasks.Task Retire(Record record)
        {
            _shelf.Items.Remove(record);
            int i = _shown.FindIndex(s => s.Record == record);
            if (i < 0) return;
            var shown = _shown[i];
            _shown.RemoveAt(i);
            if (shown.Anchor != null && shown.Anchor.Created) await shown.Anchor.EraseAnchorAsync();
            if (shown.Root != null) Destroy(shown.Root.gameObject);
        }

        Shown Show(Record record, Transform parent, Vector3 position, Quaternion rotation, float grow)
        {
            var root = new GameObject("Trophy").transform;
            root.SetParent(parent != null ? parent : transform, false);
            root.SetPositionAndRotation(position, rotation);

            // A crown: five gold crystals fanned up and out of the wall, the middle one tallest, an iridescent jewel at
            // their root (the focal point: hue contrast against all that gold), star glints on the tips, and a
            // breathing halo *behind* the crystals so it lights the wall rather than washing out the facets.
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 26f;                      // degrees from straight up, in the wall's plane
                float tall = i == 2 ? 1.3f : i == 1 || i == 3 ? 1.1f : 0.9f;
                Quaternion fan = Quaternion.AngleAxis(-a, Vector3.forward);
                // The crystal's long axis is its local up: fan it out, then lean it out of the wall a little.
                Quaternion rot = fan * Quaternion.AngleAxis(-18f, Vector3.right);
                Vector3 pos = fan * (Vector3.up * 0.035f) + Vector3.forward * 0.02f;
                Crystal(root, pos, rot, new Vector3(1f, tall, 1f) * _crystalSize, CrystalKind.Gold, _gold, 0.15f);
                // CrystalMesh spans y -0.45..1.1: the tip sits ~1.05 up the crystal's axis.
                if (i % 2 == 0) Quad(root, "Glint", _glintMaterial, pos + rot * (Vector3.up * (1.05f * tall * _crystalSize)), 0.11f);
            }
            Crystal(root, new Vector3(0f, -0.004f, 0.045f), Quaternion.AngleAxis(-62f, Vector3.right),
                    Vector3.one * (_crystalSize * 0.55f), CrystalKind.Prism, Color.white, 0.25f);
            Quad(root, "Halo", _haloMaterial, new Vector3(0f, 0.045f, 0f), 0.36f); // a warm pool on the wall: findable from the seat

            var shown = new Shown { Record = record, Root = root, Grow = grow };
            if (_yieldRadius > 0f && (position - _yieldAt).sqrMagnitude < _yieldRadius * _yieldRadius) { shown.Hidden = true; shown.Grow = 0f; }
            root.localScale = Vector3.one * (shown.Hidden ? 0f : OutBack(grow));
            if (shown.Hidden) root.gameObject.SetActive(false);
            _shown.Add(shown);
            return shown;
        }

        void Crystal(Transform root, Vector3 pos, Quaternion rot, Vector3 scale, CrystalKind kind, Color color, float glow)
        {
            var go = new GameObject("Crystal");
            go.transform.SetParent(root, false);
            go.transform.SetLocalPositionAndRotation(pos, rot);
            go.transform.localScale = scale;
            go.AddComponent<MeshFilter>().sharedMesh = _crystalMesh;
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = _crystalMaterial;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
            _block.Clear();
            _block.SetColor(BaseColorId, color);
            _block.SetFloat(KindId, (float)kind);
            _block.SetFloat(ScaleId, 1f);
            _block.SetFloat(GlowId, glow);
            r.SetPropertyBlock(_block);
        }

        static void Quad(Transform root, string name, Material material, Vector3 pos, float size)
        {
            if (material == null) return;
            var go = new GameObject(name);
            go.transform.SetParent(root, false);
            go.transform.localPosition = pos;
            go.transform.localScale = Vector3.one * size;
            go.AddComponent<MeshFilter>().sharedMesh = RewardPicker.Quad();
            var r = go.AddComponent<MeshRenderer>();
            r.sharedMaterial = material;
            r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        void Update()
        {
            // Crowns grow in (placed, restored or returning) over about a second and sink away in a third of one, then
            // sit still: no per-frame cost after (the glints and the halo animate in their shader).
            for (int i = 0; i < _shown.Count; i++)
            {
                var s = _shown[i];
                if (s.Root == null) continue;
                if (s.Hidden)
                {
                    if (!s.Root.gameObject.activeSelf) continue;
                    s.Grow = Mathf.Max(0f, s.Grow - RealTime.DeltaTime / 0.35f);
                    s.Root.localScale = Vector3.one * (s.Grow * s.Grow);
                    if (s.Grow <= 0f) s.Root.gameObject.SetActive(false);
                    continue;
                }
                if (s.Grow >= 1f) continue;
                if (!s.Root.gameObject.activeSelf) s.Root.gameObject.SetActive(true);
                s.Grow = Mathf.Min(1f, s.Grow + RealTime.DeltaTime / 1.1f);
                s.Root.localScale = Vector3.one * OutBack(s.Grow);
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
