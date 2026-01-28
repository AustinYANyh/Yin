using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;

namespace Yin;

public partial class MainWindow : Window
{
    private BitmapImage? _currentImage;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;

    public MainWindow()
    {
        InitializeComponent();
    }

    // Data class for EXIF
    public class ExifInfo
    {
        public string Make { get; set; } = "";
        public string Model { get; set; } = "";
        public string FocalLength { get; set; } = "";
        public string FNumber { get; set; } = "";
        public string ExposureTime { get; set; } = "";
        public string ISOSpeed { get; set; } = "";
        public DateTime DateTaken { get; set; }
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*"
        };
        if (openFileDialog.ShowDialog() == true)
        {
            LoadImage(openFileDialog.FileName);
        }
    }

    private void Image_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0)
            {
                LoadImage(files[0]);
            }
        }
    }

    private void Image_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void LoadImage(string path)
    {
        try
        {
            _currentFilePath = path;
            
            // Load image with cache option to keep file unlocked if needed, 
            // but for simple app OnLoad is fine.
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad; // Load into memory
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze(); // Make it cross-thread accessible if needed

            _currentImage = bitmap;
            // Pass file path to read metadata reliably
            _currentExif = ReadExifData(path);
            
            UpdatePreview();
            TxtStatus.Text = $"Loaded: {Path.GetFileName(path)} | {_currentExif.Model}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading image: {ex.Message}");
        }
    }

    private ExifInfo ReadExifData(string filePath)
    {
        var info = new ExifInfo();
        try
        {
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.IgnoreColorProfile, BitmapCacheOption.OnLoad);
                if (decoder.Frames.Count > 0 && decoder.Frames[0].Metadata is BitmapMetadata metadata)
                {
                    // Helper to get query raw value safely
                    object? GetQueryRaw(string query)
                    {
                        try { return metadata.GetQuery(query); }
                        catch { return null; }
                    }
                    
                    // Helper to parse rational from potential ulong (packed) or other types
                    double? GetRational(string query)
                    {
                        var val = GetQueryRaw(query);
                        if (val is ulong u)
                        {
                            // WPF packs Rationals into ulong: High 32 bits = Denominator, Low 32 bits = Numerator
                            uint den = (uint)(u >> 32);
                            uint num = (uint)(u & 0xFFFFFFFFL);
                            if (den == 0) return null;
                            return (double)num / den;
                        }
                        if (val is double d) return d;
                        if (val is decimal dec) return (double)dec;
                        if (val is string s && double.TryParse(s, out double parsed)) return parsed;
                        return null;
                    }

                    string GetString(string query)
                    {
                         return GetQueryRaw(query)?.ToString() ?? "";
                    }
        
                    info.Make = GetString("/app1/ifd/0/{ushort=271}");
                    info.Model = GetString("/app1/ifd/0/{ushort=272}");
                    
                    // Aperture (FNumber)
                    var fVal = GetRational("/app1/ifd/exif/{ushort=33437}");
                    if (fVal.HasValue) info.FNumber = $"f/{fVal.Value:0.0}";
                    else info.FNumber = "";

                    // Shutter Speed (ExposureTime)
                    var tVal = GetRational("/app1/ifd/exif/{ushort=33434}");
                    if (tVal.HasValue)
                    {
                        double t = tVal.Value;
                        if (t < 1 && t > 0)
                            info.ExposureTime = $"1/{Math.Round(1/t)}";
                        else
                            info.ExposureTime = t.ToString("0.#####"); // Handle long exposure > 1s
                    }
                    else info.ExposureTime = "";

                    // ISO
                    info.ISOSpeed = GetString("/app1/ifd/exif/{ushort=34855}");
        
                    // Focal Length
                    var flVal = GetRational("/app1/ifd/exif/{ushort=37386}");
                    if (flVal.HasValue) info.FocalLength = $"{Math.Round(flVal.Value)}mm"; // Usually integer mm
                    else info.FocalLength = "";
        
                    // Date
                    string date = GetString("/app1/ifd/exif/{ushort=36867}");
                    if (DateTime.TryParseExact(date, "yyyy:MM:dd HH:mm:ss", null, System.Globalization.DateTimeStyles.None, out DateTime dt))
                        info.DateTaken = dt;
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Metadata read error: {ex.Message}");
        }
        
        // Clean up Model if it contains Make
        if (!string.IsNullOrEmpty(info.Make) && !string.IsNullOrEmpty(info.Model))
        {
            if (info.Model.StartsWith(info.Make, StringComparison.OrdinalIgnoreCase))
            {
                info.Model = info.Model.Substring(info.Make.Length).Trim();
            }
        }
        // Fallback for brand if empty
        if (string.IsNullOrEmpty(info.Make)) info.Make = "CAMERA";

        return info;
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage != null)
        {
            UpdatePreview();
        }
    }

    private void UpdatePreview()
    {
        if (_currentImage == null) return;

        // Debounce logic could be added here if performance is slow, 
        // but for a single image, immediate update is usually fine.
        var finalImage = RenderFinalImage();
        ImgPreview.Source = finalImage;
    }

    private RenderTargetBitmap RenderFinalImage()
    {
        // Parameters
        double scalePercent = SliderScale.Value / 100.0;
        double minMargin = SliderMargin.Value;
        double minHMargin = SliderHMargin.Value;
        double cornerRadius = SliderCorner.Value;
        double shadowSize = SliderShadow.Value;
        double textSpacing = SliderTextSpacing.Value;

        // Original Dimensions
        double wImg = _currentImage.PixelWidth;
        double hImg = _currentImage.PixelHeight;

        // Calculate Border Dimensions
        // New Logic: Scale determines the "Thickness" of the border uniformly
        // We calculate the border based on the Scale applied to the SHORTER side (or just vertical),
        // to ensure a uniform look, and then allow Min Margins to expand it.

        // 1. Calculate base border thickness based on Scale
        // hBorderBase = hImg / scale
        // vBaseMargin = (hBorderBase - hImg) / 2
        double hBorderBase = hImg / scalePercent;
        double baseMargin = (hBorderBase - hImg) / 2;

        // 2. Apply Min Margins
        // We use the same baseMargin for both Vertical and Horizontal to start with (Uniform Border)
        // Then we ensure it's at least minMargin / minHMargin
        double finalVMargin = Math.Max(baseMargin, minMargin);
        double finalHMargin = Math.Max(baseMargin, minHMargin);

        // 3. Calculate Final Border Dimensions
        double wBorder = wImg + (finalHMargin * 2);
        double hBorder = hImg + (finalVMargin * 2);

        // Create Visual
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // 1. Draw Background (White)
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, wBorder, hBorder));

            // 2. Draw Image with Shadow and Rounded Corners
            // We need a separate drawing for the image to apply effects
            
            // Calculate Image Position (Centered)
            double xImg = (wBorder - wImg) / 2;
            double yImg = (hBorder - hImg) / 2;
            Rect imgRect = new Rect(xImg, yImg, wImg, hImg);

            // Clip for rounded corners
            Geometry clipGeom = new RectangleGeometry(imgRect, cornerRadius, cornerRadius);
            dc.PushClip(clipGeom);

            // Draw Image
            dc.DrawImage(_currentImage, imgRect);
            
            dc.Pop(); // Pop Clip

            // Shadow - DrawingContext doesn't support DropShadowEffect directly on primitives easily in one pass.
            // A common trick is to draw the shadow rectangle first, then the image.
            // Or better: use a container visual if we were building a tree, but here we are drawing.
            // For simple drawing context, we can simulate shadow or draw a semi-transparent rectangle.
            // However, high quality blur shadow is hard with just DrawRectangle.
            // Let's try to simulate a simple shadow or skip complex blur for performance, 
            // OR use a separate visual for the image and apply effect, then render that visual into the bitmap.
        }

        // To support Shadow properly, it's easier to build a visual tree, arrange it, and then render.
        // Let's switch to that approach as it supports Effects and Layout easier.
        return RenderUsingVisualTree(wImg, hImg, wBorder, hBorder, scalePercent, minMargin, cornerRadius, shadowSize, textSpacing);
    }

    private RenderTargetBitmap RenderUsingVisualTree(double wImg, double hImg, double wBorder, double hBorder, 
        double scale, double minMargin, double cornerRadius, double shadowSize, double textSpacing)
    {
        // Container
        Grid grid = new Grid();
        grid.Width = wBorder;
        grid.Height = hBorder;
        grid.Background = Brushes.White;

        // Image Container (for Shadow and Corner)
        Border imgContainer = new Border();
        imgContainer.Width = wImg;
        imgContainer.Height = hImg;
        imgContainer.HorizontalAlignment = HorizontalAlignment.Center;
        imgContainer.VerticalAlignment = VerticalAlignment.Center;
        
        // Shadow
        if (shadowSize > 0)
        {
            imgContainer.Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                Direction = 315,
                ShadowDepth = shadowSize / 2, // Approximate depth
                BlurRadius = shadowSize,
                Opacity = 0.4
            };
        }

        // Image with Corner Radius
        // To clip an Image in WPF, we can use an ImageBrush on a Border with CornerRadius
        // OR use <Image> with <Image.Clip>. Border with ImageBrush is easier for CornerRadius.
        Border imgBorder = new Border();
        imgBorder.CornerRadius = new CornerRadius(cornerRadius);
        imgBorder.Background = new ImageBrush(_currentImage) { Stretch = Stretch.Fill }; // Use Fill because container is exactly image size
        // Ensure high quality scaling
        RenderOptions.SetBitmapScalingMode(imgBorder, BitmapScalingMode.HighQuality);

        imgContainer.Child = imgBorder;
        grid.Children.Add(imgContainer);

        // Text Info
        // Top: Brand (e.g. HASSELBLAD)
        // Bottom: EXIF
        
        // Brand Label
        string brandText = (_currentExif?.Make ?? "CAMERA").ToUpper();
        if (brandText.Contains("HASSELBLAD")) brandText = "HASSELBLAD"; // Normalize
        else if (brandText.Contains("SONY")) brandText = "SONY";
        else if (brandText.Contains("NIKON")) brandText = "NIKON";
        else if (brandText.Contains("CANON")) brandText = "CANON";
        else if (brandText.Contains("FUJI")) brandText = "FUJIFILM";
        else if (brandText.Contains("LEICA")) brandText = "LEICA";
        
        // Use StackPanel to simulate character spacing since WPF TextBlock doesn't support it directly
        StackPanel spBrand = new StackPanel();
        spBrand.Orientation = Orientation.Horizontal;
        spBrand.HorizontalAlignment = HorizontalAlignment.Center;
        spBrand.VerticalAlignment = VerticalAlignment.Top;

        // Position: Top margin relative to border
        // Let's place it halfway between top edge and image top.
        // Top Margin = (hBorder - hImg) / 2.
        double topSpace = (hBorder - hImg) / 2;
        spBrand.Margin = new Thickness(0, topSpace * 0.4, 0, 0); // Adjust position

        double fontSize = hBorder * 0.02;
        FontFamily font = new FontFamily("Arial");
        FontWeight weight = FontWeights.Bold;
        Brush brush = Brushes.Black;

        for (int i = 0; i < brandText.Length; i++)
        {
            TextBlock charBlock = new TextBlock();
            charBlock.Text = brandText[i].ToString();
            charBlock.FontFamily = font;
            charBlock.FontWeight = weight;
            charBlock.FontSize = fontSize;
            charBlock.Foreground = brush;
            
            // Add spacing to right of character, except the last one
            if (i < brandText.Length - 1)
            {
                charBlock.Margin = new Thickness(0, 0, textSpacing, 0);
            }
            
            spBrand.Children.Add(charBlock);
        }
        
        grid.Children.Add(spBrand);

        // EXIF Label
        // Format: "FL 28mm   Aperture f/2.4   Shutter 1/100   ISO200"
        if (_currentExif != null)
        {
            string exifText = $"FL {_currentExif.FocalLength}   Aperture {_currentExif.FNumber}   Shutter {_currentExif.ExposureTime}   ISO{_currentExif.ISOSpeed}";
            TextBlock txtExif = new TextBlock();
            txtExif.Text = exifText;
            txtExif.FontFamily = new FontFamily("Arial");
            txtExif.FontSize = hBorder * 0.015;
            txtExif.Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            txtExif.HorizontalAlignment = HorizontalAlignment.Center;
            txtExif.VerticalAlignment = VerticalAlignment.Bottom;
            txtExif.Margin = new Thickness(0, 0, 0, topSpace * 0.4); // Same distance from bottom
            
            grid.Children.Add(txtExif);
        }

        // Layout
        grid.Measure(new Size(wBorder, hBorder));
        grid.Arrange(new Rect(0, 0, wBorder, hBorder));
        grid.UpdateLayout();

        // Render
        RenderTargetBitmap rtb = new RenderTargetBitmap((int)wBorder, (int)hBorder, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        return rtb;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;

        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Filter = "JPEG Image|*.jpg",
            FileName = $"Frame_{Path.GetFileNameWithoutExtension(_currentFilePath)}.jpg"
        };

        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                var final = RenderFinalImage();
                JpegBitmapEncoder encoder = new JpegBitmapEncoder();
                encoder.QualityLevel = 100;
                encoder.Frames.Add(BitmapFrame.Create(final));

                using (FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create))
                {
                    encoder.Save(fs);
                }
                MessageBox.Show("Saved successfully!");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error saving: {ex.Message}");
            }
        }
    }
}
