using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using JETHelper.Dictionaries.Models;

namespace JETHelper.Dictionaries.Catalog;

/// <summary>
/// Resolves dictionary folders without relying on the FFXIV process working
/// directory.
///
/// The resolver intentionally combines two locations:
/// 1. the first automatic/bundled Assets/Dictionaries folder it can find;
/// 2. the optional user-configured folder from /jetconfig.
///
/// This allows redistributable dictionaries to ship with JETHelper while users
/// keep separately obtained dictionaries outside the repository/plugin package.
/// </summary>
public static class DictionaryPathResolver
{
    private const string PluginFolderName = "JETHelper";

    /// <summary>
    /// Returns every top-level dictionary ZIP from the bundled and configured
    /// folders. Exact path duplicates are removed here; logical dictionary
    /// duplicates are removed later by DictionaryCatalog using index metadata.
    /// </summary>
    public static IReadOnlyList<DictionaryFileCandidate>
        FindDictionaryZipCandidates(string? configuredDictionaryFolderPath)
    {
        var results = new List<DictionaryFileCandidate>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var bundledFolder = FindBundledDictionaryFolder();
        AddFolderZips(bundledFolder,
            DictionarySourceOrigin.Bundled,
            results,
            seenPaths);

        var configuredFolder = FindConfiguredDictionaryFolder(
            configuredDictionaryFolderPath);
        AddFolderZips(configuredFolder,
            DictionarySourceOrigin.UserConfigured,
            results,
            seenPaths);

        return results;
    }

    /// <summary>
    /// Compatibility helper used by any code that only needs the paths.
    /// </summary>
    public static List<string>
        FindAllDictionaryZips(string? configuredDictionaryFolderPath)
        => FindDictionaryZipCandidates(configuredDictionaryFolderPath)
            .Select(candidate => candidate.FilePath)
            .ToList();

    private static string? FindConfiguredDictionaryFolder(
        string? configuredDictionaryFolderPath)
    {
        var configuredRoot = NormalizeOptionalPath(
            configuredDictionaryFolderPath);
        if (configuredRoot is null)
            return null;

        foreach (var candidate in
                 BuildCandidateDirectoriesForDirectory(configuredRoot))
        {
            try
            {
                var fullPath = Path.GetFullPath(candidate);
                if (Directory.Exists(fullPath) && ContainsZipFiles(fullPath))
                    return fullPath;
            }
            catch
            {
                // Invalid candidates are ignored; the config UI already shows
                // whether the saved root path exists.
            }
        }

        return configuredRoot;
    }

    private static void AddFolderZips(
        string? folder,
        DictionarySourceOrigin origin,
        ICollection<DictionaryFileCandidate> results,
        ISet<string> seenPaths)
    {
        if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder))
            return;

        string[] files;
        try
        {
            files = Directory.GetFiles(folder,
                "*.zip",
                SearchOption.TopDirectoryOnly);
        }
        catch
        {
            return;
        }

        foreach (var file in files.OrderBy(Path.GetFileName,
                     StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(file);
            }
            catch
            {
                continue;
            }

            if (seenPaths.Add(fullPath))
                results.Add(new DictionaryFileCandidate(fullPath, origin));
        }
    }

    /// <summary>
    /// Finds one authoritative automatic dictionary folder. Selecting the first
    /// matching folder prevents development source/output copies and installed
    /// copies from all being loaded simultaneously.
    /// </summary>
    private static string? FindBundledDictionaryFolder()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in GetAutomaticSearchDirectories())
        {
            string rootFullPath;
            try
            {
                rootFullPath = Path.GetFullPath(root);
            }
            catch
            {
                continue;
            }

            foreach (var candidate in
                     BuildCandidateDirectoriesForDirectory(root))
            {
                string fullPath;
                try
                {
                    fullPath = Path.GetFullPath(candidate);
                }
                catch
                {
                    continue;
                }

                // Automatic roots are usually plugin/project directories, not
                // dictionary folders themselves. Only accept the root directly
                // when it is explicitly named Dictionaries; otherwise an
                // unrelated ZIP beside JETHelper.dll could hijack discovery.
                var isRootCandidate = string.Equals(
                    fullPath,
                    rootFullPath,
                    StringComparison.OrdinalIgnoreCase);
                if (isRootCandidate
                    && !Path.GetFileName(fullPath).Equals(
                        "Dictionaries",
                        StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!seen.Add(fullPath)
                    || !Directory.Exists(fullPath)
                    || !ContainsZipFiles(fullPath))
                    continue;

                return fullPath;
            }
        }

        return null;
    }

    private static bool ContainsZipFiles(string folder)
    {
        try
        {
            return Directory.EnumerateFiles(folder,
                    "*.zip",
                    SearchOption.TopDirectoryOnly)
                .Any();
        }
        catch
        {
            return false;
        }
    }

    private static IEnumerable<string>
        BuildCandidateDirectoriesForDirectory(string directory)
    {
        // The root itself may already be Assets/Dictionaries.
        yield return directory;

        // Normal installed/output layout:
        // ...\JETHelper\1.0.0\Assets\Dictionaries\
        yield return Path.Combine(directory, "Assets", "Dictionaries");

        // Older/simple layout retained as a harmless fallback.
        yield return Path.Combine(directory, "Dictionaries");

        // Repository-root layout:
        // ...\JETHelper\JETHelper\Assets\Dictionaries\
        yield return Path.Combine(directory,
            PluginFolderName,
            "Assets",
            "Dictionaries");
    }

    private static IEnumerable<string> GetAutomaticSearchDirectories()
    {
        // Assembly location is the most reliable root during normal plugin use.
        foreach (var directory in GetDirectoryAndParents(
                     GetAssemblyDirectory()))
            yield return directory;

        // Dalamud's installed-plugin folder contains version-numbered children.
        var launcherPluginDirectory = GetXivLauncherPluginDirectory();
        foreach (var versionDirectory in
                 GetVersionSubdirectories(launcherPluginDirectory))
            yield return versionDirectory;

        foreach (var directory in GetDirectoryAndParents(
                     launcherPluginDirectory))
            yield return directory;

        // Predictable development/download fallbacks. No whole-drive scan.
        foreach (var directory in GetKnownUserSearchRoots())
            yield return directory;

        // Last-resort process paths. These often point at FFXIV, but are kept
        // after the useful locations so they cannot override a plugin folder.
        foreach (var directory in GetDirectoryAndParents(
                     AppContext.BaseDirectory))
            yield return directory;

        foreach (var directory in GetDirectoryAndParents(
                     Directory.GetCurrentDirectory()))
            yield return directory;
    }

    private static IEnumerable<string> GetDirectoryAndParents(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            yield break;

        DirectoryInfo? directory;
        try
        {
            directory = new DirectoryInfo(path);
        }
        catch
        {
            yield break;
        }

        while (directory is not null)
        {
            yield return directory.FullName;
            directory = directory.Parent;
        }
    }

    private static IEnumerable<string> GetVersionSubdirectories(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            yield break;

        string[] children;
        try
        {
            children = Directory.GetDirectories(path);
        }
        catch
        {
            yield break;
        }

        foreach (var child in children.OrderByDescending(
                     GetDirectoryVersionSortKey))
            yield return child;
    }

    private static Version GetDirectoryVersionSortKey(string directoryPath)
    {
        var folderName = Path.GetFileName(directoryPath);
        return Version.TryParse(folderName, out var version)
            ? version
            : new Version(0, 0, 0, 0);
    }

    private static IEnumerable<string> GetKnownUserSearchRoots()
    {
        var profile = Environment.GetFolderPath(
            Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrWhiteSpace(profile))
            yield break;

        yield return Path.Combine(profile, "Downloads", PluginFolderName);
        yield return Path.Combine(profile, "Desktop", PluginFolderName);
        yield return Path.Combine(profile, "Documents", PluginFolderName);
        yield return Path.Combine(profile, PluginFolderName);
    }

    private static string? GetAssemblyDirectory()
    {
        var assemblyLocation = Assembly.GetExecutingAssembly().Location;
        return string.IsNullOrWhiteSpace(assemblyLocation)
            ? null
            : Path.GetDirectoryName(assemblyLocation);
    }

    private static string? GetXivLauncherPluginDirectory()
    {
        var appData = Environment.GetFolderPath(
            Environment.SpecialFolder.ApplicationData);
        if (string.IsNullOrWhiteSpace(appData))
            return null;

        return Path.Combine(appData,
            "XIVLauncher",
            "installedPlugins",
            PluginFolderName);
    }

    private static string? NormalizeOptionalPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return null;

        try
        {
            var trimmed = path.Trim().Trim('"');
            return Path.GetFullPath(
                Environment.ExpandEnvironmentVariables(trimmed));
        }
        catch
        {
            return null;
        }
    }
}
