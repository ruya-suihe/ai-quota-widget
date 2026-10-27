// WPF dimensions are device-independent pixels (DIP). Insets divided by zoom
// preserve the original size at unit scale; physical-pixel checks also include DPI.
internal static class LayoutMetrics {
    public const string FontFamily = "Microsoft YaHei UI";
    public const double DefaultWidth = 200, MinimumWidth = 100, MaximumWidth = 480;
    public const double MinimumScale = 0.4, MaximumScale = 1.5, DefaultTextThreshold = 0.8;
    public const double NarrowWidth = 200;
    public const double QuotaFontSize = 13, NarrowQuotaFontSize = 11, SmallTitleFontSize = 10;
    public const double NormalBorder = 1, HorizontalPadding = 8, VerticalInsetAtUnitScale = 2;
    public const double HorizontalExtraInsetAtUnitScale = 2;
    public const double FrameAllowance = 2 * (HorizontalPadding + NormalBorder);
    public const double BarTextInset = 6, NarrowLabelGap = 8, CenteredLabelGap = 6;
    public const double DetailColumnGap = 12, TextMeasureAllowance = 2;
    public const double TitleIconGap = 4, IconGapAtUnitScale = 6, MinimumIconSize = 8, IconSlotInset = 2;
    public const double IconSizeAtUnitScale = 13;
    public const double CompactToolbarHeight = 16, ExpandedToolbarHeight = 20;
    public const double CompactRowHeight = 21, CompactTimerRowHeight = 7, CompactRowsGap = 6;
    public const double CompactQuotaHeight = 19, MinimalWeekHeight = CompactQuotaHeight / 2;
    public const double MinimalRowsGap = 2;
    public const double DefaultHourBarHeight = 36, DefaultWeekBarHeight = 36;
    public const double MinimumBarHeight = 6, MaximumBarHeight = 36;
    public static double NormalizeBarHeight(double value,double fallback) {
        return double.IsNaN(value)||double.IsInfinity(value) ? fallback : System.Math.Max(MinimumBarHeight,System.Math.Min(MaximumBarHeight,value));
    }
    public const double EmptyExpandedMargin = 30, EmptyBottomMargin = 6;
    public const double NormalMarkerWidth = 2, CaptionMarkerWidth = 3.5;
}
