using System.IO;
using System.Windows.Media.Imaging;
using RapidOcrNet;
using SkiaSharp;

namespace Translation.App;

public sealed class OcrService : IDisposable
{
    private readonly RapidOcr _ocr = new();
    private readonly RapidOcrOptions _options = RapidOcrOptions.Default with
    {
        DoAngle = false,
        RecMaxDegreeOfParallelism = 1,
    };
    private readonly object _gate = new();

    public OcrService(string modelDirectory)
    {
        var modelSet = RapidOcrModelSet.PPOCRv5Latin with
        {
            DetModelPath = Path.Combine(modelDirectory, "ch_PP-OCRv5_det_mobile.onnx"),
            ClsModelPath = Path.Combine(modelDirectory, "ch_PP-LCNet_x0_25_textline_ori_cls_mobile.onnx"),
            RecModelPath = Path.Combine(modelDirectory, "ch_PP-OCRv5_rec_mobile.onnx"),
            KeysPath = Path.Combine(modelDirectory, "ppocrv5_dict.txt"),
        };
        _ocr.InitModels(modelSet);
    }

    public string Recognize(BitmapSource image)
    {
        lock (_gate)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(image));
            using var stream = new MemoryStream();
            encoder.Save(stream);
            stream.Position = 0;

            using var skBitmap = SKBitmap.Decode(stream);
            var result = _ocr.Detect(skBitmap, _options);
            return result.StrRes;
        }
    }

    public void Dispose()
    {
        _ocr.Dispose();
    }
}
