using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Profiling;
using UnityEditorInternal;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Zero GC per frame (TECH_GUIDE section 1), checked from the profiler's own data: every GC.Alloc sample under the
    /// PlayerLoop is attributed to the two samples above it (with deep profiling on, that is the exact managed method),
    /// over the recorded Play Mode frames. Editor-loop allocations (inspector, the CLI bridge) are ignored.
    /// Use: <c>GcProbe.Begin()</c> in Play, exercise the game, <c>GcProbe.StartReport()</c>, poll
    /// <c>GcProbe.Result</c> until it is not null (the walk runs across Editor updates: deep-profiled frames are big),
    /// then <c>GcProbe.End()</c>.
    /// </summary>
    public static class GcProbe
    {
        const int FramesPerUpdate = 40;

        static readonly Dictionary<string, double> s_bytes = new();
        static readonly Dictionary<string, int> s_calls = new();
        static readonly List<List<int>> s_kids = new();   // one child list per depth, reused
        static readonly List<string> s_path = new();
        static int s_next, s_last, s_frames, s_dirtyFrames, s_top, s_depth = 2;
        static double s_total;

        /// <summary>The finished report, or null while it is still being built.</summary>
        public static string Result { get; private set; }

        public static string Begin()
        {
            ProfilerDriver.ClearAllFrames();
            ProfilerDriver.profileEditor = false;
            ProfilerDriver.enabled = true;
            UnityEngine.Profiling.Profiler.enabled = true;
            Result = null;
            return "profiling";
        }

        public static string End()
        {
            ProfilerDriver.enabled = false;
            UnityEngine.Profiling.Profiler.enabled = false;
            return "stopped";
        }

        /// <summary>Stops recording and starts walking the recorded frames.</summary>
        /// <param name="depth">How many callers name an allocation site (2 = "Method < Caller").</param>
        public static string StartReport(int top = 12, int depth = 2)
        {
            s_depth = System.Math.Max(1, depth);
            ProfilerDriver.enabled = false;
            s_bytes.Clear();
            s_calls.Clear();
            s_frames = s_dirtyFrames = 0;
            s_total = 0;
            s_top = top;
            s_next = ProfilerDriver.firstFrameIndex;
            s_last = ProfilerDriver.lastFrameIndex;
            Result = null;
            if (s_next < 0 || s_last < s_next) { Result = "no frames"; return Result; }
            EditorApplication.update -= Step;
            EditorApplication.update += Step;
            return $"walking frames {s_next}..{s_last}";
        }

        static void Step()
        {
            int end = System.Math.Min(s_last, s_next + FramesPerUpdate - 1);
            for (int f = s_next; f <= end; f++)
            {
                using var view = ProfilerDriver.GetHierarchyFrameDataView(f, 0,
                    HierarchyFrameDataView.ViewModes.Default, HierarchyFrameDataView.columnDontSort, false);
                if (view == null || !view.valid) continue;
                s_frames++;
                double frameBytes = 0;
                s_path.Clear();
                Walk(view, view.GetRootItemID(), 0, false, ref frameBytes);
                if (frameBytes > 0) s_dirtyFrames++;
                s_total += frameBytes;
            }
            s_next = end + 1;
            if (s_next <= s_last) return;
            EditorApplication.update -= Step;
            var sb = new StringBuilder();
            sb.Append($"frames {s_frames}, with GC {s_dirtyFrames}, total {s_total / 1024.0:F1} KB, avg {(s_frames > 0 ? s_total / s_frames : 0):F0} B/frame");
            foreach (var kv in s_bytes.OrderByDescending(k => k.Value).Take(s_top))
                sb.Append($"\n  {kv.Value / 1024.0,8:F1} KB  {s_calls[kv.Key],5}x  {kv.Key}");
            Result = sb.ToString();
        }

        static void Walk(HierarchyFrameDataView view, int id, int depth, bool inPlayer, ref double frameBytes)
        {
            string name = view.GetItemName(id);
            if (name == "PlayerLoop") inPlayer = true;
            if (name == "EditorLoop") return;
            if (name == "GC.Alloc")
            {
                if (!inPlayer) return;
                double b = view.GetItemColumnDataAsDouble(id, HierarchyFrameDataView.columnGcMemory);
                var key = new StringBuilder();
                for (int i = s_path.Count - 1, n = 0; i >= 0 && n < s_depth; i--, n++)
                {
                    if (n > 0) key.Append(" < ");
                    key.Append(Short(s_path[i]));
                }
                string site = key.Length > 0 ? key.ToString() : "?";
                s_bytes[site] = (s_bytes.TryGetValue(site, out double v) ? v : 0) + b;
                s_calls[site] = (s_calls.TryGetValue(site, out int c) ? c : 0) + (int)view.GetItemColumnDataAsFloat(id, HierarchyFrameDataView.columnCalls);
                frameBytes += b;
                return;
            }
            while (s_kids.Count <= depth) s_kids.Add(new List<int>());
            var kids = s_kids[depth];
            kids.Clear();
            view.GetItemChildren(id, kids);
            if (kids.Count == 0) return;
            s_path.Add(name);
            for (int i = 0; i < kids.Count; i++) Walk(view, kids[i], depth + 1, inPlayer, ref frameBytes);
            s_path.RemoveAt(s_path.Count - 1);
        }

        // "Ricochet.Runtime.dll!Ricochet.Gameplay::ScoreHud.LateUpdate() [Invoke]" -> "ScoreHud.LateUpdate()"
        static string Short(string sample)
        {
            int bang = sample.IndexOf("::", System.StringComparison.Ordinal);
            string s = bang >= 0 ? sample.Substring(bang + 2) : sample;
            int bracket = s.IndexOf(" [", System.StringComparison.Ordinal);
            return bracket > 0 ? s.Substring(0, bracket) : s;
        }
    }
}
