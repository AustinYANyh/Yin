using Microsoft.Win32;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using System.Text.RegularExpressions;
using MetadataExtractor.Formats.Xmp;
using Yin.Models;
using Yin.Services;

namespace Yin;

public partial class MainWindow : Window
{
    private BitmapImage? _currentImage;
    private string _currentFilePath = string.Empty;
    private ExifInfo? _currentExif;


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
            ForceLogoPath = null, // 使用自动解析逻辑
            LogoOffsetY = 0
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印边框",
            Scale = 85,
            MarginTop = 60, MarginBottom = 80,
            MarginLeft = 70, MarginRight = 70,
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
            ReferenceShortEdge = 1800,
        });

        _templates.Add(new TemplateModel
        {
            Name = "哈苏水印居中",
            Scale = 90,
            MarginTop = 120, MarginBottom = 220,
            MarginLeft = 120, MarginRight = 120,
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

        _templates.Add(new TemplateModel
        {
            Name = "底部两行机身+参数",
            Scale = 90,
            MarginTop = 150, MarginBottom = 400, // 从 630 调整为 400，外观更紧凑
            MarginLeft = 150, MarginRight = 150,
            Corner = 0,
            Shadow = 20,
            Spacing = 5,
            Layout = LayoutMode.TwoLines_Bottom_Centered,
            IsMarginPriority = true,
            IsSyncVertical = false, // 上下边距不同步
            IsSyncHorizontal = true,
            IsSmartAdaptation = true, // 默认启用智能自适应
            ForceLogoPath = null,
            LogoOffsetY = 0,
            DefaultMake = "SONY",
            DefaultModel = "ILCE-7RM5",
            DefaultLens = "FE 70-200mm GM OSS II",
            DefaultFocal = "70mm",
            DefaultFNumber = "f/2.8",
            DefaultShutter = "1/800",
            DefaultISO = "100",
            ReferenceShortEdge = 1800 // 以短边为参考调整
        });

        CmbTemplates.ItemsSource = _templates;
        CmbTemplates.DisplayMemberPath = "Name";
        CmbTemplates.SelectedIndex = 3; // 默认选择
    }

    // 应用模板到界面；必要时考虑按参考短边缩放
    private void ApplyTemplateValues(TemplateModel tmpl)
    {
        // 当启用自适应时计算缩放因子
        double factor = 1.0;
        if (tmpl.ReferenceShortEdge > 0 && _currentImage != null)
        {
             // 使用最短边（短边）确保不同宽高比下缩放稳定
             double shortEdge = Math.Min(_currentImage.PixelWidth, _currentImage.PixelHeight);
             factor = shortEdge / tmpl.ReferenceShortEdge;
             
             // 合理范围校验
             if (factor < 0.1) factor = 0.1;
             if (factor > 10) factor = 10;
        }

        // 1. 先更新同步复选框状态
        ChkSyncVertical.Checked -= ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked -= ChkSyncHorizontal_Checked;

        ChkSyncVertical.IsChecked = tmpl.IsSyncVertical;
        ChkSyncHorizontal.IsChecked = tmpl.IsSyncHorizontal;

        ChkSyncVertical.Checked += ChkSyncVertical_Checked;
        ChkSyncHorizontal.Checked += ChkSyncHorizontal_Checked;

        // 2. 用缩放后的数值更新滑块
        SliderScale.Value = tmpl.Scale;
        
        SliderTopMargin.Value = tmpl.MarginTop * factor;
        SliderBottomMargin.Value = tmpl.MarginBottom * factor;
        SliderLeftMargin.Value = tmpl.MarginLeft * factor;
        SliderRightMargin.Value = tmpl.MarginRight * factor;
        
        SliderCorner.Value = tmpl.Corner * factor;
        SliderShadow.Value = tmpl.Shadow * factor;
        SliderTextSpacing.Value = tmpl.Spacing * factor; // 间距通常也需要随缩放调整
        SliderLogoOffsetY.Value = tmpl.LogoOffsetY * factor;
        
        if (tmpl.Name == "哈苏水印边框" && _currentImage != null)
        {
            SliderTopMargin.Value *= 1.5;
            SliderBottomMargin.Value *= 1.5;
            double wImg = _currentImage.PixelWidth;
            double hImg = _currentImage.PixelHeight;
            double wBorderPred = wImg + SliderLeftMargin.Value + SliderRightMargin.Value;
            double hBorderPred = hImg + SliderTopMargin.Value + SliderBottomMargin.Value;
            double refDim = Math.Min(wBorderPred, hBorderPred);
            double factorLogo = (tmpl.ReferenceShortEdge > 0) ? refDim / tmpl.ReferenceShortEdge : 1.0;
            double baseLogoPx = 32;
            double logoHeight = baseLogoPx * factorLogo;
            double topMin = logoHeight * 2.0 + 10;
            double paramFont = refDim * 0.018;
            double bottomMin = paramFont * 2.5 + 10;
            if (SliderTopMargin.Value < topMin) SliderTopMargin.Value = topMin;
            if (SliderBottomMargin.Value < bottomMin) SliderBottomMargin.Value = bottomMin;
            double sideMin = paramFont * 2.0;
            double lr = Math.Max(Math.Max(SliderLeftMargin.Value, SliderRightMargin.Value), sideMin);
            SliderLeftMargin.Value = lr;
            SliderRightMargin.Value = lr;
        }
        
        
        // 哈苏居中：根据样例自动设置边距（横/竖）
        if (tmpl.Name == "哈苏水印居中" && _currentImage != null)
        {
            double w = _currentImage.PixelWidth;
            double h = _currentImage.PixelHeight;
            bool portrait = h > w;
            
            double shortEdge = Math.Min(w, h);
            double edgeF = portrait ? 0.018 : 0.022; // 统一细边
            double bottomF = edgeF; // 与其它边一致
            
            SliderTopMargin.Value = shortEdge * edgeF;
            SliderBottomMargin.Value = shortEdge * bottomF;
            SliderLeftMargin.Value = shortEdge * edgeF;
            SliderRightMargin.Value = shortEdge * edgeF;
            
            double logoOffsetF = portrait ? 0.035 : 0.030; // 竖图略微提高 Logo 高度
            SliderLogoOffsetY.Value = shortEdge * logoOffsetF;
        }
        
        // 更新选项
        ChkMarginPriority.IsChecked = tmpl.IsMarginPriority;
        ChkSmartAdaptation.IsChecked = tmpl.IsSmartAdaptation;
        
        // 更新自定义文本默认值
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
    
    // 滑块同步逻辑
    private void SliderTopMargin_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (ChkSyncVertical?.IsChecked == true && SliderBottomMargin != null)
        {
             // 避免相等时的循环触发
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
        // 立即同步
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
        // 立即同步
        if (SliderRightMargin != null && SliderLeftMargin != null)
             SliderRightMargin.Value = SliderLeftMargin.Value;
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
            
            // 加载图像（使用 OnLoad 以便文件解锁）
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.UriSource = new Uri(path);
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
            bitmap.EndInit();
            bitmap.Freeze();

            _currentImage = bitmap;
            // 读取 EXIF 元数据
            _currentExif = ExifService.ReadExifData(path);
            
            // 按需重新应用自适应模板参数
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
        // 若性能较慢可考虑加入防抖，但单张渲染通常即时更新即可
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
            TxtMake = TxtMake.Text,
            TxtModel = TxtModel.Text,
            TxtLens = TxtLens.Text,
            TxtFocal = TxtFocal.Text,
            TxtFNumber = TxtFNumber.Text,
            TxtShutter = TxtShutter.Text,
            TxtISO = TxtISO.Text
        };
        var finalImage = RenderingService.RenderFinalImage(ctx);
        ImgPreview.Source = finalImage;
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
                    TxtMake = TxtMake.Text,
                    TxtModel = TxtModel.Text,
                    TxtLens = TxtLens.Text,
                    TxtFocal = TxtFocal.Text,
                    TxtFNumber = TxtFNumber.Text,
                    TxtShutter = TxtShutter.Text,
                    TxtISO = TxtISO.Text
                };
                var final = RenderingService.RenderFinalImage(ctx);
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
