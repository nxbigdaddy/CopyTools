namespace CopyTools.Application;

/// <summary>对超大数据集提供分页视图，作为界面虚拟化列表的数据基础。</summary>
public sealed class LargeListPager<T>
{
    private readonly IReadOnlyList<T> _items;
    private readonly int _pageSize;

    public LargeListPager(IReadOnlyList<T> items, int pageSize)
    {
        _items = items;
        _pageSize = Math.Max(1, pageSize);
    }

    public int TotalCount => _items.Count;

    public int PageCount => (_items.Count + _pageSize - 1) / _pageSize;

    public int PageSize => _pageSize;

    public IReadOnlyList<T> GetPage(int pageIndex)
    {
        if (_items.Count == 0)
        {
            return [];
        }

        var index = Math.Clamp(pageIndex, 0, PageCount - 1);
        return _items.Skip(index * _pageSize).Take(_pageSize).ToArray();
    }
}
