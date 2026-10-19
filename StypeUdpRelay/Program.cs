using System.Text.Json;

namespace StypeUdpRelay;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public sealed class MainForm : Form
{
    private readonly TextBox listen = new() { Text = "0.0.0.0", Width = 150 };
    private readonly NumericUpDown port = new() { Minimum = 1, Maximum = 65535, Value = 6299, Width = 90 };
    private readonly DataGridView grid = new() { Dock = DockStyle.Fill, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, AllowUserToAddRows = true, RowHeadersVisible = true, RowHeadersWidth = 28, SelectionMode = DataGridViewSelectionMode.FullRowSelect, MultiSelect = false, BackgroundColor = Color.White };
    private readonly CheckBox auto = new() { Text = "Start forwarding when app opens", AutoSize = true };
    private readonly Button start = new() { Text = "Start", Width = 110, Height = 34 };
    private readonly Button deleteRow = new() { Text = "Delete selected row", Width = 170, Height = 34 };
    private readonly Button stop = new() { Text = "Stop", Width = 110, Height = 34, Enabled = false };
    private readonly Label status = new() { AutoSize = true, Text = "Stopped", ForeColor = Color.DimGray };
    private readonly Label stats = new() { AutoSize = true };
    private readonly Label detail = new() { AutoSize = true };
    private readonly System.Windows.Forms.Timer timer = new() { Interval = 500 };
    private readonly string configPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "JM Tools", "StypeUdpRelay", "settings.json");
    private Relay? relay;
    private long previous;
    private DateTime previousTime = DateTime.UtcNow;

    public MainForm()
    {
        Text = "JM · stYpe UDP Relay 1.1.1";
        Font = new Font("Segoe UI", 10);
        ClientSize = new Size(730, 510);
        MinimumSize = new Size(650, 500);
        StartPosition = FormStartPosition.CenterScreen;
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(20), ColumnCount = 1, RowCount = 8 };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 30));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 45));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44));
        layout.Controls.Add(new Label { Text = "UDP tracking · one input, multiple destinations", AutoSize = true, Font = new Font(Font, FontStyle.Bold) });
        var input = new FlowLayoutPanel { Dock = DockStyle.Fill };
        input.Controls.AddRange([new Label { Text = "Listen IP", AutoSize = true, Padding = new Padding(0, 5, 0, 0) }, listen, new Label { Text = "Port", AutoSize = true, Padding = new Padding(12, 5, 0, 0) }, port]);
        layout.Controls.Add(input);
        layout.Controls.Add(new Label { Text = "Destinations - select a row, then click Delete selected row", AutoSize = true });
        grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = "enabled", HeaderText = "Send", FillWeight = 30, SortMode = DataGridViewColumnSortMode.NotSortable });
        grid.Columns.Add("address", "IPv4 address"); grid.Columns.Add("port", "UDP port");
        foreach (DataGridViewColumn column in grid.Columns) column.SortMode = DataGridViewColumnSortMode.NotSortable;
        grid.DefaultValuesNeeded += (_, e) => e.Row.Cells["enabled"].Value = true;
        grid.CurrentCellDirtyStateChanged += (_, _) =>
        {
            if (grid.IsCurrentCellDirty && grid.CurrentCell is DataGridViewCheckBoxCell)
                grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
        };
        layout.Controls.Add(grid);
        layout.Controls.Add(auto);
        var buttons = new FlowLayoutPanel { Dock = DockStyle.Fill };
        var save = new Button { Text = "Save settings", Width = 140, Height = 34 };
        buttons.Controls.AddRange([start, stop, save, deleteRow]); layout.Controls.Add(buttons);
        layout.Controls.Add(status);
        var info = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        info.Controls.AddRange([stats, detail]); layout.Controls.Add(info);
        Controls.Add(layout);
        start.Click += (_, _) => StartRelay();
        stop.Click += (_, _) => StopRelay();
        deleteRow.Click += (_, _) => DeleteSelectedRow();
        grid.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Delete && !grid.IsCurrentCellInEditMode && relay == null)
            {
                DeleteSelectedRow();
                e.Handled = e.SuppressKeyPress = true;
            }
        };
        save.Click += (_, _) => { try { Save(ReadConfig()); status.Text = "Settings saved"; } catch (Exception ex) { ShowError(ex); } };
        timer.Tick += (_, _) => RefreshStats();
        FormClosing += (_, _) => { timer.Stop(); relay?.Dispose(); };
        Shown += (_, _) => { if (auto.Checked) StartRelay(); };
        try
        {
            var config = File.Exists(configPath) ? JsonSerializer.Deserialize<RelayConfig>(File.ReadAllText(configPath)) ?? throw new Exception("Invalid settings file.") : new RelayConfig();
            listen.Text = config.ListenAddress; port.Value = config.ListenPort; auto.Checked = config.AutoStart;
            foreach (var t in config.Targets) grid.Rows.Add(t.Enabled, t.Address, t.Port);
        }
        catch (Exception ex) { ShowError(ex); }
        grid.CellValueChanged += (_, e) =>
        {
            if (relay == null || e.RowIndex < 0 || e.ColumnIndex != grid.Columns["enabled"]!.Index) return;
            try
            {
                relay.SetTargetEnabled(e.RowIndex, Convert.ToBoolean(grid.Rows[e.RowIndex].Cells["enabled"].Value));
                RefreshStats();
                Save(ReadConfig());
            }
            catch (Exception ex) { ShowError(ex); }
        };
    }

    private RelayConfig ReadConfig()
    {
        grid.EndEdit();
        var config = new RelayConfig { ListenAddress = listen.Text.Trim(), ListenPort = (int)port.Value, AutoStart = auto.Checked, Targets = [] };
        foreach (DataGridViewRow row in grid.Rows)
        {
            if (row.IsNewRow) continue;
            var address = Convert.ToString(row.Cells["address"].Value)?.Trim() ?? "";
            var portText = Convert.ToString(row.Cells["port"].Value)?.Trim() ?? "";
            if (address.Length == 0 && portText.Length == 0) continue;
            if (address.Length == 0 || !int.TryParse(portText, out int p))
                throw new Exception($"Enter an IPv4 address and valid UDP port on row {row.Index + 1}, or clear both fields to ignore it.");
            config.Targets.Add(new Target(address, p, Convert.ToBoolean(row.Cells["enabled"].Value)));
        }
        return config;
    }

    private void DeleteSelectedRow()
    {
        if (relay != null) return;
        var row = grid.CurrentRow;
        if (row == null || row.IsNewRow) { grid.CancelEdit(); return; }
        grid.CancelEdit();
        if (row.DataGridView == grid && !row.IsNewRow) grid.Rows.Remove(row);
    }

    private void Save(RelayConfig config)
    {
        // Validate without binding a socket, also when already running.
        Relay.ParseAddress(config.ListenAddress);
        if (config.Targets.Count == 0) throw new Exception("Add at least one destination.");
        foreach (var t in config.Targets) { Relay.ParseAddress(t.Address); if (t.Port is < 1 or > 65535) throw new Exception("Port must be 1–65535."); }
        Directory.CreateDirectory(Path.GetDirectoryName(configPath)!);
        string temp = configPath + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temp, configPath, true);
    }

    private void StartRelay()
    {
        try
        {
            var config = ReadConfig();
            var next = new Relay(config);
            try { Save(config); next.Start(); } catch { next.Dispose(); throw; }
            // Remove ignored blank rows so row indexes match the live relay routes.
            grid.Rows.Clear();
            foreach (var target in config.Targets) grid.Rows.Add(target.Enabled, target.Address, target.Port);
            relay = next; previous = 0; previousTime = DateTime.UtcNow;
            listen.Enabled = port.Enabled = auto.Enabled = start.Enabled = deleteRow.Enabled = false;
            grid.Columns["address"]!.ReadOnly = grid.Columns["port"]!.ReadOnly = true;
            grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = false;
            stop.Enabled = true; timer.Start(); RefreshStats();
        }
        catch (Exception ex) { ShowError(ex); }
    }

    private void StopRelay()
    {
        timer.Stop(); relay?.Dispose(); relay = null;
        listen.Enabled = port.Enabled = auto.Enabled = start.Enabled = deleteRow.Enabled = true;
        grid.Columns["address"]!.ReadOnly = grid.Columns["port"]!.ReadOnly = false;
        grid.AllowUserToAddRows = grid.AllowUserToDeleteRows = true;
        stop.Enabled = false; status.Text = "Stopped"; status.ForeColor = Color.DimGray;
    }

    private void RefreshStats()
    {
        if (relay == null) return;
        var now = DateTime.UtcNow;
        var age = relay.LastPacket is DateTime last ? (now - last).TotalSeconds : double.PositiveInfinity;
        status.Text = !relay.Running ? "Receiver stopped — press Stop, then Start" : age < 2 ? "Receiving and forwarding" : "Listening — waiting for UDP data";
        if (relay.Running) status.Text = relay.ActiveTargetCount == 0 ? "Listening - all destinations switched OFF" : status.Text + $" | Active destinations: {relay.ActiveTargetCount}";
        status.ForeColor = !relay.Running ? Color.Firebrick : age < 2 ? Color.ForestGreen : Color.DarkOrange;
        long count = relay.Received;
        double rate = (count - previous) / Math.Max((now - previousTime).TotalSeconds, .001);
        stats.Text = $"Received: {count:N0}   |   Sent copies: {relay.Sent:N0}   |   {rate:F1} packets/s   |   Errors: {relay.Errors:N0}";
        detail.Text = relay.LastError.Length > 0 ? relay.LastError : $"Source: {relay.LastSender}   |   Last packet: {(double.IsInfinity(age) ? "—" : $"{age:F1}s ago")}";
        previous = count; previousTime = now;
    }

    private void ShowError(Exception ex) => MessageBox.Show(this, ex.Message, "UDP Relay", MessageBoxButtons.OK, MessageBoxIcon.Warning);
}
