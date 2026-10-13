using Godot;

namespace Thornwake.Rewrite.Net;

/// <summary>
/// The multiplayer choice made on the launcher's Multiplayer page (name, host or join, address, port), remembered in
/// user://multiplayer.cfg so the next start offers the same. WorldLab reads <see cref="Pending"/> when it starts (the
/// command line's --host / --join do the same for tests).
/// </summary>
public static class NetLaunch
{
    public enum Role { None, Host, Join }
    private const string FilePath = "user://multiplayer.cfg";

    public static string Name { get; set; } = "";
    public static string Address { get; set; } = "";
    public static int Port { get; set; } = NetSession.DefaultPort;
    /// <summary>What WorldLab should do when it opens (set by the launcher, cleared when used).</summary>
    public static Role Pending { get; set; }

    public static void Load()
    {
        var file = new ConfigFile();
        if (file.Load(FilePath) == Error.Ok)
        {
            Name = file.GetValue("multiplayer", "name", "").AsString();
            Address = file.GetValue("multiplayer", "address", "").AsString();
            Port = file.GetValue("multiplayer", "port", NetSession.DefaultPort).AsInt32();
        }
        if (Name.Length == 0) Name = System.Environment.UserName;
    }

    public static void Save()
    {
        var file = new ConfigFile();
        file.SetValue("multiplayer", "name", Name);
        file.SetValue("multiplayer", "address", Address);
        file.SetValue("multiplayer", "port", Port);
        file.Save(FilePath);
    }
}
