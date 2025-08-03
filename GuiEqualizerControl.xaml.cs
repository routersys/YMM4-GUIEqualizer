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
        public EQBand SelectedBand
        {
            get => (EQBand)GetValue(SelectedBandProperty);
            set => SetValue(SelectedBandProperty, value);
        }

        public event EventHandler? BeginEdit;
        public event EventHandler? EndEdit;

        private double currentTime = 0;
        private Point lastRightClickPosition;
        private double minFreq = 20, maxFreq = 20000;
        private double minGain = -24, maxGain = 24;
        private bool isDragging = false;

        private AnimationValue? _targetFreqKeyframe;
        private AnimationValue? _targetGainKeyframe;

        private readonly SolidColorBrush gridLineBrush = new(Color.FromArgb(100, 80, 80, 80));
        private readonly SolidColorBrush thumbFillBrush = new(Color.FromArgb(200, 60, 150, 255));
        private readonly SolidColorBrush thumbSelectedFillBrush = new(Color.FromArgb(220, 255, 215, 0));
        private readonly SolidColorBrush thumbStrokeBrush = new(Colors.White);
        private readonly SolidColorBrush curveBrush = new(Color.FromArgb(200, 60, 150, 255));
        private readonly SolidColorBrush timelineBrush = new(Color.FromArgb(150, 255, 0, 0));
        private Path? eqCurvePath;

        public GuiEqualizerControl() => InitializeComponent();

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
            if (e.OldItems != null) foreach (INotifyPropertyChanged item in e.OldItems) item.PropertyChanged -= Band_PropertyChanged;
            if (e.NewItems != null) foreach (INotifyPropertyChanged item in e.NewItems) item.PropertyChanged += Band_PropertyChanged;
            UpdateBandHeaders();
            UpdateDefaultSelection();
            DrawAll();
        }

        private void Band_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (isDragging) return;

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

            const int totalFrames = 1000;
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
            const int totalFrames = 1000;
            int currentFrame = (int)(totalFrames * currentTime);

            if (ItemsSource == null) return;

            foreach (var band in ItemsSource)
            {
                bool isSelected = band == SelectedBand;
                var thumb = new Thumb { Width = 14, Height = 14, DataContext = band, Template = CreateThumbTemplate(band.IsEnabled ? (isSelected ? thumbSelectedFillBrush : thumbFillBrush) : Brushes.Gray) };
                thumb.DragStarted += Thumb_DragStarted;
                thumb.DragDelta += Thumb_DragDelta;
                thumb.DragCompleted += Thumb_DragCompleted;

                Canvas.SetLeft(thumb, FreqToX(band.Frequency.GetValue(currentFrame, totalFrames, 60)) - thumb.Width / 2);
                Canvas.SetTop(thumb, GainToY(band.Gain.GetValue(currentFrame, totalFrames, 60)) - thumb.Height / 2);
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

            if (currentTime < 0.5)
            {
                return animation.Values.First();
            }
            else
            {
                return animation.Values.Last();
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