using System;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Arrakis.Core
{
    /// <summary>Exports and imports all Arrakis settings and data as one .zip file.</summary>
    public static class Backup
    {
        // Big or easily recreated folders are left out.
        private static readonly string[] Skip = { "wallpapers", "sounds", "logs" };

        public static void Export(string zipPath)
        {
            Settings.Save();
            if (File.Exists(zipPath)) File.Delete(zipPath);
            using var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create);
            foreach (var file in Directory.GetFiles(Settings.Folder, "*", SearchOption.AllDirectories))
            {
                string rel = Path.GetRelativePath(Settings.Folder, file);
                string top = rel.Split(Path.DirectorySeparatorChar)[0];
                if (Skip.Contains(top, StringComparer.OrdinalIgnoreCase)) continue;
                if (rel.EndsWith(".log", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) || rel.EndsWith(".flag", StringComparison.OrdinalIgnoreCase)) continue;
                zip.CreateEntryFromFile(file, rel.Replace('\\', '/'));
            }
        }

        /// <summary>Restores a backup. Arrakis restarts afterwards so everything is reloaded.</summary>
        public static void Import(string zipPath)
        {
            using var zip = ZipFile.OpenRead(zipPath);
            if (!zip.Entries.Any(e => e.FullName.Equals("settings.json", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("That file isn't an Arrakis backup.");
            string root = Path.GetFullPath(Settings.Folder);
            foreach (var entry in zip.Entries)
            {
                if (string.IsNullOrEmpty(entry.Name)) continue;
                string target = Path.GetFullPath(Path.Combine(root, entry.FullName));
                if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase)) continue;   // ignore anything trying to escape the folder
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                entry.ExtractToFile(target, true);
            }
        }
    }
}
