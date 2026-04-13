using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using Yin.Models;

namespace Yin.Services;

/// <summary>
/// 渲染服务（负责根据上下文生成最终图像）
/// </summary>
public static class RenderingService
{
    private static readonly Uri SignatureFontBaseUri = new("pack://application:,,,/Yin;component/Source/", UriKind.Absolute);
    private const string SignatureFontFamilyPath = "./#方正字迹-周东芬草书 简";
    private const string SignatureFallbackFontFamily = "STXingkai";
    private const string SignatureLine1Text = "青山有思，白鹤忘机";
    private const string SignatureLine3Body = "唯有通透处事，方能从容自在";

    /// <summary>
    /// 生成最终图像（包含边框、品牌/参数、阴影与圆角等）
    /// </summary>
    public static RenderTargetBitmap RenderFinalImage(RenderContext ctx)
    {
        if (ctx.CurrentImage == null) throw new ArgumentNullException(nameof(ctx.CurrentImage));
        if (ctx.Layout == LayoutMode.SignatureWatermark_Bottom_Centered)
        {
            return RenderSignatureWatermarkImage(ctx);
        }

        double scalePercent = ctx.ScalePercent / 100.0;
        double marginTop = ctx.MarginTop;
        double marginBottom = ctx.MarginBottom;
        double marginLeft = ctx.MarginLeft;
        double marginRight = ctx.MarginRight;
        double cornerRadius = ctx.CornerRadius;
        double shadowSize = ctx.ShadowSize;
        double textSpacing = ctx.TextSpacing;
        double logoOffsetY = ctx.LogoOffsetY;
        double wImg = ctx.CurrentImage.PixelWidth;
        double hImg = ctx.CurrentImage.PixelHeight;

        double wBorder, hBorder;
        double finalMarginTop, finalMarginBottom, finalMarginLeft, finalMarginRight;

        if (ctx.IsMarginPriority)
        {
            finalMarginTop = marginTop;
            finalMarginBottom = marginBottom;
            finalMarginLeft = marginLeft;
            finalMarginRight = marginRight;
            wBorder = wImg + marginLeft + marginRight;
            hBorder = hImg + marginTop + marginBottom;
        }
        else
        {
            double hBorderBase = hImg / scalePercent;
            double baseMargin = (hBorderBase - hImg) / 2;
            finalMarginTop = Math.Max(baseMargin, marginTop);
            finalMarginBottom = Math.Max(baseMargin, marginBottom);
            finalMarginLeft = Math.Max(baseMargin, marginLeft);
            finalMarginRight = Math.Max(baseMargin, marginRight);
            wBorder = wImg + finalMarginLeft + finalMarginRight;
            hBorder = hImg + finalMarginTop + finalMarginBottom;
        }

        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, wBorder, hBorder));
            double xImg = finalMarginLeft;
            double yImg = finalMarginTop;
            Rect imgRect = new Rect(xImg, yImg, wImg, hImg);
            Geometry clipGeom = new RectangleGeometry(imgRect, cornerRadius, cornerRadius);
            dc.PushClip(clipGeom);
            dc.DrawImage(ctx.CurrentImage, imgRect);
            dc.Pop();
        }

        return RenderUsingVisualTree(ctx, wImg, hImg, wBorder, hBorder,
            scalePercent, finalMarginTop, finalMarginBottom, finalMarginLeft, finalMarginRight,
            cornerRadius, shadowSize, textSpacing, logoOffsetY);
    }

    public static RenderTargetBitmap RenderOverlayImage(RenderContext ctx)
    {
        if (ctx.CurrentImage == null) throw new ArgumentNullException(nameof(ctx.CurrentImage));
        double wImg = ctx.CurrentImage.PixelWidth;
        double hImg = ctx.CurrentImage.PixelHeight;
        double scalePercent = ctx.ScalePercent / 100.0;
        double marginTop = ctx.MarginTop;
        double textSpacing = ctx.TextSpacing;
        double marginBottom = ctx.MarginBottom;
        double marginLeft = ctx.MarginLeft;
        double marginRight = ctx.MarginRight;
        double logoOffsetY = ctx.LogoOffsetY;
        double cornerRadius = ctx.CornerRadius;
        double shadowSize = ctx.ShadowSize;
        double wBorder, hBorder;
        double finalMarginTop, finalMarginBottom, finalMarginLeft, finalMarginRight;
        if (ctx.IsMarginPriority)
        {
            finalMarginTop = marginTop;
            finalMarginBottom = marginBottom;
            finalMarginLeft = marginLeft;
            finalMarginRight = marginRight;
            wBorder = wImg + marginLeft + marginRight;
            hBorder = hImg + marginTop + marginBottom;
        }
        else
        {
            double hBorderBase = hImg / scalePercent;
            double baseMargin = (hBorderBase - hImg) / 2;
            finalMarginTop = Math.Max(baseMargin, marginTop);
            finalMarginBottom = Math.Max(baseMargin, marginBottom);
            finalMarginLeft = Math.Max(baseMargin, marginLeft);
            finalMarginRight = Math.Max(baseMargin, marginRight);
            wBorder = wImg + finalMarginLeft + finalMarginRight;
            hBorder = hImg + finalMarginTop + finalMarginBottom;
        }

        Grid grid = new Grid
        {
            Width = wBorder,
            Height = hBorder,
            Background = Brushes.Transparent
        };

        Rectangle bgRect = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        double longest = Math.Max(wImg, hImg);
        double targetLongest = 15.0;
        double ds = Math.Min(1.0, targetLongest / longest);
        TransformedBitmap down = new TransformedBitmap();
        down.BeginInit();
        down.Source = ctx.CurrentImage;
        down.Transform = new ScaleTransform(ds, ds);
        down.EndInit();
        bgRect.Fill = new ImageBrush(down)
        {
            Stretch = Stretch.UniformToFill,
            AlignmentX = AlignmentX.Center,
            AlignmentY = AlignmentY.Center
        };
        RenderOptions.SetBitmapScalingMode(bgRect, BitmapScalingMode.Linear);
        bgRect.Effect = new BlurEffect { Radius = 30 };
        grid.Children.Add(bgRect);

        Rectangle vignetteRect = new Rectangle
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            IsHitTestVisible = false
        };
        RadialGradientBrush vignetteBrush = new RadialGradientBrush
        {
            Center = new Point(0.5, 0.5),
            GradientOrigin = new Point(0.5, 0.5),
            RadiusX = 0.8,
            RadiusY = 0.8
        };
        vignetteBrush.GradientStops.Add(new GradientStop(Color.FromArgb(0, 0, 0, 0), 0.0));
        vignetteBrush.GradientStops.Add(new GradientStop(Color.FromArgb(60, 0, 0, 0), 0.6));
        vignetteBrush.GradientStops.Add(new GradientStop(Color.FromArgb(140, 0, 0, 0), 1.0));
        vignetteRect.Fill = vignetteBrush;
        grid.Children.Add(vignetteRect);

        Border imgContainer = new Border
        {
            Width = wImg,
            Height = hImg,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(finalMarginLeft, finalMarginTop, finalMarginRight, finalMarginBottom),
            Effect = null
        };

        if (shadowSize > 0)
        {
            imgContainer.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 315,
                ShadowDepth = shadowSize / 2,
                BlurRadius = shadowSize,
                Opacity = 0.4
            };
        }

        Border imgBorder = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new ImageBrush(ctx.CurrentImage) { Stretch = Stretch.Fill }
        };
        RenderOptions.SetBitmapScalingMode(imgBorder, BitmapScalingMode.HighQuality);
        imgContainer.Child = imgBorder;
        grid.Children.Add(imgContainer);

        if (ctx.Layout == LayoutMode.TwoLines_Bottom_Centered)
        {
            Brush textBrush = Brushes.White;
            Brush subTextBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
            bool showSmartPanel = true;

            if (ctx.IsSmartAdaptation && ctx.CurrentImage is BitmapSource bmp)
            {
                Rect analysisRect = new Rect(0, bmp.PixelHeight * 0.85, bmp.PixelWidth, bmp.PixelHeight * 0.15);
                var stats = AnalyzeRegion(bmp, analysisRect);
                if (stats.avgLuma > 0.7)
                {
                    textBrush = Brushes.Black;
                    subTextBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
                }
            }

            // 底部遮罩已移除

            StackPanel spContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, Math.Max(10, (finalMarginBottom * 0.3) - logoOffsetY))
            };

            double refDim = Math.Min(wBorder, hBorder);
            double fontSizeL1 = refDim * 0.025;
            string GetValue(string? exifVal, string boxVal)
            {
                if (!string.IsNullOrWhiteSpace(exifVal)) return exifVal;
                return boxVal ?? "";
            }

            string makeStr = GetValue(ctx.Exif?.Make, ctx.TxtMake);
            if (string.IsNullOrWhiteSpace(makeStr)) makeStr = "CAMERA";

            TextBlock txtBrand = new TextBlock
            {
                Text = makeStr.ToUpper() + " ",
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Bold,
                FontSize = fontSizeL1,
                Foreground = textBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            string modelStr = GetValue(ctx.Exif?.Model?.Replace("ILCE-", "ILCE-"), ctx.TxtModel);
            TextBlock txtModel = new TextBlock
            {
                Text = modelStr,
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Normal,
                FontSize = fontSizeL1,
                Foreground = textBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            StackPanel spLine1 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, hBorder * 0.005)
            };

            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect
                {
                    Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                spLine1.Effect = glow;
            }
            spLine1.Children.Add(txtBrand);
            spLine1.Children.Add(txtModel);

            string lens = GetValue(ctx.Exif?.LensModel, ctx.TxtLens);
            string focal = GetValue(ctx.Exif?.FocalLength, ctx.TxtFocal);
            string aperture = GetValue(ctx.Exif?.FNumber, ctx.TxtFNumber);
            string shutter = GetValue(ctx.Exif?.ExposureTime, ctx.TxtShutter);
            string iso = GetValue(ctx.Exif?.ISOSpeed, ctx.TxtISO);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(lens)) parts.Add(lens);
            if (!string.IsNullOrEmpty(focal)) parts.Add(focal);
            if (!string.IsNullOrEmpty(aperture)) parts.Add(aperture);
            if (!string.IsNullOrEmpty(shutter)) parts.Add(shutter);
            if (!string.IsNullOrEmpty(iso) && !iso.StartsWith("ISO")) parts.Add("ISO" + iso);
            else if (!string.IsNullOrEmpty(iso)) parts.Add(iso);
            string line2Text = string.Join("  ", parts);

            TextBlock txtLine2 = new TextBlock
            {
                Text = line2Text,
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Normal,
                FontSize = fontSizeL1 * 0.75,
                Foreground = subTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };

            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect
                {
                    Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                txtLine2.Effect = glow;
            }

            spContainer.Children.Add(spLine1);
            spContainer.Children.Add(txtLine2);
            grid.Children.Add(spContainer);
        }

        grid.Measure(new Size(wBorder, hBorder));
        grid.Arrange(new Rect(0, 0, wBorder, hBorder));
        grid.UpdateLayout();
        double outScale = (ctx.OutputScale <= 0 || ctx.OutputScale > 1.0) ? 1.0 : ctx.OutputScale;
        int outW = Math.Max(1, (int)Math.Round(wBorder * outScale));
        int outH = Math.Max(1, (int)Math.Round(hBorder * outScale));
        RenderTargetBitmap rtb = new RenderTargetBitmap(outW, outH, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        return rtb;
    }
    private static (double avgLuma, double variance) AnalyzeRegion(BitmapSource source, Rect region)
    {
        try
        {
            int x = (int)Math.Max(0, region.X);
            int y = (int)Math.Max(0, region.Y);
            int w = (int)Math.Min(source.PixelWidth - x, region.Width);
            int h = (int)Math.Min(source.PixelHeight - y, region.Height);
            if (w <= 0 || h <= 0) return (0, 0);
            CroppedBitmap crop = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            FormatConvertedBitmap gray = new FormatConvertedBitmap();
            gray.BeginInit();
            gray.Source = crop;
            gray.DestinationFormat = PixelFormats.Gray8;
            gray.EndInit();
            int stride = w;
            byte[] pixels = new byte[h * stride];
            gray.CopyPixels(pixels, stride, 0);
            long sum = 0;
            long sumSq = 0;
            int step = 4;
            int count = 0;
            for (int i = 0; i < pixels.Length; i += step)
            {
                int val = pixels[i];
                sum += val;
                sumSq += (val * val);
                count++;
            }
            if (count == 0) return (0, 0);
            double mean = (double)sum / count;
            double variance = ((double)sumSq / count) - (mean * mean);
            return (mean / 255.0, variance);
        }
        catch
        {
            return (0.5, 0);
        }
    }

    private static FontFamily GetSignatureFontFamily()
    {
        try
        {
            return new FontFamily(SignatureFontBaseUri, SignatureFontFamilyPath);
        }
        catch
        {
            return new FontFamily(SignatureFallbackFontFamily);
        }
    }

    private static string GetPreferredValue(string? exifValue, string fallbackValue)
    {
        return !string.IsNullOrWhiteSpace(exifValue) ? exifValue : (fallbackValue ?? "");
    }

    private static string BuildSignatureLine2(RenderContext ctx)
    {
        string model = GetPreferredValue(ctx.Exif?.Model, ctx.TxtModel).Trim();
        string lens = GetPreferredValue(ctx.Exif?.LensModel, ctx.TxtLens).Trim();

        if (string.IsNullOrWhiteSpace(model))
        {
            model = "CAMERA";
        }

        if (string.IsNullOrWhiteSpace(lens))
        {
            lens = "LENS";
        }

        if (!string.IsNullOrWhiteSpace(model) && !string.IsNullOrWhiteSpace(lens))
        {
            return $"{model} | {lens}";
        }

        if (!string.IsNullOrWhiteSpace(model))
        {
            return model;
        }

        if (!string.IsNullOrWhiteSpace(lens))
        {
            return lens;
        }

        return "Unknown Model";
    }

    private static string BuildSignatureLine3(RenderContext ctx)
    {
        string location = GetPreferredValue(ctx.Exif?.LocationText, ctx.TxtLocation).Trim();
        return location;
    }

    private static RenderTargetBitmap RenderSignatureWatermarkImage(RenderContext ctx)
    {
        if (ctx.CurrentImage == null) throw new ArgumentNullException(nameof(ctx.CurrentImage));

        double width = ctx.CurrentImage.PixelWidth;
        double height = ctx.CurrentImage.PixelHeight;
        double shortEdge = Math.Min(width, height);
        double bottomOffset = Math.Clamp(ctx.MarginBottom, shortEdge * 0.03, shortEdge * 0.18);

        Grid grid = new Grid
        {
            Width = width,
            Height = height,
            Background = Brushes.Transparent
        };

        Image image = new Image
        {
            Source = ctx.CurrentImage,
            Stretch = Stretch.Fill,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.HighQuality);
        grid.Children.Add(image);

        StackPanel signatureContainer = new StackPanel
        {
            Orientation = Orientation.Vertical,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, bottomOffset)
        };

        TextBlock line1 = new TextBlock
        {
            Text = SignatureLine1Text,
            FontFamily = GetSignatureFontFamily(),
            FontSize = shortEdge * 0.038,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, shortEdge * 0.005),
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(0.95, 1.0)
        };

        TextBlock line2 = new TextBlock
        {
            Text = BuildSignatureLine2(ctx),
            FontFamily = new FontFamily("Bahnschrift"),
            FontWeight = FontWeights.SemiBold,
            FontSize = shortEdge * 0.0174,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center,
            TextAlignment = TextAlignment.Center,
            Margin = new Thickness(0, 0, 0, shortEdge * 0.009)
        };

        StackPanel line3 = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock line3OpenQuote = new TextBlock
        {
            Text = "\u201C",
            FontFamily = new FontFamily("SimSun"),
            FontSize = shortEdge * 0.0175,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock line3Body = new TextBlock
        {
            Text = SignatureLine3Body,
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = shortEdge * 0.0172,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        TextBlock line3CloseQuote = new TextBlock
        {
            Text = "\u201D",
            FontFamily = new FontFamily("SimSun"),
            FontSize = shortEdge * 0.0175,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center
        };

        string location = BuildSignatureLine3(ctx);
        TextBlock line3Location = new TextBlock
        {
            Text = location,
            FontFamily = new FontFamily("Microsoft YaHei"),
            FontSize = shortEdge * 0.0172,
            Foreground = Brushes.White,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(shortEdge * 0.012, 0, 0, 0)
        };

        line3.Children.Add(line3OpenQuote);
        line3.Children.Add(line3Body);
        line3.Children.Add(line3CloseQuote);
        if (!string.IsNullOrWhiteSpace(location))
        {
            line3.Children.Add(line3Location);
        }

        signatureContainer.Children.Add(line1);
        signatureContainer.Children.Add(line2);
        signatureContainer.Children.Add(line3);
        grid.Children.Add(signatureContainer);

        grid.Measure(new Size(width, height));
        grid.Arrange(new Rect(0, 0, width, height));
        grid.UpdateLayout();

        RenderTargetBitmap rtb = new RenderTargetBitmap((int)width, (int)height, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        return rtb;
    }

    private static RenderTargetBitmap RenderUsingVisualTree(
        RenderContext ctx,
        double wImg, double hImg, double wBorder, double hBorder,
        double scale, double marginTop, double marginBottom, double marginLeft, double marginRight,
        double cornerRadius, double shadowSize, double textSpacing, double logoOffsetY)
    {
        Grid grid = new Grid
        {
            Width = wBorder,
            Height = hBorder,
            Background = Brushes.White
        };

        Border imgContainer = new Border
        {
            Width = wImg,
            Height = hImg,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom)
        };

        if (shadowSize > 0)
        {
            imgContainer.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 315,
                ShadowDepth = shadowSize / 2,
                BlurRadius = shadowSize,
                Opacity = 0.4
            };
        }

        Border imgBorder = new Border
        {
            CornerRadius = new CornerRadius(cornerRadius),
            Background = new ImageBrush(ctx.CurrentImage) { Stretch = Stretch.Fill }
        };
        RenderOptions.SetBitmapScalingMode(imgBorder, BitmapScalingMode.HighQuality);
        imgContainer.Child = imgBorder;
        grid.Children.Add(imgContainer);

        string brandText = (ctx.Exif?.Make ?? "CAMERA").ToUpper();
        if (brandText.Contains("HASSELBLAD")) brandText = "HASSELBLAD";
        else if (brandText.Contains("SONY")) brandText = "SONY";
        else if (brandText.Contains("NIKON")) brandText = "NIKON";
        else if (brandText.Contains("CANON")) brandText = "CANON";
        else if (brandText.Contains("FUJI")) brandText = "FUJIFILM";
        else if (brandText.Contains("LEICA")) brandText = "LEICA";

        FrameworkElement? brandElement = null;

        if (ctx.Template != null && !string.IsNullOrEmpty(ctx.Template.ForceLogoPath))
        {
            string resourcePath = $"pack://application:,,,/Yin;component/{ctx.Template.ForceLogoPath.Replace('\\', '/')}";
            try
            {
                var logo = new BitmapImage();
                logo.BeginInit();
                logo.UriSource = new Uri(resourcePath);
                logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.EndInit();
                Image imgLogo = new Image
                {
                    Source = logo,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                if (ctx.Template?.Name == "哈苏水印边框")
                {
                    double refDim = Math.Min(wBorder, hBorder);
                    double factor = (ctx.Template.ReferenceShortEdge > 0) ? refDim / ctx.Template.ReferenceShortEdge : 1.0;
                    imgLogo.Height = 32 * factor;
                    RenderOptions.SetBitmapScalingMode(imgLogo, BitmapScalingMode.HighQuality);
                }
                else
                {
                    imgLogo.Height = hBorder * 0.025;
                }
                brandElement = imgLogo;
            }
            catch { }
        }

        if (brandElement == null && brandText == "HASSELBLAD")
        {
            string resourcePath = "pack://application:,,,/Yin;component/Source/Hasselblad.png";
            try
            {
                var logo = new BitmapImage();
                logo.BeginInit();
                logo.UriSource = new Uri(resourcePath);
                logo.CacheOption = BitmapCacheOption.OnLoad;
                logo.EndInit();
                Image imgLogo = new Image
                {
                    Source = logo,
                    Stretch = Stretch.Uniform,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                if (ctx.Template?.Name == "哈苏水印边框")
                {
                    double refDim = Math.Min(wBorder, hBorder);
                    double factor = (ctx.Template.ReferenceShortEdge > 0) ? refDim / ctx.Template.ReferenceShortEdge : 1.0;
                    imgLogo.Height = 32 * factor;
                    RenderOptions.SetBitmapScalingMode(imgLogo, BitmapScalingMode.HighQuality);
                }
                else
                {
                    imgLogo.Height = hBorder * 0.025;
                }
                brandElement = imgLogo;
            }
            catch { }
        }

        if (brandElement == null)
        {
            StackPanel spBrand = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            double fontSize = hBorder * 0.02;
            FontFamily font = new FontFamily("Arial");
            FontWeight weight = FontWeights.Bold;
            Brush brush = Brushes.Black;
            for (int i = 0; i < brandText.Length; i++)
            {
                TextBlock charBlock = new TextBlock
                {
                    Text = brandText[i].ToString(),
                    FontFamily = font,
                    FontWeight = weight,
                    FontSize = fontSize,
                    Foreground = brush
                };
                if (i < brandText.Length - 1)
                {
                    charBlock.Margin = new Thickness(0, 0, textSpacing, 0);
                }
                spBrand.Children.Add(charBlock);
            }
            brandElement = spBrand;
        }

        if (ctx.Layout == LayoutMode.BrandTop_ExifBottom)
        {
            Grid topRegion = new Grid
            {
                VerticalAlignment = VerticalAlignment.Top,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = marginTop
            };
            brandElement.HorizontalAlignment = HorizontalAlignment.Center;
            brandElement.VerticalAlignment = VerticalAlignment.Center;
            topRegion.Children.Add(brandElement);
            grid.Children.Add(topRegion);
        }
        else if (ctx.Layout == LayoutMode.BrandBottom_Centered)
        {
            Brush textBrush = Brushes.White;
            string logoPath = ctx.Template?.ForceLogoPath ?? "Source/Hasselblad_white.png";
            if (ctx.IsSmartAdaptation && ctx.CurrentImage is BitmapSource bmp)
            {
                Rect analysisRect = new Rect(0, bmp.PixelHeight * 0.85, bmp.PixelWidth, bmp.PixelHeight * 0.15);
                var stats = AnalyzeRegion(bmp, analysisRect);
                if (stats.avgLuma > 0.7)
                {
                    textBrush = Brushes.Black;
                    if (logoPath.Contains("white")) logoPath = logoPath.Replace("white", "black");
                    else if (!logoPath.Contains("black")) logoPath = "Source/Hasselblad.png";
                }
            }
            brandElement.VerticalAlignment = VerticalAlignment.Bottom;
            if (brandElement is Image && !((BitmapImage)((Image)brandElement).Source).UriSource.OriginalString.Contains(logoPath))
            {
                try
                {
                    string resourcePath = $"pack://application:,,,/Yin;component/{logoPath}";
                    var logo = new BitmapImage();
                    logo.BeginInit();
                    logo.UriSource = new Uri(resourcePath);
                    logo.CacheOption = BitmapCacheOption.OnLoad;
                    logo.EndInit();
                    ((Image)brandElement).Source = logo;
                }
                catch { }
            }
            else if (brandElement is TextBlock tb) tb.Foreground = textBrush;
            else if (brandElement is StackPanel sp)
            {
                foreach (var child in sp.Children)
                {
                    if (child is TextBlock ctb) ctb.Foreground = textBrush;
                }
            }
            brandElement.VerticalAlignment = VerticalAlignment.Bottom;
            bool portrait = ctx.CurrentImage.PixelHeight > ctx.CurrentImage.PixelWidth;
            double coef = portrait ? 1.6 : 1.3;
            var offset = (marginBottom * coef) + logoOffsetY;
            brandElement.Margin = new Thickness(0, 0, 0, offset);
            grid.Children.Add(brandElement);
        }
        else if (ctx.Layout == LayoutMode.TwoLines_Bottom_Centered)
        {
            Brush textBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Brush subTextBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            bool showSmartPanel = false;
            if (ctx.IsSmartAdaptation && ctx.CurrentImage is BitmapSource bmp)
            {
                Rect analysisRect = new Rect(0, bmp.PixelHeight * 0.85, bmp.PixelWidth, bmp.PixelHeight * 0.15);
                var stats = AnalyzeRegion(bmp, analysisRect);
                bool isCompact = (marginBottom < 100);
                if (isCompact)
                {
                    if (stats.avgLuma < 0.5)
                    {
                        textBrush = Brushes.White;
                        subTextBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                    }
                    if (stats.variance > 2000)
                    {
                        showSmartPanel = true;
                    }
                }
            }
            StackPanel spContainer = new StackPanel
            {
                Orientation = Orientation.Vertical,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom
            };
            Grid panelGrid = new Grid();
            if (showSmartPanel)
            {
                Border panelBg = new Border
                {
                    Background = (textBrush == Brushes.White) ? Brushes.Black : Brushes.White,
                    Opacity = 0.3,
                    CornerRadius = new CornerRadius(10),
                    Effect = new BlurEffect { Radius = 20 }
                };
                panelGrid.Children.Add(panelBg);
                spContainer.Margin = new Thickness(20, 10, 20, 10);
            }
            StackPanel spLine1 = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, hBorder * 0.005)
            };
            double refDim = Math.Min(wBorder, hBorder);
            double fontSizeL1 = refDim * 0.025;
            string GetValue(string? exifVal, string boxVal)
            {
                if (!string.IsNullOrWhiteSpace(exifVal)) return exifVal;
                return boxVal ?? "";
            }
            string makeStr = GetValue(ctx.Exif?.Make, ctx.TxtMake);
            if (string.IsNullOrWhiteSpace(makeStr)) makeStr = "CAMERA";
            TextBlock txtBrand = new TextBlock
            {
                Text = makeStr.ToUpper() + " ",
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Bold,
                FontSize = fontSizeL1,
                Foreground = textBrush
            };
            string modelStr = GetValue(ctx.Exif?.Model?.Replace("ILCE-", "ILCE-"), ctx.TxtModel);
            TextBlock txtModel = new TextBlock
            {
                Text = modelStr,
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Normal,
                FontSize = fontSizeL1,
                Foreground = textBrush
            };
            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect
                {
                    Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                spLine1.Effect = glow;
            }
            spLine1.Children.Add(txtBrand);
            spLine1.Children.Add(txtModel);
            string lens = GetValue(ctx.Exif?.LensModel, ctx.TxtLens);
            string focal = GetValue(ctx.Exif?.FocalLength, ctx.TxtFocal);
            string aperture = GetValue(ctx.Exif?.FNumber, ctx.TxtFNumber);
            string shutter = GetValue(ctx.Exif?.ExposureTime, ctx.TxtShutter);
            string iso = GetValue(ctx.Exif?.ISOSpeed, ctx.TxtISO);
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(lens)) parts.Add(lens);
            if (!string.IsNullOrEmpty(focal)) parts.Add(focal);
            if (!string.IsNullOrEmpty(aperture)) parts.Add(aperture);
            if (!string.IsNullOrEmpty(shutter)) parts.Add(shutter);
            if (!string.IsNullOrEmpty(iso) && !iso.StartsWith("ISO")) parts.Add("ISO" + iso);
            else if (!string.IsNullOrEmpty(iso)) parts.Add(iso);
            string line2Text = string.Join("  ", parts);
            TextBlock txtLine2 = new TextBlock
            {
                Text = line2Text,
                FontFamily = new FontFamily("Bahnschrift"),
                FontWeight = FontWeights.Normal,
                FontSize = fontSizeL1 * 0.75,
                Foreground = subTextBrush,
                HorizontalAlignment = HorizontalAlignment.Center
            };
            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect
                {
                    Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White,
                    BlurRadius = 8,
                    ShadowDepth = 0,
                    Opacity = 0.8
                };
                txtLine2.Effect = glow;
            }
            spContainer.Children.Add(spLine1);
            spContainer.Children.Add(txtLine2);
            if (showSmartPanel)
            {
                panelGrid.Children.Add(spContainer);
                panelGrid.HorizontalAlignment = HorizontalAlignment.Center;
                panelGrid.VerticalAlignment = VerticalAlignment.Bottom;
                panelGrid.Margin = new Thickness(0, 0, 0, (marginBottom * 0.3) - logoOffsetY);
                grid.Children.Add(panelGrid);
            }
            else
            {
                spContainer.VerticalAlignment = VerticalAlignment.Bottom;
                spContainer.Margin = new Thickness(0, 0, 0, (marginBottom * 0.3) - logoOffsetY);
                grid.Children.Add(spContainer);
            }
        }
        if (ctx.Layout == LayoutMode.BrandTop_ExifBottom && ctx.Exif != null)
        {
            string focal = !string.IsNullOrWhiteSpace(ctx.TxtFocal) ? ctx.TxtFocal : (ctx.Exif.FocalLength);
            string aperture = !string.IsNullOrWhiteSpace(ctx.TxtFNumber) ? ctx.TxtFNumber : (ctx.Exif.FNumber);
            string shutter = !string.IsNullOrWhiteSpace(ctx.TxtShutter) ? ctx.TxtShutter : (ctx.Exif.ExposureTime);
            string iso = !string.IsNullOrWhiteSpace(ctx.TxtISO) ? ctx.TxtISO : (ctx.Exif.ISOSpeed);
            if (!iso.StartsWith("ISO") && !string.IsNullOrEmpty(iso)) iso = "ISO" + iso;
            string exifText = $"FL {focal}   Aperture {aperture}   Shutter {shutter}   {iso}";
            Grid bottomRegion = new Grid
            {
                VerticalAlignment = VerticalAlignment.Bottom,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = marginBottom
            };
            TextBlock txtExif = new TextBlock
            {
                Text = exifText,
                FontFamily = new FontFamily("Arial"),
                FontSize = Math.Min(wBorder, hBorder) * 0.018,
                Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50)),
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            };
            bottomRegion.Children.Add(txtExif);
            grid.Children.Add(bottomRegion);
        }

        grid.Measure(new Size(wBorder, hBorder));
        grid.Arrange(new Rect(0, 0, wBorder, hBorder));
        grid.UpdateLayout();
        RenderTargetBitmap rtb = new RenderTargetBitmap((int)wBorder, (int)hBorder, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        return rtb;
    }
}
