namespace ZhifaRemote.Services;

public sealed class NotifyService : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon? _icon;

    public NotifyService()
    {
        try
        {
            _icon = new System.Windows.Forms.NotifyIcon
            {
                Text = "纸伐局域网远控",
                Icon = System.Drawing.SystemIcons.Information,
                Visible = true
            };
        }
        catch
        {
            _icon = null;
        }
    }

    public void Show(string title, string body)
    {
        if (_icon is null) return;
        try
        {
            _icon.ShowBalloonTip(5000, title, body, System.Windows.Forms.ToolTipIcon.Info);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        if (_icon is null) return;
        try
        {
            _icon.Visible = false;
        }
        catch
        {
        }
        try
        {
            _icon.Dispose();
        }
        catch
        {
        }
    }
}
