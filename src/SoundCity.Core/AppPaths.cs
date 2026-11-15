namespace SoundCity.Core;

public static class AppPaths
{
    public static string DataDirectory => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SoundCity");
    public static string Database => Path.Combine(DataDirectory, "openscq30.sqlite");
    public static string Backend => Path.Combine(AppContext.BaseDirectory, "tools", "openscq30.exe");
}
