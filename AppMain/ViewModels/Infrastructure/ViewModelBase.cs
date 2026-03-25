using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AppMain.ViewModels.Infrastructure
{
    public abstract class ViewModelBase : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        /// <typeparam name="T">属性类型。</typeparam>
        /// <param name="field">后备字段。</param>
        /// <param name="value">新值。</param>
        /// <param name="name">属性名（由编译器自动填充）。</param>
        /// <returns>若值变化返回 <c>true</c>，否则 <c>false</c>。</returns>
        protected bool SetProperty<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return false;
            }

            field = value;
            RaisePropertyChanged(name);
            return true;
        }
        /// <param name="propertyName">属性名。</param>
        protected void RaisePropertyChanged(string? propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}


