using System.Windows;
using System.Windows.Controls;

namespace DlcvTest
{
    public partial class SettingsWindow : Window
    {
        private StandardSettingsView _standardView;
        private VisualParametersSettingsView _visualParamsView;

        public bool StartBatchPredictRequested { get; private set; }

        public SettingsWindow(bool openVisualParams = false)
        {
            InitializeComponent();
            
            // 初始化视图
            _standardView = new StandardSettingsView();
            _visualParamsView = new VisualParametersSettingsView();

            // 默认显示标准视图
            MainContent.Content = _standardView;

            if (openVisualParams)
            {
                btnVisualParams.IsChecked = true;
                _visualParamsView.RefreshSettings();
                MainContent.Content = _visualParamsView;
                btnStartBatchPredictPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void SidebarButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender == btnStandard)
            {
                MainContent.Content = _standardView;
                btnStartBatchPredictPanel.Visibility = Visibility.Visible;
            }
            else if (sender == btnVisualParams)
            {
                // 切换到可视化参数视图时，刷新设置
                _visualParamsView.RefreshSettings();
                MainContent.Content = _visualParamsView;
                btnStartBatchPredictPanel.Visibility = Visibility.Collapsed;
            }
        }

        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                this.Close();
            }
        }

        private void btnStartBatchPredict_Click(object sender, RoutedEventArgs e)
        {
            StartBatchPredictRequested = true;
            Close();
        }
    }
}

