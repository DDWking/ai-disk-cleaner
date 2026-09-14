using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models
{
    /// <summary>
    /// 本检查只用到删除目标里的 <c>CleanItem? Source</c> 这一个引用位。
    /// 不链接真实 CleanItem（那会拖进整条清理/AI 依赖链），用一个最小类型满足编译。
    /// </summary>
    public class CleanItem
    {
        public bool IsDirectory { get; set; }
    }
}

namespace AiDiskCleaner.Services
{
    /// <summary>界面文案桩：文件夹删除的文案都在 FolderDeleteText 里，这里只需要语言开关。</summary>
    public static class Loc
    {
        public static bool IsEn => false;
    }

    /// <summary>
    /// 默认探针桩。真实探针不参与本检查（全部走 <c>FakeProbe</c>），
    /// 但 <see cref="FolderDeleteService"/> / <see cref="DeletionPreflight"/> 的默认参数需要这个类型存在。
    /// </summary>
    public sealed class Win32FileSystemProbe : IFileSystemProbe
    {
        public static readonly Win32FileSystemProbe Instance = new();
        public FileProbeInfo Probe(string path) => FileProbeInfo.Missing("stub probe");
        public FileIdentity? TryGetIdentity(string path) => null;
        public void SendToRecycle(IReadOnlyList<string> paths) { }
    }
}
