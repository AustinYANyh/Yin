using Microsoft.Win32;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Yin.Models;
using Yin.Services;
using DataFormats = System.Windows.DataFormats;
using Directory = System.IO.Directory;
using DragDropEffects = System.Windows.DragDropEffects;
using DragEventArgs = System.Windows.DragEventArgs;
using MessageBox = System.Windows.MessageBox;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using SaveFileDialog = Microsoft.Win32.SaveFileDialog;

namespace Yin;

public partial class MainWindow : Window
{
    private const string DefaultBorderTemplateName = "底部两行机身+参数";
    private const string DefaultOverlayTemplateName = "底部机身带参数_overlay";
    private const double DefaultOverlayStyleReferenceShortEdge = 1800;
    private const double DefaultOverlayCornerRadius = 24;
    private const double DefaultOverlayShadowSize = 18;
    private const double MinOverlayCornerRadius = 8;
    private const double MaxOverlayCornerRadius = 64;
    private const double MinOverlayShadowSize = 6;
    private const double MaxOverlayShadowSize = 40;

    private enum RenderMode
    {
        Border,
        Overlay
    }

    private sealed class ModeUiState
    {
        public string? TemplateName { get; init; }
        public LayoutMode Layout { get; init; }
        public double Scale { get; init; }
        public double MarginTop { get; init; }
        public double MarginBottom { get; init; }
        public double MarginLeft { get; init; }
        public double MarginRight { get; init; }
        public double CornerRadius { get; init; }
        public double ShadowSize { get; init; }
        public double TextSpacing { get; init; }
        public double LogoOffsetY { get; init; }
        public bool IsMarginPriority { get; init; }
        public bool IsSmartAdaptation { get; init; }
        public bool IsSyncVertical { get; init; }
        public bool IsSyncHorizontal { get; init; }
        public string TxtMake { get; init; } = "";
        public string TxtModel { get; init; } = "";
        public string TxtLens { get; init; } = "";
        public string TxtFocal { get; init; } = "";
        public string TxtFNumber { get; init; } = "";
        public string TxtShutter { get; init; } = "";
        public string TxtISO { get; init; } = "";
        public string TxtLocation { get; init; } = "";
    }

    private BitmapSource? _currentImage;
    private BitmapSource? _previewImage;
    private CancellationTokenSource? _previewCts;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;

    private CancellationTokenSource? _batchCts;
    private readonly List<string> _batchFiles = new();

    private sealed class BatchRunSummary
    {
        public int Total { get; init; }
        public int Succeeded { get; set; }
        public int Failed { get; set; }
        public bool Canceled { get; set; }
        public TimeSpan Elapsed { get; set; }
        public int Processed => Succeeded + Failed;
    }

    private sealed class UserDefaults
    {
        public string Make { get; set; } = "";
        public string Model { get; set; } = "";
        public string Lens { get; set; } = "";
        public string Focal { get; set; } = "";
        public string FNumber { get; set; } = "";
        public string Shutter { get; set; } = "";
        public string ISO { get; set; } = "";
        public string Location { get; set; } = "";
        public string BatchOutputDir { get; set; } = "";
    }

    private UserDefaults _userDefaults = new();
    private static readonly string UserDefaultsPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Yin", "user_defaults.json");

    private readonly List<TemplateModel> _borderTemplates = new();
    private readonly List<TemplateModel> _overlayTemplates = new();

    private RenderMode _currentMode = RenderMode.Border;
    private LayoutMode _currentLayout = LayoutMode.BrandTop_ExifBottom;
    private TemplateModel? _currentTemplate;
    private ModeUiState? _borderModeState;
    private ModeUiState? _overlayModeState;
    private bool _isRestoringUi;
    private bool _isSwitchingMode;

    public MainWindow()
    {
        InitializeComponent();
        InitializeTemplates();
        LoadUserDefaults();
        InitializeMode();
    }

    private void LoadUserDefaults()
    {
        try
        {
            if (File.Exists(UserDefaultsPath))
            {
                string json = File.ReadAllText(UserDefaultsPath);
                _userDefaults = JsonSerializer.Deserialize<UserDefaults>(json) ?? new UserDefaults();
            }
        }
        catch
        {
            _userDefaults = new UserDefaults();
        }
    }

    private void SaveUserDefaults()
    {
        try
        {
            _userDefaults.Make = TxtMake.Text;
            _userDefaults.Model = TxtModel.Text;
            _userDefaults.Lens = TxtLens.Text;
            _userDefaults.Focal = TxtFocal.Text;
            _userDefaults.FNumber = TxtFNumber.Text;
            _userDefaults.Shutter = TxtShutter.Text;
            _userDefaults.ISO = TxtISO.Text;
            _userDefaults.Location = TxtLocation.Text;
            _userDefaults.BatchOutputDir = TxtBatchOutputDir.Text.Trim();

            Directory.CreateDirectory(Path.GetDirectoryName(UserDefaultsPath)!);
            File.WriteAllText(UserDefaultsPath, JsonSerializer.Serialize(_userDefaults));
        }
        catch { }
    }

    private void ApplyUserDefaultsToUi()
    {
        TxtMake.Text = _userDefaults.Make;
        TxtModel.Text = _userDefaults.Model;
        TxtLens.Text = _userDefaults.Lens;
        TxtFocal.Text = _userDefaults.Focal;
        TxtFNumber.Text = _userDefaults.FNumber;
        TxtShutter.Text = _userDefaults.Shutter;
        TxtISO.Text = _userDefaults.ISO;
        TxtLocation.Text = _userDefaults.Location;
        TxtBatchOutputDir.Text = GetPreferredBatchOutputDir();
    }

    private void InitializeTemplates()
    {
        _borderTemplates.Add(new TemplateModel
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
        });

        _borderTemplates.Add(new TemplateModel
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
        });

        _borderTemplates.Add(new TemplateModel
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
        });

        _borderTemplates.Add(new TemplateModel
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
        });

        _borderTemplates.Add(new TemplateModel
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
        });

        _overlayTemplates.Add(new TemplateModel
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
        });

        CmbTemplates.DisplayMemberPath = "Name";
    }

    private void InitializeMode()
    {
        ApplyUserDefaultsToUi();
        _currentMode = RenderMode.Border;
        SwitchMode(RenderMode.Border, true);
        RadioModeBorder.IsChecked = true;
    }

    private void SwitchMode(RenderMode mode, bool forceDefault = false)
    {
        if (_isSwitchingMode)
        {
            return;
        }

        if (!forceDefault && mode == _currentMode && _currentTemplate != null)
        {
            UpdateModeUi();
            return;
        }

        _isSwitchingMode = true;
        try
        {
            if (!forceDefault && _currentTemplate != null)
            {
                SaveCurrentModeState();
            }

            _currentMode = mode;
            BindTemplatesForMode(mode);
            UpdateModeUi();

            bool restored = !forceDefault && TryRestoreModeState(mode);
            if (!restored)
            {
                ApplyDefaultTemplateForMode(mode);
            }

            if (_currentImage != null)
            {
                UpdatePreview();
            }
            else
            {
                ImgPreview.Source = null;
            }

            UpdateStatus();
        }
        finally
        {
            _isSwitchingMode = false;
        }
    }

    private void BindTemplatesForMode(RenderMode mode)
    {
        _isRestoringUi = true;
        try
        {
            CmbTemplates.ItemsSource = null;
            CmbTemplates.ItemsSource = GetTemplatesForMode(mode);
        }
        finally
        {
            _isRestoringUi = false;
        }
    }

    private List<TemplateModel> GetTemplatesForMode(RenderMode mode)
    {
        return mode == RenderMode.Border ? _borderTemplates : _overlayTemplates;
    }

    private string GetDefaultTemplateName(RenderMode mode)
    {
        return mode == RenderMode.Border ? DefaultBorderTemplateName : DefaultOverlayTemplateName;
    }

    private TemplateModel? FindTemplate(RenderMode mode, string? templateName)
    {
        if (string.IsNullOrWhiteSpace(templateName))
        {
            return null;
        }

        return GetTemplatesForMode(mode).FirstOrDefault(t => t.Name == templateName);
    }

    private void ApplyDefaultTemplateForMode(RenderMode mode)
    {
        TemplateModel? tmpl = FindTemplate(mode, GetDefaultTemplateName(mode)) ?? GetTemplatesForMode(mode).FirstOrDefault();
        if (tmpl == null)
        {
            return;
        }

        SelectTemplate(tmpl);
        ApplyTemplateValues(tmpl);
    }

    private bool TryRestoreModeState(RenderMode mode)
    {
        ModeUiState? state = GetModeState(mode);
        if (state == null)
        {
            return false;
        }

        TemplateModel? tmpl = FindTemplate(mode, state.TemplateName) ?? GetTemplatesForMode(mode).FirstOrDefault();
        if (tmpl == null)
        {
            return false;
        }

        SelectTemplate(tmpl);
        RestoreUiState(tmpl, state);
        return true;
    }

    private ModeUiState? GetModeState(RenderMode mode)
    {
        return mode == RenderMode.Border ? _borderModeState : _overlayModeState;
    }

    private void SaveCurrentModeState()
    {
        if (_currentTemplate == null)
        {
            return;
        }

        ModeUiState state = CaptureCurrentUiState();
        if (_currentMode == RenderMode.Border)
        {
            _borderModeState = state;
        }
        else
        {
            _overlayModeState = state;
        }
    }

    private ModeUiState CaptureCurrentUiState()
    {
        return new ModeUiState
        {
            TemplateName = _currentTemplate?.Name,
            Layout = _currentLayout,
            Scale = SliderScale.Value,
            MarginTop = SliderTopMargin.Value,
            MarginBottom = SliderBottomMargin.Value,
            MarginLeft = SliderLeftMargin.Value,
            MarginRight = SliderRightMargin.Value,
            CornerRadius = SliderCorner.Value,
            ShadowSize = SliderShadow.Value,
            TextSpacing = SliderTextSpacing.Value,
            LogoOffsetY = SliderLogoOffsetY.Value,
            IsMarginPriority = ChkMarginPriority.IsChecked == true,
            IsSmartAdaptation = ChkSmartAdaptation.IsChecked == true,
            IsSyncVertical = ChkSyncVertical.IsChecked == true,
            IsSyncHorizontal = ChkSyncHorizontal.IsChecked == true,
            TxtMake = TxtMake.Text,
            TxtModel = TxtModel.Text,
            TxtLens = TxtLens.Text,
            TxtFocal = TxtFocal.Text,
            TxtFNumber = TxtFNumber.Text,
            TxtShutter = TxtShutter.Text,
            TxtISO = TxtISO.Text,
            TxtLocation = TxtLocation.Text
        };
    }

    private void RestoreUiState(TemplateModel template, ModeUiState state)
    {
        _currentTemplate = template;
        _currentLayout = state.Layout;

        _isRestoringUi = true;
        try
        {
            CmbTemplates.SelectedItem = template;
            ChkSyncVertical.IsChecked = state.IsSyncVertical;
            ChkSyncHorizontal.IsChecked = state.IsSyncHorizontal;
            SliderScale.Value = ClampToSliderRange(SliderScale, state.Scale);
            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, state.MarginTop);
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, state.MarginBottom);
            SliderLeftMargin.Value = ClampToSliderRange(SliderLeftMargin, state.MarginLeft);
            SliderRightMargin.Value = ClampToSliderRange(SliderRightMargin, state.MarginRight);
            SliderCorner.Value = ClampToSliderRange(SliderCorner, state.CornerRadius);
            SliderShadow.Value = ClampToSliderRange(SliderShadow, state.ShadowSize);
            SliderTextSpacing.Value = ClampToSliderRange(SliderTextSpacing, state.TextSpacing);
            SliderLogoOffsetY.Value = ClampToSliderRange(SliderLogoOffsetY, state.LogoOffsetY);
            ChkMarginPriority.IsChecked = state.IsMarginPriority;
            ChkSmartAdaptation.IsChecked = state.IsSmartAdaptation;
            TxtMake.Text = state.TxtMake;
            TxtModel.Text = state.TxtModel;
            TxtLens.Text = state.TxtLens;
            TxtFocal.Text = state.TxtFocal;
            TxtFNumber.Text = state.TxtFNumber;
            TxtShutter.Text = state.TxtShutter;
            TxtISO.Text = state.TxtISO;
            TxtLocation.Text = state.TxtLocation;
        }
        finally
        {
            _isRestoringUi = false;
        }
    }

    private void SelectTemplate(TemplateModel tmpl)
    {
        _isRestoringUi = true;
        try
        {
            CmbTemplates.SelectedItem = tmpl;
        }
        finally
        {
            _isRestoringUi = false;
        }
    }

    private void ApplyTemplateValues(TemplateModel tmpl, bool applyDefaults = true)
    {
        double factor = GetTemplateScaleFactor(tmpl);

        _isRestoringUi = true;
        try
        {
            ChkSyncVertical.IsChecked = tmpl.IsSyncVertical;
            ChkSyncHorizontal.IsChecked = tmpl.IsSyncHorizontal;

            SliderScale.Value = ClampToSliderRange(SliderScale, tmpl.Scale);
            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, tmpl.MarginTop * factor);
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, tmpl.MarginBottom * factor);
            SliderLeftMargin.Value = ClampToSliderRange(SliderLeftMargin, tmpl.MarginLeft * factor);
            SliderRightMargin.Value = ClampToSliderRange(SliderRightMargin, tmpl.MarginRight * factor);
            SliderCorner.Value = ClampToSliderRange(SliderCorner, tmpl.Corner * factor);
            SliderShadow.Value = ClampToSliderRange(SliderShadow, tmpl.Shadow * factor);
            SliderTextSpacing.Value = ClampToSliderRange(SliderTextSpacing, tmpl.Spacing * factor);
            SliderLogoOffsetY.Value = ClampToSliderRange(SliderLogoOffsetY, tmpl.LogoOffsetY * factor);

            if (_currentMode == RenderMode.Border)
            {
                ApplyBorderTemplateTweaks(tmpl);
            }

            ChkMarginPriority.IsChecked = tmpl.IsMarginPriority;
            ChkSmartAdaptation.IsChecked = _currentMode == RenderMode.Overlay ? true : tmpl.IsSmartAdaptation;

            if (applyDefaults)
            {
                ApplyUserDefaultsToUi();
            }
        }
        finally
        {
            _isRestoringUi = false;
        }

        _currentLayout = tmpl.Layout;
        _currentTemplate = tmpl;

        if (_currentMode == RenderMode.Overlay)
        {
            ApplyOverlayVisualDefaults();
        }

        UpdateStatus();
    }

    private double GetTemplateScaleFactor(TemplateModel tmpl)
    {
        if (tmpl.ReferenceShortEdge <= 0 || _currentImage == null)
        {
            return 1.0;
        }

        double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
        return Math.Clamp(shortEdge / tmpl.ReferenceShortEdge, 0.1, 10.0);
    }

    private void ApplyBorderTemplateTweaks(TemplateModel tmpl)
    {
        if (_currentImage == null)
        {
            return;
        }

        if (tmpl.Name == "哈苏水印边框")
        {
            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, SliderTopMargin.Value * 1.5);
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, SliderBottomMargin.Value * 1.5);

            double wImg = _currentImage.PixelWidth;
            double hImg = _currentImage.PixelHeight;
            double wBorderPred = wImg + SliderLeftMargin.Value + SliderRightMargin.Value;
            double hBorderPred = hImg + SliderTopMargin.Value + SliderBottomMargin.Value;
            double refDim = Math.Min(wBorderPred, hBorderPred);
            double factorLogo = tmpl.ReferenceShortEdge > 0 ? refDim / tmpl.ReferenceShortEdge : 1.0;
            double logoHeight = 32 * factorLogo;
            double topMin = logoHeight * 2.0 + 10;
            double paramFont = refDim * 0.018;
            double bottomMin = paramFont * 2.5 + 10;

            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, Math.Max(SliderTopMargin.Value, topMin));
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, Math.Max(SliderBottomMargin.Value, bottomMin));

            double sideMin = paramFont * 2.0;
            double lr = Math.Max(Math.Max(SliderLeftMargin.Value, SliderRightMargin.Value), sideMin);
            SliderLeftMargin.Value = ClampToSliderRange(SliderLeftMargin, lr);
            SliderRightMargin.Value = ClampToSliderRange(SliderRightMargin, lr);
        }

        if (tmpl.Name == "哈苏水印居中")
        {
            double w = _currentImage.PixelWidth;
            double h = _currentImage.PixelHeight;
            bool portrait = h > w;
            double shortEdge = Math.Min(w, h);
            double edgeFactor = portrait ? 0.018 : 0.022;
            double logoOffsetFactor = portrait ? 0.035 : 0.030;

            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, shortEdge * edgeFactor);
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, shortEdge * edgeFactor);
            SliderLeftMargin.Value = ClampToSliderRange(SliderLeftMargin, shortEdge * edgeFactor);
            SliderRightMargin.Value = ClampToSliderRange(SliderRightMargin, shortEdge * edgeFactor);
            SliderLogoOffsetY.Value = ClampToSliderRange(SliderLogoOffsetY, shortEdge * logoOffsetFactor);
        }

        if (tmpl.Name == "签名水印")
        {
            double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
            double minBottom = Math.Clamp(shortEdge * 0.045, 36, 96);
            SliderTopMargin.Value = ClampToSliderRange(SliderTopMargin, 0);
            SliderLeftMargin.Value = ClampToSliderRange(SliderLeftMargin, 0);
            SliderRightMargin.Value = ClampToSliderRange(SliderRightMargin, 0);
            SliderBottomMargin.Value = ClampToSliderRange(SliderBottomMargin, Math.Max(SliderBottomMargin.Value, minBottom));
        }
    }

    private void ApplyOverlayVisualDefaults()
    {
        if (_currentMode != RenderMode.Overlay || _currentImage == null)
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

        double baseCorner = _currentTemplate?.Corner > 0 ? _currentTemplate.Corner : DefaultOverlayCornerRadius;
        double baseShadow = _currentTemplate?.Shadow > 0 ? _currentTemplate.Shadow : DefaultOverlayShadowSize;
        double corner = Math.Clamp(Math.Round(baseCorner * factor), MinOverlayCornerRadius, MaxOverlayCornerRadius);
        double shadow = Math.Clamp(Math.Round(baseShadow * factor), MinOverlayShadowSize, MaxOverlayShadowSize);

        SliderCorner.Value = ClampToSliderRange(SliderCorner, corner);
        SliderShadow.Value = ClampToSliderRange(SliderShadow, shadow);
    }

    private static double ClampToSliderRange(Slider slider, double value)
    {
        return Math.Clamp(value, slider.Minimum, slider.Maximum);
    }

    private void UpdateModeUi()
    {
        string modeLabel = _currentMode == RenderMode.Border ? "边框" : "Overlay";
        TxtModeDescription.Text = $"当前模式：{modeLabel}";
        BtnSave.Content = _currentMode == RenderMode.Border ? "保存边框图片" : "保存 Overlay 图片";
        Title = _currentMode == RenderMode.Border
            ? "Watermark Border Generator"
            : "Watermark Border Generator - Overlay";
    }

    private RenderContext CreateRenderContext()
    {
        return new RenderContext
        {
            CurrentImage = _currentImage!,
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
            TxtISO = TxtISO.Text,
            TxtLocation = TxtLocation.Text
        };
    }

    private RenderTargetBitmap RenderCurrentMode(RenderContext ctx)
    {
        return _currentMode == RenderMode.Border
            ? RenderingService.RenderFinalImage(ctx)
            : RenderingService.RenderOverlayImage(ctx);
    }

    private void UpdateStatus()
    {
        string modeLabel = _currentMode == RenderMode.Border ? "边框" : "Overlay";
        if (_currentImage == null)
        {
            TxtStatus.Text = $"模式：{modeLabel} | 未加载图片";
            return;
        }

        string model = string.IsNullOrWhiteSpace(_currentExif?.Model) ? "Unknown Model" : _currentExif!.Model!;
        string template = _currentTemplate?.Name ?? "未选择模板";
        string status = $"模式：{modeLabel} | Loaded: {Path.GetFileName(_currentFilePath)} | {model} | 模板：{template}";
        if (_currentMode == RenderMode.Overlay)
        {
            status += $" | Corner {SliderCorner.Value:N0} | Shadow {SliderShadow.Value:N0}";
        }

        TxtStatus.Text = status;
    }

    private void CmbTemplates_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isRestoringUi)
        {
            return;
        }

        if (CmbTemplates.SelectedItem is not TemplateModel tmpl)
        {
            return;
        }

        _currentTemplate = tmpl;
        ApplyTemplateValues(tmpl);

        if (_currentImage != null)
        {
            UpdatePreview();
        }
    }

    private void RadioModeBorder_Checked(object sender, RoutedEventArgs e)
    {
        SwitchMode(RenderMode.Border);
    }

    private void RadioModeOverlay_Checked(object sender, RoutedEventArgs e)
    {
        SwitchMode(RenderMode.Overlay);
    }

    private void SliderTopMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRestoringUi || ChkSyncVertical == null || SliderBottomMargin == null)
        {
            return;
        }

        if (ChkSyncVertical.IsChecked == true && Math.Abs(SliderBottomMargin.Value - e.NewValue) > 0.01)
        {
            SliderBottomMargin.Value = e.NewValue;
        }
    }

    private void SliderBottomMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRestoringUi || ChkSyncVertical == null || SliderTopMargin == null)
        {
            return;
        }

        if (ChkSyncVertical.IsChecked == true && Math.Abs(SliderTopMargin.Value - e.NewValue) > 0.01)
        {
            SliderTopMargin.Value = e.NewValue;
        }
    }

    private void ChkSyncVertical_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRestoringUi || SliderTopMargin == null || SliderBottomMargin == null)
        {
            return;
        }

        SliderBottomMargin.Value = SliderTopMargin.Value;
    }

    private void SliderLeftMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRestoringUi || ChkSyncHorizontal == null || SliderRightMargin == null)
        {
            return;
        }

        if (ChkSyncHorizontal.IsChecked == true && Math.Abs(SliderRightMargin.Value - e.NewValue) > 0.01)
        {
            SliderRightMargin.Value = e.NewValue;
        }
    }

    private void SliderRightMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isRestoringUi || ChkSyncHorizontal == null || SliderLeftMargin == null)
        {
            return;
        }

        if (ChkSyncHorizontal.IsChecked == true && Math.Abs(SliderLeftMargin.Value - e.NewValue) > 0.01)
        {
            SliderLeftMargin.Value = e.NewValue;
        }
    }

    private void ChkSyncHorizontal_Checked(object sender, RoutedEventArgs e)
    {
        if (_isRestoringUi || SliderLeftMargin == null || SliderRightMargin == null)
        {
            return;
        }

        SliderRightMargin.Value = SliderLeftMargin.Value;
    }

    private async void BtnOpen_Click(object sender, RoutedEventArgs e)
    {
        OpenFileDialog openFileDialog = new OpenFileDialog
        {
            Filter = "Image files (*.jpg, *.jpeg, *.png, *.bmp)|*.jpg;*.jpeg;*.png;*.bmp|All files (*.*)|*.*"
        };

        if (openFileDialog.ShowDialog() == true)
        {
            await LoadImageAsync(openFileDialog.FileName);
        }
    }

    private async void Image_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
        if (files.Length > 0)
        {
            await LoadImageAsync(files[0]);
        }
    }

    private void Image_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private async Task LoadImageAsync(string path)
    {
        try
        {
            _currentFilePath = path;
            BitmapSource bitmap = LoadBitmapFrozen(path);

            _currentImage = bitmap;
            _previewImage = CreatePreviewThumbnail(bitmap, 1200);
            UpdateStatus("正在读取 EXIF / 地点...");
            _currentExif = await ExifService.ReadExifDataAsync(path);

            if (_currentTemplate != null && (_currentTemplate.ReferenceShortEdge > 0 || _currentMode == RenderMode.Overlay))
            {
                ApplyTemplateValues(_currentTemplate, applyDefaults: false);
            }

            UpdatePreview();
            UpdateStatus();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error loading image: {ex.Message}");
        }
    }

    private void UpdateStatus(string statusText)
    {
        TxtStatus.Text = statusText;
    }

    private void BtnUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage != null)
        {
            UpdatePreview();
        }
    }

    private void Window_Closing(object sender, System.ComponentModel.CancelEventArgs e)
    {
        SaveUserDefaults();
    }

    private void BtnSaveDefaults_Click(object sender, RoutedEventArgs e)
    {
        SaveUserDefaults();
    }

    private void BtnResetDefaults_Click(object sender, RoutedEventArgs e)
    {
        if (_currentTemplate == null) return;
        TxtMake.Text = _currentTemplate.DefaultMake;
        TxtModel.Text = _currentTemplate.DefaultModel;
        TxtLens.Text = _currentTemplate.DefaultLens;
        TxtFocal.Text = _currentTemplate.DefaultFocal;
        TxtFNumber.Text = _currentTemplate.DefaultFNumber;
        TxtShutter.Text = _currentTemplate.DefaultShutter;
        TxtISO.Text = _currentTemplate.DefaultISO;
        TxtLocation.Text = _currentTemplate.DefaultLocation;
    }

    private async void UpdatePreview()
    {
        if (_currentImage == null) return;

        _previewCts?.Cancel();
        _previewCts?.Dispose();
        _previewCts = new CancellationTokenSource();
        var cts = _previewCts;

        UpdateStatus("渲染预览中...");

        try
        {
            RenderContext ctx = CreatePreviewRenderContext();
            RenderTargetBitmap? result = null;

            await Dispatcher.InvokeAsync(() =>
            {
                if (!cts.IsCancellationRequested)
                    result = RenderCurrentMode(ctx);
            }, System.Windows.Threading.DispatcherPriority.Background, cts.Token);

            if (!cts.IsCancellationRequested && result != null)
                ImgPreview.Source = result;
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!cts.IsCancellationRequested)
                UpdateStatus();
        }
    }

    private RenderContext CreatePreviewRenderContext()
    {
        var ctx = CreateRenderContext();
        if (_previewImage != null && _currentImage != null)
        {
            double ratio = (double)_previewImage.PixelWidth / _currentImage.PixelWidth;
            ctx.CurrentImage = _previewImage;
            ctx.MarginTop    *= ratio;
            ctx.MarginBottom *= ratio;
            ctx.MarginLeft   *= ratio;
            ctx.MarginRight  *= ratio;
            ctx.CornerRadius *= ratio;
            ctx.ShadowSize   *= ratio;
            ctx.LogoOffsetY  *= ratio;
            ctx.TextSpacing  *= ratio;
        }
        return ctx;
    }

    private static BitmapSource CreatePreviewThumbnail(BitmapSource source, int maxLongestEdge)
    {
        double longest = Math.Max(source.PixelWidth, source.PixelHeight);
        if (longest <= maxLongestEdge) return source;
        double scale = maxLongestEdge / longest;
        var tb = new TransformedBitmap();
        tb.BeginInit();
        tb.Source = source;
        tb.Transform = new ScaleTransform(scale, scale);
        tb.EndInit();
        tb.Freeze();
        return tb;
    }

    private void BtnSave_Click(object sender, RoutedEventArgs e)
    {
        if (_currentImage == null)
        {
            return;
        }

        string prefix = _currentMode == RenderMode.Border ? "Frame" : "Overlay";
        SaveFileDialog saveFileDialog = new SaveFileDialog
        {
            Filter = "JPEG Image|*.jpg",
            FileName = $"{prefix}_{Path.GetFileNameWithoutExtension(_currentFilePath)}{(_currentTemplate != null ? $"_{_currentTemplate.Name}" : $"_{_currentLayout}")}.jpg"
        };

        if (saveFileDialog.ShowDialog() != true)
        {
            return;
        }

        try
        {
            RenderContext ctx = CreateRenderContext();
            RenderTargetBitmap final = RenderCurrentMode(ctx);
            JpegBitmapEncoder encoder = new JpegBitmapEncoder
            {
                QualityLevel = 100
            };
            encoder.Frames.Add(BitmapFrame.Create(final));

            using FileStream fs = new FileStream(saveFileDialog.FileName, FileMode.Create);
            encoder.Save(fs);

            MessageBox.Show("Saved successfully!");
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error saving: {ex.Message}");
        }
    }

    // ── Batch Processing ────────────────────────────────────────────────────

    private void BtnBatchAddFiles_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.tiff;*.tif"
        };
        if (dlg.ShowDialog() != true) return;
        AddBatchFiles(dlg.FileNames);
    }

    private void LstBatchFiles_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
            AddBatchFiles(files);
    }

    private void AddBatchFiles(IEnumerable<string> paths)
    {
        foreach (string p in paths)
        {
            if (!_batchFiles.Contains(p))
                _batchFiles.Add(p);
        }
        RefreshBatchFileList();
    }

    private void RefreshBatchFileList()
    {
        LstBatchFiles.Items.Clear();
        foreach (string p in _batchFiles)
            LstBatchFiles.Items.Add(Path.GetFileName(p));
        TxtBatchCount.Text = $"共 {_batchFiles.Count} 张";
    }

    private void BtnBatchClear_Click(object sender, RoutedEventArgs e)
    {
        _batchFiles.Clear();
        RefreshBatchFileList();
    }

    private void BtnBatchSelectDir_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = "选择输出目录"
        };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            SetBatchOutputDir(dlg.SelectedPath);
    }

    private void BtnBatchOpenDir_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            string outputDir = EnsureBatchOutputDir();
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{outputDir}\"",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"无法打开输出目录: {ex.Message}");
        }
    }

    private async void BtnBatchStart_Click(object sender, RoutedEventArgs e)
    {
        if (_batchFiles.Count == 0)
        {
            MessageBox.Show("请先添加待处理文件。");
            return;
        }
        string outputDir;
        try
        {
            outputDir = EnsureBatchOutputDir();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"输出目录不可用: {ex.Message}");
            return;
        }

        _batchCts = new CancellationTokenSource();
        BtnBatchStart.IsEnabled = false;
        BtnBatchStop.IsEnabled = true;
        LstBatchLog.Items.Clear();
        PrgBatch.Value = 0;
        TxtBatchProgress.Text = $"0 / {_batchFiles.Count}";

        try
        {
            AppendBatchLog("── 批量处理开始 ──");
            AppendBatchLog($"模式: {GetRenderModeDisplayName(_currentMode)} | 模板: {GetBatchTemplateDisplayName()} | 布局: {_currentLayout}");
            AppendBatchLog($"输出目录: {outputDir}");
            AppendBatchLog($"任务数量: {_batchFiles.Count}");
            AppendBatchLog(
                $"全局参数: 缩放 {SliderScale.Value:N0}% | 边距优先 {FormatBool(ChkMarginPriority.IsChecked == true)} | 智能适配 {FormatBool(ChkSmartAdaptation.IsChecked == true)}");
            AppendBatchLog(
                $"文本覆盖: Make={FormatLogValue(TxtMake.Text)} | Model={FormatLogValue(TxtModel.Text)} | Lens={FormatLogValue(TxtLens.Text)} | Focal={FormatLogValue(TxtFocal.Text)} | FNumber={FormatLogValue(TxtFNumber.Text)} | Shutter={FormatLogValue(TxtShutter.Text)} | ISO={FormatLogValue(TxtISO.Text)} | Location={FormatLogValue(TxtLocation.Text)}");

            BatchRunSummary summary = await RunBatchAsync(_batchFiles.ToList(), outputDir, _batchCts.Token);
            string action = summary.Canceled ? "已停止" : "完成";
            AppendBatchLog(
                $"── {action}：成功 {summary.Succeeded}，失败 {summary.Failed}，已处理 {summary.Processed}/{summary.Total}，耗时 {FormatElapsed(summary.Elapsed)} ──");
        }
        finally
        {
            BtnBatchStart.IsEnabled = true;
            BtnBatchStop.IsEnabled = false;
            _batchCts.Dispose();
            _batchCts = null;
        }
    }

    private void BtnBatchStop_Click(object sender, RoutedEventArgs e)
    {
        _batchCts?.Cancel();
    }

    private async Task<BatchRunSummary> RunBatchAsync(
        IReadOnlyList<string> files,
        string outputDir,
        CancellationToken ct)
    {
        BatchRunSummary summary = new() { Total = files.Count };
        Stopwatch batchStopwatch = Stopwatch.StartNew();

        try
        {
            for (int i = 0; i < files.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                string path = files[i];
                string fileName = Path.GetFileName(path);
                Stopwatch fileStopwatch = Stopwatch.StartNew();

                TxtBatchProgress.Text = $"{i} / {files.Count}  {fileName}";
                PrgBatch.Value = (double)i / files.Count * 100;
                AppendBatchLog($"[{i + 1}/{files.Count}] 开始处理: {fileName}");
                AppendBatchLog($"    源文件: {path}");

                try
                {
                    ExifInfo? exif = null;
                    try
                    {
                        exif = await ExifService.ReadExifDataAsync(path);
                        AppendBatchLog($"    EXIF: {FormatExifLog(exif)}");
                    }
                    catch (Exception ex)
                    {
                        AppendBatchLog($"    ! EXIF 读取失败: {ex.GetType().Name}: {ex.Message}");
                    }

                    RenderTargetBitmap? rtb = null;
                    BitmapSource? img = null;
                    RenderContext? ctx = null;
                    await Dispatcher.InvokeAsync(() =>
                    {
                        img = LoadBitmapFrozen(path);
                        ctx = ComputeBatchRenderContext(img, exif);
                        rtb = RenderCurrentMode(ctx);
                    }, System.Windows.Threading.DispatcherPriority.Background, ct);

                    AppendBatchLog($"    图像: {img!.PixelWidth}x{img.PixelHeight}");
                    AppendBatchLog($"    渲染: {FormatRenderContextLog(ctx!)}");

                    string outPath = BuildBatchOutputPath(outputDir, path);
                    byte[] jpegBytes = EncodeJpeg(rtb!);
                    await Task.Run(() => File.WriteAllBytes(outPath, jpegBytes), ct);

                    AppendBatchLog($"    输出: {outPath}");
                    AppendBatchLog($"    结果: {rtb.PixelWidth}x{rtb.PixelHeight} | {FormatFileSize(jpegBytes.Length)}");
                    AppendBatchLog($"✓  {fileName} | 耗时 {FormatElapsed(fileStopwatch.Elapsed)}");
                    summary.Succeeded++;
                }
                catch (OperationCanceledException)
                {
                    AppendBatchLog($"!  停止于: {fileName}");
                    throw;
                }
                catch (Exception ex)
                {
                    AppendBatchLog($"✗  {fileName} | {ex.GetType().Name}: {ex.Message}");
                    summary.Failed++;
                }
            }
        }
        catch (OperationCanceledException)
        {
            summary.Canceled = true;
        }
        finally
        {
            batchStopwatch.Stop();
            summary.Elapsed = batchStopwatch.Elapsed;
        }

        TxtBatchProgress.Text = $"{files.Count} / {files.Count}";
        PrgBatch.Value = 100;
        return summary;
    }

    private static BitmapSource LoadBitmapFrozen(string path)
    {
        // Load exactly like the single-image path: BitmapImage with OnLoad cache.
        // Must be called on an STA thread (UI thread or Task.Run with STA).
        var bitmap = new BitmapImage();
        bitmap.BeginInit();
        bitmap.UriSource = new Uri(path);
        bitmap.CacheOption = BitmapCacheOption.OnLoad;
        bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
        bitmap.EndInit();
        bitmap.Freeze();
        return ApplyExifOrientation(bitmap, path);
    }

    private static BitmapSource ApplyExifOrientation(BitmapSource bitmap, string path)
    {
        int orientation = ReadExifOrientation(path);
        if (orientation is not (3 or 6 or 8))
        {
            return bitmap;
        }

        var transformed = new TransformedBitmap();
        transformed.BeginInit();
        transformed.Source = bitmap;
        transformed.Transform = orientation switch
        {
            3 => new RotateTransform(180),
            6 => new RotateTransform(90),
            8 => new RotateTransform(270),
            _ => Transform.Identity
        };
        transformed.EndInit();
        transformed.Freeze();
        return transformed;
    }

    private static int ReadExifOrientation(string path)
    {
        try
        {
            var ifd0 = ImageMetadataReader.ReadMetadata(path).OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null && ifd0.TryGetInt32(ExifIfd0Directory.TagOrientation, out int orientation))
            {
                return orientation;
            }
        }
        catch
        {
            // Ignore EXIF orientation read failures and fall back to raw pixels.
        }

        return 1;
    }

    /// <summary>
    /// Computes a RenderContext for a batch image by replicating exactly what
    /// ApplyTemplateValues + ApplyBorderTemplateTweaks + CreateRenderContext does
    /// for the single-image path, but for an arbitrary image size.
    /// </summary>
    private RenderContext ComputeBatchRenderContext(BitmapSource img, ExifInfo? exif)
    {
        TemplateModel? tmpl = _currentTemplate;

        double scalePercent = tmpl?.Scale ?? SliderScale.Value;
        bool isMarginPriority = tmpl?.IsMarginPriority ?? (ChkMarginPriority.IsChecked == true);
        bool isSmartAdaptation = _currentMode == RenderMode.Overlay
            ? true
            : (tmpl?.IsSmartAdaptation ?? (ChkSmartAdaptation.IsChecked == true));
        LayoutMode layout = tmpl?.Layout ?? _currentLayout;

        double marginTop, marginBottom, marginLeft, marginRight;
        double cornerRadius, shadowSize, textSpacing, logoOffsetY;

        if (tmpl != null && tmpl.ReferenceShortEdge > 0)
        {
            double shortEdge = Math.Min(img.PixelWidth, img.PixelHeight);
            double factor = Math.Clamp(shortEdge / tmpl.ReferenceShortEdge, 0.1, 10.0);

            marginTop    = tmpl.MarginTop    * factor;
            marginBottom = tmpl.MarginBottom * factor;
            marginLeft   = tmpl.MarginLeft   * factor;
            marginRight  = tmpl.MarginRight  * factor;
            cornerRadius = tmpl.Corner       * factor;
            shadowSize   = tmpl.Shadow       * factor;
            textSpacing  = tmpl.Spacing      * factor;
            logoOffsetY  = tmpl.LogoOffsetY  * factor;

            // Clamp to slider ranges, same as ApplyTemplateValues does via ClampToSliderRange
            marginTop    = Math.Clamp(marginTop,    SliderTopMargin.Minimum,    SliderTopMargin.Maximum);
            marginBottom = Math.Clamp(marginBottom, SliderBottomMargin.Minimum, SliderBottomMargin.Maximum);
            marginLeft   = Math.Clamp(marginLeft,   SliderLeftMargin.Minimum,   SliderLeftMargin.Maximum);
            marginRight  = Math.Clamp(marginRight,  SliderRightMargin.Minimum,  SliderRightMargin.Maximum);
            cornerRadius = Math.Clamp(cornerRadius, SliderCorner.Minimum,       SliderCorner.Maximum);
            shadowSize   = Math.Clamp(shadowSize,   SliderShadow.Minimum,       SliderShadow.Maximum);
            textSpacing  = Math.Clamp(textSpacing,  SliderTextSpacing.Minimum,  SliderTextSpacing.Maximum);
            logoOffsetY  = Math.Clamp(logoOffsetY,  SliderLogoOffsetY.Minimum,  SliderLogoOffsetY.Maximum);

            if (_currentMode == RenderMode.Border)
            {
                // Mirror ApplyBorderTemplateTweaks exactly
                ApplyTemplateTweaksToContext(
                    img, tmpl,
                    ref marginTop, ref marginBottom, ref marginLeft, ref marginRight, ref logoOffsetY);

                // Re-clamp after tweaks (same as slider clamping in ApplyBorderTemplateTweaks)
                marginTop    = Math.Clamp(marginTop,    SliderTopMargin.Minimum,    SliderTopMargin.Maximum);
                marginBottom = Math.Clamp(marginBottom, SliderBottomMargin.Minimum, SliderBottomMargin.Maximum);
                marginLeft   = Math.Clamp(marginLeft,   SliderLeftMargin.Minimum,   SliderLeftMargin.Maximum);
                marginRight  = Math.Clamp(marginRight,  SliderRightMargin.Minimum,  SliderRightMargin.Maximum);
                logoOffsetY  = Math.Clamp(logoOffsetY,  SliderLogoOffsetY.Minimum,  SliderLogoOffsetY.Maximum);
            }
            else if (_currentMode == RenderMode.Overlay)
            {
                // Mirror ApplyOverlayVisualDefaults
                double refEdge = tmpl.ReferenceShortEdge > 0 ? tmpl.ReferenceShortEdge : DefaultOverlayStyleReferenceShortEdge;
                double overlayFactor = Math.Clamp(shortEdge / refEdge, 0.35, 3.0);
                double baseCorner = tmpl.Corner > 0 ? tmpl.Corner : DefaultOverlayCornerRadius;
                double baseShadow = tmpl.Shadow > 0 ? tmpl.Shadow : DefaultOverlayShadowSize;
                cornerRadius = Math.Clamp(Math.Round(baseCorner * overlayFactor), MinOverlayCornerRadius, MaxOverlayCornerRadius);
                shadowSize   = Math.Clamp(Math.Round(baseShadow * overlayFactor), MinOverlayShadowSize,   MaxOverlayShadowSize);
            }
        }
        else
        {
            // No reference edge — use current slider values as-is (same as single-image path)
            marginTop    = SliderTopMargin.Value;
            marginBottom = SliderBottomMargin.Value;
            marginLeft   = SliderLeftMargin.Value;
            marginRight  = SliderRightMargin.Value;
            cornerRadius = SliderCorner.Value;
            shadowSize   = SliderShadow.Value;
            textSpacing  = SliderTextSpacing.Value;
            logoOffsetY  = SliderLogoOffsetY.Value;
        }

        return new RenderContext
        {
            CurrentImage    = img,
            Exif            = exif,
            Template        = tmpl,
            Layout          = layout,
            IsMarginPriority  = isMarginPriority,
            IsSmartAdaptation = isSmartAdaptation,
            ScalePercent    = scalePercent,
            MarginTop       = marginTop,
            MarginBottom    = marginBottom,
            MarginLeft      = marginLeft,
            MarginRight     = marginRight,
            CornerRadius    = cornerRadius,
            ShadowSize      = shadowSize,
            TextSpacing     = textSpacing,
            LogoOffsetY     = logoOffsetY,
            OutputScale     = 1.0,
            TxtMake         = TxtMake.Text,
            TxtModel        = TxtModel.Text,
            TxtLens         = TxtLens.Text,
            TxtFocal        = TxtFocal.Text,
            TxtFNumber      = TxtFNumber.Text,
            TxtShutter      = TxtShutter.Text,
            TxtISO          = TxtISO.Text,
            TxtLocation     = TxtLocation.Text
        };
    }

    private static void ApplyTemplateTweaksToContext(
        BitmapSource img, TemplateModel tmpl,
        ref double marginTop, ref double marginBottom,
        ref double marginLeft, ref double marginRight,
        ref double logoOffsetY)
    {
        double wImg = img.PixelWidth;
        double hImg = img.PixelHeight;

        if (tmpl.Name == "哈苏水印边框")
        {
            marginTop    *= 1.5;
            marginBottom *= 1.5;

            double wBorderPred = wImg + marginLeft + marginRight;
            double hBorderPred = hImg + marginTop  + marginBottom;
            double refDim    = Math.Min(wBorderPred, hBorderPred);
            double factorLogo = tmpl.ReferenceShortEdge > 0 ? refDim / tmpl.ReferenceShortEdge : 1.0;
            double logoHeight = 32 * factorLogo;
            double topMin     = logoHeight * 2.0 + 10;
            double paramFont  = refDim * 0.018;
            double bottomMin  = paramFont * 2.5 + 10;
            double sideMin    = paramFont * 2.0;

            marginTop    = Math.Max(marginTop,    topMin);
            marginBottom = Math.Max(marginBottom, bottomMin);
            double lr    = Math.Max(Math.Max(marginLeft, marginRight), sideMin);
            marginLeft   = lr;
            marginRight  = lr;
        }

        if (tmpl.Name == "哈苏水印居中")
        {
            double shortEdge  = Math.Min(wImg, hImg);
            bool portrait     = hImg > wImg;
            double edgeFactor = portrait ? 0.018 : 0.022;
            double margin     = shortEdge * edgeFactor;
            marginTop    = margin;
            marginBottom = margin;
            marginLeft   = margin;
            marginRight  = margin;
            logoOffsetY  = shortEdge * (portrait ? 0.035 : 0.030);
        }

        if (tmpl.Name == "签名水印")
        {
            double shortEdge = Math.Min(wImg, hImg);
            double minBottom = Math.Clamp(shortEdge * 0.045, 36, 96);
            marginTop   = 0;
            marginLeft  = 0;
            marginRight = 0;
            marginBottom = Math.Max(marginBottom, minBottom);
        }
    }

    private string BuildBatchOutputPath(string outputDir, string sourcePath)
    {
        string prefix = _currentMode == RenderMode.Border ? "Frame" : "Overlay";
        string suffix = _currentTemplate?.Name ?? _currentLayout.ToString();
        return Path.Combine(outputDir,
            $"{prefix}_{Path.GetFileNameWithoutExtension(sourcePath)}_{suffix}.jpg");
    }

    private string GetPreferredBatchOutputDir()
    {
        return string.IsNullOrWhiteSpace(_userDefaults.BatchOutputDir)
            ? GetDefaultBatchOutputDir()
            : _userDefaults.BatchOutputDir;
    }

    private static string GetDefaultBatchOutputDir()
    {
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (string.IsNullOrWhiteSpace(pictures))
        {
            pictures = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        }

        return Path.Combine(pictures, "YinBatchOutput");
    }

    private void SetBatchOutputDir(string path)
    {
        TxtBatchOutputDir.Text = path.Trim();
        SaveUserDefaults();
    }

    private string EnsureBatchOutputDir()
    {
        string outputDir = TxtBatchOutputDir.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            outputDir = GetPreferredBatchOutputDir();
        }

        Directory.CreateDirectory(outputDir);
        SetBatchOutputDir(outputDir);
        return outputDir;
    }

    private static byte[] EncodeJpeg(RenderTargetBitmap rtb)
    {
        var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
        encoder.Frames.Add(BitmapFrame.Create(rtb));
        using var ms = new System.IO.MemoryStream();
        encoder.Save(ms);
        return ms.ToArray();
    }

    private void AppendBatchLog(string line)
    {
        LstBatchLog.Items.Add(line);
        if (LstBatchLog.Items.Count > 0)
            LstBatchLog.ScrollIntoView(LstBatchLog.Items[^1]);
    }

    private static string GetRenderModeDisplayName(RenderMode mode)
    {
        return mode == RenderMode.Border ? "边框" : "Overlay";
    }

    private string GetBatchTemplateDisplayName()
    {
        return _currentTemplate?.Name ?? $"未选择模板({_currentLayout})";
    }

    private static string FormatBool(bool value)
    {
        return value ? "开" : "关";
    }

    private static string FormatLogValue(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? "<空>" : value.Trim();
    }

    private static string FormatExifLog(ExifInfo? exif)
    {
        if (exif == null)
        {
            return "<无 EXIF>";
        }

        string dateTaken = exif.DateTaken == default ? "<空>" : exif.DateTaken.ToString("yyyy-MM-dd HH:mm:ss");
        string gps = exif.Latitude.HasValue && exif.Longitude.HasValue
            ? $"{exif.Latitude.Value:F6},{exif.Longitude.Value:F6}"
            : "<空>";

        return
            $"Make={FormatLogValue(exif.Make)} | Model={FormatLogValue(exif.Model)} | Lens={FormatLogValue(exif.LensModel)} | Focal={FormatLogValue(exif.FocalLength)} | FNumber={FormatLogValue(exif.FNumber)} | Shutter={FormatLogValue(exif.ExposureTime)} | ISO={FormatLogValue(exif.ISOSpeed)} | DateTaken={dateTaken} | Location={FormatLogValue(exif.LocationText)} | GPS={gps}";
    }

    private string FormatRenderContextLog(RenderContext ctx)
    {
        string template = ctx.Template?.Name ?? "<无模板>";
        return
            $"模式={GetRenderModeDisplayName(_currentMode)} | 模板={template} | 布局={ctx.Layout} | 缩放={ctx.ScalePercent:N0}% | 边距优先={FormatBool(ctx.IsMarginPriority)} | 智能适配={FormatBool(ctx.IsSmartAdaptation)} | Margin(T/B/L/R)={ctx.MarginTop:N0}/{ctx.MarginBottom:N0}/{ctx.MarginLeft:N0}/{ctx.MarginRight:N0} | Corner={ctx.CornerRadius:N0} | Shadow={ctx.ShadowSize:N0} | Spacing={ctx.TextSpacing:N0} | LogoOffsetY={ctx.LogoOffsetY:N0}";
    }

    private static string FormatElapsed(TimeSpan elapsed)
    {
        if (elapsed.TotalSeconds < 1)
        {
            return $"{elapsed.TotalMilliseconds:N0} ms";
        }

        return $"{elapsed.TotalSeconds:N2} s";
    }

    private static string FormatFileSize(int bytes)
    {
        if (bytes < 1024)
        {
            return $"{bytes} B";
        }

        double kilobytes = bytes / 1024d;
        if (kilobytes < 1024)
        {
            return $"{kilobytes:N1} KB";
        }

        return $"{kilobytes / 1024d:N2} MB";
    }
}
