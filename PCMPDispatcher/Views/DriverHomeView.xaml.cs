using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PCMPDispatcher;

public partial class DriverHomeView : UserControl
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DotChaser? _chaser;
    private bool _routeSelected;

    /// <summary>Raised when the driver presses "Go to set-up!".</summary>
    public event Action? GoSetupRequested;

    public DriverHomeView()
    {
        InitializeComponent();
        DrvUserName.Text = Services.UserSession.VisibleName;

        _clock.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            DrvClockText.Text = now.ToString("HH:mm:ss");
            DrvDateText.Text  = now.ToString("dd.MM.yyyy");
        };
        _clock.Start();

        _chaser = new DotChaser(DrvDot1Brush, DrvDot2Brush, DrvDot3Brush,
                                DrvDot4Brush, DrvDot5Brush, DrvDot6Brush);
        _chaser.Start();
    }

    public void Open()
    {
        DrvUserName.Text = Services.UserSession.VisibleName;
        Services.Avatars.Apply(DrvAvatarImg, DrvAvatarInitials);
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void OnDrvRouteCardToggle(object sender, MouseButtonEventArgs e)
    {
        _routeSelected = !_routeSelected;
        DrvRouteDescPanel.Visibility = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        DrvProceedBtn.Visibility     = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        DrvComingSoonCard.Visibility = _routeSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnDrvProceedClick(object sender, RoutedEventArgs e) => GoSetupRequested?.Invoke();

}
