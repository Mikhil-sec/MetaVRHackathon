using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Applies the locked Quest / VR Glasses settings from docs/TECH_GUIDE.md §2.
    /// Idempotent: safe to re-run after package upgrades.
    /// </summary>
    public static class QuestProjectConfigurator
    {
        const string MobileRpAssetPath = "Assets/Settings/Mobile_RPAsset.asset";
        const string MobileRendererPath = "Assets/Settings/Mobile_Renderer.asset";

        // Exact OpenXR feature UI names to enable; everything else is left as-is.
        static readonly string[] RequiredFeatures =
        {
            "Meta XR Feature",
            "Meta XR Foveation",
            "Meta XR Eye Tracked Foveation",
            "Meta XR Subsampled Layout",
            "Hand Tracking Subsystem",
            "Meta Hand Tracking Aim",
            "Hand Interaction Profile",
            "Eye Gaze Interaction Profile",
            "Meta Quest Touch Plus Controller Profile",
        };

        // Enabled by earlier substring matching or package defaults; not needed on Quest.
        static readonly string[] UnwantedFeatures =
        {
            "Microsoft Hand Interaction Profile",
            "Detached Meta Quest Touch Plus Controller Profile",
        };

        [MenuItem("Ricochet/Configure Project for Quest")]
        public static void ConfigureFromMenu() => Debug.Log(Configure());

        public static string Configure()
        {
            var log = new StringBuilder();
            ConfigurePlayer(log);
            ConfigureQuality(log);
            ConfigureUrp(log);
            ConfigureXr(log);
            ConfigureMetaFeatures(log);
            Time.fixedDeltaTime = 1f / 90f;
            log.AppendLine("Physics fixed timestep = 1/90");
            AssetDatabase.SaveAssets();
            return log.ToString();
        }

        static void ConfigurePlayer(StringBuilder log)
        {
            var android = NamedBuildTarget.Android;
            PlayerSettings.productName = "RICOCHET";
            PlayerSettings.SetApplicationIdentifier(android, "com.ricochet.mr");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.Vulkan });
            PlayerSettings.colorSpace = ColorSpace.Linear;
            PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel32;
            PlayerSettings.SetMobileMTRendering(android, true);
            PlayerSettings.gpuSkinning = true;
            PlayerSettings.stereoRenderingPath = StereoRenderingPath.Instancing;
            EditorUserBuildSettings.androidBuildSubtarget = MobileTextureSubtarget.ASTC;
            log.AppendLine("Player: IL2CPP, ARM64, Vulkan, Linear, minSdk 32, ASTC, multiview");
        }

        static void ConfigureQuality(StringBuilder log)
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAssetPath);
            GraphicsSettings.defaultRenderPipeline = rp;

            // Keep a single "Quest" quality level driven by the mobile URP asset.
            var names = QualitySettings.names;
            for (int i = 0; i < names.Length; i++)
            {
                QualitySettings.SetQualityLevel(i, false);
                QualitySettings.renderPipeline = rp;
                QualitySettings.antiAliasing = 4;
                QualitySettings.vSyncCount = 0;
                QualitySettings.globalTextureMipmapLimit = 0;
                QualitySettings.anisotropicFiltering = AnisotropicFiltering.Enable;
            }
            int mobileIndex = System.Array.FindIndex(names, n => n.ToLower().Contains("mobile"));
            if (mobileIndex >= 0) QualitySettings.SetQualityLevel(mobileIndex, true);
            log.AppendLine($"Quality: all levels -> {MobileRpAssetPath}, MSAA 4x, vsync off");
        }

        static void ConfigureUrp(StringBuilder log)
        {
            var rp = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(MobileRpAssetPath);
            var so = new SerializedObject(rp);
            so.FindProperty("m_SupportsHDR").boolValue = false;
            so.FindProperty("m_MSAA").intValue = 4;
            so.FindProperty("m_RenderScale").floatValue = 1f;
            so.FindProperty("m_RequireDepthTexture").boolValue = false;
            so.FindProperty("m_RequireOpaqueTexture").boolValue = false;
            so.FindProperty("m_SupportsTerrainHoles").boolValue = false;
            so.FindProperty("m_MainLightShadowsSupported").boolValue = false;
            so.FindProperty("m_AdditionalLightShadowsSupported").boolValue = false;
            so.FindProperty("m_SoftShadowsSupported").boolValue = false;
            so.FindProperty("m_UseSRPBatcher").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rp);

            var renderer = AssetDatabase.LoadAssetAtPath<UniversalRendererData>(MobileRendererPath);
            var rso = new SerializedObject(renderer);
            rso.FindProperty("postProcessData").objectReferenceValue = null;
            rso.FindProperty("m_IntermediateTextureMode").intValue = (int)IntermediateTextureMode.Auto;
            rso.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(renderer);
            log.AppendLine("URP: HDR off, MSAA 4x, depth/opaque tex off, shadows off, SRP batcher on, post off, intermediate Auto");
        }

        // Android is the device build; Standalone drives Editor Play Mode through Meta XR Simulator.
        static void ConfigureXr(StringBuilder log)
        {
            ConfigureXr(log, BuildTargetGroup.Android);
            ConfigureXr(log, BuildTargetGroup.Standalone);
        }

        static void ConfigureXr(StringBuilder log, BuildTargetGroup group)
        {
            // GetOrCreate is internal; it creates the asset if XR Plug-in Management was never opened.
            var all = typeof(XRGeneralSettingsPerBuildTarget)
                .GetMethod("GetOrCreate", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
                ?.Invoke(null, null) as XRGeneralSettingsPerBuildTarget;
            if (all != null)
            {
                if (!all.HasSettingsForBuildTarget(group))
                    all.CreateDefaultSettingsForBuildTarget(group);
                if (!all.HasManagerSettingsForBuildTarget(group))
                    all.CreateDefaultManagerSettingsForBuildTarget(group);
                EditorUtility.SetDirty(all);
            }
            var perTarget = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(group);
            if (perTarget == null)
            {
                log.AppendLine($"XR: could not create XR General Settings for {group}");
                return;
            }
            perTarget.InitManagerOnStart = true;
            XRPackageMetadataStore.AssignLoader(perTarget.AssignedSettings, "UnityEngine.XR.OpenXR.OpenXRLoader", group);

            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(group);
            var enabled = new List<string>();
            foreach (var feature in openXr.GetFeatures())
            {
                string label = UiName(feature);
                bool want = RequiredFeatures.Contains(label);
                if (want) enabled.Add(label);
                if (want || UnwantedFeatures.Contains(label)) feature.enabled = want;
            }
            EditorUtility.SetDirty(openXr);
            log.AppendLine($"XR {group}: OpenXR loader; enabled features: " + string.Join(", ", enabled));
        }

        static void ConfigureMetaFeatures(StringBuilder log)
        {
            var config = OVRProjectConfig.CachedProjectConfig;
            // Hands-first; controllers stay optional (competition rule: must never need pairing).
            config.handTrackingSupport = OVRProjectConfig.HandTrackingSupport.ControllersAndHands;
            // Supported, not Required: the Pocket Arena fallback runs without scene data or eye tracking.
            config.insightPassthroughSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.sceneSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.eyeTrackingSupport = OVRProjectConfig.FeatureSupport.Supported;
            config.boundaryVisibilitySupport = OVRProjectConfig.FeatureSupport.Supported;
            config.anchorSupport = OVRProjectConfig.AnchorSupport.Enabled;
            config.focusAware = true;
            OVRProjectConfig.CommitProjectConfig(config);
            log.AppendLine("Meta: hands+controllers, passthrough/scene/eye/boundary Supported, anchors Enabled");
        }

        // OpenXRFeature.nameUi is internal; the value is the attribute's UiName.
        static string UiName(OpenXRFeature feature)
        {
            var field = typeof(OpenXRFeature).GetField("nameUi", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            return (field?.GetValue(feature) as string) ?? feature.name;
        }

        [MenuItem("Ricochet/List OpenXR Features (Android)")]
        public static void ListFeaturesFromMenu() => Debug.Log(ListFeatures());

        public static string ListFeatures()
        {
            var openXr = OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if (openXr == null) return "No OpenXR settings for Android";
            return string.Join("\n", openXr.GetFeatures().Select(f => $"{(f.enabled ? "[x]" : "[ ]")} {UiName(f)} ({f.GetType().Name})"));
        }
    }
}
