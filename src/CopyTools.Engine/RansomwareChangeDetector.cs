namespace CopyTools.Engine;

/// <summary>识别短时间窗口内的批量文件变更，用于主动防勒索告警。</summary>
public sealed class RansomwareChangeDetector
{
    public sealed record ChangeEvent(string Path, DateTime OccurredAtUtc);

    private readonly int _thresholdCount;
    private readonly TimeSpan _window;

    public RansomwareChangeDetector(int thresholdCount = 100, TimeSpan? window = null)
    {
        _thresholdCount = Math.Max(1, thresholdCount);
        _window = window ?? TimeSpan.FromSeconds(60);
    }

    public bool Detect(IReadOnlyList<ChangeEvent> events)
    {
        var ordered = events.OrderBy(item => item.OccurredAtUtc).ToArray();
        var left = 0;
        for (var right = 0; right < ordered.Length; right++)
        {
            while (ordered[right].OccurredAtUtc - ordered[left].OccurredAtUtc > _window)
            {
                left++;
            }

            if (right - left + 1 >= _thresholdCount)
            {
                return true;
            }
        }

        return false;
    }
}
