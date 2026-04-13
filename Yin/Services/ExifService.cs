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
    private static readonly string[] InvalidLocationTokens =
    {
        "CELLID", "CELL", "LAC", "MCC", "MNC", "CID", "BASESTATION", "BTS",
        "SOURCEAUTOCOMPUTED", "SOURCECOMPUTED", "AUTOCOMPUTED", "COMPUTED",
        "SOURCEMANUAL", "SOURCEESTIMATED", "SOURCESETEXPLICITLY", "SETEXPLICITLY",
        "EXPLICITLY", "DERIVED", "AUTOLOCATION", "LOCATIONSOURCE"
    };

    private static readonly string[] IgnoredLocationKeyTokens =
    {
        "Source", "Computed", "Derived", "Estimate", "Estimated", "Provider", "Method", "Reference"
    };

    private static readonly string[] LocationPriorityTokens =
    {
        "LocationShownSublocation", "LocationShownCity", "Sublocation", "SubLocation", "Location",
        "City", "ProvinceState", "Province", "State", "CountryName", "Country"
    };

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

            PopulateLocationInfo(info, directories);
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

    private static void PopulateLocationInfo(ExifInfo info, IEnumerable<MetadataExtractor.Directory> directories)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps != null)
        {
            try
            {
                var geo = gps.GetGeoLocation();
                if (geo != null && !geo.IsZero)
                {
                    info.Latitude = geo.Latitude;
                    info.Longitude = geo.Longitude;
                }
            }
            catch
            {
                // Ignore malformed GPS payloads and continue with textual location parsing.
            }
        }

        var candidates = new List<(string value, string key)>();

        if (gps != null)
        {
            AddLocationCandidate(candidates, gps.GetDescription(GpsDirectory.TagAreaInformation), "gps:AreaInformation");
            AddLocationCandidate(candidates, gps.GetDescription(GpsDirectory.TagProcessingMethod), "gps:ProcessingMethod");
            AddLocationCandidate(candidates, gps.GetDescription(GpsDirectory.TagMapDatum), "gps:MapDatum");
        }

        foreach (var xmp in directories.OfType<XmpDirectory>())
        {
            foreach (var kv in xmp.GetXmpProperties())
            {
                string key = kv.Key ?? "";
                string value = kv.Value ?? "";
                if (!LooksLikeLocationKey(key))
                {
                    continue;
                }

                AddLocationCandidate(candidates, value, key);
            }
        }

        string structuredLocation = BuildStructuredLocation(directories.OfType<XmpDirectory>());
        if (!string.IsNullOrWhiteSpace(structuredLocation))
        {
            info.LocationText = structuredLocation;
            return;
        }

        if (candidates.Count == 0)
        {
            return;
        }

        string best = ChooseBestLocationText(candidates);
        if (!string.IsNullOrWhiteSpace(best))
        {
            info.LocationText = best;
        }
    }

    private static bool LooksLikeLocationKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        foreach (string ignoredToken in IgnoredLocationKeyTokens)
        {
            if (key.IndexOf(ignoredToken, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }
        }

        string normalized = key.Replace("-", "", StringComparison.OrdinalIgnoreCase);
        return normalized.IndexOf("location", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("city", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("province", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("state", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("country", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("sublocation", StringComparison.OrdinalIgnoreCase) >= 0
               || normalized.IndexOf("administrativearea", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static void AddLocationCandidate(List<(string value, string key)> candidates, string? value, string key)
    {
        string normalized = NormalizeLocationText(value);
        if (string.IsNullOrWhiteSpace(normalized) || IsInvalidLocationValue(normalized))
        {
            return;
        }

        if (candidates.Any(c => string.Equals(c.value, normalized, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        candidates.Add((normalized, key));
    }

    private static string NormalizeLocationText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        string normalized = value.Trim();
        normalized = normalized.Trim('\u0000', ' ', '\t', '\r', '\n', '"', '\'');
        normalized = Regex.Replace(normalized, @"\s+", " ");
        normalized = normalized.Replace(">", " ").Replace("|", " ");
        normalized = Regex.Replace(normalized, @"\b(?:GPS|WGS-84|WGS84)\b", "", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"\b(?:Unknown|Undefined|Digital Camera|ASCII)\b", "", RegexOptions.IgnoreCase);
        normalized = Regex.Replace(normalized, @"[:;,/]+$", "");
        normalized = normalized.Trim();
        return normalized;
    }

    private static bool IsInvalidLocationValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        string compact = Regex.Replace(value, @"[\s_\-]", "").ToUpperInvariant();
        foreach (string token in InvalidLocationTokens)
        {
            if (compact == token || compact.StartsWith(token, StringComparison.Ordinal))
            {
                return true;
            }
        }

        if (compact.Contains("SOURCE", StringComparison.Ordinal) &&
            (compact.Contains("COMPUTED", StringComparison.Ordinal)
             || compact.Contains("MANUAL", StringComparison.Ordinal)
             || compact.Contains("ESTIMATED", StringComparison.Ordinal)
             || compact.Contains("EXPLICIT", StringComparison.Ordinal)))
        {
            return true;
        }

        if (compact.Contains("SET", StringComparison.Ordinal) &&
            compact.Contains("EXPLICIT", StringComparison.Ordinal))
        {
            return true;
        }

        if (Regex.IsMatch(value, @"^(?:[A-Z]{2,}\d*|\d+[A-Z]*)$", RegexOptions.CultureInvariant))
        {
            return true;
        }

        return false;
    }

    private static string BuildStructuredLocation(IEnumerable<XmpDirectory> directories)
    {
        string sublocation = "";
        string city = "";
        string province = "";
        string country = "";

        foreach (var xmp in directories)
        {
            foreach (var kv in xmp.GetXmpProperties())
            {
                string key = kv.Key ?? "";
                string value = NormalizeLocationText(kv.Value);
                if (string.IsNullOrWhiteSpace(value) || IsInvalidLocationValue(value))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(sublocation) &&
                    (key.IndexOf("LocationShownSublocation", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("Sublocation", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("SubLocation", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    sublocation = value;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(city) &&
                    key.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    city = value;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(province) &&
                    (key.IndexOf("ProvinceState", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("Province", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("AdministrativeArea", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    province = value;
                    continue;
                }

                if (string.IsNullOrWhiteSpace(country) &&
                    key.IndexOf("Country", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    country = value;
                }
            }
        }

        var parts = new List<string>();
        AddDistinctLocationPart(parts, province);
        AddDistinctLocationPart(parts, city);
        AddDistinctLocationPart(parts, sublocation);

        if (parts.Count == 0)
        {
            AddDistinctLocationPart(parts, country);
            AddDistinctLocationPart(parts, province);
            AddDistinctLocationPart(parts, city);
        }

        return string.Join(" ", parts);
    }

    private static void AddDistinctLocationPart(List<string> parts, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        if (parts.Any(p => string.Equals(p, value, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        if (parts.Any(p => p.Contains(value, StringComparison.OrdinalIgnoreCase) || value.Contains(p, StringComparison.OrdinalIgnoreCase)))
        {
            return;
        }

        parts.Add(value);
    }

    private static string ChooseBestLocationText(List<(string value, string key)> candidates)
    {
        string best = "";
        int bestScore = int.MinValue;

        foreach (var candidate in candidates)
        {
            string value = candidate.value;
            string key = candidate.key ?? "";
            int score = 0;

            for (int i = 0; i < LocationPriorityTokens.Length; i++)
            {
                if (key.IndexOf(LocationPriorityTokens[i], StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    score += 40 - (i * 4);
                    break;
                }
            }

            if (Regex.IsMatch(value, @"[\u4e00-\u9fff]")) score += 10;
            if (value.EndsWith("市", StringComparison.Ordinal) || value.EndsWith("区", StringComparison.Ordinal)) score += 8;
            if (value.EndsWith("省", StringComparison.Ordinal) || value.EndsWith("县", StringComparison.Ordinal)) score += 6;
            if (value.Length is >= 2 and <= 18) score += 6;
            if (value.Length > 24) score -= 12;
            if (Regex.IsMatch(value, @"^\d+(\.\d+)?[, ]+\d+(\.\d+)?$")) score -= 20;
            if (value.IndexOf("http", StringComparison.OrdinalIgnoreCase) >= 0) score -= 20;

            if (score > bestScore)
            {
                bestScore = score;
                best = value;
            }
        }

        return best;
    }

    /// <summary>
    /// 选择最合理的镜头名称（依据来源优先级与特征打分）
    /// </summary>
    public static string ChooseBestLensName(List<(string desc, string name)> candidates)
    {
        string Normalize(string x)
        {
            string y = x.Trim();
            y = Regex.Replace(y, @"\s+", " ");
            y = Regex.Replace(y, @"\bF\s*([0-9](?:\.[0-9]+)?)\b", m => $"f/{m.Groups[1].Value}", RegexOptions.IgnoreCase);
            y = y.Replace(" II", " Ⅱ").Replace(" III", " Ⅲ").Replace(" IV", " Ⅳ");
            y = Regex.Replace(y, @"^SONY\s+", "", RegexOptions.IgnoreCase);
            return y;
        }
        string best = "";
        int bestScore = int.MinValue;
        foreach (var c in candidates)
        {
            string d = Normalize(c.desc);
            string n = c.name ?? "";
            int s = 0;
            if (string.Equals(n, "Lens", StringComparison.OrdinalIgnoreCase)) s += 6;
            if (string.Equals(n, "Lens Model", StringComparison.OrdinalIgnoreCase) || string.Equals(n, "LensModel", StringComparison.OrdinalIgnoreCase)) s += 5;
            if (n.IndexOf("aux:Lens", StringComparison.OrdinalIgnoreCase) >= 0) s += 7;
            if (n.IndexOf("exifEX:LensModel", StringComparison.OrdinalIgnoreCase) >= 0) s += 6;
            if (n.IndexOf("LensType", StringComparison.OrdinalIgnoreCase) >= 0 || n.IndexOf("Lens Type", StringComparison.OrdinalIgnoreCase) >= 0) s += 1;
            bool hasMmRange = Regex.IsMatch(d, @"\d{1,3}\s*-\s*\d{1,3}\s*mm", RegexOptions.IgnoreCase);
            bool hasMmPrime = Regex.IsMatch(d, @"\b\d{1,3}\s*mm\b", RegexOptions.IgnoreCase);
            bool hasF = Regex.IsMatch(d, @"f/?\s*\d(\.\d+)?", RegexOptions.IgnoreCase);
            if (hasMmRange) s += 6;
            if (!hasMmRange && hasMmPrime) s += 5;
            if (hasF) s += 4;
            var tokens = new[] { "FE", "GM", "OSS", "ZA", "NIKKOR", "RF", "EF", "L", "APO", "DG", "DN", "ART", "XCD", "HC", "HCD", "ZEISS", "TAMRON", "SIGMA", "SAMYANG", "VOIGT", "SUMMILUX", "SUMMICRON", "Noct", "G-Master", "G Master", "G", "E" };
            foreach (var t in tokens)
            {
                if (d.IndexOf(t, StringComparison.OrdinalIgnoreCase) >= 0) { s += 3; break; }
            }
            if (d.IndexOf("Ⅱ", StringComparison.OrdinalIgnoreCase) >= 0 || d.IndexOf("III", StringComparison.OrdinalIgnoreCase) >= 0 || d.IndexOf("Ⅲ", StringComparison.OrdinalIgnoreCase) >= 0) s += 1;
            if (Regex.IsMatch(d, @"[A-Za-z]{2,}")) s += 1;
            if (d.Length >= 8) s += 1;
            if (s > bestScore) { bestScore = s; best = d; }
        }
        return best;
    }
}
