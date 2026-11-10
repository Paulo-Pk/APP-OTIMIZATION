using System.Windows;
using PCControlUltimate.App.ViewModels;

namespace PCControlUltimate.App;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    public MainWindow() : this(new MainViewModel())
    {
    }
}