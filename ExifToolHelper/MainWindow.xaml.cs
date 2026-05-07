using System.Collections.Generic;

using System;
using System.IO;
using System.Linq;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Exif.Makernotes;

namespace ExifToolHelper
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public static class Program
    {
        
        // MakerNotes的十六进制标签值（固定为0x927C，所有版本通用）
        private const int TAG_MAKER_NOTE = 0x927C;
        // LensModel的十六进制标签值（固定为0xA434，所有版本通用）
        private const int TAG_LENS_MODEL = 0xA434;
        
        public static void Main(string[] args)
        {
            
            try
            {
                string imagePath = (args != null && args.Length > 0) ? args[0] : "";
                imagePath = @"C:\Users\AustinYanyh\Desktop\微信图片_20260208164948_727_272.jpg";
                
                if (!File.Exists(imagePath))
                {
                    // Console.WriteLine("错误：图片文件不存在！");
                    return;
                }

                // 解析图片所有元数据
                var directories = ImageMetadataReader.ReadMetadata(imagePath);
                
                // 1. 尝试读取通用LensModel字段
                var exifIfd0Directory = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                string lensModel = exifIfd0Directory?.GetDescription(ExifIfd0Directory.TagLensModel) ?? "未找到LensModel字段";
                
                // 3. 核心：手动解析索尼MakerNotes中的LensID
                int sonyLensId = 0;
                string sonyLensName = "未知镜头";
                byte[] makerNoteBytes = null;

                // 遍历所有目录，找到MakerNotes原始字节（不依赖任何特定Directory类）
                foreach (var dir in directories)
                {
                    if (dir.ContainsTag(TAG_MAKER_NOTE))
                    {
                        makerNoteBytes = dir.GetByteArray(TAG_MAKER_NOTE);
                        break;
                    }
                }

                // 解析索尼MakerNotes
                if (makerNoteBytes != null && makerNoteBytes.Length > 0)
                {
                    // 检查是否为索尼MakerNotes（以"SONY"开头）
                    if (makerNoteBytes.Length >= 4 && 
                        makerNoteBytes[0] == 'S' && makerNoteBytes[1] == 'O' &&
                        makerNoteBytes[2] == 'N' && makerNoteBytes[3] == 'Y')
                    {
                        // 索尼LensID常见偏移量（0x1C，不同机型可微调）
                        int lensIdOffset = 0x1C;
                        if (makerNoteBytes.Length >= lensIdOffset + 2)
                        {
                            // 读取2字节转换为整数（索尼LensID为16位无符号数）
                            sonyLensId = BitConverter.ToUInt16(makerNoteBytes, lensIdOffset);
                            // 映射为可读镜头名
                            SonyLensIdMap.TryGetValue(sonyLensId, out sonyLensName);
                        }
                    }
                }
                
                

                // 3. 输出结果
                // Console.WriteLine("===== 镜头信息解析结果 =====");
                // Console.WriteLine($"通用LensModel字段：{lensModel}");
                // Console.WriteLine($"索尼LensID数值：{sonyLensId}");
                // Console.WriteLine($"解析后的完整镜头名：{sonyLensName}");
                // Console.WriteLine("============================");
            }
            catch (Exception ex)
            {
                // 输出异常信息用于调试
                Console.WriteLine($"解析出错：{ex.Message}");
            }

        }
        
        private static readonly Dictionary<int, string> SonyLensIdMap = new Dictionary<int, string>
        {
            // 常见索尼镜头ID与名称映射（可根据需要扩展）
            { 0, "Unknown Lens" },
            { 1, "Sony 16mm f/2.8 Fisheye" },
            { 2, "Sony 20mm f/2.8" },
            { 3, "Sony 24mm f/2.8" },
            { 4, "Sony 28mm f/2.8" },
            { 5, "Sony 35mm f/2.8" },
            { 6, "Sony 50mm f/1.4" },
            { 7, "Sony 50mm f/2.8 Macro" },
            { 8, "Sony 85mm f/1.4" },
            { 9, "Sony 100mm f/2.8 Macro" },
            { 10, "Sony 135mm f/2.8 [T4.5] STF" },
            { 11, "Sony 200mm f/2.8 G" },
            { 12, "Sony 300mm f/2.8 G" },
            { 13, "Sony 70-200mm f/2.8 G" },
            { 14, "Sony 75-300mm f/4.5-5.6" },
            { 15, "Sony 100-400mm f/4.5-5.6 GM OSS" },
            { 16, "Sony 16-35mm f/2.8 GM" },
            { 17, "Sony 24-70mm f/2.8 GM" },
            { 18, "Sony 24-70mm f/2.8 GM II" },
            { 19, "Sony FE 24mm f/1.4 GM" },
            { 20, "Sony FE 35mm f/1.4 GM" },
            { 21, "Sony FE 85mm f/1.4 GM" },
            { 22, "Sony FE 85mm f/1.8" },
            { 23, "Sony FE 50mm f/1.8" },
            { 24, "Sony E 16-55mm f/2.8 G" },
            { 25, "Sony E 18-105mm f/4 G OSS" },
            { 26, "Sony E 18-135mm f/3.5-5.6 OSS" },
            // 可根据需要补充更多镜头ID映射
        };
    }
}
