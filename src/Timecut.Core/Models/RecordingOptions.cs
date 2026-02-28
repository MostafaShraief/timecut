namespace Timecut.Core.Models;

public class RecordingOptions
{
    public double Fps { get; set; } = 30;
    public double Duration { get; set; } = 5;
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public double PageZoom { get; set; } = 1.0;
    public string PixelFormat { get; set; } = "yuv420p";
    public string? OutputFileName { get; set; }
    public ScreenshotFormat ScreenshotFormat { get; set; } = ScreenshotFormat.Jpeg;
    public int ScreenshotQuality { get; set; } = 80;
    public double StartOffset { get; set; }
    public string VideoCodec { get; set; } = "libx264";
    public string EncodingPreset { get; set; } = "ultrafast";

    public RecordingOptions Clone() => (RecordingOptions)MemberwiseClone();
}

public enum ScreenshotFormat
{
    Jpeg,
    Png
}
