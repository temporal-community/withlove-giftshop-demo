using System.Globalization;

namespace WithLove.Web.Components.Shared;

internal static class PriceFormatter
{
    private static readonly NumberFormatInfo UsdFormat = CreateUsdFormat();

    public static string FormatUsd(decimal amount) => amount.ToString("C", UsdFormat);

    private static NumberFormatInfo CreateUsdFormat()
    {
        var format = (NumberFormatInfo)NumberFormatInfo.InvariantInfo.Clone();
        format.CurrencySymbol = "$";
        return NumberFormatInfo.ReadOnly(format);
    }
}
