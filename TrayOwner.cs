namespace SWCouponManager;

internal sealed class TrayOwner : IDisposable
{
    private readonly NotifyIcon _icon;
    private bool _disposed;

    internal TrayOwner(Action showWindow, Action togglePause, Action exit)
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("창 열기", null, (_, _) => showWindow());
        menu.Items.Add("자동 실행 일시정지/재개", null, (_, _) => togglePause());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("종료", null, (_, _) => exit());
        _icon = new NotifyIcon
        {
            Text = "SWCouponManager",
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true
        };
        _icon.DoubleClick += (_, _) => showWindow();
    }

    internal bool IsDisposed => _disposed;

    internal void SetPaused(bool paused) =>
        _icon.Text = paused ? "SWCouponManager · 일시정지" : "SWCouponManager · 자동 실행 중";

    public void Dispose()
    {
        if (_disposed) return;
        _icon.Visible = false;
        _icon.ContextMenuStrip?.Dispose();
        _icon.Dispose();
        _disposed = true;
    }
}
