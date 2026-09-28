using System.Linq;
using Meta.XR.MRUtilityKit;
using Ricochet.Audio;
using Ricochet.Gameplay;
using Ricochet.Room;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Creates the grey-box assets (layers, materials, meshes, prefabs) and wires the gameplay objects
    /// into the Main scene built by SceneBootstrapper. Idempotent.
    /// </summary>
    public static class PrototypeBuilder
    {
        const string Root = "Assets/_Project";
        const string MatDir = Root + "/Art/Materials";
        const string MeshDir = Root + "/Art/Meshes";
        const string PrefabDir = Root + "/Prefabs";

        [MenuItem("Ricochet/Build Prototype Scene")]
        public static void BuildFromMenu() => Debug.Log(Build());

        public static string Build()
        {
            var log = new System.Text.StringBuilder();
            EnsureLayers(log);
            System.IO.Directory.CreateDirectory(MatDir);
            System.IO.Directory.CreateDirectory(MeshDir);
            System.IO.Directory.CreateDirectory(PrefabDir);
            AssetDatabase.Refresh();

            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var particlesUnlit = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            var simpleLit = Shader.Find("Universal Render Pipeline/Simple Lit");

            var crystalMat = Mat("Crystal", unlit, new Color(0.55f, 0.35f, 1f));
            var sparkMat = Mat("Spark", unlit, new Color(0.75f, 0.95f, 1f));
            var trailMat = AdditiveMat("SparkTrail", particlesUnlit, new Color(0.35f, 0.85f, 1f, 1f));
            var aimMat = AdditiveMat("AimLine", particlesUnlit, new Color(1f, 1f, 1f, 0.6f));
            var roomPreviewMat = Mat("RoomPreview_Desktop", simpleLit, new Color(0.42f, 0.44f, 0.5f));

            var bouncy = AssetDatabase.LoadAssetAtPath<PhysicsMaterial>(Root + "/Art/SparkBounce.physicMaterial");
            if (bouncy == null)
            {
                bouncy = new PhysicsMaterial("SparkBounce");
                AssetDatabase.CreateAsset(bouncy, Root + "/Art/SparkBounce.physicMaterial");
            }
            bouncy.bounciness = 0.82f;
            bouncy.dynamicFriction = 0f;
            bouncy.staticFriction = 0f;
            bouncy.bounceCombine = PhysicsMaterialCombine.Maximum;
            bouncy.frictionCombine = PhysicsMaterialCombine.Minimum;

            var crystalMesh = SaveMesh(CrystalMesh.Get(), MeshDir + "/Crystal.asset");
            var crystalPrefab = BuildCrystalPrefab(crystalMesh, crystalMat);
            log.AppendLine("Assets: materials, crystal mesh, crystal prefab");

            // Scene wiring.
            var scene = EditorSceneManager.OpenScene(SceneBootstrapper.MainScenePath);
            RemoveUnwanted(log);

            var rig = Object.FindAnyObjectByType<OVRCameraRig>();
            var head = rig.centerEyeAnchor;

            var game = GetOrCreate("Game");
            var playArea = GetOrAdd<PlayArea>(game);
            Set(playArea, "_rigRoot", rig.transform);
            Set(playArea, "_head", head);
            Set(GetOrAdd<DesktopLook>(game), "_rigRoot", rig.transform);
            var sfx = GetOrAdd<SfxPlayer>(game);

            var board = GetOrAdd<BoardGenerator>(GetOrCreate("Board", game.transform));
            Set(board, "_crystalPrefab", crystalPrefab.GetComponent<Crystal>());

            var spark = BuildSpark(game.transform, sparkMat, trailMat, bouncy);
            var slingGo = GetOrCreate("Sling", game.transform);
            var sling = GetOrAdd<Sling>(slingGo);
            var previewGo = GetOrCreate("TrajectoryPreview", slingGo.transform);
            var line = GetOrAdd<LineRenderer>(previewGo);
            line.sharedMaterial = aimMat;
            line.widthCurve = new AnimationCurve(new Keyframe(0f, 0.006f), new Keyframe(1f, 0.002f));
            line.numCapVertices = 2;
            line.shadowCastingMode = ShadowCastingMode.Off;
            line.receiveShadows = false;
            var preview = GetOrAdd<TrajectoryPreview>(previewGo);
            Set(sling, "_spark", spark);
            Set(sling, "_preview", preview);

            var director = GetOrAdd<ShotDirector>(game);
            Set(director, "_playArea", playArea);
            Set(director, "_sling", sling);
            Set(director, "_spark", spark);
            Set(director, "_board", board);
            Set(director, "_sfx", sfx);
            Set(director, "_leftHand", FindHand("ComprehensiveInteractorsLeft"));
            Set(director, "_rightHand", FindHand("ComprehensiveInteractorsRight"));

            ConfigureMruk(log);
            var effectMesh = Object.FindAnyObjectByType<EffectMesh>();
            var desktop = GetOrCreate("DesktopOnly");
            GetOrAdd<DesktopOnly>(desktop);
            var preview3d = GetOrAdd<DesktopRoomPreview>(game);
            Set(preview3d, "_effectMesh", effectMesh);
            Set(preview3d, "_previewMaterial", roomPreviewMat);
            var lightGo = GetOrCreate("DesktopLight", desktop.transform);
            var light = GetOrAdd<Light>(lightGo);
            light.type = LightType.Directional;
            light.shadows = LightShadows.None;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            log.AppendLine("Scene wired and saved: " + SceneBootstrapper.MainScenePath);
            return log.ToString();
        }

        static void EnsureLayers(System.Text.StringBuilder log)
        {
            var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
            var layers = tagManager.FindProperty("layers");
            layers.GetArrayElementAtIndex(Layers.Room).stringValue = "Room";
            layers.GetArrayElementAtIndex(Layers.Spark).stringValue = "Spark";
            layers.GetArrayElementAtIndex(Layers.Crystal).stringValue = "Crystal";
            tagManager.ApplyModifiedPropertiesWithoutUndo();

            // Only Spark collides with Room and Crystal; nothing else needs to.
            for (int a = 0; a < 32; a++)
            {
                Physics.IgnoreLayerCollision(Layers.Spark, a, a != Layers.Room && a != Layers.Crystal);
            }
            Physics.IgnoreLayerCollision(Layers.Room, Layers.Room, true);
            Physics.IgnoreLayerCollision(Layers.Crystal, Layers.Crystal, true);
            Physics.IgnoreLayerCollision(Layers.Room, Layers.Crystal, true);
            log.AppendLine("Layers: Room=6 Spark=7 Crystal=8, collision matrix set");
        }

        static void ConfigureMruk(System.Text.StringBuilder log)
        {
            var mruk = Object.FindAnyObjectByType<MRUK>();
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.DeviceWithPrefabFallback;
            EditorUtility.SetDirty(mruk);

            var effectMesh = Object.FindAnyObjectByType<EffectMesh>();
            effectMesh.Colliders = true;
            effectMesh.HideMesh = true;
            effectMesh.CastShadow = false;
            effectMesh.Layer = Layers.Room;
            effectMesh.Labels =
                MRUKAnchor.SceneLabels.FLOOR | MRUKAnchor.SceneLabels.CEILING | MRUKAnchor.SceneLabels.WALL_FACE |
                MRUKAnchor.SceneLabels.INVISIBLE_WALL_FACE | MRUKAnchor.SceneLabels.INNER_WALL_FACE |
                MRUKAnchor.SceneLabels.TABLE | MRUKAnchor.SceneLabels.COUCH | MRUKAnchor.SceneLabels.STORAGE |
                MRUKAnchor.SceneLabels.BED | MRUKAnchor.SceneLabels.SCREEN | MRUKAnchor.SceneLabels.LAMP |
                MRUKAnchor.SceneLabels.PLANT | MRUKAnchor.SceneLabels.WALL_ART | MRUKAnchor.SceneLabels.OTHER |
                MRUKAnchor.SceneLabels.DOOR_FRAME | MRUKAnchor.SceneLabels.WINDOW_FRAME;
            EditorUtility.SetDirty(effectMesh);
            log.AppendLine("MRUK: DeviceWithPrefabFallback; EffectMesh: hidden colliders on layer Room");
        }

        /// <summary>Seated game: no locomotion, and no Building Block demo UI.</summary>
        static void RemoveUnwanted(System.Text.StringBuilder log)
        {
            // Anything locomotion-related (teleport, turning, microgesture stepping) plus the Gaze block's demo canvas.
            bool Unwanted(GameObject g) =>
                g.name.Contains("Locomot") || g.name == "Dummy Canvas";
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>()
                         .Where(g => g != null && g.scene.IsValid() && Unwanted(g)).ToList())
            {
                if (go == null) continue; // already destroyed with a parent
                log.AppendLine("Removed " + go.name);
                Object.DestroyImmediate(go);
            }
        }

        static Oculus.Interaction.Input.Hand FindHand(string parentName)
        {
            var parent = GameObject.Find(parentName);
            return parent != null ? parent.GetComponent<Oculus.Interaction.Input.Hand>() : null;
        }

        static GameObject BuildCrystalPrefab(Mesh mesh, Material mat)
        {
            string path = PrefabDir + "/Crystal.prefab";
            var go = new GameObject("Crystal");
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            var col = go.AddComponent<SphereCollider>();
            col.center = new Vector3(0f, 0.3f, 0f);
            col.radius = 0.55f;
            go.transform.localScale = Vector3.one * 0.09f;
            var crystal = go.AddComponent<Crystal>();
            Set(crystal, "_renderer", mr);
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static Spark BuildSpark(Transform parent, Material mat, Material trailMat, PhysicsMaterial bouncy)
        {
            var existing = parent.Find("Spark");
            if (existing != null) Object.DestroyImmediate(existing.gameObject);

            var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            go.name = "Spark";
            go.transform.SetParent(parent, false);
            go.transform.localScale = Vector3.one * 0.05f;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            go.GetComponent<SphereCollider>().sharedMaterial = bouncy;
            go.AddComponent<Rigidbody>();
            var trail = go.AddComponent<TrailRenderer>();
            trail.sharedMaterial = trailMat;
            trail.time = 0.35f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 0.035f), new Keyframe(1f, 0f));
            trail.minVertexDistance = 0.02f;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.emitting = false;
            var spark = go.AddComponent<Spark>();
            Set(spark, "_trail", trail);
            return spark;
        }

        static Material Mat(string name, Shader shader, Color color)
        {
            string path = MatDir + "/" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, path);
            }
            mat.shader = shader;
            mat.SetColor("_BaseColor", color);
            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Material AdditiveMat(string name, Shader shader, Color color)
        {
            var mat = Mat(name, shader, color);
            mat.SetFloat("_Surface", 1f);  // Transparent
            mat.SetFloat("_Blend", 2f);    // Additive
            mat.SetFloat("_ZWrite", 0f);
            mat.SetOverrideTag("RenderType", "Transparent");
            mat.renderQueue = (int)RenderQueue.Transparent;
            mat.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
            mat.SetInt("_DstBlend", (int)BlendMode.One);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.EnableKeyword("_BLENDMODE_ADD");
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static Mesh SaveMesh(Mesh mesh, string path)
        {
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) return existing;
            var copy = Object.Instantiate(mesh);
            copy.name = "Crystal";
            AssetDatabase.CreateAsset(copy, path);
            return copy;
        }

        static GameObject GetOrCreate(string name, Transform parent = null)
        {
            var t = parent != null ? parent.Find(name) : GameObject.Find(name)?.transform;
            if (t != null) return t.gameObject;
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            return go;
        }

        static GameObject GetOrCreate(string name, GameObject parent) => GetOrCreate(name, parent.transform);

        static T GetOrAdd<T>(GameObject go) where T : Component =>
            go.TryGetComponent(out T c) ? c : go.AddComponent<T>();

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) throw new System.ArgumentException($"{target.GetType().Name}.{field} not found");
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
