using System.Windows.Media.Imaging;
using Yin.Models;

namespace Yin.Services;

public sealed class RenderTextOverrides
{
    public string Make { get; init; } = "";
    public string Model { get; init; } = "";
    public string Lens { get; init; } = "";
    public string Focal { get; init; } = "";
    public string FNumber { get; init; } = "";
    public string Shutter { get; init; } = "";
    public string ISO { get; init; } = "";
    public string Location { get; init; } = "";
}

public static class RenderContextFactory
{
    private const double SliderMinScale = 50;
    private const double SliderMaxScale = 95;
    private const double SliderMinMargin = 0;
    private const double SliderMaxMargin = 1000;
    private const double SliderMinCorner = 0;
    private const double SliderMaxCorner = 100;
    private const double SliderMinShadow = 0;
    private const double SliderMaxShadow = 100;
    private const double SliderMinSpacing = 0;
    private const double SliderMaxSpacing = 50;
    private const double SliderMinLogoOffsetY = -1000;
    private const double SliderMaxLogoOffsetY = 1000;
    private const double MinOverlayCornerRadius = 8;
    private const double MaxOverlayCornerRadius = 64;
    private const double MinOverlayShadowSize = 6;
    private const double MaxOverlayShadowSize = 40;

    public static RenderContext Create(
        BitmapSource image,
        ExifInfo? exif,
        TemplateModel template,
        WatermarkRenderMode mode,
        RenderTextOverrides? textOverrides = null)
    {
        textOverrides ??= new RenderTextOverrides();

        double scalePercent = Math.Clamp(template.Scale, SliderMinScale, SliderMaxScale);
        bool isSmartAdaptation = mode == WatermarkRenderMode.Overlay || template.IsSmartAdaptation;

        double marginTop;
        double marginBottom;
        double marginLeft;
        double marginRight;
        double cornerRadius;
        double shadowSize;
        double textSpacing;
        double logoOffsetY;

        if (template.ReferenceShortEdge > 0)
        {
            double shortEdge = Math.Min(image.PixelWidth, image.PixelHeight);
            double factor = Math.Clamp(shortEdge / template.ReferenceShortEdge, 0.1, 10.0);

            marginTop = ClampMargin(template.MarginTop * factor);
            marginBottom = ClampMargin(template.MarginBottom * factor);
            marginLeft = ClampMargin(template.MarginLeft * factor);
            marginRight = ClampMargin(template.MarginRight * factor);
            cornerRadius = Math.Clamp(template.Corner * factor, SliderMinCorner, SliderMaxCorner);
            shadowSize = Math.Clamp(template.Shadow * factor, SliderMinShadow, SliderMaxShadow);
            textSpacing = Math.Clamp(template.Spacing * factor, SliderMinSpacing, SliderMaxSpacing);
            logoOffsetY = Math.Clamp(template.LogoOffsetY * factor, SliderMinLogoOffsetY, SliderMaxLogoOffsetY);

            if (mode == WatermarkRenderMode.Border)
            {
                ApplyBorderTemplateTweaks(image, template, ref marginTop, ref marginBottom, ref marginLeft, ref marginRight, ref logoOffsetY);
                marginTop = ClampMargin(marginTop);
                marginBottom = ClampMargin(marginBottom);
                marginLeft = ClampMargin(marginLeft);
                marginRight = ClampMargin(marginRight);
                logoOffsetY = Math.Clamp(logoOffsetY, SliderMinLogoOffsetY, SliderMaxLogoOffsetY);
            }
            else
            {
                double refEdge = template.ReferenceShortEdge > 0
                    ? template.ReferenceShortEdge
                    : TemplateService.DefaultOverlayStyleReferenceShortEdge;
                double overlayFactor = Math.Clamp(shortEdge / refEdge, 0.35, 3.0);
                double baseCorner = template.Corner > 0 ? template.Corner : TemplateService.DefaultOverlayCornerRadius;
                double baseShadow = template.Shadow > 0 ? template.Shadow : TemplateService.DefaultOverlayShadowSize;
                cornerRadius = Math.Clamp(Math.Round(baseCorner * overlayFactor), MinOverlayCornerRadius, MaxOverlayCornerRadius);
                shadowSize = Math.Clamp(Math.Round(baseShadow * overlayFactor), MinOverlayShadowSize, MaxOverlayShadowSize);
            }
        }
        else
        {
            marginTop = ClampMargin(template.MarginTop);
            marginBottom = ClampMargin(template.MarginBottom);
            marginLeft = ClampMargin(template.MarginLeft);
            marginRight = ClampMargin(template.MarginRight);
            cornerRadius = Math.Clamp(template.Corner, SliderMinCorner, SliderMaxCorner);
            shadowSize = Math.Clamp(template.Shadow, SliderMinShadow, SliderMaxShadow);
            textSpacing = Math.Clamp(template.Spacing, SliderMinSpacing, SliderMaxSpacing);
            logoOffsetY = Math.Clamp(template.LogoOffsetY, SliderMinLogoOffsetY, SliderMaxLogoOffsetY);
        }

        return new RenderContext
        {
            CurrentImage = image,
            Exif = BuildEffectiveExif(exif, template, textOverrides),
            Template = template,
            Layout = template.Layout,
            IsMarginPriority = template.IsMarginPriority,
            IsSmartAdaptation = isSmartAdaptation,
            ScalePercent = scalePercent,
            MarginTop = marginTop,
            MarginBottom = marginBottom,
            MarginLeft = marginLeft,
            MarginRight = marginRight,
            CornerRadius = cornerRadius,
            ShadowSize = shadowSize,
            TextSpacing = textSpacing,
            LogoOffsetY = logoOffsetY,
            OutputScale = 1.0,
            TxtMake = textOverrides.Make,
            TxtModel = textOverrides.Model,
            TxtLens = textOverrides.Lens,
            TxtFocal = textOverrides.Focal,
            TxtFNumber = textOverrides.FNumber,
            TxtShutter = textOverrides.Shutter,
            TxtISO = textOverrides.ISO,
            TxtLocation = textOverrides.Location
        };
    }

    private static double ClampMargin(double value)
    {
        return Math.Clamp(value, SliderMinMargin, SliderMaxMargin);
    }

    private static ExifInfo BuildEffectiveExif(ExifInfo? exif, TemplateModel template, RenderTextOverrides overrides)
    {
        exif ??= new ExifInfo();
        return new ExifInfo
        {
            Make = FirstNonBlank(overrides.Make, exif.Make, template.DefaultMake),
            Model = FirstNonBlank(overrides.Model, exif.Model, template.DefaultModel),
            LensModel = FirstNonBlank(overrides.Lens, exif.LensModel, template.DefaultLens),
            FocalLength = FirstNonBlank(overrides.Focal, exif.FocalLength, template.DefaultFocal),
            FNumber = FirstNonBlank(overrides.FNumber, exif.FNumber, template.DefaultFNumber),
            ExposureTime = FirstNonBlank(overrides.Shutter, exif.ExposureTime, template.DefaultShutter),
            ISOSpeed = FirstNonBlank(overrides.ISO, exif.ISOSpeed, template.DefaultISO),
            LocationText = FirstNonBlank(overrides.Location, exif.LocationText, template.DefaultLocation),
            Latitude = exif.Latitude,
            Longitude = exif.Longitude,
            LocationDebugLog = exif.LocationDebugLog,
            DateTaken = exif.DateTaken
        };
    }

    private static string FirstNonBlank(params string?[] values)
    {
        foreach (string? value in values)
        {
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }
        }

        return "";
    }

    private static void ApplyBorderTemplateTweaks(
        BitmapSource img,
        TemplateModel template,
        ref double marginTop,
        ref double marginBottom,
        ref double marginLeft,
        ref double marginRight,
        ref double logoOffsetY)
    {
        double wImg = img.PixelWidth;
        double hImg = img.PixelHeight;

        if (template.Name == "哈苏水印边框")
        {
            marginTop *= 1.5;
            marginBottom *= 1.5;

            double wBorderPred = wImg + marginLeft + marginRight;
            double hBorderPred = hImg + marginTop + marginBottom;
            double refDim = Math.Min(wBorderPred, hBorderPred);
            double factorLogo = template.ReferenceShortEdge > 0 ? refDim / template.ReferenceShortEdge : 1.0;
            double logoHeight = 32 * factorLogo;
            double topMin = logoHeight * 2.0 + 10;
            double paramFont = refDim * 0.018;
            double bottomMin = paramFont * 2.5 + 10;
            double sideMin = paramFont * 2.0;

            marginTop = Math.Max(marginTop, topMin);
            marginBottom = Math.Max(marginBottom, bottomMin);
            double lr = Math.Max(Math.Max(marginLeft, marginRight), sideMin);
            marginLeft = lr;
            marginRight = lr;
        }

        if (template.Name == "哈苏水印居中")
        {
            double shortEdge = Math.Min(wImg, hImg);
            bool portrait = hImg > wImg;
            double edgeFactor = portrait ? 0.018 : 0.022;
            double margin = shortEdge * edgeFactor;
            marginTop = margin;
            marginBottom = margin;
            marginLeft = margin;
            marginRight = margin;
            logoOffsetY = shortEdge * (portrait ? 0.035 : 0.030);
        }

        if (template.Name == "签名水印")
        {
            double shortEdge = Math.Min(wImg, hImg);
            double minBottom = Math.Clamp(shortEdge * 0.045, 36, 96);
            marginTop = 0;
            marginLeft = 0;
            marginRight = 0;
            marginBottom = Math.Max(marginBottom, minBottom);
        }
    }
}
