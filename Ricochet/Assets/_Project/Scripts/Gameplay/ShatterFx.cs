using UnityEngine;

namespace Ricochet.Gameplay
{
    /// <summary>
    /// Crystal shatter bursts (docs/TECH_GUIDE.md section 4): small additive shard bursts, pooled,
    /// emitted without allocations. The particle systems are configured here in code, not in a prefab.
    /// </summary>
    public sealed class ShatterFx : MonoBehaviour
    {
        [SerializeField] Material _shardMaterial;
        [SerializeField] int _poolSize = 6;
        [SerializeField] int _shardsPerBurst = 16;

        ParticleSystem[] _pool;
        int _next;

        void Awake()
        {
            _pool = new ParticleSystem[_poolSize];
            for (int i = 0; i < _poolSize; i++) _pool[i] = CreateSystem(i);
        }

        /// <summary>A burst of shards flying out of a popped crystal.</summary>
        public void Burst(Vector3 position, Color color)
        {
            var ps = _pool[_next];
            _next = (_next + 1) % _pool.Length;
            ps.transform.position = position;
            var p = new ParticleSystem.EmitParams { startColor = color, applyShapeToPosition = true };
            ps.Emit(p, _shardsPerBurst);
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
            main.startSize = new ParticleSystem.MinMaxCurve(0.022f, 0.05f);
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
