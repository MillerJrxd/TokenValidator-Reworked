using System.Windows.Controls;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows;
using System.Windows.Threading;
using Panel = System.Windows.Controls.Panel;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using Brushes = System.Windows.Media.Brushes;
using System.Windows.Media.Effects;

namespace TokenValidator.Utils
{
    public static class ThemeManager
    {
        #region Variables
        private static bool _seasonalEffectsEnabled = false;
        private static Canvas? _snowCanvas;
        private static Canvas? _lightsCanvas;
        private static DispatcherTimer? _snowMonitorTimer;
        private static DispatcherTimer? _lightsTimer;
        private static Window? _mainWindow;
        private static readonly List<Button> _modifiedButtons = new();
        private static readonly List<Ellipse> _lightBulbs = new();

        public static bool SeasonalEffectsEnabled
        {
            get => _seasonalEffectsEnabled;
            set
            {
                if (_seasonalEffectsEnabled == value) return;

                _seasonalEffectsEnabled = value;
                if (!value)
                {
                    ClearSeasonalEffects();
                }
                else
                {
                    if (_mainWindow != null)
                    {
                        ApplySeasonalTheme(_mainWindow);
                    }
                }
            }
        }
        #endregion

        #region Initialization/Clearing
        public static void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.IsVisibleChanged += OnWindowVisibilityChanged;

            try
            {
                SeasonalEffectsEnabled = Properties.Settings.Default?.SeasonalEffects ?? false;
            }
            catch 
            {
                SeasonalEffectsEnabled = false;
            }
            SeasonalEffectsEnabled = _seasonalEffectsEnabled;
            UpdateSeasonalEffects();
        }

        public static void ApplySeasonalTheme(Window? window = null)
        {
            if (window == null) return;

            ClearSeasonalEffects();
            
            var today = DateTime.Now;
            
            if (today.Month == 12 && today.Day >= 11 && today.Day <= 30)
            {
                if (Properties.Settings.Default.SeasonalEffects)
                {
                    ApplyWinterTheme(window);
                    AddHolidayLights(window);
                }
            }
        }

        public static void UpdateSeasonalEffects()
        {
            if (Properties.Settings.Default.SeasonalEffects)
            {
                if (_mainWindow != null)
                {
                    ApplySeasonalTheme(_mainWindow);
                }
            }
            else
            {
                ClearSeasonalEffects();
            }
        }

        public static void ClearSeasonalEffects()
        {
            if (_snowCanvas != null)
            {
                if (_snowCanvas.Parent is Panel parent)
                {
                    parent.Children.Remove(_snowCanvas);
                }
                _snowCanvas = null;
            }

            if (_lightsCanvas != null)
            {
                if (_lightsCanvas.Parent is Panel parent)
                {
                    parent.Children.Remove(_lightsCanvas);
                }
                _lightsCanvas = null;
            }

            if (_snowMonitorTimer != null)
            {
                _snowMonitorTimer.Stop();
                _snowMonitorTimer = null;
            }

            if (_lightsTimer != null)
            {
                _lightsTimer.Stop();
                _lightsTimer = null;
            }

            foreach (var button in _modifiedButtons)
            {
                if (button.Content is Grid grid && grid.Children.Count > 0)
                {
                    var originalContent = grid.Children[0];
                    grid.Children.Clear();
                    button.Content = originalContent;
                }
            }
            _modifiedButtons.Clear();
            _lightBulbs.Clear();
            _mainWindow.IsVisibleChanged -= OnWindowVisibilityChanged;
        }

        private static void OnWindowVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (_mainWindow.IsVisible)
            {
                StartAnimations();
            }
            else
            {
                StopAnimations();
            }
        }

        private static void StartAnimations()
        {
            if (_snowMonitorTimer != null && !_snowMonitorTimer.IsEnabled)
                _snowMonitorTimer.Start();

            if (_lightsTimer != null && !_lightsTimer.IsEnabled)
                _lightsTimer.Start();
        }

        private static void StopAnimations()
        {
            _snowMonitorTimer?.Stop();
            _lightsTimer?.Stop();
        }

        #endregion

        #region Winter Theme
        private static void ApplyWinterTheme(Window window)
        {
            ClearSeasonalEffects();

            _snowCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false
            };

            Grid.SetRowSpan(_snowCanvas, 3);
            ((Grid)window.Content).Children.Add(_snowCanvas);

            StartSnowAnimation(window);
        }

        private static void AddHolidayLights(Window window)
        {
            _lightsCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Margin = new Thickness(0, 30, 0, 0)
            };

            Grid.SetRowSpan(_lightsCanvas, 3);
            ((Grid)window.Content).Children.Add(_lightsCanvas);

            int lightCount = 25;
            double spacing = window.ActualWidth / (lightCount + 1);
            Random random = new();

            for (int i = 0; i < lightCount; i++)
            {
                var light = new Ellipse
                {
                    Width = 12,
                    Height = 12,
                    Stroke = Brushes.Gold,
                    StrokeThickness = 1,
                    Fill = GetRandomLightColor(random),
                    Tag = i
                };

                Canvas.SetLeft(light, spacing * (i + 1) - 6);
                Canvas.SetTop(light, 5);
                _lightsCanvas.Children.Add(light);
                _lightBulbs.Add(light);
            }

            var wire = new Polyline
            {
                Points = new PointCollection(),
                Stroke = Brushes.Goldenrod,
                StrokeThickness = 1
            };

            for (int i = 0; i < lightCount; i++)
            {
                wire.Points.Add(new System.Windows.Point(spacing * (i + 1), 11));
            }

            _lightsCanvas.Children.Add(wire);
            Panel.SetZIndex(wire, -1);

            _lightsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _lightsTimer.Tick += (s, e) => AnimateLights(random);
            _lightsTimer.Start();
        }

        private static System.Windows.Media.SolidColorBrush GetRandomLightColor(Random random)
        {
            Color[] colors = {
                Colors.Red,
                Colors.Green,
                Colors.Blue,
                Colors.Yellow,
                Colors.Orange,
                Colors.Purple,
                Colors.Pink,
                Colors.Cyan,
                Colors.Magenta,
                Colors.LightGreen
            };

            return new SolidColorBrush(colors[random.Next(colors.Length)]);
        }

        private static void AnimateLights(Random random)
        {
            foreach (var light in _lightBulbs)
            {
                if (random.NextDouble() > 0.7)
                {
                    light.Fill = GetRandomLightColor(random);
                }

                var glow = new DropShadowEffect
                {
                    Color = ((SolidColorBrush)light.Fill).Color,
                    BlurRadius = 10,
                    ShadowDepth = 0,
                    Opacity = 0.7
                };

                light.Effect = glow;

                var timer = new DispatcherTimer
                {
                    Interval = TimeSpan.FromMilliseconds(200)
                };
                timer.Tick += (s, e) =>
                {
                    light.Effect = null;
                    timer.Stop();
                };
                timer.Start();
            }
        }

        private static void StartSnowAnimation(Window window)
        {
            var random = new Random();
            int snowflakeCount = 70;

            _snowMonitorTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(3)
            };
            _snowMonitorTimer.Tick += (s, e) => MonitorSnowflakes(window);
            _snowMonitorTimer.Start();

            for (int i = 0; i < snowflakeCount; i++)
            {
                CreateSnowflake(window, random, i);
            }
        }

        private static void CreateSnowflake(Window window, Random random, int index)
        {
            if (_snowCanvas == null) return;

            var snowflake = new Ellipse
            {
                Width = 2 + (index % 4),
                Height = 2 + (index % 4),
                Fill = Brushes.White,
                Opacity = 0.5 + (index % 10) * 0.05,
                Tag = index
            };

            _snowCanvas.Children.Add(snowflake);
            AnimateSnowflake(window, snowflake, random);
        }

        private static void AnimateSnowflake(Window window, Ellipse snowflake, Random random)
        {
            double startX = random.Next(0, (int)window.ActualWidth);
            double startY = random.Next(-100, -10);

            Canvas.SetLeft(snowflake, startX);
            Canvas.SetTop(snowflake, startY);

            var fallDuration = TimeSpan.FromSeconds(5 + random.NextDouble() * 10);
            var fallAnimation = new DoubleAnimation
            {
                To = window.ActualHeight + 10,
                Duration = fallDuration,
                FillBehavior = FillBehavior.Stop
            };

            var swayAnimation = new DoubleAnimation
            {
                From = startX,
                To = startX + random.Next(-50, 50),
                Duration = TimeSpan.FromSeconds(2 + random.NextDouble() * 3),
                AutoReverse = true,
                RepeatBehavior = RepeatBehavior.Forever
            };

            fallAnimation.Completed += (s, e) =>
            {
                if (_snowCanvas?.Children.Contains(snowflake) == true)
                {
                    Canvas.SetTop(snowflake, random.Next(-100, -10));
                    Canvas.SetLeft(snowflake, random.Next(0, (int)window.ActualWidth));
                    AnimateSnowflake(window, snowflake, random);
                }
            };

            snowflake.BeginAnimation(Canvas.TopProperty, fallAnimation);
            snowflake.BeginAnimation(Canvas.LeftProperty, swayAnimation);
        }

        private static void MonitorSnowflakes(Window window)
        {
            if (_snowCanvas == null) return;

            var random = new Random();
            foreach (var child in _snowCanvas.Children.OfType<Ellipse>())
            {
                var top = Canvas.GetTop(child);
                if (top < 10)
                {
                    Canvas.SetTop(child, random.Next(-100, -10));
                    Canvas.SetLeft(child, random.Next(0, (int)window.ActualWidth));
                    AnimateSnowflake(window, child, random);
                }
            }
        }
        #endregion
    }
}