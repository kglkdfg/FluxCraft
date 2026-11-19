using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using FluxCraft.Application.Abstractions;
using FluxCraft.Domain.Instances;
using FluxCraft.Application.Abstractions;

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
        foreach (var instance in loaded.OrderBy(x => x.Name)) Instances.Add(instance);
        SelectedInstance = Instances.FirstOrDefault();
        RefreshVisibility();
        StatusMessage = $"Загружено сборок: {Instances.Count}";
    }

    partial void OnSelectedInstanceChanged(GameInstance? value)\n    {\n        RefreshVisibility();\n        InstallJavaCommand.NotifyCanExecuteChanged();\n    }
    partial void OnIsCreatePanelVisibleChanged(bool value) => RefreshVisibility();

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
        catch (Exception exception) { StatusMessage = $"Не удалось сохранить сборку: {exception.Message}"; }
    }

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
        catch (Exception exception) { StatusMessage = $"Не удалось удалить сборку: {exception.Message}"; }
    }
}
