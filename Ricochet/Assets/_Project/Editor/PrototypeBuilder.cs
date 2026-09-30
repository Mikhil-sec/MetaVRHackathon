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

            var crystalMat = Mat("Crystal", Shader.Find("Ricochet/CrystalGlass"), new Color(0.55f, 0.35f, 1f));
            var sparkMat = Mat("Spark", Shader.Find("Ricochet/SparkCore"), Color.white);
            var trailMat = Mat("SparkRibbon", Shader.Find("Ricochet/Ribbon"), Color.white);
            var aimMat = AdditiveMat("AimLine", particlesUnlit, new Color(1f, 1f, 1f, 0.6f));
            var roomGlowMat = Mat("RoomGlow", Shader.Find("Ricochet/RoomGlow"), Color.clear);

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

            var board = Fresh<BoardGenerator>(GetOrCreate("Board", game.transform));
            Set(board, "_crystalPrefab", crystalPrefab.GetComponent<Crystal>());

            var spark = BuildSpark(game.transform, sparkMat, trailMat, bouncy);
            var slingGo = GetOrCreate("Sling", game.transform);
            var sling = Fresh<Sling>(slingGo);
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
            BuildSlingFx(slingGo, sling, spark, sfx);

            var director = Fresh<ShotDirector>(game);
            Set(director, "_playArea", playArea);
            Set(director, "_sling", sling);
            Set(director, "_spark", spark);
            Set(director, "_board", board);
            Set(director, "_sfx", sfx);
            Set(director, "_leftHand", FindHand("ComprehensiveInteractorsLeft"));
            Set(director, "_rightHand", FindHand("ComprehensiveInteractorsRight"));
            Set(director, "_trackingSpace", rig.transform.Find("TrackingSpace"));
            var glow = Fresh<RoomGlow>(game);
            Set(glow, "_spark", spark);
            Set(director, "_glow", glow);
            var fx = Fresh<ShatterFx>(game);
            Set(fx, "_shardMaterial", Mat("Shard", Shader.Find("Ricochet/SoftParticle"), Color.white)); // soft streaks, no texture
            var shatterRing = Mat("ShatterRing", Shader.Find("Ricochet/Ring"), Color.white);
            shatterRing.SetFloat("_Width", 0.025f);
            Set(fx, "_ringMaterial", shatterRing);
            Set(director, "_fx", fx);
            Set(director, "_popups", Fresh<ScorePopups>(game));

            ConfigureMruk(log, roomGlowMat);
            var desktop = GetOrCreate("DesktopOnly");
            GetOrAdd<DesktopOnly>(desktop);
            Set(Fresh<DesktopRoomPreview>(game), "_playArea", playArea);
            var mood = Fresh<PassthroughMood>(game);
            Set(mood, "_playArea", playArea);
            Set(mood, "_layer", Object.FindAnyObjectByType<OVRPassthroughLayer>());

            // Juice: last-crystal slow motion and the score readout.
            var warp = Fresh<TimeWarp>(game);
            var drama = Fresh<ShotDrama>(game);
            Set(drama, "_spark", spark);
            Set(drama, "_board", board);
            Set(drama, "_director", director);
            Set(drama, "_warp", warp);
            Set(drama, "_sfx", sfx);
            Set(drama, "_glow", glow);
            Set(drama, "_mood", mood);
            Set(drama, "_lockRing", Quad("LockRing", GetOrCreate("DramaFx", game.transform),
                Mat("DramaRing", Shader.Find("Ricochet/Ring"), Color.white), 0.3f));

            // Focus (gaze): a four-dash reticle on the crystal you glance at while aiming.
            var focus = Fresh<GazeFocus>(game);
            var focusMat = Mat("FocusRing", Shader.Find("Ricochet/Ring"), Color.white);
            focusMat.SetFloat("_Segments", 4f);
            focusMat.SetFloat("_Gap", 0.45f);
            focusMat.SetFloat("_Width", 0.03f);
            Set(focus, "_marker", Quad("FocusRing", GetOrCreate("DramaFx", game.transform), focusMat, 0.34f));
            Set(focus, "_playArea", playArea);
            Set(focus, "_board", board);
            Set(focus, "_sling", sling);
            Set(focus, "_spark", spark);
            Set(focus, "_sfx", sfx);
            Set(director, "_focus", focus);
            var hud = Fresh<ScoreHud>(GetOrCreate("ScoreHud", game.transform));
            Set(hud, "_playArea", playArea);
            Set(hud, "_director", director);
            Set(hud, "_board", board);

            // Encounter: rift, creature, light motes, and the turn director (CONCEPT section 3).
            var encounter = BuildEncounter(game, playArea, director, board, sling, sfx, glow, warp, fx,
                game.GetComponent<ScorePopups>(), crystalMesh);
            Set(drama, "_encounter", encounter);
            Set(hud, "_encounter", encounter);
            var glyphMat = Mat("IntentGlyph", Shader.Find("Ricochet/IntentGlyph"), Color.white);
            Set(hud, "_glyphMaterial", glyphMat);
            log.AppendLine("Encounter: rift, creature, motes, director");

            // The run: pick-1-of-3 reward orbs between rifts, and the title card for its big beats.
            var rewards = Fresh<RewardPicker>(GetOrCreate("Rewards", game.transform));
            Set(rewards, "_playArea", playArea);
            Set(rewards, "_sling", sling);
            Set(rewards, "_sfx", sfx);
            Set(rewards, "_glow", glow);
            Set(rewards, "_fx", fx);
            Set(rewards, "_orbMesh", spark.transform.Find("Visual").GetComponent<MeshFilter>().sharedMesh);
            var orbMat = Mat("RewardOrb", Shader.Find("Ricochet/SparkCore"), Color.white);
            orbMat.SetFloat("_Swirl", 0.6f);
            orbMat.SetFloat("_RimPower", 1.8f);
            orbMat.SetFloat("_Intensity", 1.1f);
            Set(rewards, "_orbMaterial", orbMat);
            Set(rewards, "_haloMaterial", HaloMat("RewardHalo", Color.white, 0.6f));
            Set(rewards, "_glyphMaterial", glyphMat);
            Set(encounter, "_rewards", rewards);
            var banner = Fresh<Banner>(GetOrCreate("Banner", game.transform));
            Set(banner, "_playArea", playArea);
            Set(encounter, "_banner", banner);
            log.AppendLine("Run: reward picker, banner");
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

        static void ConfigureMruk(System.Text.StringBuilder log, Material roomGlow)
        {
            var mruk = Object.FindAnyObjectByType<MRUK>();
            mruk.SceneSettings.DataSource = MRUK.SceneDataSource.DeviceWithPrefabFallback;
            EditorUtility.SetDirty(mruk);

            // The room mesh renders RoomGlow: invisible over passthrough except for our light, and depth for occlusion.
            var effectMesh = Object.FindAnyObjectByType<EffectMesh>();
            effectMesh.Colliders = true;
            effectMesh.HideMesh = false;
            effectMesh.MeshMaterial = roomGlow;
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
            log.AppendLine("MRUK: DeviceWithPrefabFallback; EffectMesh: RoomGlow mesh + colliders on layer Room");
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
            // Generous collider (~9 cm) around a ~6.5 cm crystal: forgiving hits, Peggle-style.
            col.radius = 0.7f;
            go.transform.localScale = Vector3.one * 0.13f;
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

            // Root: physics and logic at unit scale. Children: a stretchable visual and a billboard halo,
            // so squash-and-stretch never touches the collider.
            var go = new GameObject("Spark");
            go.transform.SetParent(parent, false);
            var col = go.AddComponent<SphereCollider>();
            col.radius = 0.025f;
            col.sharedMaterial = bouncy;
            go.AddComponent<Rigidbody>();

            var visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            visual.name = "Visual";
            Object.DestroyImmediate(visual.GetComponent<Collider>());
            visual.transform.SetParent(go.transform, false);
            visual.transform.localScale = Vector3.one * 0.05f;
            var mr = visual.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            var halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "Halo";
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(go.transform, false);
            halo.transform.localScale = Vector3.one * 0.16f;
            var hr = halo.GetComponent<MeshRenderer>();
            hr.sharedMaterial = HaloMat("SparkHalo", new Color(0.35f, 0.85f, 1f), 1.1f);
            hr.shadowCastingMode = ShadowCastingMode.Off;
            hr.receiveShadows = false;
            // The light trail: our own ribbon mesh on a world-origin sibling (TECH_GUIDE section 4).
            var ribbonGo = GetOrCreate("SparkRibbon", parent);
            ribbonGo.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            GetOrAdd<MeshFilter>(ribbonGo);
            var ribbonRenderer = GetOrAdd<MeshRenderer>(ribbonGo);
            ribbonRenderer.sharedMaterial = trailMat;
            ribbonRenderer.shadowCastingMode = ShadowCastingMode.Off;
            ribbonRenderer.receiveShadows = false;
            var ribbon = Fresh<SparkRibbon>(ribbonGo);
            Set(ribbon, "_target", go.transform);
            var spark = go.AddComponent<Spark>();
            Set(spark, "_ribbon", ribbon);
            Set(spark, "_visual", visual.transform);
            Set(spark, "_halo", halo.transform);
            return spark;
        }

        /// <summary>The sling's band, posts, hover ring, grab-me ripple, launch flash and fingertip glows (SlingFx).</summary>
        static void BuildSlingFx(GameObject slingGo, Sling sling, Spark spark, SfxPlayer sfx)
        {
            var root = GetOrCreate("SlingFx", slingGo.transform);
            var ringMat = Mat("SlingRing", Shader.Find("Ricochet/Ring"), Color.white);
            var haloMat = HaloMat("SlingGlow", new Color(0.35f, 0.85f, 1f), 1f);

            var bandGo = GetOrCreate("Band", root.transform);
            var band = GetOrAdd<LineRenderer>(bandGo);
            var bandMat = Mat("SlingBand", Shader.Find("Ricochet/Ribbon"), Color.white);
            bandMat.SetFloat("_Intensity", 1.2f);
            bandMat.SetFloat("_Core", 0.8f);
            band.sharedMaterial = bandMat;
            band.textureMode = LineTextureMode.Stretch; // uv.y runs across the band: the Ribbon glow profile
            band.alignment = LineAlignment.View;
            band.numCornerVertices = 0;
            band.numCapVertices = 0;
            band.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
            band.shadowCastingMode = ShadowCastingMode.Off;
            band.receiveShadows = false;

            var fx = Fresh<SlingFx>(root);
            Set(fx, "_sling", sling);
            Set(fx, "_spark", spark);
            Set(fx, "_sfx", sfx);
            Set(fx, "_band", band);
            Set(fx, "_postLeft", Quad("PostLeft", root, haloMat, 0.045f));
            Set(fx, "_postRight", Quad("PostRight", root, haloMat, 0.045f));
            Set(fx, "_ring", Quad("Ring", root, ringMat, 0.13f));
            Set(fx, "_ripple", Quad("Ripple", root, ringMat, 0.22f));
            Set(fx, "_flash", Quad("Flash", root, haloMat, 0.22f));

            // Zero-text onboarding: the ghost of light that demonstrates pinch-pull-release (GhostHandDemo).
            var ghostGo = GetOrCreate("GhostHand", root.transform);
            var ghostGlow = HaloMat("GhostGlow", new Color(0.7f, 0.95f, 1f), 1f);
            var pathGo = GetOrCreate("Path", ghostGo.transform);
            var path = GetOrAdd<LineRenderer>(pathGo);
            path.sharedMaterial = Mat("GhostPath", Shader.Find("Ricochet/Ribbon"), Color.white);
            path.textureMode = LineTextureMode.Stretch;
            path.alignment = LineAlignment.View;
            path.widthCurve = new AnimationCurve(new Keyframe(0f, 0.012f), new Keyframe(1f, 0.004f));
            path.startColor = path.endColor = new Color(0.7f, 0.95f, 1f, 1f);
            path.shadowCastingMode = ShadowCastingMode.Off;
            path.receiveShadows = false;
            var ghost = Fresh<GhostHandDemo>(ghostGo);
            Set(ghost, "_sling", sling);
            Set(ghost, "_head", Object.FindAnyObjectByType<OVRCameraRig>().centerEyeAnchor);
            Set(ghost, "_thumb", Quad("Thumb", ghostGo, ghostGlow, 0.028f));
            Set(ghost, "_index", Quad("Index", ghostGo, ghostGlow, 0.028f));
            Set(ghost, "_ghostSpark", Quad("GhostSpark", ghostGo, ghostGlow, 0.05f));
            Set(ghost, "_path", path);

            var so = new SerializedObject(fx);
            var tips = so.FindProperty("_tips");
            tips.arraySize = 2;
            tips.GetArrayElementAtIndex(0).objectReferenceValue = Quad("TipLeft", root, haloMat, 0.05f);
            tips.GetArrayElementAtIndex(1).objectReferenceValue = Quad("TipRight", root, haloMat, 0.05f);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>A collider-free billboard quad (Halo/Ring shaders face the camera in the vertex stage).</summary>
        static MeshRenderer Quad(string name, GameObject parent, Material mat, float size)
        {
            var t = parent.transform.Find(name);
            GameObject go;
            if (t == null)
            {
                go = GameObject.CreatePrimitive(PrimitiveType.Quad);
                go.name = name;
                Object.DestroyImmediate(go.GetComponent<Collider>());
                go.transform.SetParent(parent.transform, false);
            }
            else go = t.gameObject;
            go.transform.localScale = Vector3.one * size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return mr;
        }

        static EncounterDirector BuildEncounter(GameObject game, PlayArea playArea, ShotDirector director,
            BoardGenerator board, Sling sling, SfxPlayer sfx, RoomGlow glow, TimeWarp warp, ShatterFx fx,
            ScorePopups popups, Mesh crystalMesh)
        {
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");

            var riftGo = GetOrCreate("Rift", game.transform);
            var crackGo = GetOrCreate("Crack", riftGo.transform);
            GetOrAdd<MeshFilter>(crackGo).sharedMesh = SaveMesh(EncounterMeshes.Rift(), MeshDir + "/Rift.asset");
            var crackRenderer = GetOrAdd<MeshRenderer>(crackGo);
            crackRenderer.sharedMaterial = Mat("Rift", Shader.Find("Ricochet/Rift"), Color.white);
            crackRenderer.shadowCastingMode = ShadowCastingMode.Off;
            crackRenderer.receiveShadows = false;
            var riftHalo = GetOrCreate("Halo", riftGo.transform);
            if (riftHalo.GetComponent<MeshFilter>() == null)
            {
                var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
                GetOrAdd<MeshFilter>(riftHalo).sharedMesh = quad.GetComponent<MeshFilter>().sharedMesh;
                Object.DestroyImmediate(quad);
            }
            var riftHaloRenderer = GetOrAdd<MeshRenderer>(riftHalo);
            riftHaloRenderer.sharedMaterial = HaloMat("RiftHalo", new Color(1f, 0.3f, 0.8f), 0.7f);
            riftHaloRenderer.shadowCastingMode = ShadowCastingMode.Off;
            riftHalo.transform.localScale = Vector3.one * 0.9f;
            // The wall fracturing around the crack: a flat wall-aligned quad (not a billboard), 1.8 x 2.4 m.
            var web = Quad("Web", riftGo, Mat("RiftWeb", Shader.Find("Ricochet/RiftWeb"), Color.white), 1f);
            web.transform.localScale = new Vector3(1.8f, 2.4f, 1f);
            web.transform.localPosition = new Vector3(0f, 0f, -0.005f); // just behind the crack, still off the wall
            web.transform.localRotation = Quaternion.Euler(0f, 180f, 0f); // the quad's front faces -z; the rift's +z is the room
            var rift = Fresh<Rift>(riftGo);
            Set(rift, "_web", web);
            Set(rift, "_crack", crackRenderer);
            Set(rift, "_halo", riftHalo.transform);
            Set(rift, "_glow", glow);

            var creatureGo = GetOrCreate("Creature", game.transform);
            creatureGo.SetActive(true);
            var creature = Fresh<Creature>(creatureGo);
            Set(creature, "_bodyMesh", SaveMesh(EncounterMeshes.Creature(), MeshDir + "/Creature.asset"));
            Set(creature, "_shardMesh", crystalMesh);
            var creatureMat = Mat("Creature", Shader.Find("Ricochet/Creature"), Color.white);
            // Rim only at the silhouette: the ink (and the void inside it) must dominate the face-on body.
            creatureMat.SetFloat("_RimPower", 3.2f);
            Set(creature, "_bodyMaterial", creatureMat);
            Set(creature, "_eyeMaterial", HaloMat("CreatureEye", new Color(1f, 0.85f, 1f), 1.8f));
            Set(creature, "_barMaterial", Mat("Bar", unlit, Color.white));
            Set(creature, "_tendrilMaterial", Mat("Tendril", Shader.Find("Ricochet/Tendril"), Color.white));
            Set(creature, "_glyphMaterial", Mat("IntentGlyph", Shader.Find("Ricochet/IntentGlyph"), Color.white));

            var motes = Fresh<LightMotes>(GetOrCreate("Motes", game.transform));
            Set(motes, "_moteMaterial", HaloMat("MoteLight", new Color(1f, 0.8f, 0.35f), 2.4f));
            Set(motes, "_boltMaterial", HaloMat("MoteBolt", new Color(1f, 0.2f, 0.55f), 1.8f));

            var encounter = Fresh<EncounterDirector>(game);
            Set(encounter, "_playArea", playArea);
            Set(encounter, "_director", director);
            Set(encounter, "_board", board);
            Set(encounter, "_sling", sling);
            Set(encounter, "_rift", rift);
            Set(encounter, "_creature", creature);
            Set(encounter, "_motes", motes);
            Set(encounter, "_sfx", sfx);
            Set(encounter, "_glow", glow);
            Set(encounter, "_warp", warp);
            Set(encounter, "_fx", fx);
            Set(encounter, "_popups", popups);
            return encounter;
        }

        static Material HaloMat(string name, Color color, float intensity)
        {
            var mat = Mat(name, Shader.Find("Ricochet/Halo"), color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Intensity", intensity);
            return mat;
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
            string name = System.IO.Path.GetFileNameWithoutExtension(path);
            if (existing != null)
            {
                // Refresh in place so procedural mesh changes land without breaking references.
                EditorUtility.CopySerialized(mesh, existing);
                existing.name = name;
                EditorUtility.SetDirty(existing);
                return existing;
            }
            var copy = Object.Instantiate(mesh);
            copy.name = name;
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

        /// <summary>
        /// GetOrAdd, then reset every serialized field to the script's defaults. Tuning lives in code;
        /// the scene only keeps the references this builder re-wires, so edited defaults always take effect.
        /// </summary>
        static T Fresh<T>(GameObject go) where T : MonoBehaviour
        {
            var component = GetOrAdd<T>(go);
            var temp = new GameObject("FreshDefaults") { hideFlags = HideFlags.HideAndDontSave };
            temp.SetActive(false);
            EditorUtility.CopySerialized(temp.AddComponent<T>(), component);
            Object.DestroyImmediate(temp);
            return component;
        }

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
