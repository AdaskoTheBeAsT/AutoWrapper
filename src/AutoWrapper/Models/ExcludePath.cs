namespace AutoWrapper.Models;

public class ExcludePath
{
    public ExcludePath(string path, ExcludeMode excludeMode = ExcludeMode.Strict)
    {
        Path = path;
        ExcludeMode = excludeMode;
    }

    public string? Path { get; set; }

    public ExcludeMode ExcludeMode { get; set; }
}
