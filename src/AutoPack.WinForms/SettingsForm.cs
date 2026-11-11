using AutoPack.Core;

namespace AutoPack.WinForms;

internal sealed class SettingsForm : Form
{
    private readonly TextBox output = new() { Dock = DockStyle.Fill };
    private readonly TextBox password = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 1024 };
    private readonly TextBox confirm = new() { Dock = DockStyle.Fill, UseSystemPasswordChar = true, MaxLength = 1024 };
    private readonly CheckBox showPassword = new() { Text = "显示输入的密码", AutoSize = true };
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
        ClientSize = new Size(650, 330);
        MinimumSize = new Size(600, 360);
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(22), ColumnCount = 3, RowCount = 6 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        for (var i = 0; i < 4; i++) layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(LabelFor("输出目录"), 0, 0); layout.Controls.Add(output, 1, 0);
        var browse = new Button { Text = "浏览…", AutoSize = true, MinimumSize = new Size(78, 34), Anchor = AnchorStyles.Top | AnchorStyles.Right };
        browse.Click += (_, _) =>
        {
            using var dialog = new FolderBrowserDialog { Description = "选择固定的输出目录", UseDescriptionForTitle = true };
            if (Directory.Exists(output.Text)) dialog.SelectedPath = output.Text;
            if (dialog.ShowDialog(this) == DialogResult.OK) output.Text = dialog.SelectedPath;
        };
        layout.Controls.Add(browse, 2, 0);
        layout.Controls.Add(LabelFor(current is null ? "加密密码" : "新密码"), 0, 1);
        layout.Controls.Add(password, 1, 1); layout.SetColumnSpan(password, 2);
        var confirmLabel = LabelFor("确认密码");
        layout.Controls.Add(confirmLabel, 0, 2);
        layout.Controls.Add(confirm, 1, 2); layout.SetColumnSpan(confirm, 2);
        showPassword.CheckedChanged += (_, _) =>
        {
            layout.SuspendLayout();
            password.UseSystemPasswordChar = !showPassword.Checked;
            confirmLabel.Visible = confirm.Visible = confirm.Enabled = !showPassword.Checked;
            if (showPassword.Checked) confirm.Clear();
            layout.ResumeLayout(true);
        };
        layout.Controls.Add(showPassword, 1, 3); layout.SetColumnSpan(showPassword, 2);
        var help = new Label
        {
            Text = current is null
                ? "保存一次后，后续打包自动使用这些设置。\n密码由 Windows 当前用户凭据加密保存。"
                : "密码已配置；密码留空即可保留原密码。\n修改密码只影响之后的压缩包，旧压缩包仍需原密码。",
            Dock = DockStyle.Fill, ForeColor = Color.DimGray, AutoSize = false
        };
        layout.Controls.Add(help, 0, 4); layout.SetColumnSpan(help, 3);
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.RightToLeft, WrapContents = false, Margin = Padding.Empty
        };
        var save = new Button { Text = "保存设置", AutoSize = true, MinimumSize = new Size(96, 34) };
        var cancel = new Button { Text = "取消", AutoSize = true, MinimumSize = new Size(96, 34), DialogResult = DialogResult.Cancel };
        save.Click += Save;
        buttons.Controls.Add(save); buttons.Controls.Add(cancel);
        layout.Controls.Add(buttons, 0, 5); layout.SetColumnSpan(buttons, 3);
        Controls.Add(layout); AcceptButton = save; CancelButton = cancel;
        output.Text = current?.OutputDirectory ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "文件传输");
        if (current is not null) password.PlaceholderText = "留空表示保持原密码";
        ResumeLayout(true);
    }

    private void Save(object? sender, EventArgs e)
    {
        try
        {
            var resolvedPassword = PasswordInput.Resolve(password.Text, confirm.Text, showPassword.Checked, current?.Password);
            var settings = new AppSettings(output.Text.Trim(), resolvedPassword);
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

    private static Label LabelFor(string text) => new() { Text = text, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
}
