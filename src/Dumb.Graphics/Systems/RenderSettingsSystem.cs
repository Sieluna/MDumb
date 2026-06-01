using System.Numerics;
using System.Runtime.CompilerServices;
using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics;

public sealed class RenderSettingsSystem : ExtractSystemBase
{
    private readonly GraphicsContext _ctx;
    private Entity _settingsBuffer;
    private bool _bufferCreated;
    private int _lastExtractVersion = -1;

    private readonly List<CachedVolume> _cachedVolumes = [];
    private readonly List<Entity> _tempEntities = [];

    public Entity SettingsBuffer => _settingsBuffer;

    public RenderSettingsSystem(GraphicsContext ctx)
        : base(Matchers.Any, extractMatcher:
            Matchers.Of<Engine.Render.RenderVolume>())
    {
        _ctx = ctx;
    }

    public override void Execute(World world, IEntityQuery query, IEntityQuery extract)
    {
        EnsureBuffer();

        // Only rebuild the sorted volume list when entities change
        if (extract.Version != _lastExtractVersion)
        {
            _cachedVolumes.Clear();
            extract.ForSlice((Entity entity, ref Engine.Render.RenderVolume volume) =>
            {
                if (volume.Weight <= 0f) return;
                _cachedVolumes.Add(new CachedVolume(entity, volume.Priority));
            });
            _cachedVolumes.Sort(static (a, b) => a.Priority.CompareTo(b.Priority));
            _lastExtractVersion = extract.Version;
        }

        // Blend using cached entities (re-reads component values each frame)
        var result = SettingsUniforms.Default;

        foreach (var cv in _cachedVolumes)
        {
            var entity = cv.Entity;
            if (!entity.IsValid) continue;

            ref var volume = ref entity.Get<Engine.Render.RenderVolume>();
            if (!volume.IsGlobal) continue;
            var t = Math.Clamp(volume.Weight, 0f, 1f);

            if (entity.Contains<Engine.Render.ShadowSettings>())
            {
                ref var ss = ref entity.Get<Engine.Render.ShadowSettings>();
                if ((volume.ShadowMask & Engine.Render.ShadowSettingsOverride.MaxDistance) != 0)
                    result.ShadowsMaxDistance = Lerp(result.ShadowsMaxDistance, ss.MaxDistance, t);
                if ((volume.ShadowMask & Engine.Render.ShadowSettingsOverride.CascadeCount) != 0)
                    result.CascadeCount = (uint)(int)Lerp((int)result.CascadeCount, ss.CascadeCount, t);
                if ((volume.ShadowMask & Engine.Render.ShadowSettingsOverride.DepthBias) != 0)
                    result.DepthBias = Lerp(result.DepthBias, ss.DepthBias, t);
                if ((volume.ShadowMask & Engine.Render.ShadowSettingsOverride.NormalBias) != 0)
                    result.NormalBias = Lerp(result.NormalBias, ss.NormalBias, t);
            }

            if (entity.Contains<Engine.Render.PostProcessSettings>())
            {
                ref var pp = ref entity.Get<Engine.Render.PostProcessSettings>();
                if ((volume.PostMask & Engine.Render.PostProcessOverride.BloomIntensity) != 0)
                    result.BloomIntensity = Lerp(result.BloomIntensity, pp.BloomIntensity, t);
                if ((volume.PostMask & Engine.Render.PostProcessOverride.BloomThreshold) != 0)
                    result.BloomThreshold = Lerp(result.BloomThreshold, pp.BloomThreshold, t);
                if ((volume.PostMask & Engine.Render.PostProcessOverride.Exposure) != 0)
                    result.Exposure = Lerp(result.Exposure, pp.Exposure, t);
                if ((volume.PostMask & Engine.Render.PostProcessOverride.AmbientColor) != 0)
                    result.AmbientColor = Vector3.Lerp(result.AmbientColor,
                        new Vector3(pp.AmbientR, pp.AmbientG, pp.AmbientB), t);
            }
        }

        Buffers.Write(_ctx, _settingsBuffer, result);
    }

    private void EnsureBuffer()
    {
        if (_bufferCreated) return;
        _settingsBuffer = Buffers.Create(_ctx,
            (ulong)Unsafe.SizeOf<SettingsUniforms>(),
            BufferUsage.Uniform | BufferUsage.CopyDst);
        _bufferCreated = true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float Lerp(float a, float b, float t) => a + (b - a) * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Lerp(int a, int b, float t) => (int)(a + (b - a) * t);

    // Caches entity reference + sort key; component values re-read each frame.
    private readonly struct CachedVolume(Entity entity, float priority)
    {
        public readonly Entity Entity = entity;
        public readonly float Priority = priority;
    }
}
