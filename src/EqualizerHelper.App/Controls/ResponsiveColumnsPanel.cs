using System;
using System.Windows;
using Size = System.Windows.Size;

namespace EqualizerHelper.App.Controls;

/// <summary>
/// Lays out cards in one column when narrow and two masonry-style columns
/// when there's room (each card goes into whichever column is shorter), so
/// pages use the space of a maximized window instead of leaving a thin strip
/// in the middle. Children marked <see cref="FullWidthProperty"/> always span
/// both columns and start below everything placed before them.
/// </summary>
public class ResponsiveColumnsPanel : System.Windows.Controls.Panel
{
    public static readonly DependencyProperty TwoColumnMinWidthProperty = DependencyProperty.Register(
        nameof(TwoColumnMinWidth), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(900.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnSpacingProperty = DependencyProperty.Register(
        nameof(ColumnSpacing), typeof(double), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(16.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty FullWidthProperty = DependencyProperty.RegisterAttached(
        "FullWidth", typeof(bool), typeof(ResponsiveColumnsPanel),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public double TwoColumnMinWidth
    {
        get => (double)GetValue(TwoColumnMinWidthProperty);
        set => SetValue(TwoColumnMinWidthProperty, value);
    }

    public double ColumnSpacing
    {
        get => (double)GetValue(ColumnSpacingProperty);
        set => SetValue(ColumnSpacingProperty, value);
    }

    public static bool GetFullWidth(UIElement element) => (bool)element.GetValue(FullWidthProperty);

    public static void SetFullWidth(UIElement element, bool value) => element.SetValue(FullWidthProperty, value);

    protected override Size MeasureOverride(Size availableSize) => Layout(availableSize, arrange: false);

    protected override Size ArrangeOverride(Size finalSize)
    {
        Layout(finalSize, arrange: true);
        return finalSize;
    }

    private Size Layout(Size size, bool arrange)
    {
        double width = double.IsInfinity(size.Width) ? TwoColumnMinWidth : size.Width;
        bool twoColumns = width >= TwoColumnMinWidth;
        double columnWidth = twoColumns ? (width - ColumnSpacing) / 2 : width;
        double left = 0, right = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed)
            {
                continue;
            }

            bool full = !twoColumns || GetFullWidth(child);
            double childWidth = full ? width : columnWidth;
            if (!arrange)
            {
                child.Measure(new Size(childWidth, double.PositiveInfinity));
            }

            double height = child.DesiredSize.Height;

            if (full)
            {
                double top = Math.Max(left, right);
                if (arrange)
                {
                    child.Arrange(new Rect(0, top, childWidth, height));
                }

                left = right = top + height;
            }
            else if (left <= right)
            {
                if (arrange)
                {
                    child.Arrange(new Rect(0, left, childWidth, height));
                }

                left += height;
            }
            else
            {
                if (arrange)
                {
                    child.Arrange(new Rect(columnWidth + ColumnSpacing, right, childWidth, height));
                }

                right += height;
            }
        }

        return new Size(width, Math.Max(left, right));
    }
}
