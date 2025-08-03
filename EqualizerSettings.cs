using YukkuriMovieMaker.Plugin;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace ymm4_guiequalizer
{
    public class EqualizerSettings : SettingsBase<EqualizerSettings>
    {
        private static readonly string settingsDir = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "settings");
        private static readonly string sizeSettingsPath = Path.Combine(settingsDir, "size.json");

        public override string Name => "GUIイコライザー設定";
        public override SettingsCategory Category => SettingsCategory.Voice;
        public override bool HasSettingView => true;
        public override object SettingView => new EqualizerSettingsWindow
        {
            DataContext = this
        };

        public bool HighQualityMode { get => highQualityMode; set => Set(ref highQualityMode, value); }
        private bool highQualityMode = false;

        public double EditorHeight { get => editorHeight; set => Set(ref editorHeight, value); }
        private double editorHeight = 200;

        public override void Initialize()
        {
            Load();
        }

        public void Load()
        {
            if (!File.Exists(sizeSettingsPath)) return;
            try
            {
                var json = File.ReadAllText(sizeSettingsPath);
                var settings = JsonSerializer.Deserialize<JsonSettings>(json);
                if (settings != null)
                {
                    this.EditorHeight = settings.EditorHeight;
                }
            }
            catch { }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(settingsDir);
                var settings = new JsonSettings { EditorHeight = this.EditorHeight };
                var json = JsonSerializer.Serialize(settings);
                File.WriteAllText(sizeSettingsPath, json);
            }
            catch { }
        }
    }

    internal class JsonSettings
    {
        public double EditorHeight { get; set; }
    }
}