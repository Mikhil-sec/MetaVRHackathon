using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering.Universal;

namespace Ricochet.Dev
{
    /// <summary>
    /// Frame-stepped trailer recorder for Meta XR Simulator, whose native MP4 recording is unsupported in this build
    /// and whose composited capture is 840x880 at ~5 fps. Driven by tools/xrrender.py through files in one folder:
    /// - Between recorded frames, game time (Time.captureDeltaTime) and the UI clock (RealTime) stand still, while
    ///   Unity keeps running frames, so hand gestures and head moves still complete.
    /// - The driver poses head and hands, then writes step.txt = k. The next frame advances exactly one video frame,
    ///   a non-XR video camera at the left-eye pose renders the game (hands included) over transparent black at
    ///   full resolution, and is saved as c_k.png (premultiplied RGBA, sRGB). ready.txt = k then tells the driver to
    ///   grab the composited image, which shows passthrough only: the XR camera renders nothing while recording.
    /// - The edit composites game over passthrough in linear light (out = room * (1 - a) + game), the compositor's
    ///   own blend, using the crop in meta.json (the eye's projection vs the video camera's).
    /// Dev only: nothing in the game references it.
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class VideoRecorder : MonoBehaviour
    {
        const float IdleDelta = 1e-5f; // game time per idle frame (0 would turn captureDeltaTime off)

        public static VideoRecorder Active { get; private set; }

        string _dir;
        float _frameDt;
        Camera _main, _cam;
        Room.PassthroughMood _mood;
        int _savedMask;
        RenderTexture _rt;
        Texture2D _read;
        int _done = -1, _pending = -1, _capture = -1;
        bool _stepNext;
        Task _write = Task.CompletedTask;

        /// <summary>Start recording into dir: square video frames of size px, half-angle tangent halfTan.</summary>
        public static string Begin(string dir, int fps = 30, int size = 1920, float halfTan = 0.83f)
        {
            if (Active != null) return "already recording";
            var main = Camera.main;
            if (main == null) return "no main camera";
            Directory.CreateDirectory(dir);
            foreach (var f in new[] { "step.txt", "ready.txt" })
                if (File.Exists(Path.Combine(dir, f))) File.Delete(Path.Combine(dir, f));
            var r = new GameObject("VideoRecorder").AddComponent<VideoRecorder>();
            r.Setup(dir, fps, size, halfTan, main);
            return "recording " + dir;
        }

        public static string End()
        {
            if (Active == null) return "not recording";
            Destroy(Active.gameObject);
            return "stopped";
        }

        void Setup(string dir, int fps, int size, float halfTan, Camera main)
        {
            Active = this;
            _dir = dir;
            _frameDt = 1f / fps;
            _main = main;
            _mood = FindAnyObjectByType<Room.PassthroughMood>();
            _savedMask = main.cullingMask;

            _rt = new RenderTexture(size, size, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            _rt.Create();
            _read = new Texture2D(size, size, TextureFormat.RGBA32, false, false);

            _cam = new GameObject("VideoCam").AddComponent<Camera>();
            _cam.transform.SetParent(transform, false);
            _cam.stereoTargetEye = StereoTargetEyeMask.None;
            _cam.clearFlags = CameraClearFlags.SolidColor;
            _cam.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _cam.cullingMask = _savedMask;
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane = main.farClipPlane;
            _cam.fieldOfView = 2f * Mathf.Atan(halfTan) * Mathf.Rad2Deg;
            _cam.aspect = 1f;
            _cam.allowHDR = false; // an HDR intermediate has no alpha
            _cam.allowMSAA = true;
            _cam.depth = main.depth - 1f;
            _cam.targetTexture = _rt;
            var data = _cam.gameObject.AddComponent<UniversalAdditionalCameraData>();
            data.renderPostProcessing = false;

            main.cullingMask = 0; // the headset eye shows passthrough only; the video camera renders the game
            Time.captureDeltaTime = IdleDelta;
            RealTime.Step(0f);

            // How the video camera's square maps into the captured left-eye image (tangent extents).
            Matrix4x4 p = main.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            float l = (p.m02 - 1f) / p.m00, r = (p.m02 + 1f) / p.m00, b = (p.m12 - 1f) / p.m11, t = (p.m12 + 1f) / p.m11;
            File.WriteAllText(Path.Combine(_dir, "meta.json"),
                $"{{\"fps\":{fps},\"size\":{size},\"halfTan\":{halfTan},\"eyeL\":{l},\"eyeR\":{r},\"eyeB\":{b},\"eyeT\":{t}}}");
            StartCoroutine(Readback());
            Debug.Log($"[Ricochet] VideoRecorder: {_dir} {size}px {fps} fps, eye tan l={l:F3} r={r:F3} b={b:F3} t={t:F3}");
        }

        void Update()
        {
            if (_stepNext)
            {
                // This frame is the recorded one: it was given exactly one video frame of game time.
                _stepNext = false;
                RealTime.Step(_frameDt);
                Time.captureDeltaTime = IdleDelta;
                _capture = _pending;
                return;
            }
            RealTime.Step(0f);
            if (_capture >= 0 || !TryReadStep(out int k) || k <= _done) return;
            _pending = k;
            _stepNext = true;
            Time.captureDeltaTime = _frameDt; // takes effect next frame
        }

        void LateUpdate()
        {
            // The left eye: the composited capture is the left eye image, so the room lines up with the game.
            Matrix4x4 eye = _main.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
            Vector3 pos = eye.GetColumn(3);
            if (!float.IsFinite(pos.x) || pos == Vector3.zero) pos = _main.transform.position;
            _cam.transform.SetPositionAndRotation(pos, _main.transform.rotation);
        }

        System.Collections.IEnumerator Readback()
        {
            var eof = new WaitForEndOfFrame();
            while (true)
            {
                yield return eof;
                if (_capture < 0) continue;
                int k = _capture;
                var prev = RenderTexture.active;
                RenderTexture.active = _rt;
                _read.ReadPixels(new Rect(0, 0, _rt.width, _rt.height), 0, 0, false);
                RenderTexture.active = prev;
                byte[] raw = _read.GetRawTextureData();
                uint w = (uint)_rt.width, h = (uint)_rt.height;
                string path = Path.Combine(_dir, $"c_{k:D5}.png");
                _write = _write.ContinueWith(_ =>
                    File.WriteAllBytes(path, ImageConversion.EncodeArrayToPNG(raw, GraphicsFormat.R8G8B8A8_SRGB, w, h)));
                _done = k;
                _capture = -1;
                // The passthrough's look changes only with the head pose and the room mood: the driver captures it
                // only when one of them moved.
                float amount = _mood != null && _mood.isActiveAndEnabled ? _mood.Amount : -1f, focus = _mood != null ? _mood.Focus : 0f;
                File.WriteAllText(Path.Combine(_dir, "ready.txt"),
                    k + " " + amount.ToString("F3", System.Globalization.CultureInfo.InvariantCulture) + " " +
                    focus.ToString("F3", System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        bool TryReadStep(out int k)
        {
            k = -1;
            try
            {
                string path = Path.Combine(_dir, "step.txt");
                return File.Exists(path) && int.TryParse(File.ReadAllText(path).Trim(), out k);
            }
            catch (IOException)
            {
                return false; // the driver is replacing it this instant
            }
        }

        void OnDestroy()
        {
            if (Active == this) Active = null;
            if (_main != null) _main.cullingMask = _savedMask;
            Time.captureDeltaTime = 0f;
            RealTime.Release();
            if (_rt != null) _rt.Release();
            Destroy(_rt);
            Destroy(_read);
            _write.Wait(5000);
        }
    }
}
