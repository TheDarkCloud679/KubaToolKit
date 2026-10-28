namespace KubaToolKit.Shared.Services;

// Mirrors QTable's own Parameters dialog: 3 XML definitions files, one
// per file category (binary tables, stats files, memory dumps). Table
// Viewer decodes files itself -- it never shells out to QTable.exe, so
// unlike that dialog there's no tool path to configure here.
public class TableViewerSettings
{
    public string TablesDefinitionsXmlPath { get; set; } = "";

    public string StatsDefinitionsXmlPath { get; set; } = "";

    public string DumpsDefinitionsXmlPath { get; set; } = "";
}
