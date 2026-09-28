using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Crystal shatter bursts (docs/TECH_GUIDE.md section 4), in three layers, all pooled and allocation-free:
    /// streaked shards that fly and fall, a lingering glitter of tiny motes, and an expanding shock ring of light.
    /// The particle systems are configured here in code, not in a prefab. Ring timing follows game time, so a pop
    /// during the slow-motion finale blooms slowly too.
    /// </summary>
    public sealed class ShatterFx : MonoBehaviour
    {
        [SerializeField] Material _shardMaterial;
        [SerializeField] int _poolSize = 6;
        [SerializeField] int _shardsPerBurst = 16;
        [SerializeField] int _glitterPerBurst = 10;
        [SerializeField] Material _ringMaterial;   // Ricochet/Ring
        [SerializeField] int _ringPool = 8;
        [SerializeField] float _ringTime = 0.32f;
        [SerializeField] float _ringSize = 0.34f;

        static readonly int ColorId = Shader.PropertyToID("_Color");
        static readonly int IntensityId = Shader.PropertyToID("_Intensity");
        static readonly int RadiusId = Shader.PropertyToID("_Radius");

        ParticleSystem[] _pool, _glitter;
        int _next;
        Renderer[] _rings;
        float[] _ringAge;
        Color[] _ringColor;
        int _nextRing;
        MaterialPropertyBlock _mpb;

        void Awake()
        {
            _pool = new ParticleSystem[_poolSize];
            _glitter = new ParticleSystem[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                _pool[i] = CreateSystem(i);
                _glitter[i] = CreateGlitter(i);
            }
            _mpb = new MaterialPropertyBlock();
            if (_ringMaterial == null) return;
            _rings = new Renderer[_ringPool];
            _ringAge = new float[_ringPool];
            _ringColor = new Color[_ringPool];
            for (int i = 0; i < _ringPool; i++)
            {
                var q = GameObject.CreatePrimitive(PrimitiveType.Quad);
                q.name = "ShatterRing" + i;
                Destroy(q.GetComponent<Collider>());
                q.transform.SetParent(transform, false);
                q.transform.localScale = Vector3.one * _ringSize;
                var r = q.GetComponent<Renderer>();
                r.sharedMaterial = _ringMaterial;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
                r.enabled = false;
                _rings[i] = r;
                _ringAge[i] = 1f;
            }
        }

        void Update()
        {
            if (_rings == null) return;
            float dt = Time.deltaTime;
            for (int i = 0; i < _rings.Length; i++)
            {
                if (_ringAge[i] >= 1f) continue;
                _ringAge[i] = Mathf.Min(1f, _ringAge[i] + dt / _ringTime);
                float t = _ringAge[i];
                var r = _rings[i];
                if (t >= 1f) { r.enabled = false; continue; }
                float e = 1f - (1f - t) * (1f - t) * (1f - t); // fast out, eased stop
                _mpb.Clear();
                _mpb.SetColor(ColorId, _ringColor[i]);
                _mpb.SetFloat(IntensityId, 2.2f * (1f - t) * (1f - t));
                _mpb.SetFloat(RadiusId, Mathf.Lerp(0.06f, 0.47f, e));
                r.SetPropertyBlock(_mpb);
            }
        }

        /// <summary>A burst of shards flying out of a popped crystal.</summary>
        public void Burst(Vector3 position, Color color)
        {
            var ps = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            ps.transform.position = position;
            var p = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
            ps.Emit(p, _shardsPerBurst);

            var glitter = _glitter[(_next + _pool.Length - 1) % _pool.Length];
            glitter.transform.position = position;
            var g = new ParticleSystem.EmitParams { startColor = Color.Lerp(color, Color.white, 0.5f), applyShapeToPosition = true };
            glitter.Emit(g, _glitterPerBurst);

            if (_rings == null) return;
            var ring = _rings[_nextRing];
            _ringAge[_nextRing] = 0f;
            _ringColor[_nextRing] = color;
            _nextRing = (_nextRing + 1) % _rings.Length;
            ring.transform.position = position;
            ring.enabled = true;
        }

        /// <summary>Tiny bright motes that drift and twinkle out after the shards have gone.</summary>
        ParticleSystem CreateGlitter(int index)
        {
            var go = new GameObject("Glitter" + index);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.25f, 0.8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.014f, 0.026f);
            main.gravityModifier = -0.02f; // light rises
            main.maxParticles = _glitterPerBurst * 2;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.5f;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            // Twinkle: swell, dip, swell, then out.
            var twinkle = new AnimationCurve(new Keyframe(0f, 0.4f), new Keyframe(0.2f, 1f), new Keyframe(0.45f, 0.5f),
                                             new Keyframe(0.7f, 1f), new Keyframe(1f, 0f));
            size.size = new ParticleSystem.MinMaxCurve(1f, twinkle);
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _shardMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        ParticleSystem CreateSystem(int index)
        {
            var go = new GameObject("Shatter" + index);
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.35f, 0.6f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(1.2f, 2.6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.07f);
            main.gravityModifier = 0.35f;
            main.maxParticles = _shardsPerBurst * 2;

            var emission = ps.emission;
            emission.enabled = false;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.03f;

            // Fade and shrink out.
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.5f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.2f));

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch; // shards streak along their motion
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 2f;
            renderer.sharedMaterial = _shardMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }
    }
}
