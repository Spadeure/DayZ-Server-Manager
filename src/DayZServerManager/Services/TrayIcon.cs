namespace DayZServerManager.Services;

/// <summary>Icône de l'application dans la zone de notification (près de l'horloge).</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly System.Windows.Forms.NotifyIcon _icon;

    public TrayIcon(Action open, Action exit)
    {
        _icon = new System.Windows.Forms.NotifyIcon { Text = "Stryxhost Manager" };
        try
        {
            var exe = Environment.ProcessPath;
            if (exe != null) _icon.Icon = System.Drawing.Icon.ExtractAssociatedIcon(exe);
        }
        catch
        {
            // Icône introuvable : l'icône de notification restera invisible.
        }

        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Ouvrir Stryxhost Manager", null, (_, _) => open());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("Quitter", null, (_, _) => exit());
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => open();
        _icon.Visible = true;
    }

    public void ShowBalloon(string title, string text)
    {
        _icon.BalloonTipTitle = title;
        _icon.BalloonTipText = text;
        _icon.ShowBalloonTip(4000);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
