namespace BonsaiWinUI.Services;

public sealed class CatalogModel
{
    public required string Id { get; init; }
    public required string Label { get; init; }
    public required string Repo { get; init; }
    public required string File { get; init; }
    public string? Mmproj { get; init; }
}

public sealed class LocalModel
{
    public required string Path { get; init; }
    public required string Name { get; init; }
    public long SizeBytes { get; init; }

    public string Display => $"{Name}  ({FormatSize(SizeBytes)})";

    public static string FormatSize(long bytes)
    {
        double n = bytes;
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        foreach (var u in units)
        {
            if (n < 1024) return $"{n:0.#} {u}";
            n /= 1024;
        }
        return $"{n:0.#} PB";
    }
}

public static class ModelCatalog
{
    public static IReadOnlyList<CatalogModel> Known { get; } =
    [
        new()
        {
            Id = "bonsai-27b-q1",
            Label = "Bonsai 27B · 1-bit (Q1_0)  [recomendado]",
            Repo = "prism-ml/Bonsai-27B-gguf",
            File = "Bonsai-27B-Q1_0.gguf",
            Mmproj = "Bonsai-27B-mmproj-Q8_0.gguf",
        },
        new()
        {
            Id = "ternary-27b-q2-g64",
            Label = "Ternary-Bonsai 27B · Q2_0_g64",
            Repo = "prism-ml/Ternary-Bonsai-27B-gguf",
            File = "Ternary-Bonsai-27B-Q2_0_g64.gguf",
        },
        new()
        {
            Id = "ternary-8b-q2-g64",
            Label = "Ternary-Bonsai 8B · Q2_0_g64",
            Repo = "prism-ml/Ternary-Bonsai-8B-gguf",
            File = "Ternary-Bonsai-8B-Q2_0_g64.gguf",
        },
        new()
        {
            Id = "ternary-1.7b-q2-g64",
            Label = "Ternary-Bonsai 1.7B · Q2_0_g64",
            Repo = "prism-ml/Ternary-Bonsai-1.7B-gguf",
            File = "Ternary-Bonsai-1.7B-Q2_0_g64.gguf",
        },
        new()
        {
            Id = "bonsai-8b-q1",
            Label = "Bonsai 8B · 1-bit (Q1_0)",
            Repo = "prism-ml/Bonsai-8B-gguf",
            File = "Bonsai-8B-Q1_0.gguf",
        },
    ];

    public static List<LocalModel> ScanLocal(string modelsDir)
    {
        var list = new List<LocalModel>();
        if (!Directory.Exists(modelsDir)) return list;

        foreach (var file in Directory.EnumerateFiles(modelsDir, "*.gguf", SearchOption.AllDirectories))
        {
            var name = Path.GetFileName(file);
            if (name.Contains("mmproj", StringComparison.OrdinalIgnoreCase))
                continue;

            var info = new FileInfo(file);
            list.Add(new LocalModel
            {
                Path = file,
                Name = name,
                SizeBytes = info.Length,
            });
        }

        return list.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public static string? FindMmproj(string modelPath, string modelsDir)
    {
        var dir = Path.GetDirectoryName(modelPath) ?? modelsDir;
        var candidates = new List<string>
        {
            Path.Combine(modelsDir, "Bonsai-27B-mmproj-Q8_0.gguf"),
            Path.Combine(modelsDir, "Bonsai-27B-mmproj-BF16.gguf"),
            Path.Combine(dir, "Bonsai-27B-mmproj-Q8_0.gguf"),
        };

        if (Directory.Exists(dir))
            candidates.AddRange(Directory.EnumerateFiles(dir, "*mmproj*.gguf"));
        if (Directory.Exists(modelsDir))
            candidates.AddRange(Directory.EnumerateFiles(modelsDir, "*mmproj*.gguf"));

        return candidates.FirstOrDefault(File.Exists);
    }
}
