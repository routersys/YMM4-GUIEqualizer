using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;

namespace ymm4_guiequalizer
{
    public partial class EqualizerSettingsWindow : Window
    {
        private ObservableCollection<PresetInfo> presetInfos = new();
        private string currentGroupFilter = "";
        private bool isInitialized = false;

        public EqualizerSettingsWindow()
        {
            InitializeComponent();
            Loaded += EqualizerSettingsWindow_Loaded;
            PresetManager.PresetsChanged += (s, e) => LoadPresetList();
        }

        private void EqualizerSettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            isInitialized = true;
            LoadDefaultPresetComboBox();
            LoadPresetList();
        }

        private void LoadDefaultPresetComboBox()
        {
            DefaultPresetComboBox.Items.Clear();
            DefaultPresetComboBox.Items.Add("なし");

            var presets = PresetManager.GetAllPresetNames();
            foreach (var preset in presets)
            {
                DefaultPresetComboBox.Items.Add(preset);
            }

            var currentDefault = EqualizerSettings.Default.DefaultPreset;
            if (string.IsNullOrEmpty(currentDefault))
            {
                DefaultPresetComboBox.SelectedIndex = 0;
            }
            else if (presets.Contains(currentDefault))
            {
                DefaultPresetComboBox.SelectedItem = currentDefault;
            }
            else
            {
                DefaultPresetComboBox.SelectedIndex = 0;
                EqualizerSettings.Default.DefaultPreset = "";
            }
        }

        private void LoadPresetList()
        {
            if (!isInitialized) return;

            presetInfos.Clear();
            var presets = PresetManager.GetAllPresetNames();

            foreach (var preset in presets)
            {
                var info = PresetManager.GetPresetInfo(preset);
                presetInfos.Add(info);
            }

            FilterPresetsByGroup();
        }

        private void FilterPresetsByGroup()
        {
            if (!isInitialized || presetInfos == null) return;

            PresetListBox.ItemsSource = null;

            var filteredPresets = presetInfos.AsEnumerable();

            if (currentGroupFilter == "favorites")
            {
                filteredPresets = filteredPresets.Where(p => p.IsFavorite);
            }
            else if (!string.IsNullOrEmpty(currentGroupFilter))
            {
                filteredPresets = filteredPresets.Where(p => p.Group == currentGroupFilter);
            }

            PresetListBox.ItemsSource = filteredPresets.OrderBy(p => p.Name).ToList();
        }

        private void DefaultPresetComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (DefaultPresetComboBox.SelectedIndex == 0)
            {
                EqualizerSettings.Default.DefaultPreset = "";
            }
            else if (DefaultPresetComboBox.SelectedItem is string preset)
            {
                EqualizerSettings.Default.DefaultPreset = preset;
            }
        }

        private void ClearDefaultPreset_Click(object sender, RoutedEventArgs e)
        {
            DefaultPresetComboBox.SelectedIndex = 0;
            EqualizerSettings.Default.DefaultPreset = "";
        }

        private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!isInitialized) return;

            if (GroupListBox.SelectedItem is ListBoxItem item)
            {
                currentGroupFilter = item.Tag?.ToString() ?? "";
                FilterPresetsByGroup();
            }
        }

        private void PresetListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        private void PresetListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo preset)
            {
                MessageBox.Show($"プリセット '{preset.Name}' の詳細:\nグループ: {preset.Group}\nお気に入り: {(preset.IsFavorite ? "はい" : "いいえ")}",
                    "プリセット詳細", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void FavoriteButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.DataContext is PresetInfo preset)
            {
                preset.IsFavorite = !preset.IsFavorite;
                PresetManager.SetPresetFavorite(preset.Name, preset.IsFavorite);

                // UI更新のため再読み込み
                LoadPresetList();
            }
        }

        private void ImportPreset_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "プリセットファイルを選択",
                Filter = "JSONファイル (*.json)|*.json|すべてのファイル (*.*)|*.*",
                Multiselect = true
            };

            if (dialog.ShowDialog() == true)
            {
                ImportPresetFiles(dialog.FileNames);
            }
        }

        private void ExportPreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo preset)
            {
                var dialog = new SaveFileDialog
                {
                    Title = "プリセットをエクスポート",
                    Filter = "JSONファイル (*.json)|*.json",
                    FileName = $"{preset.Name}.json"
                };

                if (dialog.ShowDialog() == true)
                {
                    if (PresetManager.ExportPreset(preset.Name, dialog.FileName))
                    {
                        MessageBox.Show("プリセットをエクスポートしました。", "完了", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
        }

        private void RenamePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo preset)
            {
                var dialog = new InputDialogWindow("新しいプリセット名を入力してください", "プリセット名の変更", preset.Name);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true)
                {
                    string newName = dialog.InputText;
                    if (!string.IsNullOrWhiteSpace(newName) && newName != preset.Name)
                    {
                        if (PresetManager.RenamePreset(preset.Name, newName))
                        {
                            LoadDefaultPresetComboBox();
                            LoadPresetList();
                        }
                    }
                }
            }
        }

        private void ChangeGroup_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo preset)
            {
                var groups = new[] { "vocal", "bgm", "sfx", "other" };
                var groupNames = new[] { "ボーカル", "BGM", "効果音", "その他" };

                var dialog = new GroupSelectionWindow(groups, groupNames, preset.Group);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true)
                {
                    PresetManager.SetPresetGroup(preset.Name, dialog.SelectedGroup);
                    if (isInitialized)
                    {
                        LoadPresetList();
                    }
                }
            }
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo preset)
            {
                if (MessageBox.Show($"プリセット「{preset.Name}」を削除しますか？", "確認",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    PresetManager.DeletePreset(preset.Name);
                    if (isInitialized)
                    {
                        LoadDefaultPresetComboBox();
                        LoadPresetList();
                    }
                }
            }
        }

        private void NewGroup_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new InputDialogWindow("新しいグループ名を入力してください", "グループの作成");
            dialog.Owner = this;

            if (dialog.ShowDialog() == true)
            {
                string groupName = dialog.InputText;
                if (!string.IsNullOrWhiteSpace(groupName))
                {
                    MessageBox.Show("カスタムグループ機能は将来のバージョンで実装予定です。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
        }

        private void DeleteGroup_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("カスタムグループ削除機能は将来のバージョンで実装予定です。", "情報", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void PresetList_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Any(f => Path.GetExtension(f).ToLower() == ".json"))
                {
                    e.Effects = DragDropEffects.Copy;
                    DropOverlay.Visibility = Visibility.Visible;
                }
                else
                {
                    e.Effects = DragDropEffects.None;
                }
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
        }

        private void PresetList_Drop(object sender, DragEventArgs e)
        {
            DropOverlay.Visibility = Visibility.Collapsed;

            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var jsonFiles = files.Where(f => Path.GetExtension(f).ToLower() == ".json").ToArray();

                if (jsonFiles.Length > 0)
                {
                    ImportPresetFiles(jsonFiles);
                }
            }
        }

        private void ImportPresetFiles(string[] filePaths)
        {
            int successCount = 0;
            int failCount = 0;

            foreach (var filePath in filePaths)
            {
                string fileName = Path.GetFileNameWithoutExtension(filePath);
                if (PresetManager.ImportPreset(filePath, fileName))
                {
                    successCount++;
                }
                else
                {
                    failCount++;
                }
            }

            string message = $"インポート完了\n成功: {successCount}件";
            if (failCount > 0)
            {
                message += $"\n失敗: {failCount}件";
            }

            MessageBox.Show(message, "インポート結果", MessageBoxButton.OK, MessageBoxImage.Information);

            if (isInitialized)
            {
                LoadDefaultPresetComboBox();
                LoadPresetList();
            }
        }
    }
}