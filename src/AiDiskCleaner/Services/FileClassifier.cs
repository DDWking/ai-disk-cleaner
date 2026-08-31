using System.IO;

namespace AiDiskCleaner.Services;

/// <summary>按文件扩展名分类。</summary>
public static class FileClassifier
{
    public static string Classify(string fileName)
    {
        if (fileName.StartsWith('$')) return "系统";
        string ext = Path.GetExtension(fileName).ToLowerInvariant();
        return Describe(ext);
    }

    public static string Describe(string ext)
    {
        ext = ext.ToLowerInvariant();
        return ext switch
        {
            ".dll" => "应用程序扩展",
            ".exe" => "应用程序",
            ".sys" => "系统文件",
            ".mui" => "系统文件",
            ".log" or ".etl" => "日志",
            ".txt" => "文本",
            ".tmp" or ".temp" or ".cache" or ".bak" or ".old" => "临时",
            ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" => "图片",
            ".mp4" or ".mkv" or ".mov" or ".avi" or ".wmv" => "视频",
            ".mp3" or ".wav" or ".flac" or ".aac" => "音频",
            ".doc" or ".docx" or ".xls" or ".xlsx" or ".pdf" or ".ppt" or ".pptx" => "文档",
            ".py" or ".js" or ".ts" or ".json" or ".cs" or ".cpp" or ".c" or ".h"
                or ".java" or ".go" or ".rs" or ".html" or ".css" => "代码",
            ".zip" => "ZIP File",
            ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" => "压缩包",
            ".msi" or ".iso" => "安装包",
            ".db" or ".sqlite" or ".dat" or ".bin" => "数据",
            ".vhd" or ".vhdx" => "硬盘映像",
            ".img" => "光盘映像",
            ".jar" => "JAR 文件",
            ".pak" => "PAK 文件",
            ".dmp" => "DMP 文件",
            ".nvph" => "NVPH 文件",
            ".bundl" => "BUNDLE 文件",
            ".ppkg" => "配置包",
            ".ress" => "RESS 文件",
            ".body" => "BODY 文件",
            ".assets" => "ASSETS 文件",
            "" or "(无扩展名)" => "(无扩展名)",
            "$mft" => "主文件表",
            "$mftmirr" => "主文件表镜像",
            "$logfile" => "日志文件",
            "$volume" => "卷",
            "$attrdef" => "属性定义",
            "$bitmap" => "簇位图",
            "$boot" => "引导",
            "$badclus" => "坏簇",
            "$secure" => "安全描述符",
            "$upcase" => "大小写表",
            "$extend" => "扩展",
            _ => "其他",
        };
    }
}
