using System;
using System.Windows.Media.Imaging;

namespace PCMPDispatcher;

/// <summary>
/// Immutable snapshot of everything the driver configured on the Set-Up page.
/// Passed to the Final page so it never has to reach into Set-Up's controls.
/// </summary>
public sealed class DriverRunConfig
{
    public string TrainType   { get; init; } = "—";
    public string TrainNumber { get; init; } = "—";
    public string Platform    { get; init; } = "—";
    public string Loco        { get; init; } = "—";
    public string WagonType   { get; init; } = "—";
    public string DriverName  { get; init; } = "Polikarp Kravchenko";

    public string[] Order    { get; init; } = Array.Empty<string>(); // station names, in travel order
    public int[]    Segments { get; init; } = Array.Empty<int>();    // running minutes between consecutive stations
    public int[]    Dwell    { get; init; } = Array.Empty<int>();    // dwell minutes per station (origin unused)

    public int DepartMinutes { get; init; }   // departure clock as minutes from midnight
    public int WagonCount    { get; init; }

    public bool OptRadio    { get; init; }
    public bool OptTextOnly { get; init; }
    public bool OptPriority { get; init; }

    public BitmapImage? LocoImg  { get; init; }
    public BitmapImage? WagonImg { get; init; }

    public string Origin      => Order.Length > 0 ? Order[0]  : "—";
    public string Destination => Order.Length > 0 ? Order[^1] : "—";
}
