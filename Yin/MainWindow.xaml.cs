using Microsoft.Win32;
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

    private BitmapImage? _currentImage;
    private BitmapSource? _previewImage;
    private CancellationTokenSource? _previewCts;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;

    private CancellationTokenSource? _batchCts;
    private readonly List<string> _batchFiles = new();

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

            BitmapImage bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

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
            TxtBatchOutputDir.Text = dlg.SelectedPath;
    }

    private async void BtnBatchStart_Click(object sender, RoutedEventArgs e)
    {
        if (_batchFiles.Count == 0)
        {
            MessageBox.Show("请先添加待处理文件。");
            return;
        }
        string outputDir = TxtBatchOutputDir.Text.Trim();
        if (string.IsNullOrEmpty(outputDir) || !Directory.Exists(outputDir))
        {
            MessageBox.Show("请选择有效的输出目录。");
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
            await RunBatchAsync(_batchFiles.ToList(), outputDir, _batchCts.Token);
            AppendBatchLog($"── 完成 {_batchFiles.Count} 张 ──");
        }
        catch (OperationCanceledException)
        {
            AppendBatchLog("── 已停止 ──");
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

    private async Task RunBatchAsync(
        IReadOnlyList<string> files,
        string outputDir,
        CancellationToken ct)
    {
        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();

            string path = files[i];
            TxtBatchProgress.Text = $"{i} / {files.Count}  {Path.GetFileName(path)}";
            PrgBatch.Value = (double)i / files.Count * 100;

            try
            {
                ExifInfo? exif = null;
                try { exif = await ExifService.ReadExifDataAsync(path); }
                catch { }

                RenderTargetBitmap? rtb = null;
                await Dispatcher.InvokeAsync(() =>
                {
                    BitmapSource img = LoadBitmapFrozen(path);
                    RenderContext ctx = ComputeBatchRenderContext(img, exif);
                    rtb = RenderCurrentMode(ctx);
                }, System.Windows.Threading.DispatcherPriority.Background, ct);

                string outPath = BuildBatchOutputPath(outputDir, path);
                byte[] jpegBytes = EncodeJpeg(rtb!);
                await Task.Run(() => File.WriteAllBytes(outPath, jpegBytes), ct);

                AppendBatchLog($"✓  {Path.GetFileName(path)}");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                AppendBatchLog($"✗  {Path.GetFileName(path)}  ({ex.Message})");
            }
        }

        TxtBatchProgress.Text = $"{files.Count} / {files.Count}";
        PrgBatch.Value = 100;
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
        return bitmap;
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
}
