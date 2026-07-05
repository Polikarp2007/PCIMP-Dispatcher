using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace PCMPDispatcher;

public partial class SplashView : UserControl
{
    /// <summary>Raised once the intro animation finishes — the host navigates on.</summary>
    public event Action? Finished;

    public SplashView()
    {
        InitializeComponent();
    }

    /// <summary>Play the Welcome → tagline intro, then signal completion.</summary>
    public async void Start()
    {
        AnimateWidth(SplashProgress, 0, 300, 6.0);
        await Task.Delay(3000);
        await FlipOut(WelcomeScale, 20, 15);
        SplashWelcome.Visibility = Visibility.Collapsed;
        SplashTagline.Visibility = Visibility.Visible;
        await FlipIn(TaglineScale, 20, 15);
        await Task.Delay(3000);
        Finished?.Invoke();
    }

    private static void AnimateWidth(FrameworkElement el, double from, double to, double seconds)
    {
        var a = new DoubleAnimation(from, to, TimeSpan.FromSeconds(seconds))
        { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut } };
        el.BeginAnimation(FrameworkElement.WidthProperty, a);
    }

    private static async Task FlipOut(ScaleTransform st, int steps, int delayMs)
    {
        for (int i = steps; i >= 0; i--)
        {
            st.ScaleY = (double)i / steps;
            await Task.Delay(delayMs);
        }
    }

    private static async Task FlipIn(ScaleTransform st, int steps, int delayMs)
    {
        for (int i = 0; i <= steps; i++)
        {
            st.ScaleY = (double)i / steps;
            await Task.Delay(delayMs);
        }
    }
}
