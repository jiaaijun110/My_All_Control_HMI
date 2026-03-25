using System.Windows;
using System.Windows.Threading;
using Services.Core;

namespace AppMain
{
    public static class GlobalExceptionHandler
    {
        /// <param name="app">当前应用实例。</param>
        public static void Register(Application app)
        {
            app.DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
            TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
        }

        private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            // 逻辑意图：将 UI 异常标记为已处理，避免界面线程直接退出。
            LoggingDatabaseInitializer.LogError("【全局捕获】UI 线程发生未处理异常", e.Exception);
            e.Handled = true;
            MessageBox.Show("UI线程发生异常，系统已拦截。", "异常保护", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            LoggingDatabaseInitializer.LogError("【全局捕获】非 UI 线程发生未处理异常", e.ExceptionObject as Exception);
            if (e.IsTerminating)
            {
                MessageBox.Show("检测到致命异常，程序即将退出。", "系统错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
        {
            // 逻辑意图：显式标记为已观察，避免 GC 回收阶段再次触发进程级异常。
            LoggingDatabaseInitializer.LogError("【异步】Task 任务中发生未处理异常", e.Exception);
            e.SetObserved();
        }
    }
}

