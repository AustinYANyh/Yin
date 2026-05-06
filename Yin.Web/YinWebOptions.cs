namespace Yin.Web;

public sealed class YinWebOptions
{
    public const string SectionName = "YinWeb";

    public long MaxUploadBytes { get; init; } = 50 * 1024 * 1024;
    public string? AccessPassword { get; init; }
}
