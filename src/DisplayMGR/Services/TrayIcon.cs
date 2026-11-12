using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using DisplayMGR.Native;
using DisplayMGR.ViewModels;
using Forms = System.Windows.Forms;

namespace DisplayMGR.Services;

/// <summary>The notification-area icon: left click toggles the panel, right click opens a menu.</summary>
public sealed class TrayIcon : IDisposable
{
    private readonly MainViewModel _viewModel;
    private readonly Forms.NotifyIcon _icon;
    private readonly Icon _image;
    private readonly Forms.ToolStripMenuItem _widgetItem;
    private readonly Forms.ToolStripMenuItem _startupItem;

    public TrayIcon(MainViewModel viewModel, Action togglePanel)
    {
        _viewModel = viewModel;
        _image = DrawIcon();

        _widgetItem = new Forms.ToolStripMenuItem("Desktop widget", null, (_, _) => viewModel.WidgetVisible = !viewModel.WidgetVisible);
        _startupItem = new Forms.ToolStripMenuItem("Start with Windows", null, (_, _) => viewModel.StartWithWindows = !viewModel.StartWithWindows);

        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open DisplayMGR", null, (_, _) => viewModel.ShowPanelCommand.Execute(null));
        menu.Items.Add(_widgetItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add("Rescan monitors", null, (_, _) => viewModel.RescanCommand.Execute(null));
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => viewModel.ExitCommand.Execute(null));
        menu.Opening += (_, _) => SyncChecks();

        _icon = new Forms.NotifyIcon
        {
            Icon = _image,
            Text = "DisplayMGR",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left)
                togglePanel();
        };
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        NativeMethods.DestroyIcon(_image.Handle);
        _image.Dispose();
    }

    private void SyncChecks()
    {
        _widgetItem.Checked = _viewModel.WidgetVisible;
        _startupItem.Checked = _viewModel.StartWithWindows;
    }

    /// <summary>Draws a small sun so the app needs no icon file.</summary>
    private static Icon DrawIcon()
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        using (var pen = new Pen(Color.White, 3f) { StartCap = LineCap.Round, EndCap = LineCap.Round })
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);
            g.FillEllipse(Brushes.White, 10, 10, 12, 12);
            for (int i = 0; i < 8; i++)
            {
                double angle = i * Math.PI / 4;
                float cos = (float)Math.Cos(angle), sin = (float)Math.Sin(angle);
                g.DrawLine(pen, 16 + cos * 10, 16 + sin * 10, 16 + cos * 14, 16 + sin * 14);
            }
        }
        return Icon.FromHandle(bitmap.GetHicon());
    }
}
