using System.Windows;
using FluxCraft.App.ViewModels;

namespace FluxCraft.App.Views;

public partial class MainWindow : Window
{
    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
