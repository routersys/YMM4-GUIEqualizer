using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;

namespace ymm4_guiequalizer
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public event EventHandler? CanExecuteChanged
        {
            add { CommandManager.RequerySuggested += value; }
            remove { CommandManager.RequerySuggested -= value; }
        }

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);
        public void Execute(object? parameter) => _execute(parameter);
    }

    internal class EqualizerSettingsViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        public ObservableCollection<string> Presets { get; } = new();

        private string? _selectedPreset;
        public string? SelectedPreset
        {
            get => _selectedPreset;
            set { _selectedPreset = value; OnPropertyChanged(nameof(SelectedPreset)); }
        }

        public ICommand RenameCommand { get; }
        public ICommand DeleteCommand { get; }

        public EqualizerSettingsViewModel()
        {
            LoadPresets();
            PresetManager.PresetsChanged += (s, e) => LoadPresets();

            RenameCommand = new RelayCommand(p => RenamePreset(), p => SelectedPreset != null);
            DeleteCommand = new RelayCommand(p => DeletePreset(), p => SelectedPreset != null);
        }

        private void LoadPresets()
        {
            Application.Current.Dispatcher.Invoke(() => {
                var current = SelectedPreset;
                Presets.Clear();
                PresetManager.GetAllPresetNames().ForEach(Presets.Add);
                if (Presets.Contains(current))
                {
                    SelectedPreset = current;
                }
                else
                {
                    SelectedPreset = null;
                }
            });
        }

        private void RenamePreset()
        {
            if (SelectedPreset == null) return;
            var dialog = new InputDialogWindow("新しいプリセット名", "プリセット名の変更", SelectedPreset);
            dialog.Owner = Application.Current.MainWindow;
            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.InputText;
                if (!string.IsNullOrWhiteSpace(newName) && newName != SelectedPreset)
                {
                    PresetManager.RenamePreset(SelectedPreset, newName);
                }
            }
        }

        private void DeletePreset()
        {
            if (SelectedPreset == null) return;
            if (MessageBox.Show($"プリセット「{SelectedPreset}」を削除しますか？", "確認", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                PresetManager.DeletePreset(SelectedPreset);
            }
        }
    }
}