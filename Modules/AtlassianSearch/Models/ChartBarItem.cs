namespace KubaToolKit.Modules.AtlassianSearch.Models;

// One bar in the Stats section's "group by" chart -- BarHeight is
// precomputed (scaled against the tallest bar in the current chart) rather
// than derived in XAML, since that scaling depends on every item at once.
public class ChartBarItem
{
    public string Label { get; set; } = "";
    public int Count { get; set; }
    public double BarHeight { get; set; }
}
