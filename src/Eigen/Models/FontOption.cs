namespace Eigen.Models;

public sealed record FontOption(
    string Id,
    string DisplayName,
    string FontFamilyValue,
    double WidthBoost)
{
    public override string ToString() => DisplayName;
}
