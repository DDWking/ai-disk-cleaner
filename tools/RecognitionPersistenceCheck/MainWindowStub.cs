using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner;

/// <summary>
/// 离线检查用的最小 <see cref="MainWindow"/> 桩：只补上
/// <c>MainWindow.RecognitionPersistence.cs</c> 依赖的成员。
///
/// 主工程依赖 BCU 子模块，离线编不了 —— 用这个桩把**接线文件本身**也纳入编译，
/// 既能尽早发现接线代码的编译错误，也能在测试里真正调用那几个钩子验证行为。
/// 它不碰真实窗口（本文件只存在于检查工程里）。
///
/// 逐项 AI 那半套删掉之后这里少了 <c>_itemAi</c>：落盘缓存现在只剩目录用途这一半。
/// </summary>
public partial class MainWindow
{
    private readonly FolderPurposeService _folderPurpose = new();
    private readonly List<OrganizeNode> _organizeAll = new();

    /// <summary>与主窗口同一口径的节点身份代次（见 MainWindow.NodeGeneration 的说明）。</summary>
    private const int NodeGeneration = 0;

    private string AiConfigSignature() => "cfg";
    private string RelativeOf(FileEntry d) => d.FullPath;
}
