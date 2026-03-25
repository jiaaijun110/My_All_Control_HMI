namespace Drivers.DVision.Siemens
{
    public sealed class ImageGrabbedEventArgs : EventArgs
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] PixelBuffer { get; init; } = Array.Empty<byte>();
        public long FrameId { get; init; }
    }
}

