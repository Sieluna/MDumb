using Dumb.Graphics.Resources;

namespace Dumb.Graphics.Pipeline;

public static class DrawCommand
{
    public static void DrawMesh(GraphicsContext ctx, ref RenderPass pass, in PhaseItem item)
    {
        pass.SetPipeline(item.Pipeline);

        if (item.BindGroups.Length > 0 && item.BindGroups[0] is { Host: not null } frameBg)
            pass.SetBindGroup(0, frameBg, stackalloc uint[] { item.ModelOffset });

        for (var i = 1u; i < item.BindGroups.Length; i++)
        {
            if (item.BindGroups[i] is { Host: not null } bg)
                pass.SetBindGroup(i, bg);
        }

        Mesh.Draw(pass, item.Mesh, item.SubMeshIndex);
    }

    public static void DrawFullScreen(GraphicsContext ctx, ref RenderPass pass, in PhaseItem item)
    {
        pass.SetPipeline(item.Pipeline);
        for (var i = 0u; i < item.BindGroups.Length; i++)
        {
            if (item.BindGroups[i] is { Host: not null } bg)
                pass.SetBindGroup(i, bg);
        }
        pass.Draw(3);
    }
}
