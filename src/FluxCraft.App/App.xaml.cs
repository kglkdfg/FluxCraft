using System.Net.Http;
using System.Windows;
using FluxCraft.App.ViewModels;
using FluxCraft.App.Views;
using FluxCraft.Application.Abstractions;
using FluxCraft.Infrastructure.Persistence;
using FluxCraft.Infrastructure.Java;
using Microsoft.Extensions.DependencyInjection;

namespace FluxCraft.App;

public partial class App : System.Windows.Application
{
    private ServiceProvider? _services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var registrations = new ServiceCollection();
        registrations.AddSingleton<IInstanceRepository>(_ => new JsonInstanceRepository());
        registrations.AddSingleton(new HttpClient());
        registrations.AddSingleton<IJavaRuntimeInstaller, JavaRuntimeInstaller>();
        registrations.AddSingleton<MainViewModel>();
        registrations.AddSingleton<MainWindow>();
        _services = registrations.BuildServiceProvider();

        try
        {
            await _services.GetRequiredService<MainViewModel>().LoadAsync();
            _services.GetRequiredService<MainWindow>().Show();
        }
        catch (Exception exception)
        {
            MessageBox.Show($"Не удалось загрузить FluxCraft: {exception.Message}",
                "FluxCraft", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _services?.Dispose();
        base.OnExit(e);
    }
}
