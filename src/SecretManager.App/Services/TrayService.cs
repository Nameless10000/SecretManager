using System.Drawing;
using System.Drawing.Drawing2D;
using Forms = System.Windows.Forms;

namespace SecretManager.App.Services;

public sealed class TrayService : IDisposable
{
    private readonly Forms.NotifyIcon icon;
    private readonly Forms.ToolStripMenuItem status;
    private readonly Icon lockedIcon = CreateIcon(true);
    private readonly Icon unlockedIcon = CreateIcon(false);
    public TrayService(Action open, Action lockVault, Action exit)
    {
        var menu = new Forms.ContextMenuStrip();
        status = new Forms.ToolStripMenuItem("Заблокировано") { Enabled = false };
        menu.Items.Add(status); menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Открыть SecretManager", null, (_, _) => open());
        menu.Items.Add("Заблокировать", null, (_, _) => lockVault());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выйти", null, (_, _) => exit());
        icon = new Forms.NotifyIcon { Icon = lockedIcon, Text = "SecretManager · Заблокировано", ContextMenuStrip = menu, Visible = true };
        icon.MouseClick += (_, args) => { if (args.Button == Forms.MouseButtons.Left) open(); };
    }
    public void Update(bool unlocked)
    {
        status.Text = unlocked ? "Разблокировано" : "Заблокировано";
        icon.Text = "SecretManager · " + status.Text;
        icon.Icon = unlocked ? unlockedIcon : lockedIcon;
    }
    private static Icon CreateIcon(bool locked)
    {
        using var bitmap = new Bitmap(32, 32);
        using var g = Graphics.FromImage(bitmap); g.SmoothingMode = SmoothingMode.AntiAlias;
        using var fill = new SolidBrush(locked ? Color.FromArgb(255, 184, 78) : Color.FromArgb(71, 205, 151));
        using var pen = new Pen(fill, 3);
        g.DrawArc(pen, 9, 3, 14, 17, 180, 180);
        g.FillRoundedRectangle(fill, new Rectangle(5, 13, 22, 17), 4);
        using var hole = new SolidBrush(Color.FromArgb(24, 28, 40));
        g.FillEllipse(hole, 13, 18, 6, 6);
        var handle = bitmap.GetHicon();
        try { using var borrowed = Icon.FromHandle(handle); return (Icon)borrowed.Clone(); }
        finally { NativeMethods.DestroyIcon(handle); }
    }
    public void Dispose() { icon.Visible = false; icon.ContextMenuStrip?.Dispose(); icon.Dispose(); lockedIcon.Dispose(); unlockedIcon.Dispose(); }
}

internal static class DrawingExtensions
{
    public static void FillRoundedRectangle(this Graphics g, Brush brush, Rectangle rect, int radius)
    {
        using var path = new GraphicsPath(); var d = radius * 2;
        path.AddArc(rect.X, rect.Y, d, d, 180, 90); path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
        path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90); path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
        path.CloseFigure(); g.FillPath(brush, path);
    }
}
