using System.Windows;
using System.Windows.Input;
using Nexus.App.ViewModels;

namespace Nexus.App.Views;

public partial class MiniHudWindow : Window
{
    public MiniHudWindow(DashboardViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;

        Left = SystemParameters.PrimaryScreenWidth - Width - 30;
        Top = 30;
    }

    private void Window_MouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void BtnClose_Click(object sender, RoutedEventArgs e)
    {
        Hide();
    }
}