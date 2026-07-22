namespace KubaToolKit.Modules.Wiki.Models;

public class WikiSection
{
    public string Name { get; set; } = "";

    public string Text { get; set; } = "";

    // File names only, resolved against WikiService.GetImagesFolderPath at
    // read time -- the actual image files live in the shared project files
    // folder, not inline in this JSON.
    public List<string> ImageFileNames { get; set; } = new();

    // Hides the notes text box and shows the first entry of
    // ImageFileNames large instead -- for a section that's really just a
    // diagram/schema, not notes.
    public bool ImageOnlyMode { get; set; }
}
