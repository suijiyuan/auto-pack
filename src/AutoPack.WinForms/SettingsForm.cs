using AutoPack.Core;

namespace AutoPack.WinForms;

internal sealed class SettingsForm : ThemedForm
{
    private readonly TextBox output = new() { Anchor = AnchorStyles.Left | AnchorStyles.Right, Margin = new Padding(0, 0, 12, 14) };
    private readonly ComboBox theme = new()
    {
        Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14),
        DropDownStyle = ComboBoxStyle.DropDownList, DrawMode = DrawMode.OwnerDrawFixed,
        AccessibleName = "界面主题"
    };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14), UseSystemPasswordChar = true, MaxLength = 1024 };
    private readonly TextBox confirm = new() { Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 14), UseSystemPasswordChar = true, MaxLength = 1024 };
    private readonly CheckBox showPassword = new() { Text = "显示输入的密码", AutoSize = true, Margin = new Padding(0, 0, 0, 18) };
    private readonly SettingsStore store;
    private readonly AppSettings? current;
    public AppSettings? SavedSettings { get; private set; }

    public SettingsForm(SettingsStore store, AppSettings? current)
    {
        SuspendLayout();
        this.store = store;
        this.current = current;
        Text = "打包设置";
        Font = new Font("Microsoft YaHei UI", 10);
        ClientSize = new Size(650, 410);
        MinimumSize = new Size(620, 440);
        Tag = ThemeRole.Surface;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, Margin = Padding.Empty };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, Padding = new Padding(24, 24, 24, 20),
            Margin = Padding.Empty, ColumnCount = 3, RowCount = 7
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 6; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.Controls.Add(LabelFor("输出目录"), 0, 0); layout.Controls.Add(output, 1, 0);
        var browse = new ThemedButton
        {
            Text = "浏览…", Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Margin = new Padding(0, 0, 0, 14)
        };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择固定的输出目录", UseDescriptionForTitle = true };
            if (Directory.Exists(output.Text)) dialog.SelectedPath = output.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath;
        };
        layout.Controls.Add(browse, 2, 0);
        theme.Items.AddRange(["浅色", "深色", "跟随系统"]);
        theme.SelectedIndex = (int)(current?.Theme ?? AppTheme.Light);
        theme.SelectedIndexChanged += (_, _) => ApplyTheme(SelectedTheme);
        theme.DrawItem += (_, e) =>
        {
            if (e.Index < 0) return;
            var palette = ThemePalette.For(EffectiveTheme);
            var selected = (e.State & DrawItemState.Selected) != 0;
            using var background = new SolidBrush(selected ? palette.Selection : palette.Surface);
            e.Graphics.FillRectangle(background, e.Bounds);
            TextRenderer.DrawText(e.Graphics, theme.GetItemText(theme.Items[e.Index]), e.Font,
                Rectangle.Inflate(e.Bounds, -4, 0), selected ? palette.SelectionText : palette.Text,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter);
            e.DrawFocusRectangle();
        };
        layout.Controls.Add(LabelFor("界面主题"), 0, 1);
        layout.Controls.Add(theme, 1, 1); layout.SetColumnSpan(theme, 2);
        layout.Controls.Add(LabelFor(current is null ? "加密密码" : "新密码"), 0, 2);
        layout.Controls.Add(password, 1, 2); layout.SetColumnSpan(password, 2);
        var confirmLabel = LabelFor("确认密码");
        layout.Controls.Add(confirmLabel, 0, 3);
        layout.Controls.Add(confirm, 1, 3); layout.SetColumnSpan(confirm, 2);
        showPassword.CheckedChanged += (_, _) =>
        {
            layout.SuspendLayout();
            password.UseSystemPasswordChar = !showPassword.Checked;
            confirmLabel.Visible = confirm.Visible = confirm.Enabled = !showPassword.Checked;
            if (showPassword.Checked) confirm.Clear();
            layout.ResumeLayout(true);
        };
        layout.Controls.Add(showPassword, 1, 4); layout.SetColumnSpan(showPassword, 2);
        var help = new Label
        {
            Text = current is null
                ? "保存一次后，后续打包自动使用这些设置。\n密码由 Windows 当前用户凭据加密保存。"
                : "密码已配置；密码留空即可保留原密码。\n修改密码只影响之后的压缩包，旧压缩包仍需原密码。",
            Dock = DockStyle.Fill, AutoSize = true, Tag = ThemeRole.Inset,
            Padding = new Padding(12, 10, 12, 10), Margin = Padding.Empty
        };
        layout.Controls.Add(help, 0, 5); layout.SetColumnSpan(help, 3);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty,
            Padding = new Padding(24, 14, 24, 14), Tag = ThemeRole.Inset
        };
        var save = new ThemedButton
        {
            Text = "保存设置", Margin = Padding.Empty
        };
        var cancel = new ThemedButton
        {
            Text = "取消", Margin = new Padding(0, 0, 10, 0), DialogResult = DialogResult.Cancel
        };
        save.Click += Save;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root); AcceptButton = save; CancelButton = cancel;
        output.Text = current?.OutputDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "文件传输");
        if (current is not null) password.PlaceholderText = "留空表示保持原密码";
        ApplyTheme(SelectedTheme);
        ResumeLayout(true);
    }

    private AppTheme SelectedTheme => theme.SelectedIndex switch
    {
        1 => AppTheme.Dark,
        2 => AppTheme.System,
        _ => AppTheme.Light
    };

    private void Save(object? sender, EventArgs e)
    {
        try
        {
            var resolvedPassword = PasswordInput.Resolve(password.Text, confirm.Text, showPassword.Checked, current?.Password);
            var settings = new AppSettings(output.Text.Trim(), resolvedPassword, SelectedTheme);
            SettingsStore.Validate(settings);
            Directory.CreateDirectory(settings.OutputDirectory);
            var probe = Path.Combine(settings.OutputDirectory, ".autopack-probe-" + Guid.NewGuid().ToString("N"));
            using (new FileStream(probe, FileMode.CreateNew, FileAccess.Write, FileShare.None, 1, FileOptions.DeleteOnClose)) { }
            store.Save(settings);
            SavedSettings = settings;
            password.Clear(); confirm.Clear();
            DialogResult = DialogResult.OK; Close();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        { MessageBox.Show(this, ex.Message, "设置未保存", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static Label LabelFor(string text) => new()
    {
        Text = text, AutoSize = true, Anchor = AnchorStyles.Left,
        Margin = new Padding(0, 0, 16, 14), TextAlign = ContentAlignment.MiddleLeft
    };
}
