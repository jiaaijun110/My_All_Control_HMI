using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace Controls.Vision
{
    public partial class VisionDisplayControl : UserControl
    {
        private readonly object _bitmapLock = new();
        private WriteableBitmap? _writeableBitmap;
        public VisionDisplayControl()
        {
            InitializeComponent();
        }
        /// <remarks>
        /// 视觉相关内存（Byte 与位图）：
        /// <list type="bullet">
        /// <item><description><paramref name="bgr32Pixels"/> 布局为每像素 4 字节：B、G、R、保留字节（常用 0 或 255）。</description></item>
        /// <item><description>总长度必须等于 <c>width × height × 4</c>，否则会抛出异常。</description></item>
        /// <item><description>非 UI 线程调用时会克隆字节数组再排队，避免与 <see cref="WriteableBitmap"/> 后端缓冲并发写。</description></item>
        /// <item><description>UI 线程上优先使用 <see cref="WriteableBitmap.Lock"/> + <see cref="Marshal.Copy"/> 写入 <see cref="WriteableBitmap.BackBuffer"/>，
        /// 再 <see cref="WriteableBitmap.AddDirtyRect"/>，避免每帧 <c>new BitmapImage</c> 引发 GC 抖动；跨行距时回退到 <see cref="WriteableBitmap.WritePixels"/>。</description></item>
        /// </list>
        /// </remarks>
        /// <param name="bgr32Pixels">BGR32 像素缓冲。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        public void PresentBgr32(byte[] bgr32Pixels, int width, int height)
        {
            ArgumentNullException.ThrowIfNull(bgr32Pixels);
            if (width <= 0 || height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            var expected = width * height * 4;
            if (bgr32Pixels.Length != expected)
            {
                throw new ArgumentException($"BGR32 缓冲长度应为 {expected}，实际为 {bgr32Pixels.Length}。");
            }

            if (!Dispatcher.CheckAccess())
            {
                var copy = new byte[bgr32Pixels.Length];
                Buffer.BlockCopy(bgr32Pixels, 0, copy, 0, copy.Length);
                _ = Dispatcher.InvokeAsync(() => PresentBgr32OnUiThread(copy, width, height), DispatcherPriority.Render);
                return;
            }

            PresentBgr32OnUiThread(bgr32Pixels, width, height);
        }
        /// <param name="bgr32Pixels">与位图尺寸匹配的 BGR32 缓冲。</param>
        /// <param name="width">宽度。</param>
        /// <param name="height">高度。</param>
        private void PresentBgr32OnUiThread(byte[] bgr32Pixels, int width, int height)
        {
            lock (_bitmapLock)
            {
                if (_writeableBitmap == null
                    || _writeableBitmap.PixelWidth != width
                    || _writeableBitmap.PixelHeight != height)
                {
                    _writeableBitmap = new WriteableBitmap(width, height, 96, 96, PixelFormats.Bgr32, null);
                    DisplayImage.Source = _writeableBitmap;
                }

                var wb = _writeableBitmap;
                var stride = width * 4;

                if (wb.BackBufferStride == stride)
                {
                    // 视觉相关内存：Lock 后 BackBuffer 为固定指针；Marshal.Copy 将托管 byte[] 拷入非托管帧缓冲。
                    wb.Lock();
                    try
                    {
                        var totalBytes = height * wb.BackBufferStride;
                        Marshal.Copy(bgr32Pixels, 0, wb.BackBuffer, totalBytes);
                        wb.AddDirtyRect(new Int32Rect(0, 0, width, height));
                    }
                    finally
                    {
                        wb.Unlock();
                    }
                }
                else
                {
                    // 行距与理论值不一致时，使用 WritePixels 由 WPF 处理跨行填充。
                    wb.WritePixels(new Int32Rect(0, 0, width, height), bgr32Pixels, stride, 0);
                }
            }
        }
    }
}

