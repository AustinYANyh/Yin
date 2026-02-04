using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Xmp;
using Yin.Models;

namespace Yin.Services;

/// <summary>
/// EXIF 读取服务（负责从文件提取相机信息与拍摄参数）
/// </summary>
public static class ExifService
{
    /// <summary>
    /// 从图像文件读取 EXIF 信息（兼容 IFD0/SubIFD/XMP 多来源）
    /// </summary>
    public static ExifInfo ReadExifData(string filePath)
    {
        var info = new ExifInfo();
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null)
            {
                info.Make = ifd0.GetDescription(ExifIfd0Directory.TagMake) ?? "";
                info.Model = ifd0.GetDescription(ExifIfd0Directory.TagModel) ?? "";
            }
            var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
            if (subIfd != null)
            {
                info.LensModel = subIfd.GetDescription(ExifSubIfdDirectory.TagLensModel) ?? "";
                var candidates = new List<(string desc, string name)>();
                if (!string.IsNullOrWhiteSpace(info.LensModel))
                    candidates.Add((info.LensModel, "Lens Model"));
                foreach (var dir in directories)
                {
                    foreach (var tag in dir.Tags)
                    {
                        var n = tag.Name;
                        if (n == null) continue;
                        if (n.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            var d = tag.Description;
                            if (!string.IsNullOrWhiteSpace(d))
                                candidates.Add((d, n));
                        }
                    }
                }
                foreach (var xmp in directories.OfType<XmpDirectory>())
                {
                    foreach (var kv in xmp.GetXmpProperties())
                    {
                        var key = kv.Key ?? "";
                        var val = kv.Value ?? "";
                        if (string.IsNullOrWhiteSpace(val)) continue;
                        if (key.IndexOf("Lens", StringComparison.OrdinalIgnoreCase) >= 0)
                            candidates.Add((val, key));
                    }
                }
                if (candidates.Count > 0)
                    info.LensModel = ChooseBestLensName(candidates);
                if (subIfd.TryGetDouble(ExifSubIfdDirectory.TagFNumber, out double f))
                {
                    info.FNumber = $"f/{f:0.0}";
                }
                else
                {
                    info.FNumber = subIfd.GetDescription(ExifSubIfdDirectory.TagFNumber) ?? "";
                }
                if (subIfd.TryGetRational(ExifSubIfdDirectory.TagExposureTime, out var exposureTime))
                {
                    double val = exposureTime.ToDouble();
                    if (val > 0 && val < 1) info.ExposureTime = $"1/{Math.Round(1.0 / val)}";
                    else info.ExposureTime = val.ToString("0.#####");
                }
                else
                {
                    info.ExposureTime = subIfd.GetDescription(ExifSubIfdDirectory.TagExposureTime)?.Replace(" sec", "") ?? "";
                }
                info.ISOSpeed = subIfd.GetDescription(ExifSubIfdDirectory.TagIsoEquivalent) ??
                                subIfd.GetDescription(0x8827) ?? "";
                if (subIfd.TryGetDouble(ExifSubIfdDirectory.TagFocalLength, out double fl))
                {
                    info.FocalLength = $"{fl}mm";
                }
                else
                {
                    info.FocalLength = subIfd.GetDescription(ExifSubIfdDirectory.TagFocalLength)?.Replace(" ", "") ?? "";
                }
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
        if (!string.IsNullOrEmpty(info.Make) && !string.IsNullOrEmpty(info.Model))
        {
            if (info.Model.StartsWith(info.Make, StringComparison.OrdinalIgnoreCase))
            {
                info.Model = info.Model.Substring(info.Make.Length).Trim();
            }
        }
        if (string.IsNullOrEmpty(info.Make)) info.Make = string.Empty;
        return info;
    }

    /// <summary>
    /// 选择最合理的镜头名称（依据来源优先级与特征打分）
    /// </summary>
    public static string ChooseBestLensName(List<(string desc, string name)> candidates)
    {
        string best = "";
        int bestScore = int.MinValue;
        foreach (var c in candidates)
        {
            string d = c.desc;
            string n = c.name;
            int s = 0;
            if (string.Equals(n, "Lens", StringComparison.OrdinalIgnoreCase)) s += 5;
            if (string.Equals(n, "Lens Model", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "LensModel", StringComparison.OrdinalIgnoreCase)) s += 4;
            if (n.IndexOf("aux:Lens", StringComparison.OrdinalIgnoreCase) >= 0) s += 6;
            if (n.IndexOf("exifEX:LensModel", StringComparison.OrdinalIgnoreCase) >= 0) s += 5;
            if (n.IndexOf("LensType", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Lens Type", StringComparison.OrdinalIgnoreCase) >= 0) s += 2;
            if (n.IndexOf("Specification", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Spec", StringComparison.OrdinalIgnoreCase) >= 0) s -= 4;
            if (Regex.IsMatch(d, @"^\s*\d{1,3}(\s*-\s*\d{1,3})?\s*mm\s+f/?\s*\d(\.\d+)?\s*$", RegexOptions.IgnoreCase)) s -= 3;
            var tokens = new[] { "FE", "GM", "OSS", "ZA", "NIKKOR", "RF", "EF", "L", "APO", "DG", "DN", "ART", "XCD", "HC", "HCD", "ZEISS", "TAMRON", "SIGMA", "SAMYANG", "VOIGT", "SUMMILUX", "SUMMICRON", "Noct", "G-Master", "G Master" };
            foreach (var t in tokens)
            {
                if (d.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) { s += 3; break; }
            }
            if (Regex.IsMatch(d, @"[A-Za-z]{2,}")) s += 1;
            if (s > bestScore) { bestScore = s; best = d; }
        }
        return best;
    }
}
