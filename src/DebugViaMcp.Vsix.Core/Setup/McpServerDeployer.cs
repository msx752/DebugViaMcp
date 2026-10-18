using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using DebugViaMcp.Vsix.Core.Abstractions;

namespace DebugViaMcp.Vsix.Core.Setup;

/// <summary>
/// Puts the MCP server that travels inside the extension where agents can start it from: a copy under
/// <c>bin\&lt;build&gt;</c> in the data folder and a launcher <c>debugviamcp.cmd</c> whose path never changes.
/// A copy (instead of running from the extension folder) keeps Visual Studio able to update the extension while an agent
/// still runs the old server; the build name is a hash of the server, so a rebuilt server is never mistaken for the old one.
/// </summary>
public sealed class McpServerDeployer
{
    public const string LauncherName = "debugviamcp.cmd";
    public const string MainAssembly = "DebugViaMcp.Mcp.dll";

    private const string BinFolder = "bin";
    private const string CurrentFile = "current.txt";
    private const string CompleteMarker = ".complete";
    private const string TempPrefix = ".tmp-";

    // Static on purpose: a running launcher is read by cmd.exe while it executes, so it must never be rewritten with other text.
    private const string LauncherText =
        "@echo off\r\n" +
        "setlocal\r\n" +
        "set /p DEBUGVIAMCP_BUILD=<\"%~dp0bin\\current.txt\"\r\n" +
        "dotnet \"%~dp0bin\\%DEBUGVIAMCP_BUILD%\\" + MainAssembly + "\" %*\r\n";

    private readonly string _bundleDirectory;
    private readonly string _root;
    private readonly ILog _log;

    /// <param name="bundleDirectory">The server files inside the installed extension.</param>
    /// <param name="root">The data folder (<c>%LOCALAPPDATA%\DebugViaMcp</c>).</param>
    public McpServerDeployer(string bundleDirectory, string root, ILog log)
    {
        _bundleDirectory = bundleDirectory ?? throw new ArgumentNullException(nameof(bundleDirectory));
        _root = root ?? throw new ArgumentNullException(nameof(root));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    public static string LauncherPath(string root) => Path.Combine(root, LauncherName);

    /// <summary>Whether the launcher exists and the build it points at is complete on disk.</summary>
    public static bool IsDeployed(string root)
    {
        try
        {
            if (!File.Exists(LauncherPath(root))) return false;
            var current = Path.Combine(root, BinFolder, CurrentFile);
            if (!File.Exists(current)) return false;
            var build = File.ReadAllText(current).Trim();
            return build.Length > 0 && File.Exists(Path.Combine(root, BinFolder, build, CompleteMarker));
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Copies the server if this build is not there yet, points the launcher at it and removes older builds that are not in use.</summary>
    /// <returns>The path of the launcher.</returns>
    public string Deploy()
    {
        if (!File.Exists(Path.Combine(_bundleDirectory, MainAssembly)))
            throw new FileNotFoundException("The MCP server is not part of this extension build.", Path.Combine(_bundleDirectory, MainAssembly));

        var build = BuildName(_bundleDirectory);
        var bin = Path.Combine(_root, BinFolder);
        var target = Path.Combine(bin, build);
        Directory.CreateDirectory(bin);

        if (!File.Exists(Path.Combine(target, CompleteMarker)))
        {
            CopyBuild(bin, target);
            _log.Info($"MCP server {build} copied to {target}");
        }

        WriteIfDifferent(Path.Combine(bin, CurrentFile), build);
        WriteIfDifferent(LauncherPath(_root), LauncherText);
        RemoveOtherBuilds(bin, build);
        return LauncherPath(_root);
    }

    private void CopyBuild(string bin, string target)
    {
        var temp = Path.Combine(bin, TempPrefix + Guid.NewGuid().ToString("N"));
        try
        {
            CopyDirectory(_bundleDirectory, temp);
            File.WriteAllText(Path.Combine(temp, CompleteMarker), "");
            if (Directory.Exists(target)) Directory.Delete(target, true);   // left half done by an earlier failure
            try
            {
                Directory.Move(temp, target);
            }
            catch (IOException) when (File.Exists(Path.Combine(target, CompleteMarker)))
            {
                // Another Visual Studio finished the same copy first.
            }
        }
        finally
        {
            TryDelete(temp);
        }
    }

    private static void CopyDirectory(string from, string to)
    {
        Directory.CreateDirectory(to);
        foreach (var file in Directory.GetFiles(from)) File.Copy(file, Path.Combine(to, Path.GetFileName(file)));
        foreach (var directory in Directory.GetDirectories(from)) CopyDirectory(directory, Path.Combine(to, Path.GetFileName(directory)));
    }

    private static void WriteIfDifferent(string path, string text)
    {
        if (File.Exists(path) && File.ReadAllText(path) == text) return;
        File.WriteAllText(path, text, new UTF8Encoding(false));
    }

    /// <summary>Older builds (and abandoned temporary copies) go; one an agent still runs keeps its locked files and is tried again next time.</summary>
    private void RemoveOtherBuilds(string bin, string keep)
    {
        foreach (var directory in Directory.GetDirectories(bin).Where(d => !string.Equals(Path.GetFileName(d), keep, StringComparison.OrdinalIgnoreCase)))
        {
            var name = Path.GetFileName(directory);
            // A temporary copy may be another Visual Studio's copy in progress: only old ones are abandoned.
            if (name.StartsWith(TempPrefix, StringComparison.Ordinal) && DateTime.UtcNow - Directory.GetCreationTimeUtc(directory) < TimeSpan.FromHours(1)) continue;
            if (!TryDelete(directory)) _log.Info($"Old MCP server build {name} is still in use; it stays for now");
        }
    }

    private static bool TryDelete(string directory)
    {
        try
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>12 hex digits of a hash over the server's own assemblies.</summary>
    internal static string BuildName(string bundleDirectory)
    {
        using var sha = SHA256.Create();
        var hash = new StringBuilder();
        foreach (var name in new[] { MainAssembly, "DebugViaMcp.Protocol.dll" })
        {
            var path = Path.Combine(bundleDirectory, name);
            if (File.Exists(path)) hash.Append(Convert.ToBase64String(sha.ComputeHash(File.ReadAllBytes(path))));
        }

        var digest = sha.ComputeHash(Encoding.UTF8.GetBytes(hash.ToString()));
        return string.Concat(digest.Take(6).Select(b => b.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }
}
