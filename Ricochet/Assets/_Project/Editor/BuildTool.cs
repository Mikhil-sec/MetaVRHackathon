using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Builds the Quest APK into the repo's /Builds (gitignored) and writes a short report next to it, so a build can
    /// be started from the CLI (it outlasts the eval timeout) and checked by polling the report.
    /// Signing: debug until a release keystore exists (the store needs a release-signed APK; see docs/STATUS.md).
    /// </summary>
    public static class BuildTool
    {
        public const string OutDir = "../Builds";
        public const string ApkPath = OutDir + "/Ricochet.apk";
        public const string ReportPath = OutDir + "/build_report.txt";

        public static string BuildApk(bool development = false)
        {
            Directory.CreateDirectory(OutDir);
            if (File.Exists(ReportPath)) File.Delete(ReportPath);
            EditorUserBuildSettings.buildAppBundle = false;
            var options = new BuildPlayerOptions
            {
                scenes = new[] { "Assets/_Project/Scenes/Main.unity" },
                locationPathName = ApkPath,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = development ? BuildOptions.Development : BuildOptions.None,
            };
            var report = BuildPipeline.BuildPlayer(options);
            var s = report.summary;
            var text = new StringBuilder();
            text.AppendLine($"result {s.result}  time {s.totalTime:mm\\:ss}  size {s.totalSize / 1048576f:F1} MB  errors {s.totalErrors}  warnings {s.totalWarnings}");
            text.AppendLine($"version {PlayerSettings.bundleVersion} ({PlayerSettings.Android.bundleVersionCode})  keystore {(PlayerSettings.Android.useCustomKeystore ? "custom" : "debug")}");
            int shown = 0;
            foreach (var step in report.steps)
                foreach (var m in step.messages)
                    if ((m.type == LogType.Error || m.type == LogType.Exception) && shown++ < 20)
                        text.AppendLine("ERROR " + m.content.Split('\n')[0]);
            File.WriteAllText(ReportPath, text.ToString());
            return text.ToString();
        }
    }
}
