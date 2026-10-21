using System.IO;
using Microsoft.Data.Sqlite;

namespace Translation.App;

/// <summary>ECDICT SQLite 词典查询（只读，首次查询才打开连接）。</summary>
public sealed class DictionaryService : IDisposable
{
    private readonly string _databasePath;
    private readonly object _gate = new();
    private SqliteConnection? _connection;

    public DictionaryService(string databasePath)
    {
        _databasePath = databasePath;
    }

    /// <summary>词典库文件是否存在；不存在时查询直接返回 null，调用方回退句子翻译。</summary>
    public bool IsAvailable => File.Exists(_databasePath);

    /// <summary>查询单词；未命中返回 null。</summary>
    public Task<DictionaryEntry?> LookupAsync(string word)
    {
        var normalized = WordPattern.Normalize(word);
        if (normalized.Length == 0 || !IsAvailable)
        {
            return Task.FromResult<DictionaryEntry?>(null);
        }

        return Task.Run(() => Lookup(normalized));
    }

    private DictionaryEntry? Lookup(string word)
    {
        lock (_gate)
        {
            EnsureConnection();

            // 1) 变形词（classes/went/running）优先还原到原词条目。
            var inflection = QueryInflection(word.ToLowerInvariant());
            if (inflection is not null)
            {
                var lemmaRow = QueryEntryRow(inflection.Lemma);
                if (lemmaRow is not null)
                {
                    return ToEntry(lemmaRow, word, BuildInflectionNote(word, inflection));
                }
            }

            // 2) 精确匹配（保住 China=中国 / china=瓷器 的大小写区分）。
            var exact = QueryEntryRow(word);
            if (exact is not null)
            {
                return ToEntry(exact, word, null);
            }

            // 3) 大小写不敏感兜底（CHINA -> China），多候选取词频更高的。
            var folded = QueryEntryRow(word, caseInsensitive: true);
            return folded is null ? null : ToEntry(folded, word, null);
        }
    }

    private sealed record InflectionLink(string Lemma, string Kinds);

    private sealed record EntryRow(
        string Word, string? Phonetic, string? Translation,
        int Collins, int Oxford, string? Tag, string? Exchange);

    private InflectionLink? QueryInflection(string form)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = "SELECT lemma, kinds FROM inflections WHERE form = @form";
        command.Parameters.AddWithValue("@form", form);
        using var reader = command.ExecuteReader();
        return reader.Read() ? new InflectionLink(reader.GetString(0), reader.GetString(1)) : null;
    }

    private EntryRow? QueryEntryRow(string word, bool caseInsensitive = false)
    {
        using var command = _connection!.CreateCommand();
        command.CommandText = caseInsensitive
            ? "SELECT word, phonetic, translation, collins, oxford, tag, exchange FROM entries "
              + "WHERE word = @word COLLATE NOCASE "
              + "ORDER BY CASE WHEN frq >= 0 THEN frq ELSE 1000000000 END LIMIT 1"
            : "SELECT word, phonetic, translation, collins, oxford, tag, exchange FROM entries WHERE word = @word";
        command.Parameters.AddWithValue("@word", word);
        using var reader = command.ExecuteReader();
        if (!reader.Read())
        {
            return null;
        }

        return new EntryRow(
            reader.GetString(0),
            ReadNullableString(reader, 1),
            ReadNullableString(reader, 2),
            reader.GetInt32(3),
            reader.GetInt32(4),
            ReadNullableString(reader, 5),
            ReadNullableString(reader, 6));
    }

    private static DictionaryEntry ToEntry(EntryRow row, string query, string? note)
    {
        return new DictionaryEntry
        {
            Query = query,
            Word = row.Word,
            Phonetic = row.Phonetic,
            Collins = row.Collins,
            Oxford = row.Oxford > 0,
            Tags = (row.Tag ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries),
            Senses = EntryParser.ParseTranslation(row.Translation),
            Forms = EntryParser.ParseExchange(row.Exchange),
            InflectionNote = note,
        };
    }

    private static string BuildInflectionNote(string query, InflectionLink link)
    {
        var labels = link.Kinds
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(EntryParser.KindLabel)
            .ToArray();
        var kindText = labels.Length > 0 ? string.Join("/", labels) : "词形变化";
        return $"{query} 是 {link.Lemma} 的{kindText}";
    }

    private static string? ReadNullableString(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private void EnsureConnection()
    {
        if (_connection is not null)
        {
            return;
        }

        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = _databasePath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        _connection = new SqliteConnection(builder.ConnectionString);
        _connection.Open();
    }

    public void Dispose()
    {
        _connection?.Dispose();
        _connection = null;
    }
}
