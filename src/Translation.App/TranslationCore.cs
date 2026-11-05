using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Translation.App;

public sealed class TranslationCore : IDisposable
{
    private const string Dll = "translation_core";
    private static readonly int Threads = Math.Clamp(Environment.ProcessorCount, 1, 16);

    private readonly string _zhEnModel;
    private readonly string _enZhModel;
    private readonly object _gate = new();
    private IntPtr _zhEn;
    private IntPtr _enZh;
    private int _activeTranslations;
    private bool _disposed;

    public TranslationCore(string zhEnModel, string enZhModel)
    {
        _zhEnModel = zhEnModel;
        _enZhModel = enZhModel;
    }

    public bool IsReady { get; private set; }

    public Task InitializeAsync()
    {
        return Task.Run(() =>
        {
            var zhEn = CreateEngine(_zhEnModel);
            var enZh = IntPtr.Zero;
            try
            {
                enZh = CreateEngine(_enZhModel);
            }
            catch
            {
                translation_engine_destroy(zhEn);
                throw;
            }

            lock (_gate)
            {
                if (_disposed)
                {
                    // Dispose 与初始化并发：句柄交回当前线程销毁，避免泄漏或悬空。
                    translation_engine_destroy(zhEn);
                    translation_engine_destroy(enZh);
                    throw new ObjectDisposedException(nameof(TranslationCore));
                }

                _zhEn = zhEn;
                _enZh = enZh;
                IsReady = true;
            }
        });
    }

    public Task<(string Text, double ElapsedMs)> TranslateAsync(
        string text,
        DetectedLanguage language,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IntPtr engine;
        lock (_gate)
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(TranslationCore));
            }

            if (!IsReady)
            {
                throw new InvalidOperationException("翻译模型尚未加载完成");
            }

            engine = language switch
            {
                DetectedLanguage.Chinese => _zhEn,
                DetectedLanguage.English => _enZh,
                _ => throw new ArgumentOutOfRangeException(nameof(language)),
            };

            _activeTranslations++;
        }

        // 不把 cancellationToken 交给 Task.Run：若任务在入队前被取消，委托不会执行，
        // _activeTranslations 就永远不会归零，Dispose 会一直等待。
        return Task.Run(() =>
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                var stopwatch = Stopwatch.StartNew();
                var translated = Translate(engine, text);
                stopwatch.Stop();
                return (translated, stopwatch.Elapsed.TotalMilliseconds);
            }
            finally
            {
                lock (_gate)
                {
                    _activeTranslations--;
                    if (_activeTranslations == 0)
                    {
                        Monitor.PulseAll(_gate);
                    }
                }
            }
        });
    }

    private static IntPtr CreateEngine(string modelDirectory)
    {
        var error = new byte[2048];
        var engine = translation_engine_create(modelDirectory, Threads, error, error.Length);
        if (engine == IntPtr.Zero)
        {
            throw new InvalidOperationException(ReadError(error));
        }

        return engine;
    }

    private static string Translate(IntPtr engine, string text)
    {
        var error = new byte[2048];
        var pointer = translation_engine_translate(engine, text, 2, 256, error, error.Length);
        if (pointer == IntPtr.Zero)
        {
            throw new InvalidOperationException(ReadError(error));
        }

        try
        {
            return Marshal.PtrToStringUTF8(pointer) ?? string.Empty;
        }
        finally
        {
            translation_engine_free_string(pointer);
        }
    }

    private static string ReadError(byte[] buffer)
    {
        var length = Array.IndexOf(buffer, (byte)0);
        return length <= 0 ? "unknown native error" : Encoding.UTF8.GetString(buffer, 0, length);
    }

    public void Dispose()
    {
        IntPtr zhEn;
        IntPtr enZh;
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            IsReady = false;

            // 等待进行中的翻译退出，避免销毁正在使用的 engine（原生侧为未定义行为）。
            while (_activeTranslations > 0)
            {
                Monitor.Wait(_gate);
            }

            zhEn = _zhEn;
            enZh = _enZh;
            _zhEn = IntPtr.Zero;
            _enZh = IntPtr.Zero;
        }

        if (zhEn != IntPtr.Zero)
        {
            translation_engine_destroy(zhEn);
        }

        if (enZh != IntPtr.Zero)
        {
            translation_engine_destroy(enZh);
        }
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr translation_engine_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelDirectory,
        int numThreads,
        [Out] byte[] errorMessage,
        int errorMessageSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr translation_engine_translate(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        int beamSize,
        int maxDecodingLength,
        [Out] byte[] errorMessage,
        int errorMessageSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void translation_engine_free_string(IntPtr text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void translation_engine_destroy(IntPtr engine);
}
