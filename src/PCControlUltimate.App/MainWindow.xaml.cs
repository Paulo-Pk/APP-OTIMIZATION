using System.Windows;
using PCControlUltimate.App.ViewModels;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        DataContext = new MainViewModel();
    }
}