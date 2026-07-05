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
    private void ShowRolePicker()
    {
        SetCaptionLight(true); // dark page → white window buttons
        RolePickerView.Open();
    }

    private void SetCaptionLight(bool light)
    {
        var brush = light ? Brushes.White : Brush("#444444");
        MinBtn.Foreground = brush;
        MaxBtn.Foreground = brush;
        CloseBtn2.Foreground = brush;
    }

    // Role chosen on the RolePicker view → navigate to the matching dashboard.
    private void OnRoleChosen(string role) => _ = NavigateTo(() =>
    {
        RolePickerView.Visibility = Visibility.Collapsed;
        if (role == "Driver") ShowDriverPage();
        else ShowMainPage();
    });

    private void ShowDriverPage()
    {
        SetCaptionLight(false); // white page → dark window buttons
        DriverHomeView.Open();
    }

    private void ShowMainPage()
    {
        SetCaptionLight(false); // white page → dark window buttons
        DispatcherHomeView.Open();
    }
}
