using Dumb.Graphics.Config;
using Sia;

namespace Dumb.Graphics.Pipeline;

public sealed class RenderPipeline : IDisposable
{
    private readonly GraphicsContext _ctx;
    private readonly RenderGraph _graph;
    private readonly SystemStage _syncStage;
    private bool _disposed;

    public PipelineConfig Config { get; }
    public FrameConfig FrameConfig { get; }

    public RenderSettingsSystem SettingsSystem { get; }
    public PhaseQueueSystem PhaseQueue { get; }
    public RenderGraph Graph => _graph;

    public RenderPipeline(
        GraphicsContext ctx,
        PipelineConfig config,
        CameraSyncSystem cameraSync,
        TransformSyncSystem transformSync,
        LightSyncSystem lightSync,
        RenderSettingsSystem settingsSystem,
        PhaseQueueSystem phaseQueue)
    {
        _ctx = ctx;
        Config = config;
        FrameConfig = FrameConfig.FromPipelineConfig(config);
        SettingsSystem = settingsSystem;
        PhaseQueue = phaseQueue;
        _graph = new RenderGraph(ctx);

        _syncStage = SystemChain.Empty
            .Add(() => cameraSync)
            .Add(() => transformSync)
            .Add(() => lightSync)
            .Add(() => settingsSystem)
            .Add(() => phaseQueue)
            .CreateStage(ctx.World);
    }

    public void Tick()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        _syncStage.Tick();
        _graph.Run(_ctx.World);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _syncStage.Dispose();
        _graph.Clear();
    }
}
