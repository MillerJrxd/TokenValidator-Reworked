using System.Windows;
using MaterialDesignThemes.Wpf;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using Color = System.Windows.Media.Color;
using Button = System.Windows.Controls.Button;
using MessageBoxResult = System.Windows.MessageBoxResult;

namespace TokenValidator
{
    /// <summary>
    /// Interaction logic for CustomMessageBox.xaml
    /// </summary>
    public partial class CustomMessageBox : Window
    {
        #region Variables/Constructor

        private MessageBoxResult _result = MessageBoxResult.None;
        private static readonly SolidColorBrush InfoBackground = new(Color.FromArgb(30, 33, 150, 243));
        private static readonly SolidColorBrush InfoForeground = new(Color.FromRgb(33, 150, 243));
        private static readonly SolidColorBrush WarningBackground = new(Color.FromArgb(30, 255, 193, 7));
        private static readonly SolidColorBrush WarningForeground = new(Color.FromRgb(255, 193, 7));
        private static readonly SolidColorBrush ErrorBackground = new(Color.FromArgb(30, 220, 53, 69));
        private static readonly SolidColorBrush ErrorForeground = new(Color.FromRgb(220, 53, 69));
        private static readonly SolidColorBrush QuestionBackground = new(Color.FromArgb(30, 156, 39, 176));
        private static readonly SolidColorBrush QuestionForeground = new(Color.FromRgb(156, 39, 176));

        public CustomMessageBox()
        {
            InitializeComponent();

            MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
        }
        #endregion

        #region Static Show Methods

        public static MessageBoxResult Show(string messageBoxText)
            => Show(messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string messageBoxText, string caption)
            => Show(messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button)
            => Show(messageBoxText, caption, button, MessageBoxImage.None);

        public static MessageBoxResult Show(string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
            => ShowCore(null, messageBoxText, caption, button, icon);

        public static MessageBoxResult Show(Window owner, string messageBoxText)
            => ShowCore(owner, messageBoxText, string.Empty, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption)
            => ShowCore(owner, messageBoxText, caption, MessageBoxButton.OK, MessageBoxImage.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button)
            => ShowCore(owner, messageBoxText, caption, button, MessageBoxImage.None);

        public static MessageBoxResult Show(Window owner, string messageBoxText, string caption, MessageBoxButton button, MessageBoxImage icon)
            => ShowCore(owner, messageBoxText, caption, button, icon);

        #endregion

        #region Core function 

        private static MessageBoxResult ShowCore(Window? owner, string message, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            var dlg = new CustomMessageBox();

            if (owner != null)
                dlg.Owner = owner;
            else if (System.Windows.Application.Current?.MainWindow.IsLoaded == true)
                dlg.Owner = System.Windows.Application.Current.MainWindow;

            dlg.Configure(caption, message, button, icon);

            dlg.ShowDialog();
            return dlg._result;
        }

        private void Configure(string caption, string message, MessageBoxButton button, MessageBoxImage icon)
        {
            TitleText.Text = string.IsNullOrEmpty(caption) ? "Message" : caption;

            MessageText.Text = message;

            ApplyIcon(icon);

            BuildButtons(button);

            Loaded += (s, e) => PlayAppearAnim();
            
        }

        private void ApplyIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage iconValue when iconValue == MessageBoxImage.Error || iconValue == MessageBoxImage.Stop:
                    SetIcon(PackIconKind.AlertCircle, ErrorForeground, ErrorBackground);
                    break;
                case MessageBoxImage iconValue when iconValue == MessageBoxImage.Warning || iconValue == MessageBoxImage.Exclamation:
                    SetIcon(PackIconKind.Alert, WarningForeground, WarningBackground);
                    break;
                case MessageBoxImage iconValue when iconValue == MessageBoxImage.Question:
                    SetIcon(PackIconKind.HelpCircle, QuestionForeground, QuestionBackground);
                    break;
                case MessageBoxImage iconValue when iconValue == MessageBoxImage.Information || iconValue == MessageBoxImage.Asterisk:
                    SetIcon(PackIconKind.InformationOutline, InfoForeground, InfoBackground);
                    break;
                default:
                    IconBorder.Visibility = Visibility.Collapsed;
                    TitleIcon.Visibility = Visibility.Collapsed;
                    break;
            }
        }

        private void SetIcon(PackIconKind iconKind, SolidColorBrush fg, SolidColorBrush bg)
        {
            ContentIcon.Kind = iconKind;
            ContentIcon.Foreground = fg;
            IconBorder.Background = bg;
            TitleIcon.Kind = iconKind;
            TitleIcon.Foreground = fg;
        }

        private void BuildButtons(MessageBoxButton buttonSet)
        {
            switch (buttonSet)
            {
                case MessageBoxButton.OK:
                    AddButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true, isCancel: true);
                    break;
                case MessageBoxButton.OKCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isDefault: false, isCancel: true);
                    AddButton("OK", MessageBoxResult.OK, isPrimary: true, isDefault: true, isCancel: false);
                    break;
                case MessageBoxButton.YesNo:
                    AddButton("No", MessageBoxResult.No, isPrimary: false, isDefault: false, isCancel: true);
                    AddButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true, isCancel: false);
                    break;
                case MessageBoxButton.YesNoCancel:
                    AddButton("Cancel", MessageBoxResult.Cancel, isPrimary: false, isDefault: false, isCancel: true);
                    AddButton("No", MessageBoxResult.No, isPrimary: false, isDefault: false, isCancel: false);
                    AddButton("Yes", MessageBoxResult.Yes, isPrimary: true, isDefault: true, isCancel: false);
                    break;
            }
        }

        private void AddButton(string label, MessageBoxResult result, bool isPrimary, bool isDefault, bool isCancel)
        {
            var btn = new Button
            {
                Content = label,
                Width = 90,
                Height = 36,
                Margin = new Thickness(8, 0, 0, 0),
                IsDefault = isDefault,
                IsCancel = isCancel,
                Style = isPrimary
                    ? (Style)FindResource("MaterialDesignRaisedButton")
                    : (Style)FindResource("MaterialDesignOutlinedButton"),
                Tag = result
            };

            MaterialDesignThemes.Wpf.ButtonAssist.SetCornerRadius(btn, new CornerRadius(6));
            btn.Click += DialogButton_Click;
            ButtonPanel.Children.Add(btn);
        }

        private void DialogButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is MessageBoxResult r)
            {
                _result = r;
                PlayCloseAnim(() => Close());
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            _result = MessageBoxResult.Cancel;
            PlayCloseAnim(() => Close());
        }

        private void PlayAppearAnim()
        {
            Opacity = 0;
            ContentContainer.RenderTransform = new ScaleTransform(0.95, 0.95, ContentContainer.ActualWidth / 2, ContentContainer.ActualHeight / 2);

            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleX = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var scaleY = new DoubleAnimation(0.95, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            BeginAnimation(OpacityProperty, fadeIn);
            ((ScaleTransform)ContentContainer.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
            ((ScaleTransform)ContentContainer.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }

        private void PlayCloseAnim(Action onComplete)
        {
            ContentContainer.RenderTransform ??= new ScaleTransform(1, 1, ContentContainer.ActualWidth / 2, ContentContainer.ActualHeight / 2);

            var fadeOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            fadeOut.Completed += (s, e) => onComplete();

            var scaleX = new DoubleAnimation(1.0, 0.95, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var scaleY = new DoubleAnimation(1.0, 0.95, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            BeginAnimation(OpacityProperty, fadeOut);
            if (ContentContainer.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }

        #endregion
    }
}
