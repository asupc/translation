using System.IO;

namespace Translation.App;

internal static class Paths
{
    public static string ModelsRoot { get; } = ResolveModelsRoot();

    public static string TranslationModel(string name)
        => Path.Combine(ModelsRoot, "translation", name);

    public static string OcrModels => Path.Combine(ModelsRoot, "ocr", "v5");

    public static string DictionaryDatabase => Path.Combine(ModelsRoot, "dictionary", "ecdict.sqlite");

    private static string ResolveModelsRoot()
    {
        var overridePath = Environment.GetEnvironmentVariable("TRANSLATION_MODELS_DIR");
        if (!string.IsNullOrWhiteSpace(overridePath)
            && Directory.Exists(Path.Combine(overridePath, "translation")))
        {
            return Path.GetFullPath(overridePath);
        }

        // Single-file apps run from an extraction directory in %TEMP%, so use the
        // real executable directory first when looking for a sibling "models" folder.
        var executableDirectory = Path.GetDirectoryName(Environment.ProcessPath ?? string.Empty);
        var starts = new List<string>();
        if (!string.IsNullOrWhiteSpace(executableDirectory) && Directory.Exists(executableDirectory))
        {
            starts.Add(executableDirectory);
        }

        if (!starts.Contains(AppContext.BaseDirectory, StringComparer.OrdinalIgnoreCase))
        {
            starts.Add(AppContext.BaseDirectory);
        }

        foreach (var start in starts)
        {
            var local = Path.Combine(start, "models");
            if (Directory.Exists(Path.Combine(local, "translation")))
            {
                return local;
            }

            var directory = new DirectoryInfo(start);
            while (directory is not null)
            {
                var candidate = Path.Combine(directory.FullName, "models");
                if (Directory.Exists(Path.Combine(candidate, "translation")))
                {
                    return candidate;
                }

                directory = directory.Parent;
            }
        }

        return Path.Combine(starts[0], "models");
    }
}
