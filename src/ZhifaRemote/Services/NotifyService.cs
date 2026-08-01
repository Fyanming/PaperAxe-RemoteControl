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
                Icon = LoadAppIcon(),
                Visible = true
            };
        }
        catch
        {
            _icon = null;
        }
    }

    private static System.Drawing.Icon LoadAppIcon()
    {
        try
        {
            var stream = System.Windows.Application.GetResourceStream(
                new Uri("pack://application:,,,/Assets/App.ico", UriKind.Absolute))?.Stream;
            if (stream is not null)
            {
                return new System.Drawing.Icon(stream);
            }
        }
        catch
        {
        }
        return System.Drawing.SystemIcons.Information;
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
