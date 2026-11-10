using System.Diagnostics;
using System.IO;
using System.Windows.Media;

namespace Translation.App;

/// <summary>
/// 离线单词发音：启动随包的 sherpa-onnx TTS 子进程合成 WAV，再用 WPF MediaPlayer 播放。
/// 子进程方式避免与主进程的 ONNX Runtime 版本冲突，窗口隐藏/退出时也能彻底停止。
/// </summary>
public sealed class PronunciationService : IDisposable
{
    private const string ExecutableName = "sherpa-onnx-offline-tts.exe";
    private const string ModelFileName = "model.onnx";
    private const string VoicesFileName = "voices.bin";
    private const string TokensFileName = "tokens.txt";
    private const string LexiconFileName = "lexicon-en-us.txt";

    private readonly string _directory;
    private readonly object _gate = new();
    private Process? _process;
    private CancellationTokenSource? _cts;
    private MediaPlayer? _player;
    private string? _playingFile;
    private bool _disposed;

    public PronunciationService(string directory)
    {
        _directory = directory;
        CleanupStaleFiles();
    }

    /// <summary>发音资源是否齐全；缺失时不显示喇叭按钮，词典功能不受影响。</summary>
    public bool IsAvailable =>
        File.Exists(ExecutablePath)
        && File.Exists(Path.Combine(_directory, "bin", "sherpa-onnx-c-api.dll"))
        && File.Exists(Path.Combine(_directory, "bin", "sherpa-onnx-cxx-api.dll"))
        && File.Exists(Path.Combine(_directory, "bin", "onnxruntime.dll"))
        && File.Exists(ModelPath)
        && File.Exists(VoicesPath)
        && File.Exists(TokensPath)
        && File.Exists(LexiconPath);

    /// <summary>合成并播放一段英文文本（单词或整句）；失败、取消或资源缺失时抛出异常。</summary>
    public async Task SpeakAsync(string text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsAvailable)
        {
            throw new InvalidOperationException($"发音模型缺失: {_directory}");
        }

        // 译文可能带换行；命令行参数里的换行对前端分词没有意义，统一压成空格。
        var spoken = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        if (spoken.Length == 0)
        {
            return;
        }

        // 先停掉上一次的生成/播放，连点时以最后一次为准。
        Stop();

        var outputName = $"tts-{Guid.NewGuid():N}.wav";
        var outputPath = Path.Combine(_directory, outputName);
        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        lock (_gate)
        {
            _cts = cts;
        }

        using var process = new Process
        {
            StartInfo = CreateStartInfo(outputName, spoken),
        };
        lock (_gate)
        {
            _process = process;
        }

        try
        {
            if (!process.Start())
            {
                throw new InvalidOperationException("无法启动发音进程");
            }

            // 先开始读取输出管道，避免日志写满导致 WaitForExit 卡死。
            var stdoutTask = process.StandardOutput.ReadToEndAsync(cts.Token);
            var stderrTask = process.StandardError.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token).ConfigureAwait(true);
            await stdoutTask.ConfigureAwait(true);
            var stderr = await stderrTask.ConfigureAwait(true);

            if (process.ExitCode != 0 || !File.Exists(outputPath))
            {
                TryDelete(outputPath);
                throw new InvalidOperationException(
                    $"发音合成失败（退出码 {process.ExitCode}）: {Summarize(stderr)}");
            }

            if (_disposed)
            {
                TryDelete(outputPath);
                return;
            }

            Play(outputPath);
        }
        catch (OperationCanceledException)
        {
            TryKill(process);
            TryDelete(outputPath);
            throw;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_process, process))
                {
                    _process = null;
                }

                if (ReferenceEquals(_cts, cts))
                {
                    _cts = null;
                }
            }

            cts.Dispose();
        }
    }

    /// <summary>停止生成与播放，并清理临时 WAV；可重复调用。</summary>
    public void Stop()
    {
        Process? process;
        CancellationTokenSource? cts;
        string? playingFile;
        lock (_gate)
        {
            process = _process;
            cts = _cts;
            playingFile = _playingFile;
            _playingFile = null;
            _player?.Close();
        }

        // 取消与结束由 SpeakAsync 的 finally 统一释放，这里只负责打断。
        cts?.Cancel();
        if (process is not null)
        {
            TryKill(process);
        }

        TryDelete(playingFile);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _player?.Close();
        _player = null;
    }

    private string ExecutablePath => Path.Combine(_directory, "bin", ExecutableName);

    private string ModelPath => Path.Combine(_directory, "model", ModelFileName);

    private string VoicesPath => Path.Combine(_directory, "model", VoicesFileName);

    private string TokensPath => Path.Combine(_directory, "model", TokensFileName);

    private string LexiconPath => Path.Combine(_directory, LexiconFileName);

    private ProcessStartInfo CreateStartInfo(string outputName, string word)
    {
        var info = new ProcessStartInfo
        {
            FileName = ExecutablePath,
            // 工作目录固定在发音资源目录，模型/输出参数全部用相对 ASCII 路径：
            // sherpa-onnx CLI 使用窄字符 argv，绝对中文路径在 Windows 下会乱码。
            WorkingDirectory = _directory,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };

        info.ArgumentList.Add(@"--kitten-model=model\model.onnx");
        info.ArgumentList.Add(@"--kitten-voices=model\voices.bin");
        info.ArgumentList.Add(@"--kitten-tokens=model\tokens.txt");
        info.ArgumentList.Add($@"--kitten-lexicon={LexiconFileName}");
        info.ArgumentList.Add("--num-threads=2");
        info.ArgumentList.Add($"--output-filename={outputName}");
        info.ArgumentList.Add(word);
        return info;
    }

    private void Play(string file)
    {
        _player ??= CreatePlayer();
        _playingFile = file;
        _player.Open(new Uri(Path.GetFullPath(file)));
        _player.Play();
    }

    private MediaPlayer CreatePlayer()
    {
        var player = new MediaPlayer { Volume = 1.0 };
        player.MediaEnded += (_, _) => OnPlaybackStopped();
        player.MediaFailed += (_, error) =>
        {
            AppLog.Write($"tts playback failed: {error.ErrorException?.Message}");
            OnPlaybackStopped();
        };
        return player;
    }

    private void OnPlaybackStopped()
    {
        string? file;
        lock (_gate)
        {
            file = _playingFile;
            _playingFile = null;
            _player?.Close();
        }

        TryDelete(file);
    }

    private void CleanupStaleFiles()
    {
        try
        {
            if (!Directory.Exists(_directory))
            {
                return;
            }

            foreach (var file in Directory.EnumerateFiles(_directory, "tts-*.wav"))
            {
                TryDelete(file);
            }
        }
        catch (IOException)
        {
            // 上次异常退出留下的文件；清理失败不影响本次使用。
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // 进程已退出。
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // 无权结束或已退出。
        }
    }

    private static void TryDelete(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Summarize(string stderr)
    {
        var text = stderr.Trim();
        if (text.Length == 0)
        {
            return "无错误输出";
        }

        const int limit = 400;
        return text.Length <= limit ? text : text[^limit..];
    }
}
