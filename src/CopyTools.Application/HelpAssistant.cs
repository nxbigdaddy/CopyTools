namespace CopyTools.Application;

public sealed record HelpAnswer(string Question, string Answer, string Source, bool Found);

/// <summary>对话式使用帮助：优先 LLM，未配置时回退到内置 FAQ 关键词检索。</summary>
public sealed class HelpAssistant
{
    private const string SystemPrompt =
        "你是 CopyTools 文件复制与同步工具的使用助手。用简洁的中文回答用户问题，"
        + "只描述工具的真实行为：破坏性操作（镜像/移动/更新/双向）都会先预览并把删除移入隔离区，"
        + "覆盖、删除数量或比例超过阈值会阻止执行。";

    private static readonly (string[] Keywords, string Answer)[] Faq =
    [
        (["镜像", "mirror"], "镜像同步会让目标目录与源目录完全一致：目标端多出的文件会被移入隔离区，而不是直接删除。执行前会生成预览，删除数量或比例超过阈值会阻止执行。"),
        (["双向", "two way"], "双向同步会把两端的新增、修改和删除相互传播，需要保存任务以记录上次成功快照；双方都修改同一文件时会保留两份冲突副本。"),
        (["冲突"], "双向同步检测到同一文件两端都被修改时会产生冲突，默认保留两份冲突副本；也可在设置中改为源端覆盖、目标端覆盖或逐项询问。"),
        (["隔离", "恢复", "quarantine"], "镜像、移动、更新等模式需要删除的文件会先移入隔离区，可随时从隔离区一键恢复到原位置。"),
        (["增量", "incremental"], "增量复制只复制目标端不存在的文件，已存在的文件会被跳过。"),
        (["校验", "sha", "verify"], "默认按大小和修改时间快速校验；需要严格校验时可启用 SHA-256，或用采样 SHA-256 在速度与准确率之间折中。"),
        (["计划", "定时", "schedule"], "计划任务支持每天、每周、每月和固定间隔，还支持应用启动、目录变化、路径或移动设备接入时自动触发。"),
        (["加密", "密码", "encrypt"], "备份可加密为 AES-256-GCM 压缩包，并支持更换密码与生成恢复码；错误密码或内容被篡改都会导致恢复失败。"),
    ];

    public async Task<HelpAnswer> AskAsync(
        string question,
        IAiAssistant? assistant = null,
        CancellationToken cancellationToken = default)
    {
        if (assistant is { IsAvailable: true })
        {
            try
            {
                var answer = await assistant.CompleteAsync(SystemPrompt, question, cancellationToken);
                if (!string.IsNullOrWhiteSpace(answer))
                {
                    return new HelpAnswer(question, answer, "ai", true);
                }
            }
            catch (Exception exception) when (exception is InvalidOperationException or IOException or HttpRequestException)
            {
            }
        }

        foreach (var (keywords, answer) in Faq)
        {
            if (keywords.Any(keyword => question.Contains(keyword, StringComparison.OrdinalIgnoreCase)))
            {
                return new HelpAnswer(question, answer, "faq", true);
            }
        }

        return new HelpAnswer(question, "未找到匹配的帮助条目，请查看离线 CHM 手册或联系支持。", "faq", false);
    }
}
