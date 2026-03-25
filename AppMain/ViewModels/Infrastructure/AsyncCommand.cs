using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AppMain.ViewModels.Infrastructure
{
    public sealed class AsyncCommand : ICommand
    {
        private readonly Func<Task> _executeAsync;
        /// <param name="executeAsync">执行委托。</param>
        public AsyncCommand(Func<Task> executeAsync)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        }
        public event EventHandler? CanExecuteChanged;
        /// <param name="parameter">命令参数。</param>
        /// <returns>恒为 true。</returns>
        public bool CanExecute(object? parameter) => true;
        /// <param name="parameter">命令参数。</param>
        public async void Execute(object? parameter)
        {
            try
            {
                await _executeAsync().ConfigureAwait(false);
            }
            catch
            {
                // 由上层 ViewModel 负责日志与状态处理。
            }
        }
    }
    /// <typeparam name="T">参数类型。</typeparam>
    public sealed class AsyncCommand<T> : ICommand
    {
        private readonly Func<T, Task> _executeAsync;
        /// <param name="executeAsync">执行委托。</param>
        public AsyncCommand(Func<T, Task> executeAsync)
        {
            _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        }
        public event EventHandler? CanExecuteChanged;
        /// <param name="parameter">命令参数。</param>
        /// <returns>恒为 true。</returns>
        public bool CanExecute(object? parameter) => true;
        /// <param name="parameter">命令参数。</param>
        public async void Execute(object? parameter)
        {
            try
            {
                T value;
                if (parameter is T direct)
                {
                    value = direct;
                }
                else if (parameter == null)
                {
                    value = default!;
                }
                else
                {
                    value = (T)Convert.ChangeType(parameter, typeof(T));
                }

                await _executeAsync(value).ConfigureAwait(false);
            }
            catch
            {
                // 由上层 ViewModel 负责日志与状态处理。
            }
        }
    }
}


