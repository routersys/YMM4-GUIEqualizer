using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using YukkuriMovieMaker.Commons;

namespace ymm4_guiequalizer
{
    public partial class GuiEqualizerControl : UserControl, IPropertyEditorControl
    {
        public static readonly DependencyProperty ItemsSourceProperty =
            DependencyProperty.Register(nameof(ItemsSource), typeof(ObservableCollection<EQBand>), typeof(GuiEqualizerControl), new PropertyMetadata(null, OnItemsSourceChanged));
        public ObservableCollection<EQBand> ItemsSource
        {
            get => (ObservableCollection<EQBand>)GetValue(ItemsSourceProperty);
            set => SetValue(ItemsSourceProperty, value);
        }

        public static readonly DependencyProperty SelectedBandProperty =
            DependencyProperty.Register(nameof(SelectedBand), typeof(EQBand), typeof(GuiEqualizerControl), new PropertyMetadata(null));
        public EQBand? SelectedBand
        {
            get => (EQBand?)GetValue(SelectedBandProperty);
            set => SetValue(SelectedBandProperty, value);
        }

        public static readonly DependencyProperty SelectedPresetNameProperty =
            DependencyProperty.Register(nameof(SelectedPresetName), typeof(string), typeof(GuiEqualizerControl), new PropertyMetadata("プリセットを選択..."));
        public string SelectedPresetName
        {
            get => (string)GetValue(SelectedPresetNameProperty);
            set => SetValue(SelectedPresetNameProperty, value);
        }

        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        private double currentTime = 0;
        private Point lastRightClickPosition;
        private double minFreq = 20, maxFreq = 20000;
        private double minGain = -24, maxGain = 24;
        private bool isDragging = false;
        private bool isLoadingPreset = false;

        private AnimationValue? _targetFreqKeyframe;
        private AnimationValue? _targetGainKeyframe;

        private readonly ObservableCollection<PresetInfo> allPresets = new();
        private string currentGroupFilter = "";

        private readonly SolidColorBrush gridLineBrush = new(Color.FromArgb(100, 80, 80, 80));
        private readonly SolidColorBrush thumbFillBrush = new(Color.FromArgb(200, 60, 150, 255));
        private readonly SolidColorBrush thumbSelectedFillBrush = new(Color.FromArgb(220, 255, 215, 0));
        private readonly SolidColorBrush thumbStrokeBrush = new(Colors.White);
        private readonly SolidColorBrush curveBrush = new(Color.FromArgb(200, 60, 150, 255));
        private readonly SolidColorBrush timelineBrush = new(Color.FromArgb(150, 255, 0, 0));
        private Path? eqCurvePath;

        public GuiEqualizerControl()
        {
            InitializeComponent();
            EditorGrid.DataContext = EqualizerSettings.Default;
            Loaded += GuiEqualizerControl_Loaded;
            Unloaded += GuiEqualizerControl_Unloaded;
        }

        private void GuiEqualizerControl_Loaded(object sender, RoutedEventArgs e)
        {
            PresetManager.PresetsChanged += OnPresetsChanged;
            LoadPresets();
        }

        private void GuiEqualizerControl_Unloaded(object sender, RoutedEventArgs e)
        {
            PresetManager.PresetsChanged -= OnPresetsChanged;
        }

        private void OnPresetsChanged(object? sender, EventArgs e) => LoadPresets();

        private static void OnItemsSourceChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var control = (GuiEqualizerControl)d;
            if (e.OldValue is ObservableCollection<EQBand> oldSource)
            {
                oldSource.CollectionChanged -= control.ItemsSource_CollectionChanged;
                foreach (var band in oldSource) band.PropertyChanged -= control.Band_PropertyChanged;
            }
            if (e.NewValue is ObservableCollection<EQBand> newSource)
            {
                newSource.CollectionChanged += control.ItemsSource_CollectionChanged;
                foreach (var band in newSource) band.PropertyChanged += control.Band_PropertyChanged;
            }
            control.UpdateDefaultSelection();
            control.UpdateTimeSliderRange();
            control.DrawAll();
        }

        private void UpdateDefaultSelection()
        {
            if (SelectedBand is null && ItemsSource?.Any() == true)
            {
                SelectedBand = ItemsSource.OrderBy(b => b.Frequency.Values.FirstOrDefault()?.Value ?? 0).FirstOrDefault();
            }
        }

        private void ItemsSource_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.OldItems != null)
            {
                foreach (var item in e.OldItems.OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged -= Band_PropertyChanged;
                }
            }
            if (e.NewItems != null)
            {
                foreach (var item in e.NewItems.OfType<INotifyPropertyChanged>())
                {
                    item.PropertyChanged += Band_PropertyChanged;
                }
            }
            UpdateBandHeaders();
            UpdateDefaultSelection();
            UpdateTimeSliderRange();
            DrawAll();
        }

        private void Band_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (isDragging || isLoadingPreset) return;

            if (sender is EQBand band && band == SelectedBand)
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    var currentSelection = SelectedBand;
                    if (currentSelection == band)
                    {
                        SelectedBand = null;
                        SelectedBand = currentSelection;
                    }
                }));
            }

            if (e.PropertyName == nameof(EQBand.Frequency) || e.PropertyName == nameof(EQBand.Gain) || e.PropertyName == nameof(EQBand.Q))
            {
                UpdateTimeSliderRange();
            }

            DrawAll();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            DrawAll();
        }

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.IsLoaded)
            {
                maxGain = ZoomSlider.Value;
                minGain = -ZoomSlider.Value;
                DrawAll();
            }
        }

        private void TimeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (this.IsLoaded)
            {
                currentTime = e.NewValue;
                if (!isDragging) DrawAll();
            }
        }

        private void UpdateTimeSliderRange()
        {
            if (ItemsSource == null || !ItemsSource.Any()) return;

            int maxFrames = 0;
            foreach (var band in ItemsSource)
            {
                maxFrames = Math.Max(maxFrames, Math.Max(band.Frequency.Values.Count, Math.Max(band.Gain.Values.Count, band.Q.Values.Count)));
            }

            if (maxFrames > 1)
            {
                TimeSlider.Maximum = 1.0;
                TimeSlider.TickFrequency = 1.0 / (maxFrames - 1);
            }
            else
            {
                TimeSlider.Maximum = 1.0;
                TimeSlider.TickFrequency = 0.1;
            }
        }

        private void SettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var settingsWindow = new EqualizerSettingsWindow
            {
                Owner = Window.GetWindow(this),
                DataContext = EqualizerSettings.Default,
                Topmost = true
            };
            settingsWindow.ShowDialog();
        }

        private void LoadPresets()
        {
            var selectedPresetName = (PresetListBox.SelectedItem as PresetInfo)?.Name;

            allPresets.Clear();
            var presetNames = PresetManager.GetAllPresetNames();
            foreach (var name in presetNames)
            {
                allPresets.Add(PresetManager.GetPresetInfo(name));
            }

            var groupNames = allPresets.Select(p => p.Group).Where(g => !string.IsNullOrEmpty(g)).Distinct().OrderBy(g => g).ToList();
            GroupListBox.Items.Clear();
            GroupListBox.Items.Add(new ListBoxItem { Content = "すべて", Tag = "" });
            GroupListBox.Items.Add(new ListBoxItem { Content = "お気に入り", Tag = "favorites" });

            var groupKeyToName = new Dictionary<string, string>
            {
                {"vocal", "ボーカル"}, {"bgm", "BGM"}, {"sfx", "効果音"}, {"other", "その他"}
            };

            foreach (var group in groupNames)
            {
                GroupListBox.Items.Add(new ListBoxItem { Content = groupKeyToName.GetValueOrDefault(group, group), Tag = group });
            }

            var itemToSelect = GroupListBox.Items.OfType<ListBoxItem>().FirstOrDefault(i => (i.Tag as string) == currentGroupFilter);
            GroupListBox.SelectedItem = itemToSelect ?? GroupListBox.Items[0];

            FilterPresets();

            if (selectedPresetName != null)
            {
                var presetToSelect = PresetListBox.Items.OfType<PresetInfo>().FirstOrDefault(p => p.Name == selectedPresetName);
                if (presetToSelect != null)
                {
                    PresetListBox.SelectedItem = presetToSelect;
                }
            }
        }

        private void GroupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GroupListBox.SelectedItem is ListBoxItem item)
            {
                currentGroupFilter = item.Tag as string ?? "";
                FilterPresets();
            }
        }

        private void FilterPresets()
        {
            IEnumerable<PresetInfo> filtered;
            if (currentGroupFilter == "favorites")
            {
                filtered = allPresets.Where(p => p.IsFavorite);
            }
            else if (!string.IsNullOrEmpty(currentGroupFilter))
            {
                filtered = allPresets.Where(p => p.Group == currentGroupFilter);
            }
            else
            {
                filtered = allPresets;
            }
            PresetListBox.ItemsSource = filtered.OrderBy(p => p.Name);
        }

        private void PresetListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (PresetListBox.SelectedItem is PresetInfo selectedPresetInfo)
            {
                var loadedBands = PresetManager.LoadPreset(selectedPresetInfo.Name);
                if (loadedBands != null && ItemsSource != null)
                {
                    BeginEdit?.Invoke(this, EventArgs.Empty);
                    isLoadingPreset = true;
                    try
                    {
                        ItemsSource.Clear();
                        foreach (var band in loadedBands)
                        {
                            ItemsSource.Add(band);
                        }
                        SelectedPresetName = selectedPresetInfo.Name;
                    }
                    finally
                    {
                        isLoadingPreset = false;
                    }
                    EndEdit?.Invoke(this, EventArgs.Empty);
                }
                PresetPopup.IsOpen = false;
            }
        }

        private void SavePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (ItemsSource == null || ItemsSource.Count == 0)
            {
                MessageBox.Show("保存するバンドがありません。", "エラー", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dialog = new InputDialogWindow("プリセット名を入力してください", "プリセットの保存");
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                string presetName = dialog.InputText;
                if (!string.IsNullOrWhiteSpace(presetName))
                {
                    if (PresetManager.SavePreset(presetName, ItemsSource))
                    {
                        SelectedPresetName = presetName;
                        LoadPresets();
                    }
                }
            }
        }

        private void RenamePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is not PresetInfo selectedPreset) return;

            var dialog = new InputDialogWindow("新しいプリセット名を入力してください", "プリセット名の変更", selectedPreset.Name);
            dialog.Owner = Window.GetWindow(this);

            if (dialog.ShowDialog() == true)
            {
                string newName = dialog.InputText;
                if (!string.IsNullOrWhiteSpace(newName) && newName != selectedPreset.Name)
                {
                    if (PresetManager.RenamePreset(selectedPreset.Name, newName))
                    {
                        if (SelectedPresetName == selectedPreset.Name)
                        {
                            SelectedPresetName = newName;
                        }
                        LoadPresets();
                    }
                }
            }
        }

        private void DeletePresetButton_Click(object sender, RoutedEventArgs e)
        {
            if (PresetListBox.SelectedItem is not PresetInfo selectedPreset) return;

            if (MessageBox.Show($"プリセット「{selectedPreset.Name}」を削除しますか？", "確認",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
            {
                PresetManager.DeletePreset(selectedPreset.Name);
                if (SelectedPresetName == selectedPreset.Name)
                {
                    SelectedPresetName = "プリセットを選択...";
                }
                LoadPresets();
            }
        }

        private void UpdateBandHeaders()
        {
            if (ItemsSource is null) return;
            var sortedBands = ItemsSource.OrderBy(b => b.Frequency.GetValue(0, 1, 60)).ToList();
            for (int i = 0; i < sortedBands.Count; i++)
            {
                sortedBands[i].Header = $"バンド {i + 1}";
            }
        }

        private void DrawAll()
        {
            if (ItemsSource is null || MainCanvas.ActualWidth <= 0 || MainCanvas.ActualHeight <= 0) return;
            MainCanvas.Children.Clear();
            DrawGrid();
            DrawCurve();
            DrawThumbs();
            DrawTimeline();
        }

        private void DrawGrid()
        {
            var freqLabels = new[] { 50, 100, 200, 500, 1000, 2000, 5000, 10000 };
            foreach (var freq in freqLabels)
            {
                double x = FreqToX(freq);
                MainCanvas.Children.Add(new Line { X1 = x, Y1 = 0, X2 = x, Y2 = MainCanvas.ActualHeight, Stroke = gridLineBrush, StrokeThickness = 1 });
                var text = new TextBlock { Text = freq < 1000 ? $"{freq}" : $"{freq / 1000}k", Foreground = gridLineBrush, FontSize = 10, Margin = new Thickness(x + 2, -2, 0, 0) };
                MainCanvas.Children.Add(text);
            }

            int numGainLines = (int)(maxGain / 6);
            for (int i = -numGainLines; i <= numGainLines; i++)
            {
                if (i == 0) continue;
                double gain = i * 6;
                if (Math.Abs(gain) > maxGain) continue;
                double y = GainToY(gain);
                MainCanvas.Children.Add(new Line { X1 = 0, Y1 = y, X2 = MainCanvas.ActualWidth, Y2 = y, Stroke = gridLineBrush, StrokeThickness = 1 });
                var text = new TextBlock { Text = $"{gain:F0}", Foreground = gridLineBrush, FontSize = 10, Margin = new Thickness(2, y, 0, 0) };
                MainCanvas.Children.Add(text);
            }
            double zeroY = GainToY(0);
            MainCanvas.Children.Add(new Line { X1 = 0, Y1 = zeroY, X2 = MainCanvas.ActualWidth, Y2 = zeroY, Stroke = gridLineBrush, StrokeThickness = 1.5 });
            MainCanvas.Children.Add(new TextBlock { Text = "0dB", Foreground = gridLineBrush, FontSize = 10, Margin = new Thickness(2, zeroY - 14, 0, 0) });
        }

        private void DrawTimeline()
        {
            double x = MainCanvas.ActualWidth * currentTime;
            var line = new Line
            {
                X1 = x,
                Y1 = 0,
                X2 = x,
                Y2 = MainCanvas.ActualHeight,
                Stroke = timelineBrush,
                StrokeThickness = 1,
                IsHitTestVisible = false
            };
            Panel.SetZIndex(line, 100);
            MainCanvas.Children.Add(line);
        }

        private void DrawCurve()
        {
            if (ItemsSource is null) return;
            if (eqCurvePath is not null) MainCanvas.Children.Remove(eqCurvePath);

            int maxFrames = 1;
            foreach (var band in ItemsSource)
            {
                maxFrames = Math.Max(maxFrames, Math.Max(band.Frequency.Values.Count, Math.Max(band.Gain.Values.Count, band.Q.Values.Count)));
            }

            int totalFrames = Math.Max(maxFrames, 1000);
            int currentFrame = (int)(totalFrames * currentTime);

            var activeBands = ItemsSource.Where(b => b.IsEnabled).OrderBy(b => b.Frequency.GetValue(currentFrame, totalFrames, 60)).ToList();
            if (activeBands.Count == 0)
            {
                var zeroY = GainToY(0);
                eqCurvePath = new Path { Stroke = curveBrush, StrokeThickness = 2, Data = new LineGeometry(new Point(0, zeroY), new Point(MainCanvas.ActualWidth, zeroY)) };
            }
            else
            {
                var points = activeBands.Select(b => new Point(
                    FreqToX(b.Frequency.GetValue(currentFrame, totalFrames, 60)),
                    GainToY(b.Gain.GetValue(currentFrame, totalFrames, 60))
                )).ToList();

                var firstBand = activeBands.First();
                double startY = firstBand.Type == FilterType.LowShelf ? points.First().Y : GainToY(0);
                points.Insert(0, new Point(0, startY));

                var lastBand = activeBands.Last();
                double endY = lastBand.Type == FilterType.HighShelf ? points.Last().Y : GainToY(0);
                points.Add(new Point(MainCanvas.ActualWidth, endY));

                eqCurvePath = new Path { Stroke = curveBrush, StrokeThickness = 2, Data = CreateSpline(points) };
            }

            Panel.SetZIndex(eqCurvePath, -1);
            MainCanvas.Children.Add(eqCurvePath);
        }

        private void DrawThumbs()
        {
            if (ItemsSource == null) return;

            int maxFrames = 1;
            foreach (var band in ItemsSource)
            {
                maxFrames = Math.Max(maxFrames, Math.Max(band.Frequency.Values.Count, Math.Max(band.Gain.Values.Count, band.Q.Values.Count)));
            }

            int totalFrames = Math.Max(maxFrames, 1000);
            int currentFrame = (int)(totalFrames * currentTime);

            foreach (var band in ItemsSource)
            {
                bool isSelected = band == SelectedBand;
                var thumb = new Thumb { Width = 14, Height = 14, DataContext = band, Template = CreateThumbTemplate(band.IsEnabled ? (isSelected ? thumbSelectedFillBrush : thumbFillBrush) : Brushes.Gray) };

                var freq = band.Frequency.GetValue(currentFrame, totalFrames, 60);
                var gain = band.Gain.GetValue(currentFrame, totalFrames, 60);
                var q = band.Q.GetValue(currentFrame, totalFrames, 60);
                thumb.ToolTip = new TextBlock
                {
                    Inlines =
                    {
                        new System.Windows.Documents.Run("周波数: ") { FontWeight = FontWeights.Bold },
                        new System.Windows.Documents.Run($"{freq:F0} Hz"),
                        new System.Windows.Documents.LineBreak(),
                        new System.Windows.Documents.Run("ゲイン: ") { FontWeight = FontWeights.Bold },
                        new System.Windows.Documents.Run($"{gain:F1} dB"),
                        new System.Windows.Documents.LineBreak(),
                        new System.Windows.Documents.Run("Q: ") { FontWeight = FontWeights.Bold },
                        new System.Windows.Documents.Run($"{q:F2}")
                    }
                };

                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragDelta += Thumb_DragDelta;
                thumb.DragCompleted += Thumb_DragCompleted;

                Canvas.SetLeft(thumb, FreqToX(freq) - thumb.Width / 2);
                Canvas.SetTop(thumb, GainToY(gain) - thumb.Height / 2);
                Panel.SetZIndex(thumb, 1);
                MainCanvas.Children.Add(thumb);
            }
        }

        private void MainCanvas_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            lastRightClickPosition = Mouse.GetPosition(MainCanvas);
            var contextMenu = new ContextMenu();
            var fe = e.Source as FrameworkElement;

            var addItem = new MenuItem { Header = "ポイントを追加" };
            addItem.Click += AddPoint_Click;
            contextMenu.Items.Add(addItem);

            var deleteItem = new MenuItem { Header = "ポイントを削除" };
            if (fe?.DataContext is EQBand band)
            {
                deleteItem.DataContext = band;
                deleteItem.Click += DeletePoint_Click;
            }
            else
            {
                deleteItem.IsEnabled = false;
            }
            contextMenu.Items.Add(deleteItem);

            if (sender is FrameworkElement fwElement)
            {
                fwElement.ContextMenu = contextMenu;
            }
        }

        private void AddPoint_Click(object sender, RoutedEventArgs e)
        {
            BeginEdit?.Invoke(this, EventArgs.Empty);
            var newBand = new EQBand(true, FilterType.Peak, XToFreq(lastRightClickPosition.X), YToGain(lastRightClickPosition.Y), 1.0, StereoMode.Stereo, "");
            ItemsSource.Add(newBand);
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private void DeletePoint_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as MenuItem)?.DataContext is EQBand band)
            {
                BeginEdit?.Invoke(this, EventArgs.Empty);
                ItemsSource.Remove(band);
                EndEdit?.Invoke(this, EventArgs.Empty);
            }
        }

        private void Thumb_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (sender is Thumb thumb && thumb.DataContext is EQBand band)
            {
                SelectedBand = band;
                isDragging = true;

                _targetFreqKeyframe = GetTargetKeyframe(band.Frequency);
                _targetGainKeyframe = GetTargetKeyframe(band.Gain);

                double startX = FreqToX(_targetFreqKeyframe.Value);
                double startY = GainToY(_targetGainKeyframe.Value);
                Canvas.SetLeft(thumb, startX - thumb.Width / 2);
                Canvas.SetTop(thumb, startY - thumb.Height / 2);

                BeginEdit?.Invoke(this, EventArgs.Empty);
            }
        }

        private AnimationValue GetTargetKeyframe(Animation animation)
        {
            if (animation.Values.Count <= 1)
            {
                return animation.Values.First();
            }

            if (animation.Values.Count == 2)
            {
                return currentTime < 0.5 ? animation.Values.First() : animation.Values.Last();
            }
            else
            {
                int targetIndex = (int)Math.Round(currentTime * (animation.Values.Count - 1));
                targetIndex = Math.Clamp(targetIndex, 0, animation.Values.Count - 1);
                return animation.Values[targetIndex];
            }
        }

        private void Thumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            if (sender is Thumb thumb && thumb.DataContext is EQBand band && band.IsEnabled && _targetFreqKeyframe != null && _targetGainKeyframe != null)
            {
                double newX = Canvas.GetLeft(thumb) + e.HorizontalChange;
                double newY = Canvas.GetTop(thumb) + e.VerticalChange;

                newX = Math.Clamp(newX, 0, MainCanvas.ActualWidth - thumb.Width);
                newY = Math.Clamp(newY, 0, MainCanvas.ActualHeight - thumb.Height);

                Canvas.SetLeft(thumb, newX);
                Canvas.SetTop(thumb, newY);

                _targetFreqKeyframe.Value = XToFreq(newX + thumb.Width / 2);
                _targetGainKeyframe.Value = YToGain(newY + thumb.Height / 2);

                DrawCurve();
            }
        }

        private void Thumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            isDragging = false;
            _targetFreqKeyframe = null;
            _targetGainKeyframe = null;
            DrawAll();
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private double FreqToX(double freq) => MainCanvas.ActualWidth * (Math.Log10(freq / minFreq) / Math.Log10(maxFreq / minFreq));
        private double XToFreq(double x) => minFreq * Math.Pow(maxFreq / minFreq, x / MainCanvas.ActualWidth);
        private double GainToY(double gain) => MainCanvas.ActualHeight * (1 - (gain - minGain) / (maxGain - minGain));
        private double YToGain(double y) => (1 - y / MainCanvas.ActualHeight) * (maxGain - minGain) + minGain;

        private ControlTemplate CreateThumbTemplate(Brush fill)
        {
            var factory = new FrameworkElementFactory(typeof(Ellipse));
            factory.SetValue(Shape.FillProperty, fill);
            factory.SetValue(Shape.StrokeProperty, thumbStrokeBrush);
            factory.SetValue(Shape.StrokeThicknessProperty, 1.5);
            return new ControlTemplate(typeof(Thumb)) { VisualTree = factory };
        }

        public static PathGeometry CreateSpline(List<Point> points, double tension = 0.5)
        {
            if (points == null || points.Count < 2) return new PathGeometry();
            var pathFigure = new PathFigure { StartPoint = points[0] };
            var polyBezierSegment = new PolyBezierSegment();
            double controlScale = tension / 0.5 * 0.175;
            for (int i = 0; i < points.Count - 1; i++)
            {
                Point p0 = i == 0 ? points[i] : points[i - 1];
                Point p1 = points[i];
                Point p2 = points[i + 1];
                Point p3 = i == points.Count - 2 ? points[i + 1] : points[i + 2];
                double cp1X = p1.X + (p2.X - p0.X) * controlScale;
                double cp1Y = p1.Y + (p2.Y - p0.Y) * controlScale;
                double cp2X = p2.X - (p3.X - p1.X) * controlScale;
                double cp2Y = p2.Y - (p3.Y - p1.Y) * controlScale;
                polyBezierSegment.Points.Add(new Point(cp1X, cp1Y));
                polyBezierSegment.Points.Add(new Point(cp2X, cp2Y));
                polyBezierSegment.Points.Add(p2);
            }
            pathFigure.Segments.Add(polyBezierSegment);
            return new PathGeometry(new[] { pathFigure });
        }

        private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e) => BeginEdit?.Invoke(this, EventArgs.Empty);
        private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
        {
            var newHeight = EditorGrid.ActualHeight + e.VerticalChange;
            EqualizerSettings.Default.EditorHeight = Math.Clamp(newHeight, 150, 600);
        }
        private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            EqualizerSettings.Default.Save();
            EndEdit?.Invoke(this, EventArgs.Empty);
        }

        private void Band_BeginEdit(object? sender, EventArgs e) => BeginEdit?.Invoke(this, EventArgs.Empty);
        private void Band_EndEdit(object? sender, EventArgs e) => EndEdit?.Invoke(this, EventArgs.Empty);
    }
}