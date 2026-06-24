using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using SysIO = System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Documents;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace PCMPDispatcher;

public partial class MainWindow
{
    private async void RunSplash()
    {
        AnimateWidth(SplashProgress, 0, 300, 6.0);
        await Task.Delay(3000);
        await FlipOut(WelcomeScale, 20, 15);
        SplashWelcome.Visibility = Visibility.Collapsed;
        SplashTagline.Visibility = Visibility.Visible;
        await FlipIn(TaglineScale, 20, 15);
        await Task.Delay(3000);
        await NavigateTo(() =>
        {
            SplashPage.Visibility = Visibility.Collapsed;
            ShowMainPage();
        });
    }

    private void ShowMainPage()
    {
        MainPage.Visibility = Visibility.Visible;
        MainPage.Opacity = 0;
        var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(300));
        MainPage.BeginAnimation(OpacityProperty, fadeIn);
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
