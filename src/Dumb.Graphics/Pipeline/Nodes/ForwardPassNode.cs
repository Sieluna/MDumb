using Sia;
using Silk.NET.WebGPU;

namespace Dumb.Graphics.Pipeline.Nodes;

public sealed class ForwardPassNode : RenderNode
{
    private readonly GraphicsContext _ctx;
    private readonly PhaseQueueSystem _phaseQueue;
    private readonly GBuffer _gbuffer;

    public Entity SwapchainView { get; set; }

    public ForwardPassNode(
        GraphicsContext ctx,
        PhaseQueueSystem phaseQueue,
        GBuffer gbuffer)
    {
        _ctx = ctx;
        _phaseQueue = phaseQueue;
        _gbuffer = gbuffer;
    }

    public override void DeclareResources()
    {
        Inputs.Add(new ResourceHandle(_gbuffer.DepthView, "GBuffer_Depth"));
        Inputs.Add(new ResourceHandle(SwapchainView, "SwapchainInput"));
    }

    public override void Execute(World world, RenderContext renderCtx)
    {
        if (SwapchainView.Host == null) return;

        unsafe
        {
            var colorAttachment = Commands.ColorAttachment(_ctx, SwapchainView,
                new Color { R = 0, G = 0, B = 0, A = 1 }, LoadOp.Load);

            var dsAttachment = Commands.DepthStencilAttachment(_ctx, _gbuffer.DepthView,
                LoadOp.Load, StoreOp.Store, 1.0f, depthReadOnly: true);

            var renderDesc = new RenderPassDescriptor
            {
                ColorAttachmentCount = 1,
                ColorAttachments = &colorAttachment,
                DepthStencilAttachment = &dsAttachment,
                OcclusionQuerySet = null,
                TimestampWrites = null,
                Label = null
            };

            var encoder = Commands.CreateEncoder(_ctx);
            var pass = encoder.BeginRenderPass(&renderDesc);
            pass.SetViewport(0, 0, _gbuffer.Width, _gbuffer.Height, 0, 1);
            pass.SetScissorRect(0, 0, _gbuffer.Width, _gbuffer.Height);

            foreach (var binItems in _phaseQueue.ForwardPhase.Bins)
            {
                foreach (var item in binItems)
                    DrawCommand.DrawMesh(_ctx, ref pass, item);
            }

            pass.End();
            var cb = encoder.Finish();
            renderCtx.AddCommandBuffer(cb);
        }
    }
}
