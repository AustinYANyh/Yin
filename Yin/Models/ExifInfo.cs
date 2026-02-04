using System;
using System.Windows.Media.Imaging;
namespace Yin.Models;

/// <summary>
/// EXIF 元信息模型（相机品牌、机型、镜头、曝光参数等）
/// </summary>
public class ExifInfo
{
    /// <summary>品牌（Make）</summary>
    public string Make { get; set; } = "";
    /// <summary>机型（Model）</summary>
    public string Model { get; set; } = "";
    /// <summary>焦距（Focal Length）</summary>
    public string FocalLength { get; set; } = "";
    /// <summary>光圈（F-Number）</summary>
    public string FNumber { get; set; } = "";
    /// <summary>快门（Exposure Time）</summary>
    public string ExposureTime { get; set; } = "";
    /// <summary>ISO 感光度</summary>
    public string ISOSpeed { get; set; } = "";
    /// <summary>镜头型号（Lens Model）</summary>
    public string LensModel { get; set; } = "";
    /// <summary>拍摄时间</summary>
    public DateTime DateTaken { get; set; }
}
