using BlazorDatasheet.Data;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.Events;

namespace BlazorDatasheet.Render;

/// <summary>
/// Provides useful functions for computing actual pixel widths of
/// cells in the sheet based on the region.
/// </summary>
public class CellLayoutProvider
{
    private Sheet _sheet;
    public double DefaultColumnWidth { get; }
    public double DefaultRowHeight { get; }
    public int VisibleRowOffset { get; private set; }
    public int VisibleColOffset { get; private set; }

    // as long as the number of columns are small (which we will restrict) 
    // then we can store the column widths & x positions in arrays
    private List<double> _columnWidths { get; } = new();
    private List<double> _columnStartPositions { get; } = new();

    /// <summary>
    /// The total width of the sheet
    /// </summary>
    public double TotalWidth { get; private set; }

    /// <summary>
    /// The total height of the sheet
    /// </summary>
    public double TotalHeight { get; private set; }

    public CellLayoutProvider(Sheet sheet, double defaultColumnWidth, double defaultRowHeight)
    {
        _sheet = sheet;
        _sheet.ColumnInserted += SheetOnColumnInserted;
        _sheet.ColumnRemoved += SheetOnColumnRemoved;
        _sheet.RowInserted += SheetOnRowInserted;
        _sheet.RowRemoved += SheetOnRowRemoved;
        DefaultColumnWidth = defaultColumnWidth;
        DefaultRowHeight = defaultRowHeight;

        // Create default array of column widths
        _columnWidths = Enumerable.Repeat(defaultColumnWidth, sheet.NumCols).ToList();

        TotalWidth = defaultColumnWidth * sheet.NumCols;
        TotalHeight = defaultRowHeight * sheet.NumRows;
        updateXPositions();
    }

    private void SheetOnRowRemoved(object? sender, RowRemovedEventArgs e)
    {
        TotalHeight -= e.NRows * DefaultRowHeight;
    }

    public void SetVisibleRowOffset(int row)
    {
        VisibleRowOffset = row;
    }

    public void SetVisibleColOffset(int col)
    {
        VisibleColOffset = col;
    }

    private void SheetOnRowInserted(object? sender, RowInsertedEventArgs e)
    {
        TotalHeight += e.NRows * DefaultRowHeight;
    }

    private void SheetOnColumnRemoved(object? sender, ColumnRemovedEventArgs e)
    {
        for (int i = e.ColumnIndex; i < e.NCols; i++)
        {
            _columnWidths.RemoveAt(i);
        }

        updateXPositions();
    }

    private void SheetOnColumnInserted(object? sender, ColumnInsertedEventArgs e)
    {
        for (int i = e.ColumnIndex; i < e.NCols; i++)
        {
            _columnWidths.Insert(e.ColumnIndex, e.Width ?? DefaultColumnWidth);
        }

        updateXPositions();
    }

    public void SetColumnWidth(int col, double width)
    {
        _columnWidths[col] = width;
        updateXPositions();
    }

    private void updateXPositions()
    {
        _columnStartPositions.Clear();
        double cumX = 0;
        for (int i = 0; i < _columnWidths.Count; i++)
        {
            _columnStartPositions.Add(cumX);
            cumX += _columnWidths[i];
        }

        TotalWidth = cumX;
    }

    public void SetSheet(Sheet sheet)
    {
        _sheet = sheet;
    }

    public double ComputeLeftPosition(IRegion region, bool includeRowHeaders)
    {
        return ComputeLeftPosition(region.TopLeft.Col, includeRowHeaders);
    }

    public double ComputeLeftPosition(int col, bool includeRowHeaders)
    {
        var extra = includeRowHeaders ? 1 : 0;
        if (col < 0)
            return 0;

        if (col > _columnWidths.Count - 1)
            return _columnWidths.Last() + _columnStartPositions.Last() +
                   ((col - _columnWidths.Count) + extra) * DefaultColumnWidth;
        return extra * DefaultColumnWidth + _columnStartPositions[col];
    }

    public double ComputeTopPosition(IRegion region, bool includeRowHeaders)
    {
        return ComputeTopPosition(region.TopLeft.Row, includeRowHeaders);
    }

    public double ComputeTopPosition(int row, bool includeColHeaders)
    {
        var extra = includeColHeaders ? 1 : 0;
        var top = (row + extra) * DefaultRowHeight;
        return top;
    }

    public double ComputeWidth(int startCol, int colSpan)
    {
        if (startCol < 0 || startCol >= _columnWidths.Count)
            return DefaultColumnWidth;

        if (colSpan == 0)
            return 0;

        var end = Math.Min(startCol + colSpan - 1, _columnWidths.Count - 1);

        var colXStart = _columnStartPositions[startCol];
        var colXEnd = _columnStartPositions[end];

        var w = colXEnd - colXStart + _columnWidths[end];
        return w;
    }

    public double ComputeWidthBetween(int startCol, int endCol)
    {
        var span = endCol - startCol + 1;
        return ComputeWidth(startCol, endCol);
    }

    public int ComputeColumn(double x,bool includeRowHeaders)
    {
        var extra = includeRowHeaders ? -1 : 0;
        for (int i = 0; i < _columnStartPositions.Count; i++)
        {
            if (x < +_columnStartPositions[i])
                return i - 1 + extra;
        }

        return (int)((x - TotalWidth) / DefaultColumnWidth + _columnWidths.Count) + extra;
    }

    public int ComputeRow(double y, bool includeColHeaders)
    {
        var extra = includeColHeaders ? -1 : 0;
        return (int)(y / DefaultRowHeight) + extra;
    }

    public double ComputeHeight(int rowSpan)
    {
        if (rowSpan > _sheet.NumRows)
            return (_sheet.NumRows * DefaultRowHeight);
        return rowSpan * DefaultRowHeight;
    }

    public double ComputeWidth(IRegion region)
    {
        return ComputeWidth(region.TopLeft.Col, region.Width);
    }

    public double ComputeHeight(IRegion region)
    {
        return ComputeHeight(region.Height);
    }
}