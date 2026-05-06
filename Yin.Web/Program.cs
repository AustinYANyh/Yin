using System.IO;
using System.Text.Json.Serialization;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Options;
using Yin.Models;
using Yin.Services;
using Yin.Web;
using Yin.Web.Models;
using Yin.Web.Services;
using Directory = System.IO.Directory;
using File = System.IO.File;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<YinWebOptions>(builder.Configuration.GetSection(YinWebOptions.SectionName));
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 50 * 1024 * 1024;
});
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});
builder.Services.AddSingleton<StaRenderDispatcher>();

builder.WebHost.ConfigureKestrel(options =>
{
    options.Limits.MaxRequestBodySize = 50 * 1024 * 1024;
});

var app = builder.Build();

app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/templates", () =>
{
    return Results.Json(new
    {
        defaultMode = WatermarkRenderMode.Border,
        modes = Enum.GetNames<WatermarkRenderMode>(),
        defaults = new
        {
            border = TemplateService.DefaultBorderTemplateName,
            overlay = TemplateService.DefaultOverlayTemplateName
        },
        templates = new
        {
            border = TemplateService.CreateTemplates(WatermarkRenderMode.Border).Select(ToTemplateDto),
            overlay = TemplateService.CreateTemplates(WatermarkRenderMode.Overlay).Select(ToTemplateDto)
        }
    });
});

app.MapPost("/api/render", async (
    HttpRequest request,
    IOptions<YinWebOptions> options,
    StaRenderDispatcher dispatcher,
    CancellationToken cancellationToken) =>
{
    YinWebOptions webOptions = options.Value;
    if (!IsAuthorized(request, webOptions))
    {
        return Results.Unauthorized();
    }

    if (!request.HasFormContentType)
    {
        return Results.BadRequest("需要 multipart/form-data 请求。");
    }

    IFormCollection form = await request.ReadFormAsync(cancellationToken);
    IFormFile? file = form.Files.GetFile("image");
    if (file == null || file.Length == 0)
    {
        return Results.BadRequest("请上传图片。");
    }

    if (file.Length > webOptions.MaxUploadBytes)
    {
        return Results.BadRequest($"图片不能超过 {webOptions.MaxUploadBytes / 1024 / 1024}MB。");
    }

    if (!IsSupportedImage(file.FileName, file.ContentType))
    {
        return Results.BadRequest("仅支持 jpg、jpeg、png、bmp 图片。");
    }

    string tempPath = Path.Combine(Path.GetTempPath(), "YinWeb", $"{Guid.NewGuid():N}{Path.GetExtension(file.FileName)}");
    Directory.CreateDirectory(Path.GetDirectoryName(tempPath)!);

    try
    {
        await using (FileStream fs = File.Create(tempPath))
        {
            await file.CopyToAsync(fs, cancellationToken);
        }

        var renderRequest = new RenderRequest
        {
            Mode = ParseMode(form["mode"]),
            TemplateName = form["templateName"].ToString(),
            Make = form["make"].ToString(),
            Model = form["model"].ToString(),
            Lens = form["lens"].ToString(),
            Focal = form["focal"].ToString(),
            FNumber = form["fNumber"].ToString(),
            Shutter = form["shutter"].ToString(),
            ISO = form["iso"].ToString(),
            Location = form["location"].ToString()
        };

        byte[] jpeg = await dispatcher.InvokeAsync(() =>
        {
            BitmapSource image = LoadBitmapFrozen(tempPath);
            ExifInfo exif = ExifService.ReadExifData(tempPath);
            TemplateModel template = TemplateService.FindTemplate(renderRequest.Mode, renderRequest.TemplateName)
                                     ?? throw new InvalidOperationException("没有可用模板。");
            RenderContext ctx = RenderContextFactory.Create(image, exif, template, renderRequest.Mode, new RenderTextOverrides
            {
                Make = renderRequest.Make,
                Model = renderRequest.Model,
                Lens = renderRequest.Lens,
                Focal = renderRequest.Focal,
                FNumber = renderRequest.FNumber,
                Shutter = renderRequest.Shutter,
                ISO = renderRequest.ISO,
                Location = renderRequest.Location
            });

            RenderTargetBitmap final = renderRequest.Mode == WatermarkRenderMode.Border
                ? RenderingService.RenderFinalImage(ctx)
                : RenderingService.RenderOverlayImage(ctx);

            return EncodeJpeg(final);
        });

        string prefix = renderRequest.Mode == WatermarkRenderMode.Border ? "Frame" : "Overlay";
        string sourceName = Path.GetFileNameWithoutExtension(file.FileName);
        string outputName = $"{prefix}_{sourceName}_{SanitizeFileName(renderRequest.TemplateName)}.jpg";
        return Results.File(jpeg, "image/jpeg", outputName);
    }
    finally
    {
        TryDelete(tempPath);
    }
});

app.Run();

static object ToTemplateDto(TemplateModel template)
{
    return new
    {
        template.Name,
        mode = template.Layout.ToString(),
        template.DefaultMake,
        template.DefaultModel,
        template.DefaultLens,
        template.DefaultFocal,
        template.DefaultFNumber,
        template.DefaultShutter,
        template.DefaultISO,
        template.DefaultLocation
    };
}

static bool IsAuthorized(HttpRequest request, YinWebOptions options)
{
    if (string.IsNullOrWhiteSpace(options.AccessPassword))
    {
        return true;
    }

    return string.Equals(request.Headers["X-Yin-Password"].ToString(), options.AccessPassword, StringComparison.Ordinal);
}

static WatermarkRenderMode ParseMode(string? value)
{
    return Enum.TryParse(value, ignoreCase: true, out WatermarkRenderMode mode)
        ? mode
        : WatermarkRenderMode.Border;
}

static bool IsSupportedImage(string fileName, string contentType)
{
    string ext = Path.GetExtension(fileName).ToLowerInvariant();
    return ext is ".jpg" or ".jpeg" or ".png" or ".bmp"
           || contentType is "image/jpeg" or "image/png" or "image/bmp";
}

static BitmapSource LoadBitmapFrozen(string path)
{
    var bitmap = new BitmapImage();
    bitmap.BeginInit();
    bitmap.UriSource = new Uri(path);
    bitmap.CacheOption = BitmapCacheOption.OnLoad;
    bitmap.CreateOptions = BitmapCreateOptions.PreservePixelFormat | BitmapCreateOptions.IgnoreColorProfile;
    bitmap.EndInit();
    BitmapSource oriented = ApplyExifOrientation(bitmap, path);
    oriented.Freeze();
    return oriented;
}

static BitmapSource ApplyExifOrientation(BitmapSource bitmap, string path)
{
    try
    {
        var directories = ImageMetadataReader.ReadMetadata(path);
        var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
        int orientation = 1;
        ifd0?.TryGetInt32(ExifIfd0Directory.TagOrientation, out orientation);
        Transform transform = orientation switch
        {
            2 => new ScaleTransform(-1, 1),
            3 => new RotateTransform(180),
            4 => new ScaleTransform(1, -1),
            5 => new TransformGroup { Children = { new RotateTransform(90), new ScaleTransform(1, -1) } },
            6 => new RotateTransform(90),
            7 => new TransformGroup { Children = { new RotateTransform(270), new ScaleTransform(1, -1) } },
            8 => new RotateTransform(270),
            _ => Transform.Identity
        };

        if (transform == Transform.Identity)
        {
            return bitmap;
        }

        var transformed = new TransformedBitmap(bitmap, transform);
        transformed.Freeze();
        return transformed;
    }
    catch
    {
        return bitmap;
    }
}

static byte[] EncodeJpeg(RenderTargetBitmap rtb)
{
    var encoder = new JpegBitmapEncoder { QualityLevel = 100 };
    encoder.Frames.Add(BitmapFrame.Create(rtb));
    using var ms = new MemoryStream();
    encoder.Save(ms);
    return ms.ToArray();
}

static string SanitizeFileName(string value)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return "template";
    }

    foreach (char c in Path.GetInvalidFileNameChars())
    {
        value = value.Replace(c, '_');
    }

    return value.Trim();
}

static void TryDelete(string path)
{
    try
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
    catch
    {
        // Temporary file cleanup failure should not fail a completed render.
    }
}
