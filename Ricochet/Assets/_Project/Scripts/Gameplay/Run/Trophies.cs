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
    /// Visual only: no colliders, so the board, the Spark and the sweep never see them.
    /// </summary>
    public sealed class Trophies : MonoBehaviour
    {
        static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        static readonly int GlowId = Shader.PropertyToID("_Glow");
        static readonly int ScaleId = Shader.PropertyToID("_Scale");
        static readonly int KindId = Shader.PropertyToID("_Kind");
        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");

        [SerializeField] PlayArea _playArea;
        [SerializeField] Mesh _crystalMesh;
        [SerializeField] Material _crystalMaterial;
        [SerializeField] Material _haloMaterial;
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
        }

        Shelf _shelf = new();
        readonly List<Shown> _shown = new();
        readonly List<OVRSpatialAnchor.UnboundAnchor> _unbound = new();
        MaterialPropertyBlock _block;
        bool _restored;

        public int Count => _shelf.Items.Count;
        static string FilePath => Path.Combine(Application.persistentDataPath, "trophies.json");
        static bool AnchorsAvailable => !PlayArea.IsDesktop;
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
                if (AnchorsAvailable && Guid.TryParse(r.Uuid, out Guid id)) uuids.Add(id);
                else if (!AnchorsAvailable && r.Room == RoomName) Show(r, null, r.Position, r.Rotation, 0f); // desktop: the stored pose
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
                unbound.BindTo(shown.Anchor); // the anchor now drives the crown's pose
                bound++;
            }
            Debug.Log($"[Ricochet] Trophies restored: {bound}/{uuids.Count} anchored crowns");
        }

        /// <summary>A sealed boss: a crown grows out of the wall where the rift was, and is anchored there.</summary>
        public async void Place(Vector3 position, Vector3 wallNormal, int score, int chain)
        {
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
            if (_fx != null) { _fx.Burst(record.Position, _gold, 1.6f); _fx.Burst(record.Position, Color.white); }
            if (_glow != null) _glow.Pulse(record.Position, _gold * 2f, 1.4f, 1.2f);
            if (_sfx != null) _sfx.PlayChord(record.Position);

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

            // A crown: five gold crystals fanned up and out of the wall, the middle one tallest, on a soft halo.
            for (int i = 0; i < 5; i++)
            {
                float a = (i - 2) * 26f;                      // degrees from straight up, in the wall's plane
                float tall = i == 2 ? 1.3f : i == 1 || i == 3 ? 1.1f : 0.9f;
                var go = new GameObject("Crystal" + i);
                go.transform.SetParent(root, false);
                Quaternion fan = Quaternion.AngleAxis(-a, Vector3.forward);
                go.transform.localPosition = fan * (Vector3.up * 0.035f) + Vector3.forward * 0.02f;
                // The crystal's long axis is its local up: fan it out, then lean it out of the wall a little.
                go.transform.localRotation = fan * Quaternion.AngleAxis(-18f, Vector3.right);
                go.transform.localScale = new Vector3(1f, tall, 1f) * _crystalSize;
                go.AddComponent<MeshFilter>().sharedMesh = _crystalMesh;
                var r = go.AddComponent<MeshRenderer>();
                r.sharedMaterial = _crystalMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                _block.Clear();
                _block.SetColor(BaseColorId, _gold);
                _block.SetFloat(KindId, (float)CrystalKind.Gold);
                _block.SetFloat(ScaleId, 1f);
                _block.SetFloat(GlowId, 0.4f);
                r.SetPropertyBlock(_block);
            }
            if (_haloMaterial != null)
            {
                var halo = new GameObject("Halo");
                halo.transform.SetParent(root, false);
                halo.transform.localPosition = new Vector3(0f, 0.04f, 0.03f);
                halo.transform.localScale = Vector3.one * 0.28f;
                halo.AddComponent<MeshFilter>().sharedMesh = RewardPicker.Quad();
                var hr = halo.AddComponent<MeshRenderer>();
                hr.sharedMaterial = _haloMaterial;
                hr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                hr.receiveShadows = false;
                _block.Clear();
                _block.SetColor(ColorId, _gold);
                _block.SetFloat(IntensityId, 0.45f);
                hr.SetPropertyBlock(_block);
            }

            var shown = new Shown { Record = record, Root = root, Grow = grow };
            root.localScale = Vector3.one * OutBack(grow);
            _shown.Add(shown);
            return shown;
        }

        void Update()
        {
            // Crowns grow in (placed or restored) over about a second, then sit still: no per-frame cost after.
            for (int i = 0; i < _shown.Count; i++)
            {
                var s = _shown[i];
                if (s.Grow >= 1f || s.Root == null) continue;
                s.Grow = Mathf.Min(1f, s.Grow + Time.unscaledDeltaTime / 1.1f);
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
