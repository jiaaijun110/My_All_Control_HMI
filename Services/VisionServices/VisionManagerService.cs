using Drivers.DVision;
using Drivers.DVision.Siemens;

namespace Services.VisionServices
{
    public sealed class VisionManagerService : IDisposable
    {
        private readonly ICameraDriver _camera;
        private readonly object _statsLock = new();
        private int _okCount;
        private int _ngCount;
        private int _productionFrameCount;
        private bool _lastResultOk = true;
        private double _lastMeanBrightness;
        private bool _disposed;
        public int OkCount
        {
            get
            {
                lock (_statsLock)
                {
                    return _okCount;
                }
            }
        }
        public int NgCount
        {
            get
            {
                lock (_statsLock)
                {
                    return _ngCount;
                }
            }
        }
        public int ProductionFrameCount
        {
            get
            {
                lock (_statsLock)
                {
                    return _productionFrameCount;
                }
            }
        }
        public double LastMeanBrightness
        {
            get
            {
                lock (_statsLock)
                {
                    return _lastMeanBrightness;
                }
            }
        }
        public bool LastResultOk
        {
            get
            {
                lock (_statsLock)
                {
                    return _lastResultOk;
                }
            }
        }
        public event EventHandler? StatisticsChanged;
        public event EventHandler<FramePresentEventArgs>? FrameReadyForDisplay;
        public event EventHandler<CameraLinkEventArgs>? CameraLinkChanged;
        /// <param name="camera">相机驱动。</param>
        public VisionManagerService(ICameraDriver camera)
        {
            _camera = camera;
            _camera.ImageGrabbed += OnImageGrabbed;

            if (camera is RealCameraDriver real)
            {
                real.LinkStateChanged += OnCameraDriverLink;
            }
        }

        private void OnCameraDriverLink(object? sender, CameraLinkEventArgs e)
        {
            CameraLinkChanged?.Invoke(this, e);
        }
        /// <param name="cancellationToken">取消令牌。</param>
        public Task OpenAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _camera.Open();
                _camera.StartGrabbing();
            }, cancellationToken);
        }
        /// <param name="cancellationToken">取消令牌。</param>
        public Task CloseAsync(CancellationToken cancellationToken = default)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                _camera.StopGrabbing();
                _camera.Close();
            }, cancellationToken);
        }

        private void OnImageGrabbed(object? sender, ImageGrabbedEventArgs e)
        {
            _ = Task.Run(() => ProcessFrameOnWorker(e));
        }
        /// <remarks>
        /// 视觉相关内存：在首行复制 <see cref="ImageGrabbedEventArgs.PixelBuffer"/> 到独立 <c>byte[]</c>，
        /// 后续算法与 UI 送显均使用该副本，避免与驱动复用缓冲冲突。
        /// </remarks>
        private void ProcessFrameOnWorker(ImageGrabbedEventArgs e)
        {
            try
            {
                var copy = new byte[e.PixelBuffer.Length];
                Buffer.BlockCopy(e.PixelBuffer, 0, copy, 0, e.PixelBuffer.Length);
                var w = e.Width;
                var h = e.Height;
                var frameId = e.FrameId;

                var mean = ComputeMeanBgrBrightness(copy, w, h);
                var ok = mean >= 35.0 && mean <= 245.0;

                lock (_statsLock)
                {
                    _productionFrameCount++;
                    _lastMeanBrightness = mean;
                    if (ok)
                    {
                        _okCount++;
                    }
                    else
                    {
                        _ngCount++;
                    }

                    _lastResultOk = ok;
                }

                StatisticsChanged?.Invoke(this, EventArgs.Empty);
                FrameReadyForDisplay?.Invoke(this, new FramePresentEventArgs(copy, w, h, ok, frameId));
            }
            catch
            {
                // 骨架阶段忽略单帧异常。
            }
        }
        /// <param name="bgr32">BGR32 字节，长度 width×height×4。</param>
        /// <param name="width">宽。</param>
        /// <param name="height">高。</param>
        /// <returns>0~255 的近似灰度均值。</returns>
        private static double ComputeMeanBgrBrightness(byte[] bgr32, int width, int height)
        {
            var pixelCount = width * height;
            if (pixelCount == 0)
            {
                return 0;
            }

            long sum = 0;
            for (var i = 0; i < bgr32.Length; i += 4)
            {
                sum += bgr32[i] + bgr32[i + 1] + bgr32[i + 2];
            }

            return sum / (3.0 * pixelCount);
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(VisionManagerService));
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
            _camera.ImageGrabbed -= OnImageGrabbed;
            if (_camera is RealCameraDriver real)
            {
                real.LinkStateChanged -= OnCameraDriverLink;
            }

            _camera.StopGrabbing();
            _camera.Close();
            if (_camera is IDisposable d)
            {
                d.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}

