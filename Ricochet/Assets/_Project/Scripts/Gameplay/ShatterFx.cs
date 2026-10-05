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
        [SerializeField] Material _confettiMaterial; // SoftParticle in flake mode
        [SerializeField] int _confettiMax = 480;
        [SerializeField] Material _shellMaterial;    // SoftParticle, brighter: a firework's streaks
        [SerializeField] int _shellSparks = 110;

        // The game's palette, one flake at a time: Spark cyan, crystal gold, creature magenta, amethyst, mint.
        static readonly Color[] ConfettiColors =
        {
            new(0.35f, 0.9f, 1f), new(1f, 0.78f, 0.32f), new(1f, 0.36f, 0.82f), new(0.68f, 0.52f, 1f), new(0.45f, 1f, 0.68f),
        };

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
        ParticleSystem _confetti, _shell;
        int _confettiHue;

        void Awake()
        {
            _pool = new ParticleSystem[_poolSize];
            _glitter = new ParticleSystem[_poolSize];
            for (int i = 0; i < _poolSize; i++)
            {
                _pool[i] = CreateSystem(i);
                _glitter[i] = CreateGlitter(i);
            }
            if (_confettiMaterial != null) _confetti = CreateConfetti();
            if (_shellMaterial != null) _shell = CreateShell();
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

        /// <summary>A burst of shards flying out of a popped crystal. ringScale widens the shock ring for bigger beats.</summary>
        public void Burst(Vector3 position, Color color, float ringScale = 1f)
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
            ring.transform.localScale = Vector3.one * (_ringSize * ringScale);
            ring.enabled = true;
        }

        /// <summary>Light flakes alive right now (dev captures).</summary>
        public int ConfettiCount => _confetti != null ? _confetti.particleCount : 0;

        /// <summary>Firework streaks alive right now (dev captures).</summary>
        public int ShellCount => _shell != null ? _shell.particleCount : 0;

        /// <summary>A handful of light flakes tossed up out of a point (Fever pops): they tumble, flutter and drift down.</summary>
        public void Confetti(Vector3 position, int count) => EmitConfetti(position, count, false);

        /// <summary>
        /// A firework of light (Fever's peak): a shell of streaks bursting every way and slowing to points under drag
        /// (a chrysanthemum), a flash and shock ring at its heart, and big confetti flakes that linger and drift down.
        /// </summary>
        /// <remarks>size scales the shell (its speed, so its reach): ~1 m across at 1, kept clear of the viewer's head.</remarks>
        public void Firework(Vector3 position, Color color, int flakes, float size = 1f)
        {
            if (_shell != null)
            {
                _shell.transform.position = position;
                var main = _shell.main;
                main.startSpeedMultiplier = size;
                var p = new ParticleSystem.EmitParams { applyShapeToPosition = true, startColor = Color.Lerp(color, Color.white, 0.2f) };
                _shell.Emit(p, _shellSparks);
            }
            Burst(position, Color.Lerp(color, Color.white, 0.4f), 2.6f * size);
            EmitConfetti(position, flakes, true, size);
        }

        void EmitConfetti(Vector3 position, int count, bool burst, float size = 1f)
        {
            if (_confetti == null) return;
            var shape = _confetti.shape;
            shape.shapeType = burst ? ParticleSystemShapeType.Sphere : ParticleSystemShapeType.Cone;
            var main = _confetti.main;
            main.startSpeed = burst ? new ParticleSystem.MinMaxCurve(1.2f * size, 2.4f * size) : new ParticleSystem.MinMaxCurve(1.3f, 2.6f);
            _confetti.transform.position = position;
            var p = new ParticleSystem.EmitParams { applyShapeToPosition = true };
            for (int i = 0; i < count; i++)
            {
                p.startColor = ConfettiColors[_confettiHue];
                _confettiHue = (_confettiHue + 1) % ConfettiColors.Length;
                // A firework's flakes are bigger cards: they are the part that stays in the air.
                if (burst) p.startSize3D = new Vector3(Random.Range(0.032f, 0.046f), Random.Range(0.05f, 0.072f), 1f);
                _confetti.Emit(p, 1);
            }
        }

        /// <summary>A firework's shell: streaks that start long and fast and shorten to glowing points as drag stops them.</summary>
        ParticleSystem CreateShell()
        {
            var go = new GameObject("FireworkShell");
            go.transform.SetParent(transform, false);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.9f, 1.35f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.6f, 3.6f);  // with the drag: a shell ~0.8-1 m across
            main.startSize = new ParticleSystem.MinMaxCurve(0.028f, 0.044f);
            main.gravityModifier = 0.25f;                                 // the embers droop as they fade
            main.maxParticles = _shellSparks * 4;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.05f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 3.4f;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 0.45f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 1f, 1f, 0.45f));
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.11f;   // ~0.4 m long at launch, a point once stopped
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = _shellMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            // The crackle: most streaks die in a twinkling white-gold ember where they stop (the shell's edge).
            var crackle = new GameObject("FireworkCrackle").AddComponent<ParticleSystem>();
            crackle.transform.SetParent(go.transform, false);
            crackle.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var cm = crackle.main;
            cm.playOnAwake = false;
            cm.loop = false;
            cm.simulationSpace = ParticleSystemSimulationSpace.World;
            cm.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.6f);
            cm.startSpeed = 0f;
            cm.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.036f);
            cm.startColor = new Color(1f, 0.92f, 0.76f);
            cm.gravityModifier = 0.1f;
            cm.maxParticles = _shellSparks * 4;
            var ce = crackle.emission;
            ce.enabled = true;
            ce.rateOverTime = 0f;
            ce.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var cs = crackle.shape;
            cs.enabled = false;
            var twinkle = crackle.sizeOverLifetime;
            twinkle.enabled = true;
            twinkle.size = new ParticleSystem.MinMaxCurve(1f, new AnimationCurve(new Keyframe(0f, 0.3f), new Keyframe(0.15f, 1f),
                new Keyframe(0.35f, 0.25f), new Keyframe(0.6f, 1f), new Keyframe(1f, 0f)));
            var cr = crackle.GetComponent<ParticleSystemRenderer>();
            cr.renderMode = ParticleSystemRenderMode.Billboard;
            cr.sharedMaterial = _shellMaterial;
            cr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            cr.receiveShadows = false;
            var subs = ps.subEmitters;
            subs.enabled = true;
            subs.AddSubEmitter(crackle, ParticleSystemSubEmitterType.Death, ParticleSystemSubEmitterProperties.InheritNothing, 0.6f);
            return ps;
        }

        /// <summary>
        /// Confetti of light: flat cards in the game's colours that pop out fast, slow to a flutter under drag and drift
        /// down slowly, spinning and flipping (their width oscillates, so each one twinkles as it turns). One system,
        /// world space, so every Fever pop and firework shares one draw call.
        /// </summary>
        ParticleSystem CreateConfetti()
        {
            var go = new GameObject("Confetti");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.LookRotation(Vector3.up, Vector3.forward); // the cone throws upward
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.loop = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startLifetime = new ParticleSystem.MinMaxCurve(2.4f, 3.6f);
            main.startSize3D = true;
            main.startSizeX = new ParticleSystem.MinMaxCurve(0.022f, 0.032f);
            main.startSizeY = new ParticleSystem.MinMaxCurve(0.034f, 0.052f);
            main.startSizeZ = 1f;
            main.startRotation = new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f);
            main.gravityModifier = 0.07f; // with the drag below, flakes settle to a ~0.3 m/s fall
            main.maxParticles = _confettiMax;
            var emission = ps.emission;
            emission.enabled = false;
            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Cone;
            shape.angle = 38f;
            shape.radius = 0.04f;
            var drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2.4f;
            var spin = ps.rotationOverLifetime;
            spin.enabled = true;
            spin.z = new ParticleSystem.MinMaxCurve(-5f, 5f);
            // The flip: the card's width swings between full and edge-on, at two rates (each flake a mix of them).
            var size = ps.sizeOverLifetime;
            size.enabled = true;
            size.separateAxes = true;
            size.x = new ParticleSystem.MinMaxCurve(1f, Flip(5), Flip(8));
            size.y = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            size.z = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Constant(0f, 1f, 1f));
            var flutter = ps.noise;
            flutter.enabled = true;
            flutter.strength = 0.35f;
            flutter.frequency = 0.9f;
            flutter.scrollSpeed = 0.3f;
            flutter.damping = true;
            flutter.quality = ParticleSystemNoiseQuality.Medium;
            var color = ps.colorOverLifetime;
            color.enabled = true;
            var fade = new Gradient();
            fade.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                         new[] { new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.04f), new GradientAlphaKey(1f, 0.6f), new GradientAlphaKey(0f, 1f) });
            color.color = fade;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Billboard;
            renderer.sharedMaterial = _confettiMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return ps;
        }

        /// <summary>|cos| over the lifetime, n half-turns, never quite edge-on (a hairline would shimmer).</summary>
        static AnimationCurve Flip(int halfTurns)
        {
            var keys = new Keyframe[halfTurns * 2 + 1];
            for (int k = 0; k < keys.Length; k++) keys[k] = new Keyframe(k / (float)(keys.Length - 1), k % 2 == 0 ? 1f : 0.12f);
            return new AnimationCurve(keys);
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
