using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxCraft.Application.Abstractions;
using FluxCraft.Domain.Instances;

namespace FluxCraft.App.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IInstanceRepository _repository;
    private readonly IJavaRuntimeInstaller _javaRuntimeInstaller;

    public ObservableCollection<GameInstance> Instances { get; } = [];

    [ObservableProperty] private GameInstance? selectedInstance;
    [ObservableProperty] private string newInstanceName = "Моя сборка";
    [ObservableProperty] private string newMinecraftVersion = "latest-release";
    [ObservableProperty] private string newLoader = "Vanilla";
    [ObservableProperty] private bool isCreatePanelVisible;
    [ObservableProperty] private string statusMessage = "Готово к работе";
    [ObservableProperty] private bool isBusy;
    [ObservableProperty] private double javaInstallProgress;

    public bool IsJavaInstallEnabled => !IsBusy && SelectedInstance is not null &&
        string.Equals(SelectedInstance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase);

    public Visibility CreatePanelVisibility => IsCreatePanelVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility SelectedPanelVisibility => SelectedInstance is not null && !IsCreatePanelVisible ? Visibility.Visible : Visibility.Collapsed;
    public Visibility EmptyStateVisibility => Instances.Count == 0 && !IsCreatePanelVisible ? Visibility.Visible : Visibility.Collapsed;

    public MainViewModel(IInstanceRepository repository, IJavaRuntimeInstaller javaRuntimeInstaller)
    {
        _repository = repository;
        _javaRuntimeInstaller = javaRuntimeInstaller;
    }

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var loaded = await _repository.GetAllAsync(cancellationToken);
        Instances.Clear();
        foreach (var instance in loaded.OrderBy(x => x.Name))
            Instances.Add(instance);
        SelectedInstance = Instances.FirstOrDefault();
        RefreshVisibility();
        StatusMessage = $"Загружено сборок: {Instances.Count}";
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)
    {
        RefreshVisibility();
        OnPropertyChanged(nameof(IsJavaInstallEnabled));
        InstallJavaCommand.NotifyCanExecuteChanged();
    }

    partial void OnIsCreatePanelVisibleChanged(bool value) => RefreshVisibility();

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsJavaInstallEnabled));
        InstallJavaCommand.NotifyCanExecuteChanged();
    }

    private void RefreshVisibility()
    {
        OnPropertyChanged(nameof(CreatePanelVisibility));
        OnPropertyChanged(nameof(SelectedPanelVisibility));
        OnPropertyChanged(nameof(EmptyStateVisibility));
    }

    [RelayCommand]
    private void ToggleCreatePanel()
    {
        IsCreatePanelVisible = !IsCreatePanelVisible;
        if (IsCreatePanelVisible)
        {
            NewInstanceName = $"Новая сборка {Instances.Count + 1}";
            StatusMessage = "Укажи имя и версию новой сборки";
        }
    }

    [RelayCommand]
    private async Task CreateInstanceAsync()
    {
        var name = NewInstanceName.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            StatusMessage = "Введи название сборки";
            return;
        }
        if (Instances.Any(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase)))
        {
            StatusMessage = "Сборка с таким названием уже существует";
            return;
        }

        var instance = new GameInstance { Name = name, MinecraftVersion = NewMinecraftVersion, Loader = NewLoader };
        try
        {
            await _repository.SaveAsync(instance);
            Instances.Add(instance);
            SelectedInstance = instance;
            IsCreatePanelVisible = false;
            StatusMessage = $"Сборка «{instance.Name}» создана";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось сохранить сборку: {exception.Message}";
        }
    }

    [RelayCommand(CanExecute = nameof(CanInstallJava))]
    private async Task InstallJavaAsync(CancellationToken cancellationToken)
    {
        if (SelectedInstance is null || !string.Equals(SelectedInstance.Loader, "Vanilla", StringComparison.OrdinalIgnoreCase))
            return;

        IsBusy = true;
        JavaInstallProgress = 0;
        StatusMessage = $"Определяем требуемую Java для Minecraft {SelectedInstance.MinecraftVersion}…";
        try
        {
            var progress = new Progress<double>(value =>
            {
                JavaInstallProgress = value;
                StatusMessage = $"Установка Java: {value:0}%";
            });
            var installation = await _javaRuntimeInstaller.EnsureInstalledAsync(
                SelectedInstance.MinecraftVersion, progress, cancellationToken);
            SelectedInstance.JavaExecutablePath = installation.ExecutablePath;
            await _repository.SaveAsync(SelectedInstance, cancellationToken);
            StatusMessage = $"Java {installation.MajorVersion} готова для Minecraft {SelectedInstance.MinecraftVersion}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Установка Java отменена";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Ошибка установки Java: {exception.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanInstallJava() => IsJavaInstallEnabled;

    [RelayCommand]
    private async Task DeleteInstanceAsync()
    {
        if (SelectedInstance is null) return;
        var instance = SelectedInstance;
        try
        {
            await _repository.DeleteAsync(instance.Id);
            Instances.Remove(instance);
            SelectedInstance = Instances.FirstOrDefault();
            StatusMessage = $"Сборка «{instance.Name}» удалена";
        }
        catch (Exception exception)
        {
            StatusMessage = $"Не удалось удалить сборку: {exception.Message}";
        }
    }
}
