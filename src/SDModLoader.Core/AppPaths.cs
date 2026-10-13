namespace SDModLoader.Core;

/// <summary>Все данные программы лежат рядом с exe (portable-режим).</summary>
public sealed class AppPaths
{
    public string Root { get; }
    public string ModsDir => Path.Combine(Root, "Mods");
    public string BackupsDir => Path.Combine(Root, "Backups");
    public string ConfigFile => Path.Combine(Root, "config.json");
    public string StateFile => Path.Combine(Root, "state.json");

    public AppPaths(string root)
    {
        Root = Path.GetFullPath(root);
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(ModsDir);
        Directory.CreateDirectory(BackupsDir);
    }

    public static AppPaths ForPortable()
    {
        var exe = Environment.ProcessPath ?? AppContext.BaseDirectory;
        var dir = Path.GetDirectoryName(exe) ?? AppContext.BaseDirectory;
        return new AppPaths(Path.Combine(dir, "Data"));
    }
}
