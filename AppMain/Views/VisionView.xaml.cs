using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Drivers.DVision;
using Services.Core;
using Services.VisionServices;

namespace AppMain.Views
{
    public partial class VisionView : UserControl
    {
        private VisionManagerService? _manager;
        private RealCameraDriver? _camera;
        private EventHandler? _statisticsHandler;
        private EventHandler<FramePresentEventArgs>? _frameHandler;
        private EventHandler<CameraLinkEventArgs>? _linkHandler;
        public VisionView()
        {
            InitializeComponent();
        }
        private async void VisionView_OnLoaded(object sender, RoutedEventArgs e)
        {
            if (_manager != null)
            {
                return;
            }

            _camera = new RealCameraDriver();
            _manager = new VisionManagerService(_camera);

            _statisticsHandler = (_, _) => _ = Dispatcher.BeginInvoke(UpdateStatisticsUi);
            _frameHandler = OnFrameReadyForDisplay;
            _linkHandler = OnCameraLinkChanged;

            _manager.StatisticsChanged += _statisticsHandler;
            _manager.FrameReadyForDisplay += _frameHandler;
            _manager.CameraLinkChanged += _linkHandler;

            await ConnectCameraAsync().ConfigureAwait(true);
        }
        private async Task ConnectCameraAsync()
        {
            if (_manager == null)
            {
                return;
            }

            BtnConnect.IsEnabled = false;
            BtnDisconnect.IsEnabled = false;
            try
            {
                LoggingDatabaseInitializer.LogInformation("视觉页：开始连接相机（IP 192.168.0.2）。");
                await _manager.OpenAsync().ConfigureAwait(true);
                BtnDisconnect.IsEnabled = true;
                LoggingDatabaseInitializer.LogInformation("视觉页：相机连接流程执行完成。");
            }
            catch (Exception ex)
            {
                // 连接失败不应导致 UI 线程崩溃；保持离线状态并提示日志。
                TxtCameraLink.Text = $"相机: 连接失败（{ex.GetType().Name}）";
                CameraLinkLamp.Fill = (Brush)FindResource("ErrorRed");
                BtnDisconnect.IsEnabled = false;
                LoggingDatabaseInitializer.LogError("视觉页：相机连接失败。", ex);
            }
            finally
            {
                BtnConnect.IsEnabled = true;
            }
        }
        private async void BtnConnect_OnClick(object sender, RoutedEventArgs e)
        {
            await ConnectCameraAsync().ConfigureAwait(true);
        }
        private async void BtnDisconnect_OnClick(object sender, RoutedEventArgs e)
        {
            if (_manager == null)
            {
                return;
            }

            BtnDisconnect.IsEnabled = false;
            BtnConnect.IsEnabled = false;
            try
            {
                await _manager.CloseAsync().ConfigureAwait(true);
            }
            finally
            {
                BtnConnect.IsEnabled = true;
                BtnDisconnect.IsEnabled = false;
            }
        }
        private void OnFrameReadyForDisplay(object? sender, FramePresentEventArgs e)
        {
            VisionDisplay.PresentBgr32(e.Bgr32Pixels, e.Width, e.Height);
            _ = Dispatcher.BeginInvoke(() => UpdateSignalUi(e.IsOk));
        }
        private void OnCameraLinkChanged(object? sender, CameraLinkEventArgs e)
        {
            _ = Dispatcher.BeginInvoke(() =>
            {
                CameraLinkLamp.Fill = e.IsUp
                    ? (Brush)FindResource("SuccessGreen")
                    : (Brush)FindResource("ErrorRed");
                TxtCameraLink.Text = e.IsUp ? "相机: 在线" : "相机: 离线 / 重连中";
            });
        }
        private async void VisionView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (_manager == null)
            {
                return;
            }

            if (_statisticsHandler != null)
            {
                _manager.StatisticsChanged -= _statisticsHandler;
            }

            if (_frameHandler != null)
            {
                _manager.FrameReadyForDisplay -= _frameHandler;
            }

            if (_linkHandler != null)
            {
                _manager.CameraLinkChanged -= _linkHandler;
            }

            var manager = _manager;
            _manager = null;
            _camera = null;
            _statisticsHandler = null;
            _frameHandler = null;
            _linkHandler = null;

            await manager.CloseAsync().ConfigureAwait(true);
            manager.Dispose();
        }
        private void UpdateStatisticsUi()
        {
            if (_manager == null)
            {
                return;
            }

            TxtOkCount.Text = _manager.OkCount.ToString();
            TxtNgCount.Text = _manager.NgCount.ToString();
            TxtTotalInspections.Text = $"生产帧数: {_manager.ProductionFrameCount} | 亮度: {_manager.LastMeanBrightness:F1}";
        }
        private void UpdateSignalUi(bool isOk)
        {
            SignalLamp.Fill = isOk
                ? (Brush)FindResource("SuccessGreen")
                : (Brush)FindResource("ErrorRed");
            TxtLastResult.Text = isOk ? "当前结果: OK" : "当前结果: NG";
        }
    }
}

