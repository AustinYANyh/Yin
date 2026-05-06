using Yin.Models;

namespace Yin.Web.Models;

public sealed class RenderRequest
{
    public WatermarkRenderMode Mode { get; init; } = WatermarkRenderMode.Border;
    public string TemplateName { get; init; } = "";
    public string Make { get; init; } = "";
    public string Model { get; init; } = "";
    public string Lens { get; init; } = "";
    public string Focal { get; init; } = "";
    public string FNumber { get; init; } = "";
    public string Shutter { get; init; } = "";
    public string ISO { get; init; } = "";
    public string Location { get; init; } = "";
}
