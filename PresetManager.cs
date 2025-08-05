using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using Newtonsoft.Json;

namespace ymm4_guiequalizer
{
    internal static class PresetManager
    {
        private static readonly string presetsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "presets");
        private static readonly string metadataPath = Path.Combine(presetsDir, "_metadata.json");

        private static readonly JsonSerializerSettings serializerSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            Formatting = Formatting.Indented
        };

        private static Dictionary<string, PresetMetadata> presetMetadata = new();

        public static event EventHandler? PresetsChanged;

        static PresetManager()
        {
            Directory.CreateDirectory(presetsDir);
            LoadMetadata();
        }

        private static void LoadMetadata()
        {
            if (File.Exists(metadataPath))
            {
                try
                {
                    var json = File.ReadAllText(metadataPath);
                    presetMetadata = JsonConvert.DeserializeObject<Dictionary<string, PresetMetadata>>(json) ?? new();
                }
                catch
                {
                    presetMetadata = new();
                }
            }
        }

        private static void SaveMetadata()
        {
            try
            {
                var json = JsonConvert.SerializeObject(presetMetadata, Formatting.Indented);
                File.WriteAllText(metadataPath, json);
            }
            catch { }
        }

        public static List<string> GetAllPresetNames()
        {
            return Directory.GetFiles(presetsDir, "*.json")
                            .Where(f => Path.GetFileName(f) != "_metadata.json")
                            .Select(Path.GetFileNameWithoutExtension)
                            .Where(name => name != null)
                            .OrderBy(name => name)
                            .ToList()!;
        }

        public static PresetInfo GetPresetInfo(string name)
        {
            var metadata = presetMetadata.GetValueOrDefault(name, new PresetMetadata());
            return new PresetInfo
            {
                Name = name,
                Group = metadata.Group,
                IsFavorite = metadata.IsFavorite
            };
        }

        public static bool SavePreset(string name, ObservableCollection<EQBand> bands)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("無効なプリセット名です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            try
            {
                var json = JsonConvert.SerializeObject(bands, serializerSettings);
                File.WriteAllText(Path.Combine(presetsDir, $"{name}.json"), json);

                if (!presetMetadata.ContainsKey(name))
                {
                    presetMetadata[name] = new PresetMetadata();
                }

                SaveMetadata();
                PresetsChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの保存に失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public static ObservableCollection<EQBand>? LoadPreset(string name)
        {
            var filePath = Path.Combine(presetsDir, $"{name}.json");
            if (!File.Exists(filePath))
                return null;

            try
            {
                var json = File.ReadAllText(filePath);
                var bands = JsonConvert.DeserializeObject<ObservableCollection<EQBand>>(json, serializerSettings);
                return bands;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットの読み込みに失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }
        }

        public static void DeletePreset(string name)
        {
            var filePath = Path.Combine(presetsDir, $"{name}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                presetMetadata.Remove(name);
                SaveMetadata();
                PresetsChanged?.Invoke(null, EventArgs.Empty);
            }
        }

        public static bool RenamePreset(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(newName) || newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("無効なプリセット名です。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            var oldPath = Path.Combine(presetsDir, $"{oldName}.json");
            var newPath = Path.Combine(presetsDir, $"{newName}.json");

            if (File.Exists(newPath))
            {
                MessageBox.Show("同じ名前のプリセットが既に存在します。", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }

            if (File.Exists(oldPath))
            {
                File.Move(oldPath, newPath);

                if (presetMetadata.ContainsKey(oldName))
                {
                    presetMetadata[newName] = presetMetadata[oldName];
                    presetMetadata.Remove(oldName);
                    SaveMetadata();
                }

                PresetsChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            return false;
        }

        public static void SetPresetGroup(string name, string group)
        {
            if (!presetMetadata.ContainsKey(name))
            {
                presetMetadata[name] = new PresetMetadata();
            }

            presetMetadata[name].Group = group;
            SaveMetadata();
        }

        public static void SetPresetFavorite(string name, bool isFavorite)
        {
            if (!presetMetadata.ContainsKey(name))
            {
                presetMetadata[name] = new PresetMetadata();
            }

            presetMetadata[name].IsFavorite = isFavorite;
            SaveMetadata();
        }

        public static bool ExportPreset(string name, string exportPath)
        {
            var sourcePath = Path.Combine(presetsDir, $"{name}.json");
            if (!File.Exists(sourcePath))
                return false;

            try
            {
                File.Copy(sourcePath, exportPath, true);
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"プリセットのエクスポートに失敗しました。\n{ex.Message}", "エラー", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }

        public static bool ImportPreset(string importPath, string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(importPath);
                var bands = JsonConvert.DeserializeObject<ObservableCollection<EQBand>>(json, serializerSettings);

                if (bands == null)
                    return false;

                var targetPath = Path.Combine(presetsDir, $"{name}.json");
                File.WriteAllText(targetPath, json);

                if (!presetMetadata.ContainsKey(name))
                {
                    presetMetadata[name] = new PresetMetadata { Group = "other" };
                    SaveMetadata();
                }

                PresetsChanged?.Invoke(null, EventArgs.Empty);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }

    public class PresetInfo
    {
        public string Name { get; set; } = "";
        public string Group { get; set; } = "";
        public bool IsFavorite { get; set; }
    }

    internal class PresetMetadata
    {
        public string Group { get; set; } = "other";
        public bool IsFavorite { get; set; } = false;
    }
}