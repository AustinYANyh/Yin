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
        
        // Split margins
        public double MarginTop { get; set; }
        public double MarginBottom { get; set; }
        public double MarginLeft { get; set; }
        public double MarginRight { get; set; }
        
        public bool IsSyncVertical { get; set; } = true; // Default sync top/bottom
        public bool IsSyncHorizontal { get; set; } = true; // Default sync left/right
        
        public bool IsSmartAdaptation { get; set; } = false; // Auto-adjust text color/background based on image content

        public double Corner { get; set; }
        public double Shadow { get; set; }
        public double Spacing { get; set; }
        public LayoutMode Layout { get; set; }
        public bool IsMarginPriority { get; set; } // If true, ignore Scale logic and trust margins strictly
        public string? ForceLogoPath { get; set; } // If set, force use this logo image
        public double LogoOffsetY { get; set; } // Vertical offset for logo
        
        // Defaults for this template (if EXIF missing)
        public string DefaultMake { get; set; } = "";
        public string DefaultModel { get; set; } = "";
        public string DefaultLens { get; set; } = "";
        public string DefaultFocal { get; set; } = "";
        public string DefaultFNumber { get; set; } = "";
        public string DefaultShutter { get; set; } = "";
        public string DefaultISO { get; set; } = "";
        
        // Resolution Adaptation
        // If > 0, the margin/shadow/corner values defined above are considered "Reference Values" 
        // for an image with this Short Edge (pixel count).
        // Using Short Edge (Min dimension) ensures consistent scaling across different Aspect Ratios (Landscape vs Portrait).
        public double ReferenceShortEdge { get; set; } = 0; 
    }

    public enum LayoutMode
    {
        BrandTop_ExifBottom,
        BrandBottom_Centered,
        TwoLines_Bottom_Centered
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
            MarginTop = 100, MarginBottom = 100,
            MarginLeft = 0, MarginRight = 0,
            Corner = 100,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandTop_ExifBottom,
            IsMarginPriority = false,
            IsSyncVertical = true,
            IsSyncHorizontal = true,
            ForceLogoPath = null, // Use parsed logic
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印边框",
            Scale = 85,
            MarginTop = 350, MarginBottom = 350,
            MarginLeft = 150, MarginRight = 150,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandTop_ExifBottom,
            IsMarginPriority = true,
            IsSyncVertical = true,
            IsSyncHorizontal = true,
            ForceLogoPath = "Source/Hasselblad.png",
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印居中",
            Scale = 90,
            MarginTop = 20, MarginBottom = 20,
            MarginLeft = 20, MarginRight = 20,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.BrandBottom_Centered,
            IsMarginPriority = true,
            IsSyncVertical = true,
            IsSyncHorizontal = true,
            ForceLogoPath = "Source/Hasselblad_white.png",
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "底部两行机身+参数",
            Scale = 90,
            MarginTop = 150, MarginBottom = 400, // Reduced from 630 to 400 for tighter look
            MarginLeft = 150, MarginRight = 150,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.TwoLines_Bottom_Centered,
            IsMarginPriority = true,
            IsSyncVertical = false, // Different top/bottom
            IsSyncHorizontal = true,
            IsSmartAdaptation = true, // Enable Smart Logic by default
            ForceLogoPath = null,
            LogoOffsetY = 0,
            DefaultMake = "SONY",
            DefaultModel = "ILCE-7RM5",
            DefaultLens = "FE 70-200mm OSS GM II",
            DefaultFocal = "70mm",
            DefaultFNumber = "f/2.8",
            DefaultShutter = "1/800",
            DefaultISO = "100",
            ReferenceShortEdge = 1800 // Adjusted for Short Edge
        });

        CmbTemplates.ItemsSource = _templates;
        CmbTemplates.DisplayMemberPath = "Name";
        CmbTemplates.SelectedIndex = 0; // Default selection
    }

    // Apply Template Values to UI, considering Scale if needed
    private void ApplyTemplateValues(TemplateModel tmpl)
    {
        // Calculate Scale Factor if Adaptive
        double factor = 1.0;
        if (tmpl.ReferenceShortEdge > 0 && _currentImage != null)
        {
             // Use Min Dimension (Short Edge) for stable scaling across aspect ratios
             double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
             factor = shortEdge / tmpl.ReferenceShortEdge;
             
             // Sanity check
             if (factor < 0.1) factor = 0.1;
             if (factor > 10) factor = 10;
        }

        // 1. Update Sync Checkboxes FIRST
        ChkSyncVertical.Checked -= ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked -= ChkSyncHorizontal_Checked;

        ChkSyncVertical.IsChecked = tmpl.IsSyncVertical;
        ChkSyncHorizontal.IsChecked = tmpl.IsSyncHorizontal;

        ChkSyncVertical.Checked += ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked += ChkSyncHorizontal_Checked;

        // 2. Update sliders with SCALED values
        SliderScale.Value = tmpl.Scale;
        
        SliderTopMargin.Value = tmpl.MarginTop * factor;
        SliderBottomMargin.Value = tmpl.MarginBottom * factor;
        SliderLeftMargin.Value = tmpl.MarginLeft * factor;
        SliderRightMargin.Value = tmpl.MarginRight * factor;
        
        SliderCorner.Value = tmpl.Corner * factor;
        SliderShadow.Value = tmpl.Shadow * factor;
        SliderTextSpacing.Value = tmpl.Spacing * factor; // Should spacing scale? Yes usually.
        SliderLogoOffsetY.Value = tmpl.LogoOffsetY * factor;
        
        // Update Options
        ChkMarginPriority.IsChecked = tmpl.IsMarginPriority;
        ChkSmartAdaptation.IsChecked = tmpl.IsSmartAdaptation;
        
        // Update Custom Text Defaults
        TxtMake.Text = tmpl.DefaultMake;
        TxtModel.Text = tmpl.DefaultModel;
        TxtLens.Text = tmpl.DefaultLens;
        TxtFocal.Text = tmpl.DefaultFocal;
        TxtFNumber.Text = tmpl.DefaultFNumber;
        TxtShutter.Text = tmpl.DefaultShutter;
        TxtISO.Text = tmpl.DefaultISO;
        
        _currentLayout = tmpl.Layout;
    }

    private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTemplates.SelectedItem is TemplateModel tmpl)
        {
            _currentTemplate = tmpl;
            ApplyTemplateValues(tmpl);
            
            if (_currentImage != null)
            {
                UpdatePreview();
            }
        }
    }
    
    // Slider Sync Logic
    private void SliderTopMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncVertical?.IsChecked == true && SliderBottomMargin != null)
        {
             // Prevent loop if already equal
             if (Math.Abs(SliderBottomMargin.Value - e.NewValue) > 0.01)
                SliderBottomMargin.Value = e.NewValue;
        }
    }

    private void SliderBottomMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncVertical?.IsChecked == true && SliderTopMargin != null)
        {
             if (Math.Abs(SliderTopMargin.Value - e.NewValue) > 0.01)
                SliderTopMargin.Value = e.NewValue;
        }
    }
    
    private void ChkSyncVertical_Checked(object sender, RoutedEventArgs e)
    {
        // Sync immediately
        if (SliderBottomMargin != null && SliderTopMargin != null)
             SliderBottomMargin.Value = SliderTopMargin.Value;
    }

    private void SliderLeftMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncHorizontal?.IsChecked == true && SliderRightMargin != null)
        {
             if (Math.Abs(SliderRightMargin.Value - e.NewValue) > 0.01)
                SliderRightMargin.Value = e.NewValue;
        }
    }

    private void SliderRightMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncHorizontal?.IsChecked == true && SliderLeftMargin != null)
        {
             if (Math.Abs(SliderLeftMargin.Value - e.NewValue) > 0.01)
                SliderLeftMargin.Value = e.NewValue;
        }
    }
    
    private void ChkSyncHorizontal_Checked(object sender, RoutedEventArgs e)
    {
        // Sync immediately
        if (SliderRightMargin != null && SliderLeftMargin != null)
             SliderRightMargin.Value = SliderLeftMargin.Value;
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
        public string LensModel { get; set; } = ""; // New Lens Model
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
            
            // Re-apply adaptive template values if needed
            if (_currentTemplate != null && _currentTemplate.ReferenceShortEdge > 0)
            {
                ApplyTemplateValues(_currentTemplate);
            }
            
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
                // Lens Model
                info.LensModel = subIfd.GetDescription(ExifSubIfdDirectory.TagLensModel) ?? "";

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
        if (string.IsNullOrEmpty(info.Make)) info.Make = string.Empty;

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
        
        // Use separate margins
        double marginTop = SliderTopMargin.Value;
        double marginBottom = SliderBottomMargin.Value;
        double marginLeft = SliderLeftMargin.Value;
        double marginRight = SliderRightMargin.Value;
        
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
        double finalMarginTop, finalMarginBottom, finalMarginLeft, finalMarginRight;

        // If Margin Priority is enabled (e.g. Hasselblad template), we ignore Scale calculation for borders
        // and strictly apply the margins to the image size.
        bool isMarginPriority = ChkMarginPriority.IsChecked == true;
        if (isMarginPriority)
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
            // Standard Logic: Use Scale to determine base border, then ensure Min Margins
            
            // 1. Calculate base border thickness based on Scale
            // hBorderBase = hImg / scale
            // vBaseMargin = (hBorderBase - hImg) / 2
            double hBorderBase = hImg / scalePercent;
            double baseMargin = (hBorderBase - hImg) / 2;
    
            // 2. Apply Min Margins
            // We ensure it's at least minMargin
            finalMarginTop = Math.Max(baseMargin, marginTop);
            finalMarginBottom = Math.Max(baseMargin, marginBottom);
            finalMarginLeft = Math.Max(baseMargin, marginLeft);
            finalMarginRight = Math.Max(baseMargin, marginRight);
    
            // 3. Calculate Final Border Dimensions
            wBorder = wImg + finalMarginLeft + finalMarginRight;
            hBorder = hImg + finalMarginTop + finalMarginBottom;
        }

        // Create Visual
        DrawingVisual visual = new DrawingVisual();
        using (DrawingContext dc = visual.RenderOpen())
        {
            // 1. Draw Background (White)
            dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, wBorder, hBorder));

            // 2. Draw Image with Shadow and Rounded Corners
            // We need a separate drawing for the image to apply effects
            
            // Calculate Image Position (Respecting Margins)
            // If margins are uneven (e.g. bottom bigger), image is not centered in border, 
            // but positioned by Left/Top margin.
            double xImg = finalMarginLeft;
            double yImg = finalMarginTop;
            
            Rect imgRect = new Rect(xImg, yImg, wImg, hImg);

            // Clip for rounded corners
            Geometry clipGeom = new RectangleGeometry(imgRect, cornerRadius, cornerRadius);
            dc.PushClip(clipGeom);

            // Draw Image
            dc.DrawImage(_currentImage, imgRect);
            
            dc.Pop(); // Pop Clip
        }

        // To support Shadow properly, it's easier to build a visual tree, arrange it, and then render.
        // Let's switch to that approach as it supports Effects and Layout easier.
        return RenderUsingVisualTree(wImg, hImg, wBorder, hBorder, scalePercent, 
            finalMarginTop, finalMarginBottom, finalMarginLeft, finalMarginRight, 
            cornerRadius, shadowSize, textSpacing, logoOffsetY);
    }

    // --- Image Analysis Helper ---
    private (double avgLuma, double variance) AnalyzeRegion(BitmapSource source, Rect region)
    {
        try
        {
            // Crop the region
            int x = (int)Math.Max(0, region.X);
            int y = (int)Math.Max(0, region.Y);
            int w = (int)Math.Min(source.PixelWidth - x, region.Width);
            int h = (int)Math.Min(source.PixelHeight - y, region.Height);

            if (w <= 0 || h <= 0) return (0, 0);

            CroppedBitmap crop = new CroppedBitmap(source, new Int32Rect(x, y, w, h));
            
            // Convert to Gray8 for easier luma calc
            FormatConvertedBitmap gray = new FormatConvertedBitmap();
            gray.BeginInit();
            gray.Source = crop;
            gray.DestinationFormat = PixelFormats.Gray8;
            gray.EndInit();

            int stride = w; // 8 bits per pixel
            byte[] pixels = new byte[h * stride];
            gray.CopyPixels(pixels, stride, 0);

            // Calculate Mean and Variance
            // To be faster, we can sample.
            long sum = 0;
            long sumSq = 0;
            int step = 4; // Sample every 4th pixel to speed up
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
            
            // Normalize Luma to 0..1
            return (mean / 255.0, variance);
        }
        catch
        {
            return (0.5, 0); // Fail safe
        }
    }

    private RenderTargetBitmap RenderUsingVisualTree(double wImg, double hImg, double wBorder, double hBorder, 
        double scale, double marginTop, double marginBottom, double marginLeft, double marginRight, 
        double cornerRadius, double shadowSize, double textSpacing, double logoOffsetY)
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
        // Alignment depends on margins
        imgContainer.HorizontalAlignment = HorizontalAlignment.Left;
        imgContainer.VerticalAlignment = VerticalAlignment.Top;
        imgContainer.Margin = new Thickness(marginLeft, marginTop, marginRight, marginBottom);
        
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
            // Top Margin Space = marginTop
            brandElement.Margin = new Thickness(0, (marginTop * 0.4) + logoOffsetY, 0, 0); 
            grid.Children.Add(brandElement);
        }
        else if (_currentLayout == LayoutMode.BrandBottom_Centered)
        {
             brandElement.VerticalAlignment = VerticalAlignment.Bottom;
             // Bottom Margin Space = marginBottom
             brandElement.Margin = new Thickness(0, 0, 0, (marginBottom * 0.4) - logoOffsetY); 
             grid.Children.Add(brandElement);
        }
        else if (_currentLayout == LayoutMode.TwoLines_Bottom_Centered)
        {
            // Both use Two Lines logic, but Overlay uses WHITE text.
            bool isOverlay = false;
            Brush textBrush = new SolidColorBrush(Color.FromRgb(30, 30, 30));
            Brush subTextBrush = new SolidColorBrush(Color.FromRgb(80, 80, 80));
            
            // --- Smart Adaptation Logic ---
            bool showSmartPanel = false;
            
            if (ChkSmartAdaptation.IsChecked == true && _currentImage is BitmapSource bmp)
            {
                // Analyze bottom 15% of image
                Rect analysisRect = new Rect(0, bmp.PixelHeight * 0.85, bmp.PixelWidth, bmp.PixelHeight * 0.15);
                var stats = AnalyzeRegion(bmp, analysisRect);
                
                // Heuristic: Is text likely overlapping image?
                // Only if marginBottom is small (< 100) or explicitly Overlay mode (which is removed, but logic remains for compact styles)
                bool isCompact = (marginBottom < 100); 
                
                if (isCompact)
                {
                    // 1. Adaptive Color
                    if (stats.avgLuma < 0.5) // Dark background
                    {
                        textBrush = Brushes.White;
                        subTextBrush = new SolidColorBrush(Color.FromRgb(220, 220, 220));
                    }
                    
                    // 2. Adaptive Panel
                    if (stats.variance > 2000)
                    {
                        showSmartPanel = true;
                    }
                }
            }
            
            StackPanel spContainer = new StackPanel();
            spContainer.Orientation = Orientation.Vertical;
            spContainer.HorizontalAlignment = HorizontalAlignment.Center;
            spContainer.VerticalAlignment = VerticalAlignment.Bottom;
            
            // Smart Panel Container (Grid)
            Grid panelGrid = new Grid();
            
            if (showSmartPanel)
            {
                Border panelBg = new Border();
                panelBg.Background = (textBrush == Brushes.White) ? Brushes.Black : Brushes.White;
                panelBg.Opacity = 0.3; 
                panelBg.CornerRadius = new CornerRadius(10);
                panelBg.Effect = new BlurEffect { Radius = 20 }; 
                
                panelGrid.Children.Add(panelBg);
                spContainer.Margin = new Thickness(20, 10, 20, 10);
            }
            
            // --- Line 1: Brand (Bold) + Model (Regular) ---
            StackPanel spLine1 = new StackPanel();
            spLine1.Orientation = Orientation.Horizontal;
            spLine1.HorizontalAlignment = HorizontalAlignment.Center;
            spLine1.Margin = new Thickness(0, 0, 0, hBorder * 0.005); 

            double refDim = Math.Min(wBorder, hBorder);
            double fontSizeL1 = refDim * 0.025; 
            
            // Helper
            string GetValue(string? exifVal, string boxVal)
            {
                if (!string.IsNullOrWhiteSpace(exifVal)) return exifVal;
                return boxVal ?? "";
            }

            // Brand Part
            string makeStr = GetValue(_currentExif?.Make, TxtMake.Text);
            if (string.IsNullOrWhiteSpace(makeStr)) makeStr = "CAMERA";
            
            TextBlock txtBrand = new TextBlock();
            txtBrand.Text = makeStr.ToUpper() + " "; 
            txtBrand.FontFamily = new FontFamily("Arial");
            txtBrand.FontWeight = FontWeights.Bold;
            txtBrand.FontSize = fontSizeL1;
            txtBrand.Foreground = textBrush; 
            
            // Model Part
            string modelStr = GetValue(_currentExif?.Model?.Replace("ILCE-", "ILCE-"), TxtModel.Text);
            TextBlock txtModel = new TextBlock();
            txtModel.Text = modelStr; 
            txtModel.FontFamily = new FontFamily("Arial");
            txtModel.FontWeight = FontWeights.Normal;
            txtModel.FontSize = fontSizeL1;
            txtModel.Foreground = textBrush;
            
            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect();
                glow.Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White;
                glow.BlurRadius = 10;
                glow.ShadowDepth = 0;
                glow.Opacity = 0.8;
                spLine1.Effect = glow;
            }

            spLine1.Children.Add(txtBrand);
            spLine1.Children.Add(txtModel);
            
            // --- Line 2: Lens (if avail) + Params ---
            string lens = GetValue(_currentExif?.LensModel, TxtLens.Text);
            string focal = GetValue(_currentExif?.FocalLength, TxtFocal.Text);
            string aperture = GetValue(_currentExif?.FNumber, TxtFNumber.Text);
            string shutter = GetValue(_currentExif?.ExposureTime, TxtShutter.Text);
            string iso = GetValue(_currentExif?.ISOSpeed, TxtISO.Text);
            
            List<string> parts = new List<string>();
            if (!string.IsNullOrEmpty(lens)) parts.Add(lens);
            if (!string.IsNullOrEmpty(focal)) parts.Add(focal);
            if (!string.IsNullOrEmpty(aperture)) parts.Add(aperture);
            if (!string.IsNullOrEmpty(shutter)) parts.Add(shutter);
            if (!string.IsNullOrEmpty(iso) && !iso.StartsWith("ISO")) parts.Add("ISO" + iso);
            else if (!string.IsNullOrEmpty(iso)) parts.Add(iso);

            string line2Text = string.Join("  ", parts);

            TextBlock txtLine2 = new TextBlock();
            txtLine2.Text = line2Text;
            txtLine2.FontFamily = new FontFamily("Arial");
            txtLine2.FontWeight = FontWeights.Normal;
            txtLine2.FontSize = fontSizeL1 * 0.75; 
            txtLine2.Foreground = subTextBrush;
            txtLine2.HorizontalAlignment = HorizontalAlignment.Center;
            
            if (showSmartPanel)
            {
                DropShadowEffect glow = new DropShadowEffect();
                glow.Color = (textBrush == Brushes.White) ? Colors.Black : Colors.White;
                glow.BlurRadius = 8;
                glow.ShadowDepth = 0;
                glow.Opacity = 0.8;
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

        // EXIF Label
        // Format: "FL 28mm   Aperture f/2.4   Shutter 1/100   ISO200"
        if (_currentLayout == LayoutMode.BrandTop_ExifBottom && _currentExif != null)
        {
            // Use Custom Text Logic here too if needed? User mainly asked for Bottom Two Lines template config.
            // But let's apply it generally for consistency if user enters text.
            
            string focal = !string.IsNullOrWhiteSpace(TxtFocal.Text) ? TxtFocal.Text : (_currentExif.FocalLength);
            string aperture = !string.IsNullOrWhiteSpace(TxtFNumber.Text) ? TxtFNumber.Text : (_currentExif.FNumber);
            string shutter = !string.IsNullOrWhiteSpace(TxtShutter.Text) ? TxtShutter.Text : (_currentExif.ExposureTime);
            string iso = !string.IsNullOrWhiteSpace(TxtISO.Text) ? TxtISO.Text : (_currentExif.ISOSpeed);
            if (!iso.StartsWith("ISO") && !string.IsNullOrEmpty(iso)) iso = "ISO" + iso;

            string exifText = $"FL {focal}   Aperture {aperture}   Shutter {shutter}   {iso}";
            TextBlock txtExif = new TextBlock();
            txtExif.Text = exifText;
            txtExif.FontFamily = new FontFamily("Arial");
            
            double refDim = Math.Min(wBorder, hBorder);
            txtExif.FontSize = refDim * 0.018; // Adjusted (was hBorder * 0.015)
            
            txtExif.Foreground = new SolidColorBrush(Color.FromRgb(50, 50, 50));
            txtExif.HorizontalAlignment = HorizontalAlignment.Center;
            txtExif.VerticalAlignment = VerticalAlignment.Bottom;
            
            // Use marginBottom
            txtExif.Margin = new Thickness(0, 0, 0, marginBottom * 0.4); 
            
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
