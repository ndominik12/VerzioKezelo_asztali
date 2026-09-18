using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;

namespace VerzioKezelo
{
    // Simple commit manager: stores commits as hidden folders in root, only changed files copied.
    public class CommitManager
    {
        public string Root { get; private set; }

        public void SetRoot(string path)
        {
            Root = path;
        }

        private IEnumerable<string> GetWorkingFiles()
        {
            if (string.IsNullOrEmpty(Root)) return Enumerable.Empty<string>();
            // all files recursively, but exclude hidden commit folders starting with dot
            return Directory.EnumerateFiles(Root, "*", SearchOption.AllDirectories)
                .Where(p => !IsInsideCommitFolder(p))
                .Select(p => GetRelative(Root, p));
        }

        private bool IsInsideCommitFolder(string fullPath)
        {
            var rel = GetRelative(Root, fullPath);
            var parts = rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (parts.Length == 0) return false;
            return parts[0].StartsWith(".");
        }

        private static string GetRelative(string basePath, string path)
        {
            if (string.IsNullOrEmpty(basePath)) return path;
            var baseFull = Path.GetFullPath(basePath);
            if (!baseFull.EndsWith(Path.DirectorySeparatorChar.ToString())) baseFull += Path.DirectorySeparatorChar;
            var baseUri = new Uri(baseFull);
            var pathUri = new Uri(Path.GetFullPath(path));
            var relUri = baseUri.MakeRelativeUri(pathUri);
            var rel = Uri.UnescapeDataString(relUri.ToString()).Replace('/', Path.DirectorySeparatorChar);
            return rel;
        }

        public List<string> GetCommits()
        {
            if (string.IsNullOrEmpty(Root)) return new List<string>();
            var dirs = Directory.GetDirectories(Root)
                .Where(d => Path.GetFileName(d).StartsWith("."))
                .OrderByDescending(d => d)
                .Select(d => Path.GetFileName(d))
                .ToList();
            return dirs;
        }

        private string MetadataPath(string commitDir) => Path.Combine(commitDir, "metadata.txt");

        private Dictionary<string, string> ReadMetadata(string commitDir)
        {
            var md = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var mp = MetadataPath(commitDir);
            if (!File.Exists(mp)) return md;
            foreach (var line in File.ReadAllLines(mp))
            {
                var idx = line.IndexOf('|');
                if (idx <= 0) continue;
                var path = line.Substring(0, idx);
                var hash = line.Substring(idx + 1);
                md[path] = hash;
            }
            return md;
        }

        private void WriteMetadata(string commitDir, Dictionary<string, string> map)
        {
            var mp = MetadataPath(commitDir);
            Directory.CreateDirectory(commitDir);
            using (var sw = new StreamWriter(mp, false))
            {
                foreach (var kv in map)
                {
                    sw.WriteLine(kv.Key + "|" + kv.Value);
                }
            }
        }

        private string ComputeHashFull(string fullPath)
        {
            using (var sha = SHA256.Create())
            using (var fs = File.OpenRead(fullPath))
            {
                var hash = sha.ComputeHash(fs);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        public string CreateCommit(string name)
        {
            if (string.IsNullOrEmpty(Root)) throw new InvalidOperationException("Root nincs beállítva");
            var ts = DateTime.Now.ToString("yyyyMMddHHmmss");
            var safeName = string.Join("_", name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            var commitFolderName = "." + ts + "_" + safeName;
            var commitDir = Path.Combine(Root, commitFolderName);
            Directory.CreateDirectory(commitDir);
            var filesDir = Path.Combine(commitDir, "files");
            Directory.CreateDirectory(filesDir);

            // build current hashes
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in GetWorkingFiles())
            {
                var full = Path.Combine(Root, rel);
                current[rel] = ComputeHashFull(full);
            }

            // get latest commit metadata (if any)
            var latest = GetCommits().FirstOrDefault();
            Dictionary<string, string> latestMap = null;
            if (latest != null)
            {
                latestMap = ReadMetadata(Path.Combine(Root, latest));
            }

            // copy only changed files
            foreach (var kv in current)
            {
                var rel = kv.Key; var hash = kv.Value;
                var changed = latestMap == null || !latestMap.TryGetValue(rel, out var prevHash) || prevHash != hash;
                if (changed)
                {
                    var src = Path.Combine(Root, rel);
                    var dest = Path.Combine(filesDir, rel);
                    var dd = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(dd)) Directory.CreateDirectory(dd);
                    File.Copy(src, dest, true);
                }
            }

            // save metadata (all files with their hashes)
            WriteMetadata(commitDir, current);

            // try to hide the folder (set hidden attribute)
            try { var di = new DirectoryInfo(commitDir); di.Attributes |= FileAttributes.Hidden; } catch { }

            return commitFolderName;
        }

        public void RestoreCommit(string commitFolderName)
        {
            var commitDir = Path.Combine(Root, commitFolderName);
            if (!Directory.Exists(commitDir)) throw new DirectoryNotFoundException("Nincs ilyen commit: " + commitFolderName);
            var targetFiles = ReadMetadata(commitDir);

            // prepare list of commits in chronological order (newest first)
            var commits = GetCommits();

            foreach (var rel in targetFiles.Keys)
            {
                // try find file in this commit or earlier
                string found = null;
                foreach (var c in commits)
                {
                    var cdir = Path.Combine(Root, c);
                    var fpath = Path.Combine(cdir, "files", rel);
                    if (File.Exists(fpath)) { found = fpath; break; }
                    if (String.Equals(c, commitFolderName, StringComparison.OrdinalIgnoreCase)) { /* keep searching earlier */ }
                }
                // Also check the selected commit specifically (in case commits ordering excludes it)
                var selPath = Path.Combine(commitDir, "files", rel);
                if (File.Exists(selPath)) found = selPath;

                if (found != null)
                {
                    var dest = Path.Combine(Root, rel);
                    var dd = Path.GetDirectoryName(dest);
                    if (!Directory.Exists(dd)) Directory.CreateDirectory(dd);
                    File.Copy(found, dest, true);
                }
                // if not found, skip
            }
        }

        public void DeleteCommit(string commitFolderName)
        {
            var commitDir = Path.Combine(Root, commitFolderName);
            if (!Directory.Exists(commitDir)) throw new DirectoryNotFoundException("Nincs ilyen commit: " + commitFolderName);
            Directory.Delete(commitDir, true);
        }

        public bool IsCommitNeeded()
        {
            if (string.IsNullOrEmpty(Root)) return false;
            var current = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var rel in GetWorkingFiles()) current[rel] = ComputeHashFull(Path.Combine(Root, rel));
            var latest = GetCommits().FirstOrDefault();
            if (latest == null) return current.Count > 0;
            var latestMap = ReadMetadata(Path.Combine(Root, latest));
            // if counts differ or any hash differs => needed
            if (latestMap.Count != current.Count) return true;
            foreach (var kv in current)
            {
                if (!latestMap.TryGetValue(kv.Key, out var h) || h != kv.Value) return true;
            }
            return false;
        }
    }
}
