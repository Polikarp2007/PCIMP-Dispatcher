using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace PCMPDispatcher;

public partial class DispatcherHomeView : UserControl
{
    private readonly DispatcherTimer _clock = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private DotChaser? _chaser;
    private bool _routeSelected;

    /// <summary>Raised when the dispatcher presses "Go to set-up!".</summary>
    public event Action? GoStationRequested;

    public DispatcherHomeView()
    {
        InitializeComponent();
        HomeUserName.Text = Services.UserSession.VisibleName;

        _clock.Tick += (_, _) =>
        {
            var now = DateTime.Now;
            TopClockText.Text = now.ToString("HH:mm:ss");
            TopDateText.Text  = now.ToString("dd.MM.yyyy");
        };
        _clock.Start();

        _chaser = new DotChaser(Dot1Brush, Dot2Brush, Dot3Brush, Dot4Brush, Dot5Brush, Dot6Brush);
        _chaser.Start();
    }

    public void Open()
    {
        HomeUserName.Text = Services.UserSession.VisibleName;
        Services.Avatars.Apply(HomeAvatarImg, HomeAvatarInitials);
        SbOnlineCount.Text = Services.OnlineCounter.DisplayText;
        Services.OnlineCounter.Updated += OnOnlineUpdated;
        Services.OnlineCounter.Start();
        Visibility = Visibility.Visible;
        Opacity = 0;
        BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300)));
    }

    private void OnOnlineUpdated(string txt) => SbOnlineCount.Text = txt;

    private void OnRouteCardToggle(object sender, MouseButtonEventArgs e)
    {
        _routeSelected = !_routeSelected;
        RouteDescPanel.Visibility = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        ProceedBtn.Visibility     = _routeSelected ? Visibility.Visible   : Visibility.Collapsed;
        ComingSoonCard.Visibility = _routeSelected ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnProceedClick(object sender, RoutedEventArgs e) => GoStationRequested?.Invoke();

    private void OnRouteCardClick(object sender, RoutedEventArgs e) => OnProceedClick(sender, e);

}
