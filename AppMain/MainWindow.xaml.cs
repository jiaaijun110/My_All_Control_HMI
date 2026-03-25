using AppMain.Views;
using Services.Core;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace AppMain
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private readonly Dictionary<string, UserControl> _pageCache = new();
        private readonly DispatcherTimer _uiTimer;
        private readonly SystemResourceMonitorService _resourceMonitor = new();
        private readonly Action<LogEventDto> _latestInfoHandler;
        private Brush _databaseLampBrush = Brushes.Gray;
        private Brush _plcStatusLampBrush = Brushes.LimeGreen;

        private string _currentUserName = string.Empty;
        private string _currentUserRoleText = string.Empty;
        private Brush _currentUserRoleBrush = Brushes.Gray;
        public MainWindow()
        {
            InitializeComponent();
            DataContext = this;

            CurrentUserName = $"Admin";
            CurrentUserRoleText = "管理员";
            CurrentUserRoleBrush = (Brush)FindResource("AccentBlue");

            PlcStatusText = "PLC: 通讯正常";
            DatabaseStatusText = "DB: Connected";
            LastInfoTickerText = "最新 INFO：—";
            PlcStatusLampBrush = (Brush)FindResource("SuccessGreen");

            _latestInfoHandler = OnLatestInfoLog;
            LoggingDatabaseInitializer.LatestInfoLog += _latestInfoHandler;
            Closed += MainWindow_OnClosed;

            NavigateTo("Dashboard");

            _uiTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _uiTimer.Tick += UiTimer_Tick;
            _uiTimer.Start();

            // 订阅 PLC 连接状态：正常长绿，故障强红（禁止闪烁）。
            var telemetry = (Application.Current as App)?.PlcTelemetry;
            if (telemetry != null)
            {
                telemetry.PropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(PlcTelemetryModel.IsConnected))
                    {
                        Dispatcher.BeginInvoke(() =>
                        {
                            UpdatePlcStatus(telemetry.IsConnected);
                        });
                    }
                };

                UpdatePlcStatus(telemetry.IsConnected);
            }
        }

        private void MainWindow_OnClosed(object? sender, EventArgs e)
        {
            LoggingDatabaseInitializer.LatestInfoLog -= _latestInfoHandler;
        }
        private void OnLatestInfoLog(LogEventDto dto)
        {
            Dispatcher.BeginInvoke(() =>
            {
                LastInfoTickerText = $"{dto.Timestamp:HH:mm:ss} {dto.Message}";
            });
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn || btn.Tag is not string target)
            {
                return;
            }

            NavigateTo(target);
        }

        private void NavigateTo(string target)
        {
            if (!_pageCache.TryGetValue(target, out UserControl? page))
            {
                page = target switch
                {
                    "Dashboard" => new DashboardView(),
                    "Motor" => new MotorView(),
                    "Vision" => new VisionView(),
                    "Recipe" => new RecipeView(),
                    "Alarm" => new AlarmView(),
                    "Settings" => new SettingsView(),
                    "Log" => new LogView(),
                    _ => new DashboardView()
                };

                _pageCache[target] = page;
            }

            MainPageContainer.Content = page;
        }

        private void UiTimer_Tick(object? sender, EventArgs e)
        {
            CurrentTimeText = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            ResourceUsageText = _resourceMonitor.GetUsageText();
            DatabaseLampBrush = _databaseLampBrush;
        }

        private string _currentTimeText = string.Empty;
        public string CurrentTimeText
        {
            get => _currentTimeText;
            set => SetField(ref _currentTimeText, value);
        }

        private string _plcStatusText = "PLC: --";
        public string PlcStatusText
        {
            get => _plcStatusText;
            set => SetField(ref _plcStatusText, value);
        }

        private string _databaseStatusText = "DB: --";
        public string DatabaseStatusText
        {
            get => _databaseStatusText;
            set => SetField(ref _databaseStatusText, value);
        }
        public string CurrentUserName
        {
            get => _currentUserName;
            set => SetField(ref _currentUserName, value);
        }
        public string CurrentUserRoleText
        {
            get => _currentUserRoleText;
            set => SetField(ref _currentUserRoleText, value);
        }
        public Brush CurrentUserRoleBrush
        {
            get => _currentUserRoleBrush;
            set => SetField(ref _currentUserRoleBrush, value);
        }

        private string _resourceUsageText = "CPU: -- | MEM: --";
        public string ResourceUsageText
        {
            get => _resourceUsageText;
            set => SetField(ref _resourceUsageText, value);
        }

        public Brush DatabaseLampBrush
        {
            get => _databaseLampBrush;
            set => SetField(ref _databaseLampBrush, value);
        }
        public Brush PlcStatusLampBrush
        {
            get => _plcStatusLampBrush;
            set => SetField(ref _plcStatusLampBrush, value);
        }

        private string _lastInfoTickerText = string.Empty;
        public string LastInfoTickerText
        {
            get => _lastInfoTickerText;
            set => SetField(ref _lastInfoTickerText, value);
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        /// <param name="isConnected">PLC 是否已建立通讯。</param>
        private void UpdatePlcStatus(bool isConnected)
        {
            if (isConnected)
            {
                PlcStatusLampBrush = (Brush)FindResource("SuccessGreen");
                PlcStatusText = "PLC: 通讯正常";
            }
            else
            {
                PlcStatusLampBrush = (Brush)FindResource("ErrorRed");
                PlcStatusText = "PLC: 通讯故障";
            }
        }
        private void SwitchUser_Click(object sender, RoutedEventArgs e)
        {
            if (CurrentUserRoleText == "管理员")
            {
                CurrentUserName = "Operator";
                CurrentUserRoleText = "操作员";
                CurrentUserRoleBrush = (Brush)FindResource("TextDim");
            }
            else
            {
                CurrentUserName = "Admin";
                CurrentUserRoleText = "管理员";
                CurrentUserRoleBrush = (Brush)FindResource("AccentBlue");
            }
        }
        private void Logout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("退出登录（模拟）。", "MOGOK", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void SetField<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
