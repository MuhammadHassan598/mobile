namespace EmpireSim.Core.Models;

/// <summary>
/// A point in map space. The starter map uses a 1000x700 viewBox;
/// coordinates are stylised, not real geography.
/// </summary>
public sealed record MapPoint(double X, double Y);
