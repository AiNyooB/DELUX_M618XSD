namespace DeluxDriver;

/// <summary>导航契约：由 MainWindow 实现，供 ViewModel/页面切换内容区。</summary>
public interface INavigationService
{
    /// <summary>导航到指定页（按页类型或标识）。</summary>
    void Navigate(string pageKey);

    /// <summary>当前页标识。</summary>
    string CurrentPageKey { get; }
}
