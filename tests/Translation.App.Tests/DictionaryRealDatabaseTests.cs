using System.IO;
using Translation.App;
using Xunit;

namespace Translation.App.Tests;

/// <summary>针对真实 ECDICT 库（models/dictionary/ecdict.sqlite）的集成验证；库不存在时自动跳过。</summary>
public sealed class DictionaryRealDatabaseTests
{
    private static string? FindDatabase()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "models", "dictionary", "ecdict.sqlite");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    [Fact]
    public async Task 真实库_class词条包含多义项与词形变化()
    {
        var database = FindDatabase();
        if (database is null)
        {
            return; // 未构建词典数据，跳过
        }

        using var service = new DictionaryService(database);
        Assert.True(service.IsAvailable);

        var entry = await service.LookupAsync("class");
        Assert.NotNull(entry);
        Assert.Equal("class", entry!.Word);
        Assert.NotEmpty(entry.Phonetic);
        Assert.True(entry.Collins > 0);
        // 名词多义项（班级/阶级/上课…）与动词义项（vt.）都要存在。
        Assert.Contains(entry.Senses, sense => sense.Pos == "n." && sense.Text.Contains("班级"));
        Assert.Contains(entry.Senses, sense => sense.Pos.StartsWith('v'));
        // 词形变化：复数 classes、过去式 classed、现在分词 classing。
        Assert.Contains(entry.Forms, form => form.Kind == "s" && form.Word == "classes");
        Assert.Contains(entry.Forms, form => form.Kind == "p" && form.Word == "classed");
        Assert.Contains(entry.Forms, form => form.Kind == "i" && form.Word == "classing");
    }

    [Fact]
    public async Task 真实库_变形词还原()
    {
        var database = FindDatabase();
        if (database is null)
        {
            return;
        }

        using var service = new DictionaryService(database);

        var classes = await service.LookupAsync("classes");
        Assert.NotNull(classes);
        Assert.Equal("class", classes!.Word);
        Assert.NotNull(classes.InflectionNote);

        var running = await service.LookupAsync("running");
        Assert.NotNull(running);
        Assert.Equal("run", running!.Word);
        Assert.Contains("现在分词", running.InflectionNote);
    }

    [Fact]
    public async Task 真实库_生僻词未命中返回null()
    {
        var database = FindDatabase();
        if (database is null)
        {
            return;
        }

        using var service = new DictionaryService(database);
        Assert.Null(await service.LookupAsync("zzzzqqqq"));
    }
}
