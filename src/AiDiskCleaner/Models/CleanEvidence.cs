namespace AiDiskCleaner.Models;

/// <summary>
/// 证据强度。决定的不是「能不能删」，而是「界面敢不敢替用户打勾」。
/// 规则必须如实声明自己的证据有多硬，不要为了好看往上抬。
///
/// 放在 Models 里（而不是规则契约里）是因为它是**候选自己的属性**：
/// <see cref="CleanItem.Evidence"/> 要带着它一路走到界面，
/// 批量勾选的判据（CleanRuleEligibility）也只看它 —— AI 完全不参与这个字段。
/// </summary>
public enum EvidenceLevel
{
    /// <summary>没有任何证据。</summary>
    None = 0,
    /// <summary>启发式：路径像临时目录、扩展名像垃圾、时间长没动过。</summary>
    Heuristic = 1,
    /// <summary>命中了维护中的签名表（认得出这是什么）。</summary>
    Signature = 2,
    /// <summary>已经验证过：算过哈希、解析过快捷方式目标、问过系统。</summary>
    Verified = 3,
}
