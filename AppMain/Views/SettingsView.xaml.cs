using AppMain.ViewModels;
using System.Windows;
using System.Windows.Controls;

namespace AppMain.Views
{
    public partial class SettingsView : UserControl
    {
        private SettingsViewModel? _viewModel;

        public SettingsView()
        {
            InitializeComponent();

            _viewModel = new SettingsViewModel();
            DataContext = _viewModel;
            Unloaded += SettingsView_OnUnloaded;
        }

        private void SettingsView_OnUnloaded(object sender, RoutedEventArgs e)
        {
            Unloaded -= SettingsView_OnUnloaded;
            _viewModel?.Dispose();
            _viewModel = null;
        }
    }
}
