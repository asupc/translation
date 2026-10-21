using System.IO;
using Microsoft.Data.Sqlite;
using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public sealed class DictionaryServiceTests : IDisposable
{
    private readonly string _directory;

    public DictionaryServiceTests()
    {
        _directory = Path.Combine(Path.GetTempPath(), "translation-dict-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task LookupAsync_变形词还原到原词并给出提示()
    {
        using var service = new DictionaryService(CreateDatabase());

        var entry = await service.LookupAsync("classes");

        Assert.NotNull(entry);
        Assert.Equal("class", entry!.Word);
        Assert.Contains("名词复数", entry.InflectionNote);
        Assert.Contains("第三人称单数", entry.InflectionNote);
        Assert.Contains(entry.Senses, sense => sense.Text == "班级");
        Assert.Contains(entry.Forms, form => form.Kind == "s" && form.Word == "classes");
    }

    [Fact]
    public async Task LookupAsync_变形词指向的原词没有词条时回退自身查询()
    {
        using var service = new DictionaryService(CreateDatabase());

        // went -> go 的索引存在，但 entries 没有 go，也没有 went 自身词条。
        Assert.Null(await service.LookupAsync("went"));
    }

    [Fact]
    public async Task LookupAsync_精确匹配保留大小写区分()
    {
        using var service = new DictionaryService(CreateDatabase());

        var porcelain = await service.LookupAsync("china");
        var country = await service.LookupAsync("China");

        Assert.Contains("瓷器", string.Join(" ", porcelain!.Senses.Select(sense => sense.Text)));
        Assert.Contains("中国", string.Join(" ", country!.Senses.Select(sense => sense.Text)));
    }

    [Fact]
    public async Task LookupAsync_大小写不敏感兜底取高频词条()
    {
        using var service = new DictionaryService(CreateDatabase());

        var entry = await service.LookupAsync("CHINA");

        Assert.NotNull(entry);
        Assert.Equal("China", entry!.Word);
    }

    [Fact]
    public async Task LookupAsync_尾部句读先规整()
    {
        using var service = new DictionaryService(CreateDatabase());

        var entry = await service.LookupAsync("class.");

        Assert.NotNull(entry);
        Assert.Equal("class", entry!.Word);
    }

    [Fact]
    public async Task LookupAsync_未命中返回null()
    {
        using var service = new DictionaryService(CreateDatabase());

        Assert.Null(await service.LookupAsync("zzzz"));
    }

    [Fact]
    public async Task LookupAsync_库文件缺失返回null()
    {
        using var service = new DictionaryService(Path.Combine(_directory, "missing.sqlite"));

        Assert.False(service.IsAvailable);
        Assert.Null(await service.LookupAsync("class"));
    }

    /// <summary>建与生产结构一致的最小词典库：class 词条 + china/China 大小写对 + 变形索引。</summary>
    private string CreateDatabase()
    {
        var path = Path.Combine(_directory, "ecdict.sqlite");
        var builder = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
        using var connection = new SqliteConnection(builder.ConnectionString);
        connection.Open();
        foreach (var sql in new[]
        {
            "CREATE TABLE entries (word TEXT PRIMARY KEY, phonetic TEXT, translation TEXT, "
              + "pos TEXT, collins INTEGER NOT NULL DEFAULT 0, oxford INTEGER NOT NULL DEFAULT 0, tag TEXT, "
              + "bnc INTEGER NOT NULL DEFAULT -1, frq INTEGER NOT NULL DEFAULT -1, exchange TEXT)",
            "CREATE TABLE inflections (form TEXT PRIMARY KEY, lemma TEXT NOT NULL, kinds TEXT NOT NULL)",
            "INSERT INTO entries (word, phonetic, translation, collins, oxford, tag, frq, exchange) VALUES "
              + "('class', 'klɑːs', 'n. 班级\nn. 阶级\nvt. 把…分类', 3, 1, 'cet4 cet6', 500, "
              + "'s:classes/3:classes/p:classified/d:classified/i:classifying')",
            "INSERT INTO entries (word, translation, frq) VALUES ('china', 'n. 瓷器', 4000)",
            "INSERT INTO entries (word, translation, frq) VALUES ('China', 'n. 中国', 300)",
            "INSERT INTO inflections (form, lemma, kinds) VALUES ('classes', 'class', 's,3')",
            "INSERT INTO inflections (form, lemma, kinds) VALUES ('went', 'go', 'p')",
        })
        {
            using var command = connection.CreateCommand();
            command.CommandText = sql;
            command.ExecuteNonQuery();
        }

        connection.Close();
        return path;
    }
}
