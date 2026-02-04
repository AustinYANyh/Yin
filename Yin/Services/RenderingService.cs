using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using Yin.Models;

namespace Yin.Services;

/// <summary>
/// 渲染服务（负责根据上下文生成最终图像）
/// </summary>
public static class RenderingService
{
    /// <summary>
    /// 生成最终图像（包含边框、品牌/参数、阴影与圆角等）
    /// </summary>
    public static RenderTargetBitmap RenderFinalImage(RenderContext ctx)
    {
        if (ctx.CurrentImage == null) throw new ArgumentNullException(nameof(ctx.CurrentImage));
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

        FrameworkElement brandElement = null;

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
            bool isOverlay = false;
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
