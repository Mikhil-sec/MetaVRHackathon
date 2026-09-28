using System.Collections;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;

namespace Ricochet.EditorTools
{
    /// <summary>
    /// Lists outstanding Meta Project Setup Tool tasks without the blocking UI path.
    /// The task API is internal to com.meta.xr.sdk.core, so this reads it via reflection.
    /// </summary>
    public static class MetaSetupAudit
    {
        [MenuItem("Ricochet/Audit Meta Project Setup (Android)")]
        public static void AuditFromMenu() => UnityEngine.Debug.Log(Audit());

        public static string Audit(BuildTargetGroup group = BuildTargetGroup.Android)
        {
            var setupType = typeof(OVRProjectSetup);
            var getTasks = setupType.GetMethod("GetTasks", BindingFlags.NonPublic | BindingFlags.Static);
            var tasks = ((IEnumerable)getTasks.Invoke(null, new object[] { group })).Cast<object>().ToList();

            var sb = new StringBuilder();
            int outstanding = 0;
            foreach (var task in tasks)
            {
                var t = task.GetType();
                if (!Lambda<bool>(t, task, "Valid", group)) continue;
                if ((bool)t.GetMethod("IsIgnored").Invoke(task, new object[] { group })) continue;
                var isDone = (System.Func<BuildTargetGroup, bool>)t.GetProperty("IsDone").GetValue(task);
                if (isDone(group)) continue;

                outstanding++;
                var level = Lambda<object>(t, task, "Level", group);
                var message = Lambda<string>(t, task, "Message", group);
                var auto = (bool)t.GetProperty("FixAutomatic").GetValue(task);
                sb.AppendLine($"[{level}] {(auto ? "(auto-fixable) " : "")}{message}");
            }
            return $"{outstanding} outstanding of {tasks.Count} tasks\n{sb}";
        }

        static T Lambda<T>(System.Type taskType, object task, string property, BuildTargetGroup group)
        {
            var optional = taskType.GetProperty(property).GetValue(task);
            if (optional == null) return default;
            return (T)optional.GetType().GetMethod("GetValue").Invoke(optional, new object[] { group });
        }
    }
}
