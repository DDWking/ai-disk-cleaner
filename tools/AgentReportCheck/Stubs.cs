using AiDiskCleaner.Models;
using AiDiskCleaner.Services;

namespace AiDiskCleaner.Models
{
    /// <summary>
    /// 本检查不链接真实 CleanItem（会拖进整条清理/规则依赖链），
    /// 但 AppSignatures / CandidateSelector 需要这个枚举。值语义与正式工程一致。
    /// </summary>
    public enum CleanRisk
    {
        Safe,
        Confirm,
        Keep,
    }
}

namespace AiDiskCleaner.Services
{
    /// <summary>AppSignatures 用到的分类文案桩；本检查不检查文案。</summary>
    public static class Loc
    {
        public static bool IsEn => false;

        public static string CatSystem => "系统";
        public static string CatBrowser => "浏览器";
        public static string CatDev => "开发";
        public static string CatChat => "聊天";
        public static string CatGame => "游戏";
        public static string CatMedia => "影音";
        public static string CatCloud => "网盘";
        public static string CatVm => "虚拟机";
        public static string CatIde => "IDE";
        public static string CatAiTool => "AI 工具";
        public static string CatOffice => "办公";
        public static string CatSecurity => "安全";
        public static string CatBloat => "预装";
        public static string CatIme => "输入法";
        public static string CatOther => "其他";
        public static string NoteCache => "缓存";
    }
}
