using System.Windows;

namespace Onyx;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    void Minimise(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    void CloseWindow(object sender, RoutedEventArgs e) => Close();
}
