using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;

namespace Yin;

public partial class MainWindow : Window
{
    private BitmapImage? _currentImage;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;

    // Template Logic
    public class TemplateModel
    {
        public string Name { get; set; } = "";
        public double Scale { get; set; }
        public double VMargin { get; set; }
        public double HMargin { get; set; }
        public double Corner { get; set; }
        public double Shadow { get; set; }
        public double Spacing { get; set; }
        public LayoutMode Layout { get; set; }
        public bool IsMarginPriority { get; set; } // If true, ignore Scale logic and trust margins strictly
        public string? ForceLogoPath { get; set; } // If set, force use this logo image
        public double LogoOffsetY { get; set; } // Vertical offset for logo
    }

    public enum LayoutMode
    {
        BrandTop_ExifBottom,
        BrandBottom_Centered
    }

    private List<TemplateModel> _templates = new List<TemplateModel>();
    private LayoutMode _currentLayout = LayoutMode.BrandTop_ExifBottom;
    private TemplateModel? _currentTemplate;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTemplates();
    }

    private void InitializeTemplates()
    {
        _templates.Add(new TemplateModel
        {
            Name = "无",
            Scale = 85,
            VMargin = 100,
            HMargin = 0,
            Corner = 100,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandTop_ExifBottom,
            IsMarginPriority = false,
            ForceLogoPath = null, // Use parsed logic
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印边框",
            Scale = 85,
            VMargin = 350,
            HMargin = 150,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandTop_ExifBottom,
            IsMarginPriority = true,
            ForceLogoPath = "Source/Hasselblad.png",
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印居中",
            Scale = 90,
            VMargin = 20,
            HMargin = 20,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandBottom_Centered,
            IsMarginPriority = true,
            ForceLogoPath = "Source/Hasselblad_white.png",
            LogoOffsetY = 0
        });

        CmbTemplates.ItemsSource = _templates;
        CmbTemplates.DisplayMemberPath = "Name";
        CmbTemplates.SelectedIndex = 0; // Default selection
    }

    private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTemplates.SelectedItem is TemplateModel tmpl)
        {
            _currentTemplate = tmpl;
            
            // Update sliders (will trigger update manually later)
            SliderScale.Value = tmpl.Scale;
            SliderMargin.Value = tmpl.VMargin;
            SliderHMargin.Value = tmpl.HMargin;
            SliderCorner.Value = tmpl.Corner;
            SliderShadow.Value = tmpl.Shadow;
            SliderTextSpacing.Value = tmpl.Spacing;
            SliderLogoOffsetY.Value = tmpl.LogoOffsetY;
            
            // Update Options
            ChkMarginPriority.IsChecked = tmpl.IsMarginPriority;
            
            _currentLayout = tmpl.Layout;
            
            if (_currentImage != null)
            {
                UpdatePreview();
            }
        }
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
            var directories = ImageMetadataReader.ReadMetadata(filePath);

            // 1. Get Make & Model from IFD0
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null)
            {
                info.Make = ifd0.GetDescription(ExifIfd0Directory.TagMake) ?? "";
                info.Model = ifd0.GetDescription(ExifIfd0Directory.TagModel) ?? "";
            }

            // 2. Get Shooting Info from SubIFD
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd != null)
            {
                // Aperture
                // Try to get formatted string first, if not custom format
                if (subIfd.TryGetDouble(ExifSubIfdDirectory.TagFNumber, out double f))
                {
                    info.FNumber = $"f/{f:0.0}";
                }
                else
                {
                    info.FNumber = subIfd.GetDescription(ExifSubIfdDirectory.TagFNumber) ?? "";
                }

                // Shutter Speed
                // Exposure Time is usually rational.
                if (subIfd.TryGetRational(ExifSubIfdDirectory.TagExposureTime, out var exposureTime))
                {
                    // Convert to fraction for display if < 1
                    double val = exposureTime.ToDouble();
                    if (val > 0 && val < 1)
                    {
                         info.ExposureTime = $"1/{Math.Round(1.0 / val)}";
                    }
                    else
                    {
                         info.ExposureTime = val.ToString("0.#####");
                    }
                }
                else
                {
                    info.ExposureTime = subIfd.GetDescription(ExifSubIfdDirectory.TagExposureTime)?.Replace(" sec", "") ?? "";
                }

                // ISO
                info.ISOSpeed = subIfd.GetDescription(ExifSubIfdDirectory.TagIsoEquivalent) ?? 
                                subIfd.GetDescription(0x8827) ?? ""; // TagIso 0x8827

                // Focal Length
                if (subIfd.TryGetDouble(ExifSubIfdDirectory.TagFocalLength, out double fl))
                {
                    info.FocalLength = $"{fl}mm";
                }
                else
                {
                    info.FocalLength = subIfd.GetDescription(ExifSubIfdDirectory.TagFocalLength)?.Replace(" ", "") ?? "";
                }

                // Date
                if (subIfd.TryGetDateTime(ExifSubIfdDirectory.TagDateTimeOriginal, out DateTime dt))
                {
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
        double logoOffsetY = SliderLogoOffsetY.Value;

        // Original Dimensions
        double wImg = _currentImage.PixelWidth;
        double hImg = _currentImage.PixelHeight;

        // Calculate Border Dimensions
        // New Logic: Scale determines the "Thickness" of the border uniformly
        // We calculate the border based on the Scale applied to the SHORTER side (or just vertical),
        // to ensure a uniform look, and then allow Min Margins to expand it.

        double wBorder, hBorder;

        // If Margin Priority is enabled (e.g. Hasselblad template), we ignore Scale calculation for borders
        // and strictly apply the margins to the image size.
        bool isMarginPriority = ChkMarginPriority.IsChecked == true;
        if (isMarginPriority)
        {
             wBorder = wImg + (minHMargin * 2);
             hBorder = hImg + (minMargin * 2);
        }
        else
        {
            // Standard Logic: Use Scale to determine base border, then ensure Min Margins
            
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
            wBorder = wImg + (finalHMargin * 2);
            hBorder = hImg + (finalVMargin * 2);
        }

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
        return RenderUsingVisualTree(wImg, hImg, wBorder, hBorder, scalePercent, minMargin, cornerRadius, shadowSize, textSpacing, logoOffsetY);
    }

    private RenderTargetBitmap RenderUsingVisualTree(double wImg, double hImg, double wBorder, double hBorder, 
        double scale, double minMargin, double cornerRadius, double shadowSize, double textSpacing, double logoOffsetY)
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
        
        FrameworkElement brandElement = null;

        // Check for Force Logo (Template override)
        if (_currentTemplate != null && !string.IsNullOrEmpty(_currentTemplate.ForceLogoPath))
        {
             // Use Pack URI for Embedded Resource
             // Format: pack://application:,,,/AssemblyName;component/Path/To/Resource
             string resourcePath = $"pack://application:,,,/Yin;component/{_currentTemplate.ForceLogoPath.Replace('\\', '/')}";
             
             try 
             {
                 var logo = new BitmapImage();
                 logo.BeginInit();
                 logo.UriSource = new Uri(resourcePath);
                 logo.CacheOption = BitmapCacheOption.OnLoad;
                 logo.EndInit();
                 
                 Image imgLogo = new Image();
                 imgLogo.Source = logo;
                 imgLogo.Stretch = Stretch.Uniform;
                 // Set height based on border height
                 imgLogo.Height = hBorder * 0.025; 
                 imgLogo.HorizontalAlignment = HorizontalAlignment.Center;
                 
                 brandElement = imgLogo;
             }
             catch { /* Ignore */ }
        }

        // If no forced logo, check for Brand Text (e.g. Hasselblad) for automatic logo loading
        if (brandElement == null && brandText == "HASSELBLAD") 
        {
             string resourcePath = "pack://application:,,,/Yin;component/Source/Hasselblad.png";
             try 
             {
                 // Load Image
                 var logo = new BitmapImage();
                 logo.BeginInit();
                 logo.UriSource = new Uri(resourcePath);
                 logo.CacheOption = BitmapCacheOption.OnLoad;
                 logo.EndInit();
                 
                 Image imgLogo = new Image();
                 imgLogo.Source = logo;
                 imgLogo.Stretch = Stretch.Uniform;
                 // Set height based on border height
                 imgLogo.Height = hBorder * 0.025; 
                 imgLogo.HorizontalAlignment = HorizontalAlignment.Center;
                 
                 brandElement = imgLogo;
             }
             catch { /* Ignore error, fall back to text */ }
        }

        if (brandElement == null)
        {
            // Use StackPanel to simulate character spacing since WPF TextBlock doesn't support it directly
            StackPanel spBrand = new StackPanel();
            spBrand.Orientation = Orientation.Horizontal;
            spBrand.HorizontalAlignment = HorizontalAlignment.Center;
            
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
            brandElement = spBrand;
        }

        // Layout: Brand Position
        if (_currentLayout == LayoutMode.BrandTop_ExifBottom)
        {
            brandElement.VerticalAlignment = VerticalAlignment.Top;
             // Top Margin = (hBorder - hImg) / 2.
            double topSpace = (hBorder - hImg) / 2;
            brandElement.Margin = new Thickness(0, (topSpace * 0.4) + logoOffsetY, 0, 0); 
        }
        else // BrandBottom_Centered
        {
             brandElement.VerticalAlignment = VerticalAlignment.Bottom;
             // Bottom Margin
             double bottomSpace = (hBorder - hImg) / 2;
             // Center it in the bottom margin space
             brandElement.Margin = new Thickness(0, 0, 0, (bottomSpace * 0.4) - logoOffsetY); 
        }
        
        grid.Children.Add(brandElement);

        // EXIF Label
        // Format: "FL 28mm   Aperture f/2.4   Shutter 1/100   ISO200"
        if (_currentLayout == LayoutMode.BrandTop_ExifBottom && _currentExif != null)
        {
            string exifText = $"FL {_currentExif.FocalLength}   Aperture {_currentExif.FNumber}   Shutter {_currentExif.ExposureTime}   ISO{_currentExif.ISOSpeed}";
            TextBlock txtExif = new TextBlock();
            txtExif.Text = exifText;
            txtExif.FontFamily = new FontFamily("Arial");
            txtExif.FontSize = hBorder * 0.015;
            txtExif.Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            txtExif.HorizontalAlignment = HorizontalAlignment.Center;
            txtExif.VerticalAlignment = VerticalAlignment.Bottom;
            
            double bottomSpace = (hBorder - hImg) / 2;
            txtExif.Margin = new Thickness(0, 0, 0, bottomSpace * 0.4); 
            
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
