using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using Services.Core;

namespace AppMain.Views
{
    public partial class LogView : UserControl
    {
        private const int MaxRealtimeRows = 500;
        private const int PageSize = 200;

        private readonly LogHistoryQueryService _historyQuery = new();
        private readonly Action<LogEventDto> _realtimeHandler;

        private bool _liveMode = true;
        private int _pageIndex;
        private int _totalHistoryCount;
        public LogView()
        {
            InitializeComponent();
            _realtimeHandler = OnRealtimeLog;
            GridLogs.ItemsSource = Rows;
        }
        public ObservableCollection<LogGridRow> Rows { get; } = new();

        private void InitDefaults()
        {
            var today = DateTime.Today;
            DpStart.SelectedDate = today;
            DpEnd.SelectedDate = today;
        }

        private void LogView_OnLoaded(object sender, RoutedEventArgs e)
        {
            InitDefaults();
            _liveMode = true;
            _pageIndex = 0;
            LoggingDatabaseInitializer.RealtimeLogAppended += _realtimeHandler;
            TbPageInfo.Text = "实时模式：新日志显示在顶部";
            BtnPrev.IsEnabled = false;
            BtnNext.IsEnabled = false;
        }

        private void LogView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            LoggingDatabaseInitializer.RealtimeLogAppended -= _realtimeHandler;
        }
        private void OnRealtimeLog(LogEventDto dto)
        {
            if (!_liveMode)
            {
                return;
            }

            // 逻辑意图：从后台线程回到 UI 线程更新 ObservableCollection，避免跨线程绑定异常。
            Dispatcher.BeginInvoke(() =>
            {
                Rows.Insert(0, LogGridRow.FromDto(dto));
                TrimRealtime();
            });
        }

        private void TrimRealtime()
        {
            while (Rows.Count > MaxRealtimeRows)
            {
                Rows.RemoveAt(Rows.Count - 1);
            }
        }

        private void BtnLive_OnClick(object sender, RoutedEventArgs e)
        {
            _liveMode = true;
            _pageIndex = 0;
            Rows.Clear();
            TbPageInfo.Text = "实时模式：新日志显示在顶部";
            BtnPrev.IsEnabled = false;
            BtnNext.IsEnabled = false;
        }

        private async void BtnQuery_OnClick(object sender, RoutedEventArgs e)
        {
            try
            {
                _liveMode = false;
                _pageIndex = 0;
                await RunHistoryQueryAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LoggingDatabaseInitializer.LogError("历史日志查询失败", ex);
            }
        }

        private async void BtnPrev_OnClick(object sender, RoutedEventArgs e)
        {
            if (_pageIndex <= 0)
            {
                return;
            }

            _pageIndex--;
            await RunHistoryQueryAsync().ConfigureAwait(true);
        }

        private async void BtnNext_OnClick(object sender, RoutedEventArgs e)
        {
            var maxPage = Math.Max(0, (_totalHistoryCount - 1) / PageSize);
            if (_pageIndex >= maxPage)
            {
                return;
            }

            _pageIndex++;
            await RunHistoryQueryAsync().ConfigureAwait(true);
        }
        private async Task RunHistoryQueryAsync()
        {
            BtnQuery.IsEnabled = false;
            try
            {
                var start = DpStart.SelectedDate?.Date ?? DateTime.Today;
                var endDay = DpEnd.SelectedDate?.Date ?? DateTime.Today;
                var end = endDay.Date.AddDays(1).AddTicks(-1);

                var levelItem = (CbLevel.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "All";
                var param = new LogHistoryQueryService.QueryParameter
                {
                    StartInclusive = start,
                    EndInclusive = end,
                    LevelFilter = levelItem,
                    Keyword = string.IsNullOrWhiteSpace(TbKeyword.Text) ? null : TbKeyword.Text,
                    PageIndex = _pageIndex,
                    PageSize = PageSize
                };

                // 逻辑意图：历史查询在 Task 中执行，避免阻塞 UI 线程。
                var sqlite = await LogHistoryQueryService.TryQuerySqliteAsync(param).ConfigureAwait(true);
                LogHistoryQueryService.QueryResult result;
                if (sqlite != null)
                {
                    result = sqlite;
                }
                else
                {
                    result = await _historyQuery.QueryAsync(param).ConfigureAwait(true);
                }

                _totalHistoryCount = result.TotalCount;
                Rows.Clear();
                foreach (var dto in result.Rows)
                {
                    Rows.Add(LogGridRow.FromDto(dto));
                }

                var maxPage = Math.Max(0, (_totalHistoryCount - 1) / PageSize);
                TbPageInfo.Text = $"历史查询 第 {_pageIndex + 1} / {maxPage + 1} 页，共 {_totalHistoryCount} 条";
                BtnPrev.IsEnabled = _pageIndex > 0;
                BtnNext.IsEnabled = _pageIndex < maxPage;
            }
            finally
            {
                BtnQuery.IsEnabled = true;
            }
        }

        private async void BtnExport_OnClick(object sender, RoutedEventArgs e)
        {
            var dlg = new SaveFileDialog
            {
                Filter = "CSV (*.csv)|*.csv",
                FileName = $"logs-{DateTime.Now:yyyyMMdd-HHmmss}.csv"
            };

            if (dlg.ShowDialog() != true)
            {
                return;
            }

            BtnExport.IsEnabled = false;
            try
            {
                // 逻辑意图：导出 IO 走异步，避免大文件写入时界面无响应。
                await Task.Run(() =>
                {
                    var sb = new StringBuilder();
                    sb.AppendLine("Time,Level,Source,Content");
                    foreach (var row in Rows)
                    {
                        static string Esc(string? s)
                        {
                            if (string.IsNullOrEmpty(s))
                            {
                                return "\"\"";
                            }

                            var t = s.Replace("\"", "\"\"");
                            return $"\"{t}\"";
                        }

                        sb.AppendLine($"{Esc(row.TimeText)},{Esc(row.Level)},{Esc(row.Source)},{Esc(row.Content)}");
                    }

                    File.WriteAllText(dlg.FileName, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                }).ConfigureAwait(true);
            }
            finally
            {
                BtnExport.IsEnabled = true;
            }
        }
    }
    public sealed class LogGridRow
    {
        public string TimeText { get; init; } = string.Empty;
        public string Level { get; init; } = string.Empty;
        public string Source { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        /// <param name="dto">日志 DTO。</param>
        /// <returns>网格行。</returns>
        public static LogGridRow FromDto(LogEventDto dto)
        {
            return new LogGridRow
            {
                TimeText = dto.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                Level = dto.Level,
                Source = dto.Source,
                Content = dto.Message
            };
        }
    }
}

