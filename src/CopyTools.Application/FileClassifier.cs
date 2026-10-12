namespace CopyTools.Application;

public sealed record FileClassification(string RelativePath, string Category, double Confidence, string Basis);

/// <summary>按扩展名/文件名把文件归入语义类别；内容级分类需授权并走 LLM。</summary>
public sealed class FileClassifier
{
    private static readonly IReadOnlyDictionary<string, string> ExtensionCategories =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            [".jpg"] = "图片", [".jpeg"] = "图片", [".png"] = "图片", [".gif"] = "图片",
            [".webp"] = "图片", [".bmp"] = "图片", [".raw"] = "图片", [".heic"] = "图片",
            [".mp4"] = "视频", [".mov"] = "视频", [".mkv"] = "视频", [".avi"] = "视频",
            [".mp3"] = "音频", [".wav"] = "音频", [".flac"] = "音频", [".aac"] = "音频",
            [".doc"] = "文档", [".docx"] = "文档", [".pdf"] = "文档", [".txt"] = "文档",
            [".md"] = "文档", [".xls"] = "文档", [".xlsx"] = "文档", [".ppt"] = "文档", [".pptx"] = "文档",
            [".zip"] = "压缩包", [".rar"] = "压缩包", [".7z"] = "压缩包", [".tar"] = "压缩包", [".gz"] = "压缩包",
            [".exe"] = "程序", [".dll"] = "程序", [".msi"] = "程序",
            [".cs"] = "代码", [".js"] = "代码", [".ts"] = "代码", [".py"] = "代码",
            [".java"] = "代码", [".json"] = "代码", [".xml"] = "代码", [".html"] = "代码",
        };

    public FileClassification Classify(string relativePath)
    {
        var extension = Path.GetExtension(relativePath);
        return ExtensionCategories.TryGetValue(extension, out var category)
            ? new FileClassification(relativePath, category, 0.9, "扩展名")
            : new FileClassification(relativePath, "其他", 0.3, "扩展名");
    }
}
