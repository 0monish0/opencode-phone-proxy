using System.Diagnostics;
using OpenCodePhoneProxy.Models;

namespace OpenCodePhoneProxy.Services;

public static class OpenCodeLocator
{
    private static readonly string[] ExcludedSegments =
    {
        "\\uninstall", "\\resources", "\\locales", "\\swiftshader", "\\crashpad",
        "\\update", "\\squirrel", "\\helper", "\\elevate", "\\vc_redist"
    };

    public static IReadOnlyList<OpenCodeCandidate> FindAll()
    {
        var candidates = new List<OpenCodeCandidate>();

        foreach (var path in KnownPaths())
        {
            AddIfValid(candidates, path, "known path");
        }

        foreach (var root in SearchRoots())
        {
            foreach (var path in Search(root, 3))
            {
                AddIfValid(candidates, path, "search");
            }
        }

        foreach (var name in new[] { "opencode-desktop", "opencode" })
        {
            foreach (var path in Where(name))
            {
                AddIfValid(candidates, path, "PATH");
            }
        }

        return candidates
            .GroupBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .OrderByDescending(candidate =>
                Path.GetFileName(candidate.Path).Contains("desktop", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(candidate => candidate.Version, StringComparer.OrdinalIgnoreCase)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static bool LooksLikeDesktop(string path)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                if (File.Exists(Path.Combine(directory, "resources", "app.asar")) ||
                    File.Exists(Path.Combine(directory, "chrome_100_percent.pak")))
                {
                    return true;
                }
            }

            return Path.GetFileName(path).Contains("desktop", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static string? FindBundledCli()
    {
        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ai.opencode.desktop", "cli"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ai.opencode.desktop", "cli"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "opencode-desktop"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "OpenCode")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => SearchFile(root, "opencode-cli.exe", 4))
            .Select(path => new FileInfo(path))
            .OrderByDescending(info => info.LastWriteTimeUtc)
            .Select(info => info.FullName)
            .FirstOrDefault();
    }

    private static IEnumerable<string> KnownPaths()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);

        yield return Path.Combine(local, "Programs", "opencode-desktop", "opencode-desktop.exe");
        yield return Path.Combine(local, "Programs", "opencode-desktop", "OpenCode.exe");
        yield return Path.Combine(local, "Programs", "OpenCode", "OpenCode.exe");
        yield return Path.Combine(local, "Programs", "OpenCode", "opencode.exe");
        yield return Path.Combine(local, "Programs", "opencode", "opencode.exe");
        yield return Path.Combine(local, "Programs", "ai.opencode.desktop", "opencode-desktop.exe");
        yield return Path.Combine(local, "opencode", "opencode.exe");
        yield return Path.Combine(programFiles, "OpenCode", "OpenCode.exe");
        yield return Path.Combine(programFiles, "opencode-desktop", "opencode-desktop.exe");
        yield return Path.Combine(programFiles, "opencode", "opencode.exe");
    }

    private static IEnumerable<string> SearchRoots()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        yield return Path.Combine(local, "Programs");
        yield return Path.Combine(local, "opencode");
        yield return Path.Combine(programFiles, "OpenCode");
        yield return Path.Combine(programFiles, "opencode");
    }

    private static void AddIfValid(List<OpenCodeCandidate> candidates, string path, string source)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path) || IsExcluded(path))
        {
            return;
        }

        var extension = Path.GetExtension(path);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var version = "";
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            version = (info.ProductVersion ?? info.FileVersion ?? "").Trim();
        }
        catch
        {
            // Version metadata is optional.
        }

        candidates.Add(new OpenCodeCandidate(Path.GetFullPath(path), version, source));
    }

    private static bool IsExcluded(string path)
    {
        var lower = path.ToLowerInvariant();
        if (ExcludedSegments.Any(segment => lower.Contains(segment)))
        {
            return true;
        }

        var name = Path.GetFileName(lower);
        return name.Contains("uninstall") ||
               name.Contains("updater") ||
               name.Contains("crash") ||
               name.Contains("helper");
    }

    private static IEnumerable<string> Search(string root, int depth)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = depth,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            return Directory.EnumerateFiles(root, "*opencode*.exe", options).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> SearchFile(string root, string pattern, int depth)
    {
        if (!Directory.Exists(root))
        {
            return Array.Empty<string>();
        }

        try
        {
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = true,
                MaxRecursionDepth = depth,
                IgnoreInaccessible = true,
                ReturnSpecialDirectories = false
            };

            return Directory.EnumerateFiles(root, pattern, options).ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> Where(string name)
    {
        try
        {
            var info = new ProcessStartInfo("where.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            info.ArgumentList.Add(name);

            using var process = Process.Start(info);
            if (process is null)
            {
                return Array.Empty<string>();
            }

            var output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return output
                .Split('\n', '\r')
                .Select(line => line.Trim())
                .Where(line => line.Length > 0 && File.Exists(line))
                .ToList();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }
}
