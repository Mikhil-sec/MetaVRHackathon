using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Builds the Main scene from Meta Building Blocks so the XR rig is wired exactly as Meta intends.
    /// Block installation is internal to the SDK, so it is invoked by reflection.
    /// </summary>
    public static class SceneBootstrapper
    {
        public const string MainScenePath = "Assets/_Project/Scenes/Main.unity";

        // Order matters: dependencies (Camera Rig) first.
        static readonly string[] Blocks =
        {
            "Camera Rig",
            "Passthrough",
            "Interactions Rig",
            "Gaze Interaction",
            "MR Utility Kit",
            "Effect Mesh",
        };

        static string s_Status = "idle";
        public static string Status => s_Status;

        public static string CreateMainScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            System.IO.Directory.CreateDirectory("Assets/_Project/Scenes");
            EditorSceneManager.SaveScene(scene, MainScenePath);
            var scenes = EditorBuildSettings.scenes.Where(s => s.path != MainScenePath).ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(MainScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            return "created " + MainScenePath;
        }

        public static string ListBlockNames()
        {
            return string.Join("\n", FindBlocks().Select(b => BlockName(b)).OrderBy(n => n));
        }

        /// <summary>Starts installing all blocks; poll <see cref="Status"/> until it reads "done" or "error".</summary>
        public static string InstallBlocks()
        {
            s_Status = "installing";
            _ = InstallAll();
            return s_Status;
        }

        static async Task InstallAll()
        {
            try
            {
                var all = FindBlocks().ToList();
                foreach (var name in Blocks)
                {
                    var block = all.FirstOrDefault(b => BlockName(b) == name);
                    if (block == null) { s_Status = "error: block not found: " + name; return; }
                    s_Status = "installing " + name;
                    var add = block.GetType().GetMethod("AddToProject", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(GameObject), typeof(System.Action) }, null);
                    await (Task)add.Invoke(block, new object[] { null, null });
                }
                EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
                s_Status = "done";
            }
            catch (System.Exception e)
            {
                s_Status = "error: " + e;
            }
        }

        static System.Collections.Generic.IEnumerable<ScriptableObject> FindBlocks()
        {
            var blockType = System.AppDomain.CurrentDomain.GetAssemblies()
                .Select(a => a.GetType("Meta.XR.BuildingBlocks.Editor.BlockData"))
                .FirstOrDefault(t => t != null);
            return AssetDatabase.FindAssets("t:" + blockType.Name)
                .Select(g => AssetDatabase.LoadAssetAtPath<ScriptableObject>(AssetDatabase.GUIDToAssetPath(g)))
                .Where(o => o != null && blockType.IsInstanceOfType(o));
        }

        static string BlockName(ScriptableObject block)
        {
            var f = block.GetType().GetField("blockName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            var p = block.GetType().GetProperty("BlockName", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            return (f?.GetValue(block) ?? p?.GetValue(block))?.ToString() ?? block.name;
        }
    }
}
