using System.Diagnostics;
using AutoPack.Core;

namespace AutoPack.WinForms;

internal sealed class MainForm : ThemedForm
{
    private readonly SettingsStore store = new(
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "AutoPack", "settings.json"),
        new WindowsSecretProtector());
    private AppSettings? settings;
    private readonly List<string> files = [];
    private readonly Button add = MakeButton("添加文件");
    private readonly Button remove = MakeButton("移除所选");
    private readonly Button clear = MakeButton("清空列表");
    private readonly Button configure = MakeButton("设置");
    private readonly Button start = MakeButton("开始打包");
    private readonly Button cancel = MakeButton("取消任务");
    private readonly Button open = MakeButton("打开输出目录");
    private readonly Label output = new() { AutoEllipsis = true, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label preview = new() { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft };
    private readonly Label count = new() { AutoSize = true, Padding = new Padding(10, 9, 0, 0) };
    private readonly ThemedProgressBar progress = new() { Dock = DockStyle.Fill };
    private readonly Label status = new() { Dock = DockStyle.Fill, AutoEllipsis = true, Text = "请添加文件。" };
    private readonly DataGridView list = new()
    {
        Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false, RowHeadersVisible = false, SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BorderStyle = BorderStyle.FixedSingle
    };
    private CancellationTokenSource? cancellation;
    private Task<PackResult>? job;
    private bool closeRequested;

    public MainForm()
    {
        SuspendLayout();
        Text = "文件传输打包"; Font = new Font("Microsoft YaHei UI", 10);
        MinimumSize = new Size(820, 620); Size = MinimumSize;
        AutoScaleDimensions = new SizeF(96, 96);
        AutoScaleMode = AutoScaleMode.Dpi; StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(26), ColumnCount = 1, RowCount = 9 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        foreach (var height in new[] { 32f, 48f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        foreach (var height in new[] { 44f, 34f, 30f, 52f, 50f }) layout.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
        layout.Controls.Add(new Label
        {
            Text = "文件传输打包", Font = new Font(Font.FontFamily, 22, FontStyle.Bold),
            AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 0, 0, 12)
        }, 0, 0);
        layout.Controls.Add(new Label { Text = "选择文件，一键整理、加密并分卷。", Dock = DockStyle.Fill, Tag = ThemeRole.Muted }, 0, 1);
        var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill };
        toolbar.Controls.AddRange([add, remove, clear, configure, count]); layout.Controls.Add(toolbar, 0, 2);
        list.Columns.Add("name", "文件名"); list.Columns.Add("size", "大小"); list.Columns.Add("path", "原始位置");
        foreach (DataGridViewColumn column in list.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        list.Columns[0].FillWeight = 35; list.Columns[1].FillWeight = 15; list.Columns[2].FillWeight = 50;
        list.RowTemplate.Height = 32; list.AllowDrop = true;
        list.DragEnter += (_, e) => { if (job is null && e.Data?.GetDataPresent(DataFormats.FileDrop) == true) e.Effect = DragDropEffects.Copy; };
        list.DragDrop += (_, e) => { if (job is null && e.Data?.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths); };
        layout.Controls.Add(list, 0, 3); layout.Controls.Add(output, 0, 4); layout.Controls.Add(preview, 0, 5);
        var bar = new Panel { Dock = DockStyle.Fill, Padding = new Padding(0, 7, 0, 7) }; bar.Controls.Add(progress);
        layout.Controls.Add(bar, 0, 6); layout.Controls.Add(status, 0, 7);
        var actions = new FlowLayoutPanel { Dock = DockStyle.Fill };
        actions.Controls.AddRange([start, cancel, open]); layout.Controls.Add(actions, 0, 8);
        Controls.Add(layout);
        cancel.Enabled = false;
        add.Click += (_, _) => { using var dialog = new OpenFileDialog { Multiselect = true, Title = "选择要整理和传输的文件", CheckFileExists = true }; if (dialog.ShowDialog(this) == DialogResult.OK) AddFiles(dialog.FileNames); };
        remove.Click += (_, _) => { foreach (var index in list.SelectedRows.Cast<DataGridViewRow>().Select(r => r.Index).OrderDescending()) files.RemoveAt(index); RefreshFiles(); };
        clear.Click += (_, _) => { files.Clear(); RefreshFiles(); };
        configure.Click += (_, _) => ShowSettings();
        start.Click += StartJob;
        cancel.Click += (_, _) => { cancellation?.Cancel(); cancel.Enabled = false; status.Text = "正在取消并清理本次任务…"; };
        open.Click += (_, _) => OpenOutput();
        FormClosing += (_, e) =>
        {
            if (job is not null)
            {
                e.Cancel = true; closeRequested = true; cancellation?.Cancel();
                status.Text = "正在取消任务，清理完成后退出…";
            }
        };
        Exception? settingsError = null;
        try { settings = store.Load(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { settingsError = ex; }
        ApplyTheme(settings?.Theme ?? AppTheme.Light);
        RefreshSettings();
        Shown += (_, _) =>
        {
            if (settingsError is not null)
                MessageBox.Show(this, settingsError.Message, "需要重新配置", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            if (settings is null) ShowSettings();
        };
        ResumeLayout(true);
    }

    private void ShowSettings()
    {
        using var dialog = new SettingsForm(store, settings);
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            settings = dialog.SavedSettings;
            ApplyTheme(settings?.Theme ?? AppTheme.Light);
        }
        RefreshSettings();
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var added = paths.Where(p => !files.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
        try
        {
            if (added.Length == 0) return;
            PackService.ValidateFiles(files.Concat(added).ToArray());
            files.AddRange(added); RefreshFiles();
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        { MessageBox.Show(this, ex.Message, "无法添加文件", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private void RefreshFiles()
    {
        list.Rows.Clear();
        foreach (var file in files)
        {
            string size;
            try { size = FormatSize(new FileInfo(file).Length); } catch (IOException) { size = "不可用"; } catch (UnauthorizedAccessException) { size = "无法读取"; }
            list.Rows.Add(Path.GetFileName(file), size, file);
        }
        count.Text = $"{files.Count} 个文件";
    }

    private void RefreshSettings()
    {
        output.Text = settings is null ? "输出目录和密码尚未配置，请打开设置。" : $"输出：{settings.OutputDirectory}    |    加密密码：已配置";
        preview.Text = "7z · AES-256 · 文件名加密 · 每卷 700 MiB";
        if (settings is not null && Directory.Exists(settings.OutputDirectory))
        {
            try { preview.Text = PackService.NextName(settings.OutputDirectory, DateOnly.FromDateTime(DateTime.Now)) + "    |    每卷 700 MiB"; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { status.Text = ex.Message; }
        }
        open.Enabled = settings is not null;
    }

    private async void StartJob(object? sender, EventArgs e)
    {
        if (settings is null) { ShowSettings(); if (settings is null) return; }
        var request = new PackRequest(files.ToArray(), settings.OutputDirectory, settings.Password);
        cancellation = new CancellationTokenSource();
        SetBusy(true); progress.Value = 0;
        var reporter = new Progress<PackProgress>(p => { if (!IsDisposed) { progress.Value = Math.Clamp(p.Percent, 0, 100); status.Text = $"{p.Stage}  {p.Percent}%"; } });
        try
        {
            job = Task.Run(() => new PackService(Path.Combine(AppContext.BaseDirectory, "tools", "7za.exe")).RunAsync(request, reporter, cancellation.Token));
            var result = await job;
            status.Text = $"完成，共 {result.Volumes.Count} 个分卷。整理文件夹保留普通文件副本，传输时请选择 .7z 分卷。";
        }
        catch (OperationCanceledException) { status.Text = "任务已取消，本次未完成产物已清理。"; progress.Value = 0; }
        catch (Exception ex)
        {
            closeRequested = false;
            status.Text = "打包失败，请检查错误提示。";
            MessageBox.Show(this, ex.Message, "打包失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            job = null; cancellation.Dispose(); cancellation = null;
            SetBusy(false); RefreshSettings();
            if (closeRequested) Close();
        }
    }

    private void SetBusy(bool busy)
    {
        foreach (var button in new[] { add, remove, clear, configure, start }) button.Enabled = !busy;
        list.Enabled = !busy; cancel.Enabled = busy;
    }

    private void OpenOutput()
    {
        if (settings is null) return;
        try
        {
            if (!Directory.Exists(settings.OutputDirectory)) throw new DirectoryNotFoundException("输出目录尚不存在，请先开始一次任务或在设置中保存目录。");
            Process.Start(new ProcessStartInfo(settings.OutputDirectory) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        { MessageBox.Show(this, ex.Message, "无法打开目录", MessageBoxButtons.OK, MessageBoxIcon.Warning); }
    }

    private static Button MakeButton(string text) => new ThemedButton { Text = text };
    private static string FormatSize(long size) => size < 1024 ? $"{size} B" : size < 1024 * 1024 ? $"{size / 1024d:F1} KiB" : size < 1024L * 1024 * 1024 ? $"{size / 1048576d:F1} MiB" : $"{size / 1073741824d:F2} GiB";
}
