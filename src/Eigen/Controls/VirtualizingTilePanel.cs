using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Eigen.Controls;

/// <summary>
/// High-performance virtualizing wrap panel for uniform-sized tile and compact list items.
/// Only realizes containers for the visible viewport plus a 1-row buffer.
/// </summary>
public sealed class VirtualizingTilePanel : VirtualizingPanel, IScrollInfo
{
    private const double ScrollLineStep = 36.0;
    private const double MouseWheelStep = 72.0;

    private Size _extent = new(0, 0);
    private Size _viewport = new(0, 0);
    private Point _offset = new(0, 0);
    private Size _cachedCellSize = Size.Empty;

    public bool CanHorizontallyScroll { get; set; }
    public bool CanVerticallyScroll { get; set; }
    public double ExtentWidth => _extent.Width;
    public double ExtentHeight => _extent.Height;
    public double ViewportWidth => _viewport.Width;
    public double ViewportHeight => _viewport.Height;
    public double HorizontalOffset => _offset.X;
    public double VerticalOffset => _offset.Y;
    public ScrollViewer? ScrollOwner { get; set; }

    protected override void OnItemsChanged(object sender, ItemsChangedEventArgs args)
    {
        if (args.Action == NotifyCollectionChangedAction.Reset)
        {
            _cachedCellSize = Size.Empty;
            SetVerticalOffset(0);
        }
        base.OnItemsChanged(sender, args);
        InvalidateMeasure();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        var itemCount = itemsControl?.Items.Count ?? 0;

        var viewW = double.IsInfinity(availableSize.Width) ? 800.0 : Math.Max(1.0, availableSize.Width);
        var viewH = double.IsInfinity(availableSize.Height) ? 600.0 : Math.Max(1.0, availableSize.Height);

        if (itemCount == 0)
        {
            CleanUpItems(0, -1);
            UpdateScrollInfo(new Size(viewW, viewH), new Size(0, 0));
            return new Size(0, 0);
        }

        var generator = ItemContainerGenerator;
        if (generator is null)
            return new Size(0, 0);

        // Ensure children collection is initialized before accessing ItemContainerGenerator
        _ = InternalChildren;

        // Probe first item if we don't have a cell size yet
        if (_cachedCellSize.IsEmpty || _cachedCellSize.Width <= 0 || _cachedCellSize.Height <= 0)
        {
            var startPos = generator.GeneratorPositionFromIndex(0);
            using (generator.StartAt(startPos, GeneratorDirection.Forward, true))
            {
                if (generator.GenerateNext(out var isNew) is UIElement firstChild)
                {
                    if (isNew)
                    {
                        AddInternalChild(firstChild);
                        generator.PrepareItemContainer(firstChild);
                    }
                    firstChild.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    _cachedCellSize = new Size(
                        Math.Max(24.0, firstChild.DesiredSize.Width),
                        Math.Max(16.0, firstChild.DesiredSize.Height));
                }
            }
        }

        var cellW = _cachedCellSize.IsEmpty ? 140.0 : _cachedCellSize.Width;
        var cellH = _cachedCellSize.IsEmpty ? 120.0 : _cachedCellSize.Height;

        var columns = Math.Max(1, (int)Math.Floor(viewW / cellW));
        var totalRows = (int)Math.Ceiling((double)itemCount / columns);
        var extentH = totalRows * cellH;

        UpdateScrollInfo(new Size(viewW, viewH), new Size(viewW, extentH));

        var firstVisibleRow = Math.Max(0, (int)Math.Floor(_offset.Y / cellH) - 1);
        var lastVisibleRow = Math.Min(totalRows - 1, (int)Math.Ceiling((_offset.Y + viewH) / cellH) + 1);

        var firstIndex = firstVisibleRow * columns;
        var lastIndex = Math.Min(itemCount - 1, (lastVisibleRow + 1) * columns - 1);

        // Realize visible range
        var genPos = generator.GeneratorPositionFromIndex(firstIndex);
        var childIndex = genPos.Offset == 0 ? genPos.Index : genPos.Index + 1;

        using (generator.StartAt(genPos, GeneratorDirection.Forward, true))
        {
            for (var itemIndex = firstIndex; itemIndex <= lastIndex; itemIndex++, childIndex++)
            {
                var child = generator.GenerateNext(out var newlyRealized) as UIElement;
                if (child is null) continue;

                if (newlyRealized)
                {
                    if (childIndex >= InternalChildren.Count)
                        AddInternalChild(child);
                    else
                        InsertInternalChild(childIndex, child);

                    generator.PrepareItemContainer(child);
                }

                child.Measure(new Size(cellW, cellH));
                // Update cell size if layout metrics (icon/font/gap) changed dynamically
                if (itemIndex == firstIndex && child.DesiredSize.Width > 0 && child.DesiredSize.Height > 0)
                {
                    var measuredW = Math.Max(24.0, child.DesiredSize.Width);
                    var measuredH = Math.Max(16.0, child.DesiredSize.Height);
                    if (Math.Abs(measuredW - _cachedCellSize.Width) > 0.5 ||
                        Math.Abs(measuredH - _cachedCellSize.Height) > 0.5)
                    {
                        _cachedCellSize = new Size(measuredW, measuredH);
                    }
                }
            }
        }

        CleanUpItems(firstIndex, lastIndex);

        var desiredW = double.IsInfinity(availableSize.Width) ? Math.Min(itemCount, columns) * cellW : availableSize.Width;
        var desiredH = double.IsInfinity(availableSize.Height) ? extentH : availableSize.Height;
        return new Size(desiredW, desiredH);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var itemsControl = ItemsControl.GetItemsOwner(this);
        var itemCount = itemsControl?.Items.Count ?? 0;
        if (itemCount == 0 || _cachedCellSize.IsEmpty)
            return finalSize;

        var cellW = _cachedCellSize.Width;
        var cellH = _cachedCellSize.Height;
        var columns = Math.Max(1, (int)Math.Floor(Math.Max(1.0, finalSize.Width) / cellW));
        var generator = ItemContainerGenerator;

        for (var i = 0; i < InternalChildren.Count; i++)
        {
            var child = InternalChildren[i];
            var itemIndex = generator.IndexFromGeneratorPosition(new GeneratorPosition(i, 0));
            if (itemIndex < 0) continue;

            var row = itemIndex / columns;
            var col = itemIndex % columns;
            var x = col * cellW - _offset.X;
            var y = row * cellH - _offset.Y;
            child.Arrange(new Rect(x, y, cellW, cellH));
        }

        return finalSize;
    }

    private void CleanUpItems(int minDesiredGenerated, int maxDesiredGenerated)
    {
        var children = InternalChildren;
        var generator = ItemContainerGenerator;
        if (generator is null) return;

        for (var i = children.Count - 1; i >= 0; i--)
        {
            var childGenPos = new GeneratorPosition(i, 0);
            var itemIndex = generator.IndexFromGeneratorPosition(childGenPos);
            if (itemIndex >= 0 && (itemIndex < minDesiredGenerated || itemIndex > maxDesiredGenerated))
            {
                if (generator is IRecyclingItemContainerGenerator recycling)
                    recycling.Recycle(childGenPos, 1);
                else
                    generator.Remove(childGenPos, 1);

                RemoveInternalChildRange(i, 1);
            }
        }
    }

    private void UpdateScrollInfo(Size viewport, Size extent)
    {
        var changed = _viewport != viewport || _extent != extent;
        _viewport = viewport;
        _extent = extent;

        var maxY = Math.Max(0.0, _extent.Height - _viewport.Height);
        if (_offset.Y > maxY)
        {
            _offset.Y = maxY;
            changed = true;
        }

        if (changed)
            ScrollOwner?.InvalidateScrollInfo();
    }

    public void LineUp() => SetVerticalOffset(_offset.Y - ScrollLineStep);
    public void LineDown() => SetVerticalOffset(_offset.Y + ScrollLineStep);
    public void LineLeft() { }
    public void LineRight() { }
    public void PageUp() => SetVerticalOffset(_offset.Y - _viewport.Height);
    public void PageDown() => SetVerticalOffset(_offset.Y + _viewport.Height);
    public void PageLeft() { }
    public void PageRight() { }
    public void MouseWheelUp() => SetVerticalOffset(_offset.Y - MouseWheelStep);
    public void MouseWheelDown() => SetVerticalOffset(_offset.Y + MouseWheelStep);
    public void MouseWheelLeft() { }
    public void MouseWheelRight() { }
    public void SetHorizontalOffset(double offset) { }

    public void SetVerticalOffset(double offset)
    {
        var max = Math.Max(0.0, _extent.Height - _viewport.Height);
        var clamped = Math.Clamp(offset, 0.0, max);
        if (Math.Abs(clamped - _offset.Y) < 0.1) return;

        _offset.Y = clamped;
        ScrollOwner?.InvalidateScrollInfo();
        InvalidateMeasure();
    }

    public Rect MakeVisible(Visual visual, Rect rectangle)
    {
        if (visual is UIElement child)
        {
            var idx = InternalChildren.IndexOf(child);
            if (idx >= 0 && !_cachedCellSize.IsEmpty)
            {
                var itemIndex = ItemContainerGenerator.IndexFromGeneratorPosition(new GeneratorPosition(idx, 0));
                if (itemIndex >= 0)
                {
                    var columns = Math.Max(1, (int)Math.Floor(Math.Max(1.0, _viewport.Width) / _cachedCellSize.Width));
                    var row = itemIndex / columns;
                    var top = row * _cachedCellSize.Height;
                    var bottom = top + _cachedCellSize.Height;

                    if (top < _offset.Y)
                        SetVerticalOffset(top);
                    else if (bottom > _offset.Y + _viewport.Height)
                        SetVerticalOffset(bottom - _viewport.Height);
                }
            }
        }
        return rectangle;
    }
}
