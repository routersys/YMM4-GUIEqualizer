using System.Collections.ObjectModel;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Windows;
using System.Windows.Data;
using YukkuriMovieMaker.Commons;
using YukkuriMovieMaker.Controls;
using YukkuriMovieMaker.Exo;
using YukkuriMovieMaker.Player.Audio.Effects;
using YukkuriMovieMaker.Plugin.Effects;
using YukkuriMovieMaker.Views.Converters;

namespace ymm4_guiequalizer
{
    [AudioEffect("GUIイコライザー", ["エフェクト"], [])]
    public class GuiEqualizerEffect : AudioEffectBase
    {
        public override string Label => "GUIイコライザー";

        [Display(Name = "バンド設定")]
        [GuiEqualizerEditor(PropertyEditorSize = PropertyEditorSize.FullWidth)]
        public ObservableCollection<EQBand> Bands { get; } = new()
        {
            new(true, FilterType.Peak, 500, 0, 1.0, StereoMode.Stereo, "バンド 1"),
        };

        public override IAudioEffectProcessor CreateAudioEffect(TimeSpan duration)
        {
            return new GuiEqualizerProcessor(this, duration);
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => Bands;
        public override IEnumerable<string> CreateExoAudioFilters(int keyFrameIndex, ExoOutputDescription exoOutputDescription) => [];
    }

    public class EQBand : Animatable
    {
        string header = "";
        [Display(AutoGenerateField = true)]
        public string Header { get => header; set => Set(ref header, value); }

        bool isEnabled;
        [Display(Name = "有効")]
        public bool IsEnabled { get => isEnabled; set => Set(ref isEnabled, value); }

        FilterType type;
        [Display(Name = "種類")]
        public FilterType Type
        {
            get => type;
            set
            {
                if (Set(ref type, value))
                {
                    OnPropertyChanged(nameof(Gain));
                }
            }
        }

        StereoMode stereoMode;
        [Display(Name = "チャンネル")]
        public StereoMode StereoMode { get => stereoMode; set => Set(ref stereoMode, value); }

        [Display(Name = "周波数")]
        [AnimationSlider("F0", "Hz", 20, 20000)]
        public Animation Frequency { get; }

        [Display(Name = "ゲイン")]
        [AnimationSlider("F1", "dB", -48, 48)]
        public Animation Gain { get; }

        [Display(Name = "Q")]
        [AnimationSlider("F2", "", 0.1, 18)]
        public Animation Q { get; }

        public EQBand() : this(true, FilterType.Peak, 1000, 0, 1, StereoMode.Stereo, "バンド") { }

        public EQBand(bool enabled, FilterType type, double freq, double gain, double q, StereoMode mode, string header)
        {
            IsEnabled = enabled;
            Type = type;
            StereoMode = mode;
            Header = header;
            Frequency = new(freq, 20, 20000);
            Gain = new(gain, -48, 48);
            Q = new(q, 0.1, 18);

            Frequency.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Frequency));
            Gain.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Gain));
            Q.PropertyChanged += (s, e) => OnPropertyChanged(nameof(Q));
        }

        protected override IEnumerable<IAnimatable> GetAnimatables() => [Frequency, Gain, Q];
    }

    public enum FilterType { [Display(Name = "ピーク")] Peak, [Display(Name = "ローシェルフ")] LowShelf, [Display(Name = "ハイシェルフ")] HighShelf, }
    public enum StereoMode { [Display(Name = "ステレオ")] Stereo, [Display(Name = "L (左)")] Left, [Display(Name = "R (右)")] Right, }

    internal class GuiEqualizerEditorAttribute : PropertyEditorAttribute2
    {
        public override FrameworkElement Create() => new GuiEqualizerControl();
        public override void SetBindings(FrameworkElement control, ItemProperty[] itemProperties)
        {
            var editor = (GuiEqualizerControl)control;
            editor.SetBinding(GuiEqualizerControl.ItemsSourceProperty, ItemPropertiesBinding.Create(itemProperties));
        }
        public override void ClearBindings(FrameworkElement control) => BindingOperations.ClearBinding(control, GuiEqualizerControl.ItemsSourceProperty);
    }
}