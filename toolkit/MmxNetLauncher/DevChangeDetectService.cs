using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace MmxNetLauncher;

public sealed class FileSnapshotEntry
{
    public string Sha256 { get; set; } = "";
    public long Length { get; set; }
    public string LastWriteUtc { get; set; } = "";
}

public sealed class DevLastPublishSnapshot
{
    public string Version { get; set; } = "";
    public string PublishedUtc { get; set; } = "";
    public string OutDir { get; set; } = "";
    public Dictionary<string, FileSnapshotEntry> Files { get; set; } =
        new(StringComparer.OrdinalIgnoreCase);
}

public sealed class ChangedPackFile
{
    public string RelPath { get; init; } = "";
    public string Kind { get; init; } = ""; // added | modified | removed | missing
}

public sealed class ChangeDetectResult
{
    public bool HasSnapshot { get; init; }
    public bool HasChanges { get; init; }
    public string LastVersion { get; init; } = "";
    public string LastPublishedUtc { get; init; } = "";
    public IReadOnlyList<ChangedPackFile> Changes { get; init; } = Array.Empty<ChangedPackFile>();

    public string SummaryLine
    {
        get
        {
            if (!HasSnapshot)
                return "Nessun publish precedente — la prima Carica crea lo snapshot.";
            if (!HasChanges)
                return "Nessuna modifica rispetto all'ultimo publish (" + LastVersion + ").";
            var n = Changes.Count;
            var sample = string.Join(", ", Changes.Take(4).Select(c => c.RelPath));
            if (n > 4) sample += "…";
            return $"Ci sono modifiche non pubblicate ({n}): {sample}";
        }
    }
}

public static class DevChangeDetectService
{
    public const string SnapshotFileName = "ac_dev_last_publish.json";

    public static string SnapshotPath(Install inst) =>
        Path.Combine(inst.Root, SnapshotFileName);

    public static DevLastPublishSnapshot? LoadSnapshot(Install inst)
    {
        try
        {
            var path = SnapshotPath(inst);
            if (!File.Exists(path)) return null;
            return JsonSerializer.Deserialize<DevLastPublishSnapshot>(File.ReadAllText(path));
        }
        catch { return null; }
    }

    public static void SaveSnapshot(Install inst, DevLastPublishSnapshot snap)
    {
        var json = JsonSerializer.Serialize(snap, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(SnapshotPath(inst), json);
    }

    public static IReadOnlyList<string> EnumeratePackRelPaths(Install inst)
    {
        var rels = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in PackService.RootFiles)
            rels.Add(NormalizeRel(name));

        rels.Add("MMX-Net-Launcher.exe");

        foreach (var r in PackService.DefaultGamedataRelPaths)
            rels.Add(NormalizeRel(Path.Combine("gamedata", r)));
        foreach (var r in PackService.OptionalRepairRelPaths)
            rels.Add(NormalizeRel(r));

        var includeList = Path.Combine(inst.Root, "toolkit", "pack", "ac_pack_include.txt");
        if (File.Exists(includeList))
        {
            foreach (var line in File.ReadAllLines(includeList))
            {
                var t = line.Trim();
                if (t.Length == 0 || t.StartsWith('#') || t.StartsWith(';')) continue;
                rels.Add(NormalizeRel(t));
            }
        }

        return rels.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static ChangeDetectResult Detect(Install inst)
    {
        var snap = LoadSnapshot(inst);
        var current = BuildCurrentEntries(inst, EnumeratePackRelPaths(inst));

        if (snap == null || snap.Files == null || snap.Files.Count == 0)
        {
            var first = current.Keys
                .Where(k => FileExists(inst, k))
                .Select(k => new ChangedPackFile { RelPath = k, Kind = "added" })
                .Take(40)
                .ToList();
            return new ChangeDetectResult
            {
                HasSnapshot = false,
                HasChanges = first.Count > 0,
                Changes = first,
            };
        }

        var changes = new List<ChangedPackFile>();
        var allKeys = new HashSet<string>(snap.Files.Keys, StringComparer.OrdinalIgnoreCase);
        foreach (var k in current.Keys) allKeys.Add(k);

        foreach (var key in allKeys.OrderBy(x => x, StringComparer.OrdinalIgnoreCase))
        {
            snap.Files.TryGetValue(key, out var old);
            current.TryGetValue(key, out var now);
            var existsNow = now != null && FileExists(inst, key);

            if (old == null && existsNow)
            {
                changes.Add(new ChangedPackFile { RelPath = key, Kind = "added" });
                continue;
            }
            if (old != null && !existsNow)
            {
                changes.Add(new ChangedPackFile { RelPath = key, Kind = "removed" });
                continue;
            }
            if (old != null && now != null &&
                (!string.Equals(old.Sha256, now.Sha256, StringComparison.OrdinalIgnoreCase) ||
                 old.Length != now.Length))
            {
                changes.Add(new ChangedPackFile { RelPath = key, Kind = "modified" });
            }
        }

        return new ChangeDetectResult
        {
            HasSnapshot = true,
            HasChanges = changes.Count > 0,
            LastVersion = snap.Version ?? "",
            LastPublishedUtc = snap.PublishedUtc ?? "",
            Changes = changes,
        };
    }

    public static void CaptureAfterPublish(Install inst, PackVersion version, string outDir)
    {
        var paths = EnumeratePackRelPaths(inst);
        var files = BuildCurrentEntries(inst, paths);
        var existing = files
            .Where(kv => FileExists(inst, kv.Key))
            .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.OrdinalIgnoreCase);

        SaveSnapshot(inst, new DevLastPublishSnapshot
        {
            Version = version.Version ?? "",
            PublishedUtc = DateTime.UtcNow.ToString("o"),
            OutDir = outDir,
            Files = existing,
        });
    }

    public static string SuggestPatchBump(string version)
    {
        var clean = (version ?? "0.1.0").Split(new[] { '-', '+' }, 2)[0].Trim();
        if (string.IsNullOrWhiteSpace(clean)) clean = "0.1.0";
        var parts = clean.Split('.');
        var nums = new List<int>();
        foreach (var p in parts)
            nums.Add(int.TryParse(p, out var n) ? n : 0);
        while (nums.Count < 3) nums.Add(0);
        nums[^1] = nums[^1] + 1;
        return string.Join(".", nums);
    }

    private static Dictionary<string, FileSnapshotEntry> BuildCurrentEntries(
        Install inst, IEnumerable<string> relPaths)
    {
        var dict = new Dictionary<string, FileSnapshotEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var rel in relPaths)
        {
            var full = ResolvePackFile(inst, rel);
            if (full == null || !File.Exists(full)) continue;
            try
            {
                var info = new FileInfo(full);
                dict[NormalizeRel(rel)] = new FileSnapshotEntry
                {
                    Sha256 = Sha256File(full),
                    Length = info.Length,
                    LastWriteUtc = info.LastWriteTimeUtc.ToString("o"),
                };
            }
            catch { /* skip unreadable */ }
        }
        return dict;
    }

    private static string? ResolvePackFile(Install inst, string rel)
    {
        var norm = NormalizeRel(rel).Replace('/', Path.DirectorySeparatorChar);
        if (norm.Equals("MMX-Net-Launcher.exe", StringComparison.OrdinalIgnoreCase))
        {
            var root = Path.Combine(inst.Root, "MMX-Net-Launcher.exe");
            if (File.Exists(root)) return root;
            var dist = Path.Combine(inst.Root, "dist", "user", "MMX-Net-Launcher.exe");
            if (File.Exists(dist)) return dist;
            return root;
        }
        return Path.Combine(inst.Root, norm);
    }

    private static bool FileExists(Install inst, string rel) =>
        ResolvePackFile(inst, rel) is { } p && File.Exists(p);

    private static string NormalizeRel(string rel) =>
        rel.Replace('\\', '/').Trim().TrimStart('/');

    private static string Sha256File(string path)
    {
        using var fs = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(fs)).ToLowerInvariant();
    }
}
