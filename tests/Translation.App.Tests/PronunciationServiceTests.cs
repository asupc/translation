using System.IO;
using Translation.App;
using Xunit;

namespace Translation.App.Tests;

public sealed class PronunciationServiceTests : IDisposable
{
    private readonly string _directory;

    public PronunciationServiceTests()
    {
        _directory = Path.Combine(
            Path.GetTempPath(),
            "translation-tts-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
    }

    public void Dispose()
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            try
            {
                Directory.Delete(_directory, recursive: true);
                return;
            }
            catch (IOException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
            catch (UnauthorizedAccessException) when (attempt < 4)
            {
                Thread.Sleep(100);
            }
        }
    }

    [Fact]
    public void IsAvailable_必需文件齐全时为真()
    {
        CreateRequiredFiles();

        using var service = new PronunciationService(_directory);

        Assert.True(service.IsAvailable);
    }

    [Fact]
    public void IsAvailable_缺少词典文件时为假()
    {
        CreateRequiredFiles();
        File.Delete(Path.Combine(_directory, "lexicon-en-us.txt"));

        using var service = new PronunciationService(_directory);

        Assert.False(service.IsAvailable);
    }

    [Fact]
    public async Task SpeakAsync_发音资源缺失时抛异常()
    {
        using var service = new PronunciationService(_directory);

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SpeakAsync("hello"));
    }

    private void CreateRequiredFiles()
    {
        Directory.CreateDirectory(Path.Combine(_directory, "bin"));
        Directory.CreateDirectory(Path.Combine(_directory, "model"));
        File.WriteAllBytes(Path.Combine(_directory, "bin", "sherpa-onnx-offline-tts.exe"), []);
        File.WriteAllBytes(Path.Combine(_directory, "bin", "sherpa-onnx-c-api.dll"), []);
        File.WriteAllBytes(Path.Combine(_directory, "bin", "sherpa-onnx-cxx-api.dll"), []);
        File.WriteAllBytes(Path.Combine(_directory, "bin", "onnxruntime.dll"), []);
        File.WriteAllBytes(Path.Combine(_directory, "model", "model.onnx"), []);
        File.WriteAllBytes(Path.Combine(_directory, "model", "voices.bin"), []);
        File.WriteAllBytes(Path.Combine(_directory, "model", "tokens.txt"), []);
        File.WriteAllBytes(Path.Combine(_directory, "lexicon-en-us.txt"), []);
    }
}
