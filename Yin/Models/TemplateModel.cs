using System;
namespace Yin.Models;

/// <summary>
/// 布局模式（品牌与参数的排版方式）
/// </summary>
public enum LayoutMode
{
    /// <summary>品牌在上、EXIF 在下</summary>
    BrandTop_ExifBottom,
    /// <summary>品牌居底部居中</summary>
    BrandBottom_Centered,
    /// <summary>底部两行（机身 + 参数）</summary>
    TwoLines_Bottom_Centered
}

/// <summary>
/// 模板配置（边框、文字、Logo 等参数）
/// </summary>
public class TemplateModel
{
    /// <summary>模板名称</summary>
    public string Name { get; set; } = "";
    /// <summary>整体缩放（百分比）</summary>
    public double Scale { get; set; }
    /// <summary>上边距</summary>
    public double MarginTop { get; set; }
    /// <summary>下边距</summary>
    public double MarginBottom { get; set; }
    /// <summary>左边距</summary>
    public double MarginLeft { get; set; }
    /// <summary>右边距</summary>
    public double MarginRight { get; set; }
    /// <summary>是否同步上下边距</summary>
    public bool IsSyncVertical { get; set; } = true;
    /// <summary>是否同步左右边距</summary>
    public bool IsSyncHorizontal { get; set; } = true;
    /// <summary>智能自适应（根据图像亮度调整文字/面板）</summary>
    public bool IsSmartAdaptation { get; set; } = false;
    /// <summary>圆角半径</summary>
    public double Corner { get; set; }
    /// <summary>阴影大小</summary>
    public double Shadow { get; set; }
    /// <summary>字符间距</summary>
    public double Spacing { get; set; }
    /// <summary>布局模式</summary>
    public LayoutMode Layout { get; set; }
    /// <summary>边距优先（忽略 Scale，严格按边距渲染）</summary>
    public bool IsMarginPriority { get; set; }
    /// <summary>强制使用的 Logo 路径（Pack 资源路径）</summary>
    public string? ForceLogoPath { get; set; }
    /// <summary>Logo 垂直偏移</summary>
    public double LogoOffsetY { get; set; }
    /// <summary>EXIF 缺失时的默认品牌</summary>
    public string DefaultMake { get; set; } = "";
    /// <summary>EXIF 缺失时的默认机型</summary>
    public string DefaultModel { get; set; } = "";
    /// <summary>默认镜头</summary>
    public string DefaultLens { get; set; } = "";
    /// <summary>默认焦距</summary>
    public string DefaultFocal { get; set; } = "";
    /// <summary>默认光圈</summary>
    public string DefaultFNumber { get; set; } = "";
    /// <summary>默认快门</summary>
    public string DefaultShutter { get; set; } = "";
    /// <summary>默认 ISO</summary>
    public string DefaultISO { get; set; } = "";
    /// <summary>
    /// 参考短边像素（用于跨分辨率自适应缩放；短边=宽高中的较小值）
    /// </summary>
    public double ReferenceShortEdge { get; set; } = 0;
}
