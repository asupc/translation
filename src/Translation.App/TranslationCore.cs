using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Translation.App;

public sealed class TranslationCore : IDisposable
{
    private const string Dll = "translation_core";
    private const int Threads = 8;

    private readonly string _zhEnModel;
    private readonly string _enZhModel;
    private IntPtr _zhEn;
    private IntPtr _enZh;

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
            _zhEn = CreateEngine(_zhEnModel);
            try
            {
                _enZh = CreateEngine(_enZhModel);
            }
            catch
            {
                translation_engine_destroy(_zhEn);
                _zhEn = IntPtr.Zero;
                throw;
            }

            IsReady = true;
        });
    }

    public Task<(string Text, double ElapsedMs)> TranslateAsync(
        string text,
        DetectedLanguage language,
        CancellationToken cancellationToken = default)
    {
        if (!IsReady)
        {
            throw new InvalidOperationException("翻译模型尚未加载完成");
        }

        var engine = language switch
        {
            DetectedLanguage.Chinese => _zhEn,
            DetectedLanguage.English => _enZh,
            _ => throw new ArgumentOutOfRangeException(nameof(language)),
        };

        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var stopwatch = Stopwatch.StartNew();
            var translated = Translate(engine, text);
            stopwatch.Stop();
            return (translated, stopwatch.Elapsed.TotalMilliseconds);
        }, cancellationToken);
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
        if (_zhEn != IntPtr.Zero)
        {
            translation_engine_destroy(_zhEn);
            _zhEn = IntPtr.Zero;
        }

        if (_enZh != IntPtr.Zero)
        {
            translation_engine_destroy(_enZh);
            _enZh = IntPtr.Zero;
        }

        IsReady = false;
    }

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr translation_engine_create(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string modelDirectory,
        int numThreads,
        byte[] errorMessage,
        int errorMessageSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr translation_engine_translate(
        IntPtr engine,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string text,
        int beamSize,
        int maxDecodingLength,
        byte[] errorMessage,
        int errorMessageSize);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void translation_engine_free_string(IntPtr text);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void translation_engine_destroy(IntPtr engine);
}
