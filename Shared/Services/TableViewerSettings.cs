namespace KubaToolKit.Shared.Services;

// QTable is an external, native (non-.NET) tool each technician installs
// locally -- its path varies per machine, hence a setting rather than a
// hardcoded constant.
public class TableViewerSettings
{
    public string QTableExePath { get; set; } = @"C:\Utilitaires\Qtable\QTable.exe";
}
