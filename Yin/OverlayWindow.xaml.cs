using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using Yin.Models;
using Yin.Services;

namespace Yin;

public partial class OverlayWindow : Window
{
    private const double DefaultOverlayStyleReferenceShortEdge = 1800;
    private const double DefaultOverlayCornerRadius = 24;
    private const double DefaultOverlayShadowSize = 18;
    private const double MinOverlayCornerRadius = 8;
    private const double MaxOverlayCornerRadius = 64;
    private const double MinOverlayShadowSize = 6;
    private const double MaxOverlayShadowSize = 40;

    private BitmapImage? _currentImage;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;

    private List<TemplateModel> _templates = new List<TemplateModel>();
    private LayoutMode _currentLayout = LayoutMode.TwoLines_Bottom_Centered;
    private TemplateModel? _currentTemplate;

    public OverlayWindow()
    {
        InitializeComponent();
        InitializeTemplates();
    }

    private void InitializeTemplates()
    {
        _templates.Add(new TemplateModel
        {
            Name = "底部机身带参数_overlay",
            Scale = 90,
            MarginTop = 150, MarginBottom = 400,
            MarginLeft = 150, MarginRight = 150,
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
        });

        CmbTemplates.ItemsSource = _templates;
        CmbTemplates.DisplayMemberPath = "Name";
        CmbTemplates.SelectedIndex = 0;
    }

    private void ApplyTemplateValues(TemplateModel tmpl)
    {
        double factor = 1.0;
        if (tmpl.ReferenceShortEdge > 0 && _currentImage != null)
        {
            double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
            factor = shortEdge / tmpl.ReferenceShortEdge;
            if (factor < 0.1) factor = 0.1;
            if (factor > 10) factor = 10;
        }

        ChkSyncVertical.Checked -= ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked -= ChkSyncHorizontal_Checked;
        ChkSyncVertical.IsChecked = tmpl.IsSyncVertical;
        ChkSyncHorizontal.IsChecked = tmpl.IsSyncHorizontal;
        ChkSyncVertical.Checked += ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked += ChkSyncHorizontal_Checked;

        SliderScale.Value = tmpl.Scale;
        SliderTopMargin.Value = tmpl.MarginTop * factor;
        SliderBottomMargin.Value = tmpl.MarginBottom * factor;
        SliderLeftMargin.Value = tmpl.MarginLeft * factor;
        SliderRightMargin.Value = tmpl.MarginRight * factor;
        SliderCorner.Value = tmpl.Corner * factor;
        SliderShadow.Value = tmpl.Shadow * factor;
        SliderTextSpacing.Value = tmpl.Spacing * factor;
        SliderLogoOffsetY.Value = tmpl.LogoOffsetY * factor;

        ChkMarginPriority.IsChecked = tmpl.IsMarginPriority;
        ChkSmartAdaptation.IsChecked = true;

        TxtMake.Text = tmpl.DefaultMake;
        TxtModel.Text = tmpl.DefaultModel;
        TxtLens.Text = tmpl.DefaultLens;
        TxtFocal.Text = tmpl.DefaultFocal;
        TxtFNumber.Text = tmpl.DefaultFNumber;
        TxtShutter.Text = tmpl.DefaultShutter;
        TxtISO.Text = tmpl.DefaultISO;

        _currentLayout = tmpl.Layout;
        _currentTemplate = tmpl;
        ApplyOverlayVisualDefaults();
    }

    private void ApplyOverlayVisualDefaults()
    {
        if (_currentImage == null)
        {
            return;
        }

        double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
        if (shortEdge <= 0)
        {
            return;
        }

        double referenceShortEdge = _currentTemplate?.ReferenceShortEdge > 0
            ? _currentTemplate.ReferenceShortEdge
            : DefaultOverlayStyleReferenceShortEdge;
        double factor = Math.Clamp(shortEdge / referenceShortEdge, 0.35, 3.0);

        double baseCorner = _currentTemplate?.Corner > 0
            ? _currentTemplate.Corner
            : DefaultOverlayCornerRadius;
        double baseShadow = _currentTemplate?.Shadow > 0
            ? _currentTemplate.Shadow
            : DefaultOverlayShadowSize;

        double corner = Math.Clamp(Math.Round(baseCorner * factor), MinOverlayCornerRadius, MaxOverlayCornerRadius);
        double shadow = Math.Clamp(Math.Round(baseShadow * factor), MinOverlayShadowSize, MaxOverlayShadowSize);

        SliderCorner.Value = ClampToSliderRange(SliderCorner, corner);
        SliderShadow.Value = ClampToSliderRange(SliderShadow, shadow);
    }

    private static double ClampToSliderRange(Slider slider, double value)
    {
        return Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CmbTemplates.SelectedItem is TemplateModel tmpl)
        {
            _currentTemplate = tmpl;
            ApplyTemplateValues(tmpl);
            if (_currentImage != null) UpdatePreview();
        }
    }

    private void SliderTopMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncVertical?.IsChecked == true && SliderBottomMargin != null)
        {
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
        if (SliderRightMargin != null && SliderLeftMargin != null)
            SliderRightMargin.Value = SliderLeftMargin.Value;
    }

    private void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog dlg = new OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() == true) LoadImage(dlg.FileName);
    }

    private void Image_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
            if (files.Length > 0) LoadImage(files[0]);
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
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();
            _currentImage = bitmap;
            _currentExif = ExifService.ReadExifData(path);
            if (_currentTemplate != null)
            {
                ApplyTemplateValues(_currentTemplate);
            }
            else
            {
                ApplyOverlayVisualDefaults();
            }
            UpdatePreview();
            TxtStatus.Text = $"Loaded: {Path.GetFileName(path)} | {_currentExif?.Model} | Corner {SliderCorner.Value:N0} | Shadow {SliderShadow.Value:N0}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading image: {ex.Message}");
        }
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage != null) UpdatePreview();
    }

    private void UpdatePreview()
    {
        if (_currentImage == null) return;
        var ctx = new RenderContext
        {
            CurrentImage = _currentImage,
            Exif = _currentExif,
            Template = _currentTemplate,
            Layout = _currentLayout,
            IsMarginPriority = ChkMarginPriority.IsChecked == true,
            IsSmartAdaptation = ChkSmartAdaptation.IsChecked == true,
            ScalePercent = SliderScale.Value,
            MarginTop = SliderTopMargin.Value,
            MarginBottom = SliderBottomMargin.Value,
            MarginLeft = SliderLeftMargin.Value,
            MarginRight = SliderRightMargin.Value,
            CornerRadius = SliderCorner.Value,
            ShadowSize = SliderShadow.Value,
            TextSpacing = SliderTextSpacing.Value,
            LogoOffsetY = SliderLogoOffsetY.Value,
            OutputScale = 1.0,
            TxtMake = TxtMake.Text,
            TxtModel = TxtModel.Text,
            TxtLens = TxtLens.Text,
            TxtFocal = TxtFocal.Text,
            TxtFNumber = TxtFNumber.Text,
            TxtShutter = TxtShutter.Text,
            TxtISO = TxtISO.Text
        };
        var finalImage = RenderingService.RenderOverlayImage(ctx);
        ImgPreview.Source = finalImage;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null) return;
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Filter = "JPEG Image|*.jpg",
            FileName = $"Overlay_{Path.GetFileNameWithoutExtension(_currentFilePath)}{(_currentTemplate != null ? $"_{_currentTemplate.Name}" : $"_{_currentLayout}")}.jpg"
        };
        if (saveFileDialog.ShowDialog() == true)
        {
            try
            {
                var ctx = new RenderContext
                {
                    CurrentImage = _currentImage,
                    Exif = _currentExif,
                    Template = _currentTemplate,
                    Layout = _currentLayout,
                    IsMarginPriority = ChkMarginPriority.IsChecked == true,
                    IsSmartAdaptation = ChkSmartAdaptation.IsChecked == true,
                    ScalePercent = SliderScale.Value,
                    MarginTop = SliderTopMargin.Value,
                    MarginBottom = SliderBottomMargin.Value,
                    MarginLeft = SliderLeftMargin.Value,
                    MarginRight = SliderRightMargin.Value,
                    CornerRadius = SliderCorner.Value,
                    ShadowSize = SliderShadow.Value,
                    TextSpacing = SliderTextSpacing.Value,
                    LogoOffsetY = SliderLogoOffsetY.Value,
                    OutputScale = 1.0,
                    TxtMake = TxtMake.Text,
                    TxtModel = TxtModel.Text,
                    TxtLens = TxtLens.Text,
                    TxtFocal = TxtFocal.Text,
                    TxtFNumber = TxtFNumber.Text,
                    TxtShutter = TxtShutter.Text,
                    TxtISO = TxtISO.Text
                };
                var final = RenderingService.RenderOverlayImage(ctx);
                JpegBitmapEncoder encoder = new JpegBitmapEncoder
                {
                    QualityLevel = 100
                };
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
