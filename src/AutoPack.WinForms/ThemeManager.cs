using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Security;
using AutoPack.Core;
using Microsoft.Win32;

namespace AutoPack.WinForms;

internal enum ThemeRole { Muted, Surface, Inset }

internal sealed record ThemePalette(
    Color Background, Color Surface, Color Inset, Color Text, Color Muted,
    Color Border, Color Button, Color Hover, Color Pressed, Color Accent,
    Color Selection, Color SelectionText)
{
    public static ThemePalette For(AppTheme theme) => theme == AppTheme.Dark
        ? new(Color.FromArgb(25, 28, 34), Color.FromArgb(34, 38, 46), Color.FromArgb(42, 47, 57),
            Color.FromArgb(235, 239, 245), Color.FromArgb(169, 180, 196), Color.FromArgb(78, 88, 104),
            Color.FromArgb(47, 54, 66), Color.FromArgb(60, 70, 86), Color.FromArgb(71, 83, 101),
            Color.FromArgb(66, 117, 223),
            Color.FromArgb(48, 77, 125), Color.White)
        : new(Color.FromArgb(246, 248, 251), Color.White, Color.FromArgb(237, 241, 247),
            Color.FromArgb(28, 36, 48), Color.FromArgb(90, 102, 118), Color.FromArgb(197, 205, 217),
            Color.White, Color.FromArgb(232, 239, 251), Color.FromArgb(216, 227, 247),
            Color.FromArgb(35, 92, 210),
            Color.FromArgb(214, 228, 255), Color.FromArgb(20, 46, 94));
}

internal static class ThemeManager
{
    private sealed class NativeThemeState { public bool Dark; }
    private static readonly ConditionalWeakTable<Control, NativeThemeState> nativeThemes = new();

    public static AppTheme Resolve(AppTheme theme)
    {
        if (theme != AppTheme.System) return theme;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("AppsUseLightTheme") is int value && value == 0
                ? AppTheme.Dark : AppTheme.Light;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            return AppTheme.Light;
        }
    }

    public static void Apply(Control control, AppTheme theme)
    {
        var palette = ThemePalette.For(theme);
        Apply(control, palette, palette.Background, theme == AppTheme.Dark);
    }

    private static void Apply(Control control, ThemePalette palette, Color inheritedBackground, bool dark)
    {
        var role = control.Tag as ThemeRole?;
        control.BackColor = role switch
        {
            ThemeRole.Surface => palette.Surface,
            ThemeRole.Inset => palette.Inset,
            _ => inheritedBackground
        };
        control.ForeColor = role == ThemeRole.Muted ? palette.Muted : palette.Text;

        switch (control)
        {
            case Button button:
                button.FlatStyle = FlatStyle.Flat;
                button.UseVisualStyleBackColor = false;
                button.BackColor = palette.Button;
                button.ForeColor = palette.Text;
                button.FlatAppearance.BorderSize = 1;
                button.FlatAppearance.BorderColor = palette.Border;
                button.FlatAppearance.MouseOverBackColor = palette.Hover;
                button.FlatAppearance.MouseDownBackColor = palette.Pressed;
                if (button is ThemedButton themedButton) themedButton.SetPalette(palette);
                break;
            case TextBox textBox:
                textBox.BackColor = palette.Surface;
                textBox.BorderStyle = BorderStyle.FixedSingle;
                break;
            case ComboBox comboBox:
                comboBox.BackColor = palette.Surface;
                comboBox.FlatStyle = FlatStyle.Standard;
                break;
            case CheckBox checkBox:
                checkBox.FlatStyle = FlatStyle.Flat;
                checkBox.UseVisualStyleBackColor = false;
                break;
            case DataGridView grid:
                grid.BackgroundColor = grid.BackColor = palette.Surface;
                grid.GridColor = palette.Border;
                grid.EnableHeadersVisualStyles = false;
                grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.Single;
                grid.DefaultCellStyle.BackColor = palette.Surface;
                grid.DefaultCellStyle.ForeColor = palette.Text;
                grid.DefaultCellStyle.SelectionBackColor = palette.Selection;
                grid.DefaultCellStyle.SelectionForeColor = palette.SelectionText;
                grid.ColumnHeadersDefaultCellStyle.BackColor = palette.Inset;
                grid.ColumnHeadersDefaultCellStyle.ForeColor = palette.Text;
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = palette.Inset;
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = palette.Text;
                grid.RowHeadersDefaultCellStyle = grid.ColumnHeadersDefaultCellStyle.Clone();
                // Row styles inherit from DefaultCellStyle, including rows added after a theme switch.
                break;
            case ThemedProgressBar progress:
                progress.BackColor = palette.Inset;
                progress.ForeColor = palette.Accent;
                break;
        }

        if (control is TextBox or ComboBox or DataGridView or ScrollBar) UpdateNativeTheme(control, dark);
        foreach (Control child in control.Controls) Apply(child, palette, control.BackColor, dark);
        control.Invalidate(true);
    }

    private static void UpdateNativeTheme(Control control, bool dark)
    {
        var state = nativeThemes.GetValue(control, key =>
        {
            var tracked = new NativeThemeState();
            key.HandleCreated += (_, _) => ApplyNativeTheme(key, tracked.Dark);
            return tracked;
        });
        state.Dark = dark;
        ApplyNativeTheme(control, dark);
    }

    private static void ApplyNativeTheme(Control control, bool dark)
    {
        if (!control.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        // Same native theme name used by WinForms 10 for its implicit dark control styling.
        _ = SetWindowTheme(control.Handle, dark ? "DarkMode_Explorer" : "Explorer", null);
    }

    public static void ApplyTitleBar(Form form, AppTheme theme)
    {
        if (!form.IsHandleCreated || !OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000)) return;
        var dark = theme == AppTheme.Dark ? 1 : 0;
        _ = DwmSetWindowAttribute(form.Handle, 20 /* DWMWA_USE_IMMERSIVE_DARK_MODE */, ref dark, sizeof(int));
    }

    [DllImport("dwmapi.dll", ExactSpelling = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("uxtheme.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int SetWindowTheme(IntPtr hwnd, string subAppName, string? subIdList);
}

internal class ThemedForm : Form
{
    private static readonly Icon applicationIcon = LoadApplicationIcon();
    private AppTheme theme;
    protected AppTheme EffectiveTheme { get; private set; } = AppTheme.Light;

    protected ThemedForm() => Icon = applicationIcon;

    private static Icon LoadApplicationIcon()
    {
        using var stream = typeof(ThemedForm).Assembly.GetManifestResourceStream("AutoPack.AppIcon.ico")
            ?? throw new InvalidOperationException("缺少应用图标资源。");
        return new Icon(stream);
    }

    protected void ApplyTheme(AppTheme value)
    {
        theme = value;
        EffectiveTheme = ThemeManager.Resolve(value);
        SuspendLayout();
        try
        {
            ThemeManager.Apply(this, EffectiveTheme);
            ThemeManager.ApplyTitleBar(this, EffectiveTheme);
        }
        finally { ResumeLayout(true); }
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        ApplyTheme(theme);
    }

    protected override void WndProc(ref Message m)
    {
        base.WndProc(ref m);
        // Windows broadcasts preference changes to top-level windows on their UI thread.
        if (m.Msg == 0x001A /* WM_SETTINGCHANGE */ && theme == AppTheme.System
            && !IsDisposed && !Disposing && ThemeManager.Resolve(theme) != EffectiveTheme)
            ApplyTheme(theme);
    }
}

internal sealed class ThemedButton : Button
{
    private ThemePalette palette = ThemePalette.For(AppTheme.Light);

    protected override Size DefaultSize => new(128, 36);

    public ThemedButton()
    {
        AutoSize = false;
        Size = LogicalToDeviceUnits(DefaultSize);
        Margin = new Padding(0, 2, 10, 2);
        FlatStyle = FlatStyle.Flat;
        UseVisualStyleBackColor = false;
    }

    public override Size GetPreferredSize(Size proposedSize) => LogicalToDeviceUnits(DefaultSize);

    protected override void SetBoundsCore(int x, int y, int width, int height, BoundsSpecified specified)
    {
        var size = LogicalToDeviceUnits(DefaultSize);
        base.SetBoundsCore(x, y, size.Width, size.Height, specified);
    }

    protected override void OnHandleCreated(EventArgs e)
    {
        base.OnHandleCreated(e);
        Size = LogicalToDeviceUnits(DefaultSize);
    }

    protected override void OnDpiChangedAfterParent(EventArgs e)
    {
        base.OnDpiChangedAfterParent(e);
        Size = LogicalToDeviceUnits(DefaultSize);
    }

    internal void SetPalette(ThemePalette value)
    {
        palette = value;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        if (Enabled)
        {
            base.OnPaint(e);
            return;
        }

        // The native flat adapter darkens disabled text from BackColor, making it unreadable in dark mode.
        e.Graphics.Clear(palette.Inset);
        ControlPaint.DrawBorder(e.Graphics, ClientRectangle, palette.Border, ButtonBorderStyle.Solid);
        TextRenderer.DrawText(e.Graphics, Text, Font, Rectangle.Inflate(ClientRectangle, -4, 0), palette.Muted,
            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
    }
}

internal sealed class ThemedProgressBar : ProgressBar
{
    public ThemedProgressBar()
    {
        SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        AccessibleName = "打包进度";
        TabStop = false;
    }

    [DefaultValue(0)]
    public new int Value
    {
        get => base.Value;
        set
        {
            base.Value = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        using var brush = new SolidBrush(ForeColor);
        var fraction = Maximum == Minimum ? 0 : (Value - Minimum) / (double)(Maximum - Minimum);
        e.Graphics.FillRectangle(brush, 0, 0, (int)(Width * fraction), Height);
    }
}
