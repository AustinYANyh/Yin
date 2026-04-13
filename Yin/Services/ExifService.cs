using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.RegularExpressions;
using System.Text.Json;
using System.Threading.Tasks;
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
    private static readonly HttpClient HttpClient = CreateHttpClient();
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
        List<string> debugLines = new()
        {
            $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] ReadExifData",
            $"File: {filePath}"
        };
        try
        {
            var directories = ImageMetadataReader.ReadMetadata(filePath);
            debugLines.Add($"DirectoryCount: {directories.Count}");
            var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
            if (ifd0 != null)
            {
                info.Make = ifd0.GetDescription(ExifIfd0Directory.TagMake) ?? "";
                info.Model = ifd0.GetDescription(ExifIfd0Directory.TagModel) ?? "";
                debugLines.Add($"IFD0 Make: {info.Make}");
                debugLines.Add($"IFD0 Model: {info.Model}");
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

            PopulateLocationInfo(info, directories, debugLines);
        }
        catch (Exception ex)
        {
            debugLines.Add($"Metadata read error: {ex}");
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
        debugLines.Add($"Latitude: {info.Latitude?.ToString() ?? "<empty>"}");
        debugLines.Add($"Longitude: {info.Longitude?.ToString() ?? "<empty>"}");
        debugLines.Add($"LocationText after EXIF/XMP: {info.LocationText}");
        info.LocationDebugLog = string.Join(Environment.NewLine, debugLines);
        return info;
    }

    public static async Task<ExifInfo> ReadExifDataAsync(string filePath)
    {
        ExifInfo info = ReadExifData(filePath);
        if (!string.IsNullOrWhiteSpace(info.LocationText) || !info.Latitude.HasValue || !info.Longitude.HasValue)
        {
            WriteLocationDebugLog(filePath, info.LocationDebugLog);
            return info;
        }

        var reverse = await ReverseGeocodeAsync(info.Latitude.Value, info.Longitude.Value);
        AppendLocationDebug(info, reverse.debugLog);
        if (!string.IsNullOrWhiteSpace(reverse.location))
        {
            info.LocationText = reverse.location;
            AppendLocationDebug(info, $"LocationText after reverse geocoding: {info.LocationText}");
        }

        WriteLocationDebugLog(filePath, info.LocationDebugLog);
        return info;
    }

    private static HttpClient CreateHttpClient()
    {
        HttpClient client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(6)
        };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("Yin", "1.0"));
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(reverse-geocoding)"));
        client.DefaultRequestHeaders.AcceptLanguage.ParseAdd("zh-CN,zh;q=0.9,en;q=0.6");
        return client;
    }

    private static async Task<(string? location, string debugLog)> ReverseGeocodeAsync(double latitude, double longitude)
    {
        StringBuilder debug = new();
        try
        {
            string lat = latitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string lon = longitude.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture);
            string url = $"https://nominatim.openstreetmap.org/reverse?format=jsonv2&lat={lat}&lon={lon}&zoom=10&addressdetails=1&layer=address";
            debug.AppendLine($"ReverseGeocode URL: {url}");
            using HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Get, url);
            using HttpResponseMessage response = await HttpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                debug.AppendLine($"ReverseGeocode HTTP status: {(int)response.StatusCode} {response.StatusCode}");
                return (null, debug.ToString());
            }

            string rawJson = await response.Content.ReadAsStringAsync();
            debug.AppendLine($"ReverseGeocode Raw JSON: {rawJson}");
            using JsonDocument doc = JsonDocument.Parse(rawJson);
            if (!doc.RootElement.TryGetProperty("address", out JsonElement address))
            {
                debug.AppendLine("ReverseGeocode address: <missing>");
                return (null, debug.ToString());
            }

            string displayName = "";
            if (doc.RootElement.TryGetProperty("display_name", out JsonElement displayNameElement))
            {
                displayName = NormalizeLocationText(displayNameElement.GetString());
            }

            List<string> parts = new();
            string state = ReadAddressPart(address, "state");
            string province = ReadAddressPart(address, "province");
            string city = ReadAddressPart(address, "city");
            string municipality = ReadAddressPart(address, "municipality");
            string stateDistrict = ReadAddressPart(address, "state_district");
            string county = ReadAddressPart(address, "county");
            string cityDistrict = ReadAddressPart(address, "city_district");
            string town = ReadAddressPart(address, "town");
            string suburb = ReadAddressPart(address, "suburb");
            string displayNameCity = ExtractCityFromDisplayName(displayName);

            if (LooksLikeDistrictLevel(city) && !string.IsNullOrWhiteSpace(displayNameCity))
            {
                city = displayNameCity;
            }
            else if (string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(displayNameCity))
            {
                city = displayNameCity;
            }

            debug.AppendLine($"ReverseGeocode display_name: {displayName}");
            debug.AppendLine($"ReverseGeocode derived city from display_name: {displayNameCity}");
            debug.AppendLine($"ReverseGeocode address.state: {state}");
            debug.AppendLine($"ReverseGeocode address.province: {province}");
            debug.AppendLine($"ReverseGeocode address.city: {city}");
            debug.AppendLine($"ReverseGeocode address.municipality: {municipality}");
            debug.AppendLine($"ReverseGeocode address.state_district: {stateDistrict}");
            debug.AppendLine($"ReverseGeocode address.county: {county}");
            debug.AppendLine($"ReverseGeocode address.city_district: {cityDistrict}");
            debug.AppendLine($"ReverseGeocode address.town: {town}");
            debug.AppendLine($"ReverseGeocode address.suburb: {suburb}");

            AddDistinctLocationPart(parts, state);
            AddDistinctLocationPart(parts, province);
            AddDistinctLocationPart(parts, city);
            AddDistinctLocationPart(parts, municipality);
            AddDistinctLocationPart(parts, stateDistrict);
            if (parts.Count == 0)
            {
                AddDistinctLocationPart(parts, county);
                AddDistinctLocationPart(parts, cityDistrict);
                AddDistinctLocationPart(parts, town);
                AddDistinctLocationPart(parts, suburb);
            }

            string result = string.Join(" ", parts);
            debug.AppendLine($"ReverseGeocode result: {result}");
            return (string.IsNullOrWhiteSpace(result) ? null : result, debug.ToString());
        }
        catch (Exception ex)
        {
            debug.AppendLine($"ReverseGeocode exception: {ex}");
            return (null, debug.ToString());
        }
    }

    private static string ReadAddressPart(JsonElement address, string propertyName)
    {
        if (!address.TryGetProperty(propertyName, out JsonElement valueElement))
        {
            return string.Empty;
        }

        return NormalizeLocationText(valueElement.GetString());
    }

    private static bool LooksLikeDistrictLevel(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        return value.EndsWith("区", StringComparison.Ordinal)
               || value.EndsWith("县", StringComparison.Ordinal)
               || value.EndsWith("旗", StringComparison.Ordinal)
               || value.EndsWith("镇", StringComparison.Ordinal)
               || value.EndsWith("乡", StringComparison.Ordinal);
    }

    private static string ExtractCityFromDisplayName(string displayName)
    {
        if (string.IsNullOrWhiteSpace(displayName))
        {
            return string.Empty;
        }

        string[] parts = displayName
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        foreach (string part in parts)
        {
            if (part.EndsWith("市", StringComparison.Ordinal) && !part.Contains("省", StringComparison.Ordinal))
            {
                return part;
            }
        }

        return string.Empty;
    }

    private static void PopulateLocationInfo(ExifInfo info, IEnumerable<MetadataExtractor.Directory> directories, List<string> debugLines)
    {
        var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
        if (gps != null)
        {
            try
            {
                var geo = gps.GetGeoLocation();
                debugLines.Add($"GPS TagLatitude: {gps.GetDescription(GpsDirectory.TagLatitude)}");
                debugLines.Add($"GPS TagLongitude: {gps.GetDescription(GpsDirectory.TagLongitude)}");
                debugLines.Add($"GPS TagProcessingMethod: {gps.GetDescription(GpsDirectory.TagProcessingMethod)}");
                if (geo != null && !geo.IsZero)
                {
                    info.Latitude = geo.Latitude;
                    info.Longitude = geo.Longitude;
                    debugLines.Add($"GPS GeoLocation parsed: {info.Latitude}, {info.Longitude}");
                }
            }
            catch (Exception ex)
            {
                debugLines.Add($"GPS parse exception: {ex.Message}");
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

                debugLines.Add($"XMP location-ish key: {key} = {value}");
                AddLocationCandidate(candidates, value, key);
            }
        }

        string structuredLocation = BuildStructuredLocation(directories.OfType<XmpDirectory>(), debugLines);
        if (!string.IsNullOrWhiteSpace(structuredLocation))
        {
            info.LocationText = structuredLocation;
            debugLines.Add($"Structured location selected: {structuredLocation}");
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
            debugLines.Add($"Candidate location selected: {best}");
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

    private static string BuildStructuredLocation(IEnumerable<XmpDirectory> directories, List<string> debugLines)
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
                    debugLines.Add($"Structured sublocation from {key}: {value}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(city) &&
                    key.IndexOf("City", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    city = value;
                    debugLines.Add($"Structured city from {key}: {value}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(province) &&
                    (key.IndexOf("ProvinceState", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("Province", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("State", StringComparison.OrdinalIgnoreCase) >= 0
                     || key.IndexOf("AdministrativeArea", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    province = value;
                    debugLines.Add($"Structured province from {key}: {value}");
                    continue;
                }

                if (string.IsNullOrWhiteSpace(country) &&
                    key.IndexOf("Country", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    country = value;
                    debugLines.Add($"Structured country from {key}: {value}");
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

    private static void AppendLocationDebug(ExifInfo info, string debugText)
    {
        if (string.IsNullOrWhiteSpace(debugText))
        {
            return;
        }

        info.LocationDebugLog = string.IsNullOrWhiteSpace(info.LocationDebugLog)
            ? debugText
            : $"{info.LocationDebugLog}{Environment.NewLine}{debugText}";
    }

    private static void WriteLocationDebugLog(string filePath, string debugText)
    {
        try
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (string.IsNullOrWhiteSpace(desktop))
            {
                return;
            }

            string logPath = Path.Combine(desktop, "Yin_location_debug.log");
            File.AppendAllText(logPath,
                $"{Environment.NewLine}=============================={Environment.NewLine}{debugText}{Environment.NewLine}",
                Encoding.UTF8);
        }
        catch
        {
            // Swallow logging errors to avoid breaking image loading.
        }
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
