namespace LightPdf.Core;

public enum ZoomMode
{
    /// <summary>Page width fills the view; follows window resizes.</summary>
    FitWidth,
    /// <summary>Whole current page fits in the view; follows window resizes.</summary>
    FitPage,
    /// <summary>Fixed zoom chosen by the user.</summary>
    Custom,
}

/// <summary>Zoom factors, where 1.0 = 100% = the page's physical size on a 96-DPI screen.</summary>
public static class Zoom
{
    public const double Min = 0.1;
    public const double Max = 8.0;

    /// <summary>DIP per PDF point at 100% (a point is 1/72 inch, a DIP is 1/96 inch).</summary>
    public const double DipPerPointAt100 = 96.0 / 72.0;

    private static readonly double[] Steps =
        [0.1, 0.25, 0.33, 0.5, 0.67, 0.75, 0.9, 1.0, 1.1, 1.25, 1.5, 1.75, 2.0, 2.5, 3.0, 4.0, 5.0, 6.0, 8.0];

    private const double Epsilon = 0.005;

    public static double Clamp(double zoom) => Math.Clamp(zoom, Min, Max);

    /// <summary>Next preset step above <paramref name="zoom"/> (Ctrl +).</summary>
    public static double StepIn(double zoom)
    {
        foreach (double s in Steps)
            if (s > zoom + Epsilon) return s;
        return Max;
    }

    /// <summary>Next preset step below <paramref name="zoom"/> (Ctrl -).</summary>
    public static double StepOut(double zoom)
    {
        for (int i = Steps.Length - 1; i >= 0; i--)
            if (Steps[i] < zoom - Epsilon) return Steps[i];
        return Min;
    }

    /// <summary>Zoom so the widest page fills <paramref name="availableWidth"/> DIP.</summary>
    public static double FitWidth(double availableWidth, double maxPageWidthPoints)
        => maxPageWidthPoints <= 0 ? 1 : Clamp(availableWidth / (maxPageWidthPoints * DipPerPointAt100));

    /// <summary>Zoom so a whole page fits inside the available area.</summary>
    public static double FitPage(double availableWidth, double availableHeight, PageSize page)
        => page.Width <= 0 || page.Height <= 0 ? 1 : Clamp(Math.Min(
            availableWidth / (page.Width * DipPerPointAt100),
            availableHeight / (page.Height * DipPerPointAt100)));
}
