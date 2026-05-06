using Yin.Models;

namespace Yin.Services;

public static class TemplateService
{
    public const string DefaultBorderTemplateName = "底部两行机身+参数";
    public const string DefaultOverlayTemplateName = "底部机身带参数_overlay";

    public const double DefaultOverlayStyleReferenceShortEdge = 1800;
    public const double DefaultOverlayCornerRadius = 24;
    public const double DefaultOverlayShadowSize = 18;

    public static IReadOnlyList<TemplateModel> CreateTemplates(WatermarkRenderMode mode)
    {
        return mode == WatermarkRenderMode.Border
            ? CreateBorderTemplates()
            : CreateOverlayTemplates();
    }

    public static TemplateModel? FindTemplate(WatermarkRenderMode mode, string? templateName)
    {
        var templates = CreateTemplates(mode);
        return templates.FirstOrDefault(t => t.Name == templateName)
               ?? templates.FirstOrDefault(t => t.Name == GetDefaultTemplateName(mode))
               ?? templates.FirstOrDefault();
    }

    public static string GetDefaultTemplateName(WatermarkRenderMode mode)
    {
        return mode == WatermarkRenderMode.Border
            ? DefaultBorderTemplateName
            : DefaultOverlayTemplateName;
    }

    private static IReadOnlyList<TemplateModel> CreateBorderTemplates()
    {
        return new List<TemplateModel>
        {
            new()
            {
                Name = "无",
                Scale = 85,
                MarginTop = 100,
                MarginBottom = 100,
                MarginLeft = 0,
                MarginRight = 0,
                Corner = 100,
                Shadow = 20,
                Spacing = 5,
                Layout = LayoutMode.BrandTop_ExifBottom,
                IsMarginPriority = false,
                IsSyncVertical = true,
                IsSyncHorizontal = true,
                ForceLogoPath = null,
                LogoOffsetY = 0
            },
            new()
            {
                Name = "哈苏水印边框",
                Scale = 85,
                MarginTop = 60,
                MarginBottom = 80,
                MarginLeft = 70,
                MarginRight = 70,
                Corner = 0,
                Shadow = 20,
                Spacing = 5,
                Layout = LayoutMode.BrandTop_ExifBottom,
                IsMarginPriority = true,
                IsSyncVertical = true,
                IsSyncHorizontal = true,
                IsSmartAdaptation = true,
                ForceLogoPath = "Source/Hasselblad.png",
                LogoOffsetY = 0,
                ReferenceShortEdge = 1800
            },
            new()
            {
                Name = "哈苏水印居中",
                Scale = 90,
                MarginTop = 120,
                MarginBottom = 220,
                MarginLeft = 120,
                MarginRight = 120,
                Corner = 0,
                Shadow = 20,
                Spacing = 5,
                Layout = LayoutMode.BrandBottom_Centered,
                IsMarginPriority = true,
                IsSyncVertical = true,
                IsSyncHorizontal = true,
                IsSmartAdaptation = true,
                ForceLogoPath = "Source/Hasselblad_white.png",
                LogoOffsetY = 40,
                ReferenceShortEdge = 1800
            },
            new()
            {
                Name = "底部两行机身+参数",
                Scale = 90,
                MarginTop = 150,
                MarginBottom = 400,
                MarginLeft = 150,
                MarginRight = 150,
                Corner = 0,
                Shadow = 20,
                Spacing = 5,
                Layout = LayoutMode.TwoLines_Bottom_Centered,
                IsMarginPriority = true,
                IsSyncVertical = false,
                IsSyncHorizontal = true,
                IsSmartAdaptation = true,
                ForceLogoPath = null,
                LogoOffsetY = 0,
                DefaultMake = "SONY",
                DefaultModel = "ILCE-7RM5",
                DefaultLens = "FE 70-200mm F2.8 GM OSS II",
                DefaultFocal = "70mm",
                DefaultFNumber = "f/2.8",
                DefaultShutter = "1/800",
                DefaultISO = "100",
                ReferenceShortEdge = 1800
            },
            new()
            {
                Name = "签名水印",
                Scale = 100,
                MarginTop = 0,
                MarginBottom = 64,
                MarginLeft = 0,
                MarginRight = 0,
                Corner = 0,
                Shadow = 0,
                Spacing = 5,
                Layout = LayoutMode.SignatureWatermark_Bottom_Centered,
                IsMarginPriority = true,
                IsSyncVertical = false,
                IsSyncHorizontal = true,
                IsSmartAdaptation = false,
                ForceLogoPath = null,
                LogoOffsetY = 0,
                DefaultMake = "SONY",
                DefaultModel = "ILCE-7RM5",
                DefaultLens = "FE 70-200mm F2.8 GM OSS II",
                DefaultFocal = "70mm",
                DefaultFNumber = "f/2.8",
                DefaultShutter = "1/800",
                DefaultISO = "100",
                DefaultLocation = "上海市",
                ReferenceShortEdge = 1800
            }
        };
    }

    private static IReadOnlyList<TemplateModel> CreateOverlayTemplates()
    {
        return new List<TemplateModel>
        {
            new()
            {
                Name = "底部机身带参数_overlay",
                Scale = 90,
                MarginTop = 150,
                MarginBottom = 400,
                MarginLeft = 150,
                MarginRight = 150,
                Corner = DefaultOverlayCornerRadius,
                Shadow = DefaultOverlayShadowSize,
                Spacing = 5,
                Layout = LayoutMode.TwoLines_Bottom_Centered,
                IsMarginPriority = true,
                IsSyncVertical = false,
                IsSyncHorizontal = true,
                IsSmartAdaptation = true,
                ForceLogoPath = null,
                LogoOffsetY = 0,
                DefaultMake = "SONY",
                DefaultModel = "ILCE-7RM5",
                DefaultLens = "FE 70-200mm F2.8 GM OSS II",
                DefaultFocal = "70mm",
                DefaultFNumber = "f/2.8",
                DefaultShutter = "1/800",
                DefaultISO = "100",
                ReferenceShortEdge = DefaultOverlayStyleReferenceShortEdge
            }
        };
    }
}
