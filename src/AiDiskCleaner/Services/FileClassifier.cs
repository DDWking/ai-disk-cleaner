using System.IO;

namespace AiDiskCleaner.Services;

/// <summary>按文件扩展名分类。</summary>
public static class FileClassifier
{
    public static string Classify(string fileName)
    {
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        if (ext is ".dll" or ".sys" or ".exe" or ".mui") return "系统";
        if (ext is ".log" or ".etl" or ".txt") return "日志";
        if (ext is ".tmp" or ".temp" or ".cache" or ".bak" or ".old") return "临时";
        if (ext is ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp"
            or ".mp4" or ".mp3" or ".mov" or ".wav" or ".mkv") return "媒体";
        if (ext is ".doc" or ".docx" or ".xls" or ".xlsx" or ".pdf" or ".ppt" or ".pptx") return "文档";
        if (ext is ".py" or ".js" or ".ts" or ".json" or ".cs" or ".cpp" or ".c" or ".h"
            or ".java" or ".go" or ".rs" or ".html" or ".css") return "代码";
        if (ext is ".zip" or ".rar" or ".7z" or ".msi" or ".tar" or ".gz" or ".iso") return "压缩包";
        if (ext is ".db" or ".sqlite" or ".dat" or ".bin") return "数据";
        return "其他";
    }
}
