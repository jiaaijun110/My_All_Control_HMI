namespace Drivers.DVision.Siemens
{
    public sealed class MockCameraDriver : ICameraDriver, IDisposable
    {
        private readonly object _sync = new();
        private readonly Random _random = new();
        private readonly Timer _timer;
        private long _frameId;
        private bool _disposed;
        private string? _sampleImagePath;

        private const int DefaultWidth = 640;
        private const int DefaultHeight = 480;

        /// <inheritdoc />
        public bool IsOpen { get; private set; }

        /// <inheritdoc />
        public bool IsGrabbing { get; private set; }

        /// <inheritdoc />
        public event EventHandler<ImageGrabbedEventArgs>? ImageGrabbed;
        /// <param name="sampleImagePath">可选：本地 BGR32 原始文件路径（宽×高×4 字节），不存在则使用噪声。</param>
        public MockCameraDriver(string? sampleImagePath = null)
        {
            _sampleImagePath = sampleImagePath;
            _timer = new Timer(OnTimerTick, null, Timeout.Infinite, Timeout.Infinite);
        }

        /// <inheritdoc />
        public void Open()
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                IsOpen = true;
            }
        }

        /// <inheritdoc />
        public void StartGrabbing()
        {
            ThrowIfDisposed();
            lock (_sync)
            {
                if (!IsOpen)
                {
                    Open();
                }

                IsGrabbing = true;
                _timer.Change(0, 33);
            }
        }

        /// <inheritdoc />
        public void StopGrabbing()
        {
            lock (_sync)
            {
                IsGrabbing = false;
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
            }
        }

        /// <inheritdoc />
        public void Close()
        {
            StopGrabbing();
            lock (_sync)
            {
                IsOpen = false;
            }
        }
        private void OnTimerTick(object? state)
        {
            if (!IsGrabbing || !IsOpen)
            {
                return;
            }

            var frameId = Interlocked.Increment(ref _frameId);
            var w = DefaultWidth;
            var h = DefaultHeight;

            // 视觉相关内存：每帧独立分配 BGR32 缓冲区（width*height*4），避免多线程复用同一缓冲导致竞态。
            var buffer = TryLoadRawBgr32(_sampleImagePath, w, h) ?? GenerateNoiseBgr32(w, h);

            ImageGrabbed?.Invoke(this, new ImageGrabbedEventArgs
            {
                Width = w,
                Height = h,
                PixelBuffer = buffer,
                FrameId = frameId
            });
        }
        /// <param name="path">文件路径。</param>
        /// <param name="width">期望宽度。</param>
        /// <param name="height">期望高度。</param>
        /// <returns>像素字节；无效则 null。</returns>
        private static byte[]? TryLoadRawBgr32(string? path, int width, int height)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                return null;
            }

            var expected = width * height * 4;
            var bytes = File.ReadAllBytes(path);
            return bytes.Length == expected ? bytes : null;
        }
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        /// <returns>新分配的缓冲区。</returns>
        private byte[] GenerateNoiseBgr32(int width, int height)
        {
            var bytes = new byte[width * height * 4];
            lock (_random)
            {
                _random.NextBytes(bytes);
            }

            for (var i = 3; i < bytes.Length; i += 4)
            {
                bytes[i] = 255;
            }

            return bytes;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(MockCameraDriver));
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            _timer.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}

