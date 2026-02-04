using System.Windows.Media.Imaging;
namespace Yin.Models;

/// <summary>
/// 渲染上下文（从界面收集的所有参数与数据）
/// </summary>
public class RenderContext
{
    /// <summary>当前位图</summary>
    public BitmapSource CurrentImage { get; set; } = default!;
    /// <summary>EXIF 信息</summary>
    public ExifInfo? Exif { get; set; }
    /// <summary>模板配置</summary>
    public TemplateModel? Template { get; set; }
    /// <summary>布局模式</summary>
    public LayoutMode Layout { get; set; }
    /// <summary>是否边距优先</summary>
    public bool IsMarginPriority { get; set; }
    /// <summary>是否智能自适应</summary>
    public bool IsSmartAdaptation { get; set; }
    /// <summary>缩放百分比（0-100）</summary>
    public double ScalePercent { get; set; }
    /// <summary>上边距</summary>
    public double MarginTop { get; set; }
    /// <summary>下边距</summary>
    public double MarginBottom { get; set; }
    /// <summary>左边距</summary>
    public double MarginLeft { get; set; }
    /// <summary>右边距</summary>
    public double MarginRight { get; set; }
    /// <summary>圆角半径</summary>
    public double CornerRadius { get; set; }
    /// <summary>阴影大小</summary>
    public double ShadowSize { get; set; }
    /// <summary>字符间距</summary>
    public double TextSpacing { get; set; }
    /// <summary>Logo 垂直偏移</summary>
    public double LogoOffsetY { get; set; }
    /// <summary>品牌文本框</summary>
    public string TxtMake { get; set; } = "";
    /// <summary>机型文本框</summary>
    public string TxtModel { get; set; } = "";
    /// <summary>镜头文本框</summary>
    public string TxtLens { get; set; } = "";
    /// <summary>焦距文本框</summary>
    public string TxtFocal { get; set; } = "";
    /// <summary>光圈文本框</summary>
    public string TxtFNumber { get; set; } = "";
    /// <summary>快门文本框</summary>
    public string TxtShutter { get; set; } = "";
    /// <summary>ISO 文本框</summary>
    public string TxtISO { get; set; } = "";
}
