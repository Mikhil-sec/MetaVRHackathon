using UnityEngine;

namespace Ricochet
{
    /// <summary>
    /// The clock for everything that ignores slow motion (UI, tension audio, the drama's own timing). It is the wall
    /// clock, except while a frame-stepped recording (Dev.VideoRecorder) drives it: then it advances exactly one video
    /// frame per recorded frame and stands still in between, like game time does via Time.captureDeltaTime.
    /// </summary>
    public static class RealTime
    {
        static bool s_stepped;
        static float s_dt, s_now;

        public static float DeltaTime => s_stepped ? s_dt : Time.unscaledDeltaTime;
        public static float Now => s_stepped ? s_now : Time.unscaledTime;

        /// <summary>Recorder only: called first thing every frame while stepping (dt is 0 between recorded frames).</summary>
        public static void Step(float dt)
        {
            if (!s_stepped) s_now = Time.unscaledTime;
            s_stepped = true;
            s_dt = dt;
            s_now += dt;
        }

        /// <summary>Recorder only: back to the wall clock. Now jumps forward to it, which nothing minds (it only grows).</summary>
        public static void Release() => s_stepped = false;
    }
}
