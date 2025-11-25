using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Color = System.Windows.Media.Color;
using Panel = System.Windows.Controls.Panel;

namespace TokenValidator.Utils
{
    public static class ThemeManager
    {
        #region Variables
        private static bool _seasonalEffectsEnabled = true;
        private static Canvas? _snowCanvas;
        private static Canvas? _lightsCanvas;
        private static DispatcherTimer? _animationTimer;
        private static DispatcherTimer? _lightsTimer;
        private static Window? _mainWindow;
        private static readonly List<Button> _modifiedButtons = new();
        private static readonly List<Ellipse> _lightBulbs = new();
        private static readonly List<Snowflake> _snowflakes = new();
        private static readonly Random _random = new();

        public static bool SeasonalEffectsEnabled
        {
            get => _seasonalEffectsEnabled;
            private set
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

        #region Snowflake Class
        private class Snowflake
        {
            public Ellipse Element { get; set; }
            public double X { get; set; }
            public double Y { get; set; }
            public double VelocityY { get; set; }
            public double VelocityX { get; set; }
            public double SwaySpeed { get; set; }
            public double SwayAmplitude { get; set; }
            public double SwayOffset { get; set; }
            public double BaseX { get; set; }

            public Snowflake(Ellipse element, double x, double y, double velocityY, double swaySpeed, double swayAmplitude)
            {
                Element = element;
                X = x;
                Y = y;
                BaseX = x;
                VelocityY = velocityY;
                VelocityX = 0;
                SwaySpeed = swaySpeed;
                SwayAmplitude = swayAmplitude;
                SwayOffset = _random.NextDouble() * 2 * Math.PI;
            }
        }
        #endregion

        #region Init/Clearing
        public static void Initialize(Window mainWindow)
        {
            _mainWindow = mainWindow;
            _mainWindow.IsVisibleChanged += OnWindowVisibilityChanged;

            try
            {
                SeasonalEffectsEnabled = Properties.Settings.Default?.SeasonalEffects ?? false;
            }
            catch (Exception)
            {
                SeasonalEffectsEnabled = false;
            }
            SeasonalEffectsEnabled = _seasonalEffectsEnabled;
            UpdateSesonalEffects();
        }

        public static void ApplySeasonalTheme(Window? window = null)
        {
            if (window == null) return;

            ClearSeasonalEffects();

            var today = DateTime.Now;

            if (today.Month == 11 & today.Day >= 11 && today.Day <= 30)
            {
                if (Properties.Settings.Default.SeasonalEffects)
                {
                    ApplyWinterTheme(window);
                    AddHolidayLights(window);
                }
            }
        }

        public static void UpdateSesonalEffects()
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

            if (_animationTimer != null)
            {
                _animationTimer.Stop();
                _animationTimer = null;
            }

            if (_lightsTimer != null)
            {
                _lightsTimer.Stop();
                _lightsTimer = null;
            }

            _snowflakes.Clear();

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

            if (_mainWindow != null)
            {
                _mainWindow.IsVisibleChanged -= OnWindowVisibilityChanged;
            }
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
            if (_animationTimer != null && !_animationTimer.IsEnabled)
                _animationTimer.Start();

            if (_lightsTimer != null && !_lightsTimer.IsEnabled)
                _lightsTimer.Start();
        }

        private static void StopAnimations()
        {
            _animationTimer?.Stop();
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
                IsHitTestVisible = false,
                CacheMode = new BitmapCache { EnableClearType = true, RenderAtScale = 1 }
            };

            Grid.SetRowSpan(_snowCanvas, 3);
            ((Grid)window.Content).Children.Add(_snowCanvas);

            InitializeSnowflakes(window);
            StartSnowAnimations();
        }

        private static void InitializeSnowflakes(Window window)
        {
            _snowflakes.Clear();
            int snowflakeCount = 50;

            for (int i = 0; i < snowflakeCount; i++)
            {
                var size = 2 + (i % 3) * 0.5;
                var snowflake = new Ellipse
                {
                    Width = size,
                    Height = size,
                    Fill = Brushes.White,
                    Opacity = 0.4 + (i % 8) * 0.05,
                    CacheMode = new BitmapCache { EnableClearType = true, RenderAtScale = 1 }
                };

                double x = _random.Next(0, (int)window.ActualWidth);
                double y = _random.Next(0, (int)window.ActualHeight);
                double velocityY = 0.5 + _random.NextDouble() * 1.5;
                double swaySpeed = 0.02 + _random.NextDouble() * 0.03;
                double swayAmplitude = 15 + _random.NextDouble() * 25;

                var flake = new Snowflake(snowflake, x, y, velocityY, swaySpeed, swayAmplitude);
                _snowflakes.Add(flake);

                Canvas.SetLeft(snowflake, x);
                Canvas.SetTop(snowflake, y);
                _snowCanvas.Children.Add(snowflake);
            }
        }

        private static void StartSnowAnimations()
        {
            _animationTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(33)
            };
            _animationTimer.Tick += UpdateSnowflakes;
            _animationTimer.Start();
        }

        private static void UpdateSnowflakes(object? sender, EventArgs e)
        {
            if (_mainWindow == null || _snowCanvas == null) return;

            double windowHeight = _mainWindow.ActualHeight;
            double windowWidth = _mainWindow.ActualWidth;

            foreach (var flake in _snowflakes)
            {
                flake.Y += flake.VelocityY;

                flake.SwayOffset += flake.SwaySpeed;
                flake.X = flake.BaseX + Math.Sin(flake.SwayOffset) * flake.SwayAmplitude;

                if (flake.Y > windowHeight + 10)
                {
                    flake.Y = -10;
                    flake.BaseX = _random.Next(0, (int)windowWidth);
                    flake.X = flake.BaseX;
                    flake.SwayOffset = _random.NextDouble() * Math.PI * 2;
                }

                if (flake.X < -10)
                {
                    flake.BaseX += windowWidth + 20;
                    flake.X = flake.BaseX + Math.Sin(flake.SwayOffset) * flake.SwayAmplitude;
                }
                else if (flake.X > windowWidth + 10)
                {
                    flake.BaseX -= windowWidth + 20;
                    flake.X = flake.BaseX + Math.Sin(flake.SwayOffset) * flake.SwayAmplitude;
                }

                Canvas.SetLeft(flake.Element, flake.X);
                Canvas.SetTop(flake.Element, flake.Y);
            }
        }
        #endregion

        #region Holiday Lights
        private static void AddHolidayLights(Window window)
        {
            _lightsCanvas = new Canvas
            {
                Background = Brushes.Transparent,
                IsHitTestVisible = false,
                Margin = new Thickness(0, 30, 0, 0),
                CacheMode = new BitmapCache { EnableClearType = true, RenderAtScale = 1 }
            };

            Grid.SetRowSpan(_lightsCanvas, 3);
            ((Grid)window.Content).Children.Add(_lightsCanvas);

            int lightCount = 20;
            double spacing = window.ActualWidth / (lightCount + 1);

            var wire = new Polyline
            {
                Stroke = new SolidColorBrush(Color.FromRgb(184, 134, 11)),
                StrokeThickness = 1.5,
                Opacity = 0.6,
                CacheMode = new BitmapCache { EnableClearType = true, RenderAtScale = 1 }
            };

            for (int i = 0; i < lightCount; i++)
            {
                double x = spacing * (i + 1);
                double droop = Math.Sin((double)i / (lightCount - 1) * Math.PI) * 8;
                wire.Points.Add(new System.Windows.Point(x, 11 + droop));
            }

            _lightsCanvas.Children.Add(wire);
            Panel.SetZIndex(wire, -1);

            for (int i = 0; i < lightCount; i++)
            {
                double x = spacing * (i + 1);
                double droop = Math.Sin((double)i / (lightCount - 1) * Math.PI) * 8;

                var light = new Ellipse
                {
                    Width = 10,
                    Height = 10,
                    Stroke = new SolidColorBrush(Color.FromRgb(255, 215, 0)),
                    StrokeThickness = 0.5,
                    Fill = GetRandomLightColor(),
                    Tag = i,
                    CacheMode = new BitmapCache { EnableClearType = true, RenderAtScale = 1 }
                };

                Canvas.SetLeft(light, x - 5);
                Canvas.SetTop(light, 6 + droop);
                _lightsCanvas.Children.Add(light);
                _lightBulbs.Add(light);
            }

            _lightsTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(800)
            };
            _lightsTimer.Tick += AnimateLights;
            _lightsTimer.Start();
        }

        private static SolidColorBrush GetRandomLightColor()
        {
            Color[] colors =
            {
                Color.FromRgb(255, 59, 59),   // Red
                Color.FromRgb(76, 175, 80),   // Green
                Color.FromRgb(33, 150, 243),  // Blue
                Color.FromRgb(255, 235, 59),  // Yellow
                Color.FromRgb(255, 152, 0),   // Orange
                Color.FromRgb(156, 39, 176),  // Purple
                Color.FromRgb(255, 105, 180), // Hot Pink
                Color.FromRgb(0, 255, 255),   // Cyan
                Color.FromRgb(255, 20, 147),  // Deep Pink
                Color.FromRgb(50, 205, 50),   // Lime Green
                Color.FromRgb(255, 215, 0),   // Gold
                Color.FromRgb(138, 43, 226),  // Blue Violet
            };

            return new SolidColorBrush(colors[_random.Next(colors.Length)]);
        }

        private static void AnimateLights(object? sender, EventArgs e)
        {
            foreach (var light in _lightBulbs)
            {
                if (_random.NextDouble() > 0.75)
                {
                    light.Fill = GetRandomLightColor();
                }

                if (_random.NextDouble() > 0.6)
                {
                    var glow = new DropShadowEffect
                    {
                        Color = ((SolidColorBrush)light.Fill).Color,
                        BlurRadius = 8,
                        ShadowDepth = 0,
                        Opacity = 0.8
                    };
                    light.Effect = glow;

                    var timer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(300)
                    };
                    timer.Tick += (s, e) =>
                    {
                        light.Effect = null;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }
        #endregion
    }
}