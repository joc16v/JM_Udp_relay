using System.Reflection;
using StypeUdpRelay;

internal static class Program
{
    [STAThread]
    private static int Main()
    {
        try
        {
            using var form = new MainForm();
            var flags = BindingFlags.Instance | BindingFlags.NonPublic;
            var grid = (DataGridView)typeof(MainForm).GetField("grid", flags)!.GetValue(form)!;
            var delete = (Button)typeof(MainForm).GetField("deleteRow", flags)!.GetValue(form)!;
            RelayConfig Read() => (RelayConfig)typeof(MainForm).GetMethod("ReadConfig", flags)!.Invoke(form, null)!;
            void Delete() => typeof(MainForm).GetMethod("DeleteSelectedRow", flags)!.Invoke(form, null);
            static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
            grid.Rows.Clear();
            grid.Rows.Add(true, "", "");
            grid.Rows.Add(true, "127.0.0.1", 6301);
            grid.Rows.Add(true, " ", " ");
            grid.Rows.Add(false, "127.0.0.1", 6302);
            grid.Rows.Add(true, "", "");
            var config = Read();
            Require(config.Targets.Count == 2, "Blank rows were not ignored");
            Require(config.Targets[0].Port == 6301 && config.Targets[0].Enabled, "First destination changed");
            Require(config.Targets[1].Port == 6302 && !config.Targets[1].Enabled, "Second destination state changed");
            Console.WriteLine("PASS: blank rows before/between/after destinations are ignored; Send states preserved.");
            grid.CurrentCell = grid.Rows[2].Cells["address"];
            Delete();
            Require(grid.Rows.Count == 5, "Delete did not remove selected blank row");
            grid.CurrentCell = grid.Rows[1].Cells["port"];
            Delete();
            Require(Read().Targets.Count == 1 && Read().Targets[0].Port == 6302, "Delete removed wrong destination");
            Require(grid.RowHeadersVisible && grid.SelectionMode == DataGridViewSelectionMode.FullRowSelect && delete.Parent != null, "Row deletion controls are not accessible");
            Console.WriteLine("PASS: deletion removes selected blank/populated row; row headers and full-row selection enabled.");
            grid.Rows.Clear();
            grid.Rows.Add(true, "127.0.0.1", "");
            bool rejected = false;
            try { Read(); } catch (TargetInvocationException ex) when (ex.InnerException?.Message.Contains("row 1") == true) { rejected = true; }
            Require(rejected, "Partially completed row must not silently disappear");
            Console.WriteLine("PASS: incomplete rows report the row number.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
}
