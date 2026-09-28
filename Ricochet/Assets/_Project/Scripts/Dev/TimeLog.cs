using System.Text;
using UnityEngine;

namespace Ricochet.Dev
{
    /// <summary>
    /// Dev-only recorder: samples Time.timeScale (and the drama flag) every frame, so an Editor eval can read back
    /// a slow-motion curve that is faster than the eval round trip. Created on demand by DevHooks.
    /// </summary>
    public sealed class TimeLog : MonoBehaviour
    {
        const int Capacity = 2048;
        readonly float[] _t = new float[Capacity];
        readonly float[] _scale = new float[Capacity];
        int _count;
        float _freezeAt = -1f, _freezeScale;

        /// <summary>After this many real seconds, slow game time to this scale (holds until released).</summary>
        public void FreezeAfter(float seconds, float scale)
        {
            _freezeAt = Time.unscaledTime + seconds;
            _freezeScale = scale;
        }

        System.Func<bool> _freezeWhen;

        /// <summary>Freeze game time the first frame this condition holds.</summary>
        public void FreezeWhen(System.Func<bool> condition, float scale)
        {
            _freezeWhen = condition;
            _freezeScale = scale;
        }

        void Update()
        {
            if (_freezeWhen != null && _freezeWhen())
            {
                _freezeWhen = null;
                var warp = FindAnyObjectByType<Gameplay.TimeWarp>();
                if (warp != null) warp.Freeze(_freezeScale);
            }
            if (_freezeAt > 0f && Time.unscaledTime >= _freezeAt)
            {
                _freezeAt = -1f;
                var warp = FindAnyObjectByType<Gameplay.TimeWarp>();
                if (warp != null) warp.Freeze(_freezeScale);
            }
            if (_count >= Capacity) return;
            _t[_count] = Time.unscaledTime;
            _scale[_count] = Time.timeScale;
            _count++;
        }

        /// <summary>Run-length summary: "t=start..end scale" segments, merging near-equal scales.</summary>
        public string Summary()
        {
            if (_count == 0) return "empty";
            var sb = new StringBuilder();
            int start = 0;
            for (int i = 1; i <= _count; i++)
            {
                if (i < _count && Mathf.Abs(_scale[i] - _scale[start]) < 0.05f) continue;
                sb.Append($"{_t[start] - _t[0]:F2}-{_t[i - 1] - _t[0]:F2}s@{_scale[start]:F2} ");
                start = i;
            }
            return sb.ToString();
        }
    }
}
