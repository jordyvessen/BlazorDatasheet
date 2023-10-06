using System.Runtime.InteropServices;
using System.Text;
using BlazorDatasheet.Commands;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.Intervals;
using BlazorDatasheet.DataStructures.RTree;
using BlazorDatasheet.DataStructures.Store;
using BlazorDatasheet.DataStructures.Util;
using BlazorDatasheet.Edit;
using BlazorDatasheet.Edit.DefaultComponents;
using BlazorDatasheet.Events;
using BlazorDatasheet.Events.Edit;
using BlazorDatasheet.Events.Validation;
using BlazorDatasheet.Formats;
using BlazorDatasheet.Interfaces;
using BlazorDatasheet.Render;
using BlazorDatasheet.Render.DefaultComponents;
using BlazorDatasheet.Selecting;
using BlazorDatasheet.Util;
using BlazorDatasheet.Validation;

namespace BlazorDatasheet.Data;

public class Sheet
{
    /// <summary>
    /// The total number of rows in the sheet
    /// </summary>
    public int NumRows { get; private set; }

    /// <summary>
    /// The total of columns in the sheet
    /// </summary>
    public int NumCols { get; private set; }

    /// <summary>
    /// The sheet's headings
    /// </summary>
    public List<Heading> ColumnHeadings { get; private set; }

    /// <summary>
    /// The sheet's row headings
    /// </summary>
    public List<Heading> RowHeadings { get; }

    /// <summary>
    /// Whether to show the row headings
    /// </summary>
    public bool ShowRowHeadings { get; set; } = true;

    /// <summary>
    /// Whether to show the column headings. Default is true.
    /// </summary>
    public bool ShowColumnHeadings { get; set; } = true;

    /// <summary>
    /// Managers commands & undo/redo. Default is true.
    /// </summary>
    public CommandManager Commands { get; }

    /// <summary>
    /// The bounds of the sheet
    /// </summary>
    public Region Region => new Region(0, NumRows - 1, 0, NumCols - 1);

    /// <summary>
    /// Provides functions for managing the sheet's conditional formatting
    /// </summary>
    public ConditionalFormatManager ConditionalFormatting { get; }

    /// <summary>
    /// The sheet's active selection
    /// </summary>
    public Selection Selection { get; }

    private readonly HashSet<(int row, int col)> _dirtyCells;

    private readonly Dictionary<string, Type> _editorTypes;
    public IReadOnlyDictionary<string, Type> EditorTypes => _editorTypes;
    private readonly Dictionary<string, Type> _renderComponentTypes;
    public IReadOnlyDictionary<string, Type> RenderComponentTypes => _renderComponentTypes;

    /// <summary>
    /// Formats applied to any rows
    /// </summary>
    internal readonly NonOverlappingIntervals<CellFormat> RowFormats = new();

    /// <summary>
    /// Formats applied to any cols
    /// </summary>
    internal readonly NonOverlappingIntervals<CellFormat> ColFormats = new();

    #region EVENTS

    /// <summary>
    /// Fired when a row is inserted into the sheet
    /// </summary>
    public event EventHandler<RowInsertedEventArgs>? RowInserted;

    /// <summary>
    /// Fired when a row is removed from the sheet.
    /// </summary>
    public event EventHandler<RowRemovedEventArgs>? RowRemoved;

    /// <summary>
    /// Fired when a column is inserted into the sheet
    /// </summary>
    public event EventHandler<ColumnInsertedEventArgs>? ColumnInserted;

    /// <summary>
    /// Fired when a column is removed from the sheet.
    /// </summary>
    public event EventHandler<ColumnRemovedEventArgs>? ColumnRemoved;

    /// <summary>
    /// Fired when one or more cells are changed
    /// </summary>
    public event EventHandler<IEnumerable<ChangeEventArgs>>? CellsChanged;

    /// <summary>
    /// Fired when a column width is changed
    /// </summary>
    public event EventHandler<ColumnWidthChangedEventArgs>? ColumnWidthChanged;

    public event EventHandler<CellsSelectedEventArgs>? CellsSelected;

    public event EventHandler<CellMetaDataChangeEventArgs>? MetaDataChanged;

    /// <summary>
    /// Fired when cell formats change
    /// </summary>
    public event EventHandler<FormatChangedEventArgs>? FormatsChanged;

    /// <summary>
    /// Fired when the sheet is invalidated (requires re-render).
    /// </summary>
    public event EventHandler<SheetInvalidateEventArgs>? SheetInvalidated;

    /// <summary>
    /// Fired before a cell's value is set. Allows for changing the value that is set.
    /// </summary>
    public event EventHandler<BeforeCellChangeEventArgs> BeforeSetCellValue;

    #endregion

    public Editor Editor { get; }

    internal CellLayoutProvider LayoutProvider { get; }

    /// <summary>
    /// Contains cell merge information and handles merges.
    /// </summary>
    public MergeManager Merges { get; }

    /// <summary>
    /// Manages and holds information on cell validators.
    /// </summary>
    public ValidationManager Validation { get; }

    private readonly IMatrixDataStore<Cell> _cellDataStore = new SparseMatrixStore<Cell>();

    private Sheet()
    {
        Merges = new MergeManager(this);
        Validation = new ValidationManager();
        ColumnHeadings = new List<Heading>();
        RowHeadings = new List<Heading>();
        Commands = new CommandManager(this);
        Selection = new Selection(this);
        Editor = new Editor(this);
        Selection.SelectionChanged += SelectionOnSelectionChanged;
        Validation.ValidatorChanged += ValidationOnValidatorChanged;
        ConditionalFormatting = new ConditionalFormatManager(this);
        _editorTypes = new Dictionary<string, Type>();
        _renderComponentTypes = new Dictionary<string, Type>();
        _dirtyCells = new HashSet<(int row, int col)>();

        RegisterDefaultEditors();
        RegisterDefaultRenderers();
    }

    public Sheet(int numRows, int numCols, Cell[,] cells) : this()
    {
        NumCols = numCols;
        NumRows = numRows;

        for (var i = 0; i < numRows; i++)
        {
            for (int j = 0; j < NumCols; j++)
            {
                var cell = cells[i, j];
                cell.Row = i;
                cell.Col = j;
                _cellDataStore.Set(i, j, cell);
            }
        }

        LayoutProvider = new CellLayoutProvider(this, 105, 25);
    }

    public Sheet(int numRows, int numCols) : this()
    {
        Validation = new ValidationManager();
        NumCols = numCols;
        NumRows = numRows;
        LayoutProvider = new CellLayoutProvider(this, 105, 25);
    }

    #region COLS

    /// <summary>
    /// Inserts a column after the index specified. If the index is outside of the range of -1 to NumCols-1,
    /// A column is added either at the beginning or end of the columns.
    /// </summary>
    /// <param name="colIndex"></param>
    public void InsertColAt(int colIndex, int nCols = 1)
    {
        var indexToAdd = Math.Min(NumCols - 1, Math.Max(colIndex, 0));
        Console.WriteLine($"Adding {nCols} cols at index {indexToAdd}");
        var cmd = new InsertColAtCommand(indexToAdd, nCols);
        Commands.ExecuteCommand(cmd);
    }

    internal void InsertColAtImpl(int colIndex, double? width = null, int nCols = 1)
    {
        _cellDataStore.InsertColAt(colIndex, nCols);
        for (int i = 0; i < nCols; i++)
        {
            if (ColumnHeadings.Count > (colIndex + i))
                ColumnHeadings.Insert(colIndex + i, new Heading());
        }

        NumCols += nCols;
        ColumnInserted?.Invoke(this, new ColumnInsertedEventArgs(colIndex, width, nCols));
    }

    /// <summary>
    /// Removes the column at the specified index
    /// </summary>
    /// <param name="colIndex"></param>
    /// <param name="nCols">The number of oclumns to remove</param>
    /// <returns>Whether the column removal was successful</returns>
    public bool RemoveCol(int colIndex, int nCols = 1)
    {
        var cmd = new RemoveColumnCommand(colIndex, nCols);
        return Commands.ExecuteCommand(cmd);
    }

    /// <summary>
    /// Internal implementation that removes the column
    /// </summary>
    /// <param name="colIndex"></param>
    /// <returns>Whether the column at index colIndex was removed</returns>
    internal bool RemoveColImpl(int colIndex, int nCols = 1)
    {
        _cellDataStore.RemoveColAt(colIndex, nCols);
        if (colIndex < ColumnHeadings.Count)
            ColumnHeadings.RemoveAt(colIndex);
        NumCols -= nCols;
        ColumnRemoved?.Invoke(this, new ColumnRemovedEventArgs(colIndex, nCols));

        return true;
    }

    public void SetColumnWidth(int col, double width)
    {
        var cmd = new SetColumnWidthCommand(col, width);
        Commands.ExecuteCommand(cmd);
    }

    internal void SetColumnWidthImpl(int col, double width)
    {
        var oldWidth = this.LayoutProvider.ComputeWidth(col, 1);
        this.LayoutProvider.SetColumnWidth(col, width);
        ColumnWidthChanged?.Invoke(this, new ColumnWidthChangedEventArgs(col, width, oldWidth));
    }

    #endregion

    #region ROWS

    /// <summary>
    /// Inserts a row at an index specified.
    /// </summary>
    /// <param name="rowIndex">The index that the new row will be at. The new row will have the index specified.</param>
    public void InsertRowAt(int rowIndex)
    {
        var indexToAddAt = Math.Min(NumRows - 1, Math.Max(rowIndex, 0));
        var cmd = new InsertRowsAtCommand(indexToAddAt);
        Commands.ExecuteCommand(cmd);
    }

    /// <summary>
    /// The internal insert function that implements adding a row
    /// This function does not add a command that is able to be undone.
    /// </summary>
    /// <param name="rowIndex"></param>
    /// <param name="nRows">The number of rows to insert</param>
    /// <returns></returns>
    internal bool InsertRowAtImpl(int rowIndex, int nRows = 1)
    {
        _cellDataStore.InsertRowAt(rowIndex, nRows);
        NumRows += nRows;

        RowInserted?.Invoke(this, new RowInsertedEventArgs(rowIndex, nRows));
        return true;
    }

    public bool RemoveRow(int index, int nRows = 1)
    {
        var cmd = new RemoveRowsCommand(index, nRows);
        return Commands.ExecuteCommand(cmd);
    }

    internal bool RemoveRowAtImpl(int rowIndex, int nRows)
    {
        var row = rowIndex;
        var endIndex = rowIndex + nRows;
        _cellDataStore.RemoveRowAt(rowIndex, nRows);
        NumRows -= nRows;
        RowRemoved?.Invoke(this, new RowRemovedEventArgs(rowIndex, nRows));
        row++;

        return true;
    }

    #endregion

    /// <summary>
    /// Returns a single cell range at the position row, col
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public BRangeCell Range(int row, int col)
    {
        return new BRangeCell(this, row, col);
    }

    /// <summary>
    /// Returns a range with the positions specified
    /// </summary>
    /// <param name="rowStart"></param>
    /// <param name="rowEnd"></param>
    /// <param name="colStart"></param>
    /// <param name="colEnd"></param>
    /// <returns></returns>
    public BRange Range(int rowStart, int rowEnd, int colStart, int colEnd)
    {
        return Range(new Region(rowStart, rowEnd, colStart, colEnd));
    }

    /// <summary>
    /// Returns a new range that contains the region specified
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public BRange Range(IRegion region)
    {
        return Range(new List<IRegion>() { region });
    }

    /// <summary>
    /// Returns a column or row range, depending on the axis provided
    /// </summary>
    /// <param name="axis">The axis of the range (row or column)</param>
    /// <param name="start">The start row/column index</param>
    /// <param name="end">The end row/column index</param>
    /// <returns></returns>
    public BRange Range(Axis axis, int start, int end)
    {
        switch (axis)
        {
            case Axis.Col:
                return Range(new ColumnRegion(start, end));
            case Axis.Row:
                return Range(new RowRegion(start, end));
        }

        throw new Exception("Cannot return a range for axis " + axis);
    }

    /// <summary>
    /// Returns a new range that contains all the regions specified
    /// </summary>
    /// <param name="regions"></param>
    /// <returns></returns>
    public BRange Range(IEnumerable<IRegion> regions)
    {
        return new BRange(this, regions);
    }


    #region CELLS

    /// <summary>
    /// Returns all cells in the specified region
    /// </summary>
    /// <param name="region"></param>
    /// <returns></returns>
    public IEnumerable<IReadOnlyCell> GetCellsInRegion(IRegion region)
    {
        return (new BRange(this, region))
               .Positions
               .Select(x => this.GetCell(x.row, x.col));
    }

    /// <summary>
    /// Returns all cells that are present in the regions given.
    /// </summary>
    /// <param name="regions"></param>
    /// <returns></returns>
    public IEnumerable<IReadOnlyCell> GetCellsInRegions(IEnumerable<IRegion> regions)
    {
        var cells = new List<IReadOnlyCell>();
        foreach (var region in regions)
            cells.AddRange(GetCellsInRegion(region));
        return cells.ToArray();
    }

    /// <summary>
    /// Returns the cell at the specified position.
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public IReadOnlyCell GetCell(int row, int col)
    {
        var cell = _cellDataStore.Get(row, col);

        if (cell == null)
            return new Cell()
            {
                Row = row,
                Col = col
            };

        cell.Row = row;
        cell.Col = col;
        return cell;
    }

    /// <summary>
    /// Returns the cell at the specified position
    /// </summary>
    /// <param name="position"></param>
    /// <returns></returns>
    public IReadOnlyCell GetCell(CellPosition position)
    {
        return GetCell(position.Row, position.Col);
    }

    internal IEnumerable<(int row, int col)> GetNonEmptyCellPositions(IRegion region)
    {
        return _cellDataStore.GetNonEmptyPositions(region.TopLeft.Row,
                                                   region.BottomRight.Row,
                                                   region.TopLeft.Col,
                                                   region.BottomRight.Col);
    }

    #endregion

    #region DATA

    public bool TrySetCellValue(int row, int col, object value)
    {
        return SetCellValues(new List<CellChange>() { new CellChange(row, col, value) });
    }

    public bool TrySetCellValueImpl(int row, int col, object? value, bool raiseEvent = true)
    {
        var cell = _cellDataStore.Get(row, col);
        if (cell == null)
        {
            cell = new Cell(value);
            _cellDataStore.Set(row, col, cell);
            if (raiseEvent)
                CellsChanged?.Invoke(this, new List<ChangeEventArgs>() { new(row, col, null, value) });

            MarkDirty(row, col);
            return true;
        }

        // Try to set the cell's value to the new value
        var oldValue = cell.GetValue();
        var setValue = cell.TrySetValue(value);
        if (setValue && raiseEvent)
        {
            var args = new ChangeEventArgs[]
            {
                new(row, col, oldValue, value)
            };
            CellsChanged?.Invoke(this, args);
        }

        // Perform data validation
        // but we don't restrict the cell value being set here,
        // it is just marked as invalid if it is invalid
        var validationResult = Validation.Validate(value, row, col);
        cell.IsValid = validationResult.IsValid;

        if (setValue)
            MarkDirty(row, col);

        return setValue;
    }

    /// <summary>
    /// Sets cell metadata, specified by name, for the cell at position row, col
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <param name="name"></param>
    /// <param name="value"></param>
    /// <returns>Whether setting the cell metadata was successful</returns>
    public bool SetCellMetaData(int row, int col, string name, object? value)
    {
        var cmd = new SetMetaDataCommand(row, col, name, value);
        return Commands.ExecuteCommand(cmd);
    }

    internal void SetMetaDataImpl(int row, int col, string name, object? value)
    {
        var cell = _cellDataStore.Get(row, col);
        if (cell == null)
        {
            cell = new Cell();
            _cellDataStore.Set(row, col, cell);
        }

        var oldMetaData = cell.GetMetaData(name);

        cell.SetCellMetaData(name, value);
        this.MetaDataChanged?.Invoke(this, new CellMetaDataChangeEventArgs(row, col, name, oldMetaData, value));
    }

    /// <summary>
    /// Returns the metadata with key "name" for the cell at row, col.
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <param name="name"></param>
    /// <returns></returns>
    public object? GetMetaData(int row, int col, string name)
    {
        return GetCell(row, col)?.GetMetaData(name);
    }


    public void SetCell(int row, int col, Cell cell)
    {
        _cellDataStore.Set(row, col, cell);
    }

    /// <summary>
    /// Gets the cell's value at row, col
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public object? GetValue(int row, int col)
    {
        return GetCell(row, col).GetValue();
    }

    /// <summary>
    /// Sets cell values to those specified.
    /// </summary>
    /// <param name="changes"></param>
    /// <returns></returns>
    public bool SetCellValues(List<CellChange> changes)
    {
        var beforeChangesEvent = new BeforeCellChangeEventArgs(changes);
        BeforeSetCellValue?.Invoke(this, beforeChangesEvent);

        if (beforeChangesEvent.Cancel)
            return false;

        var cmd = new SetCellValuesCommand(changes);
        return Commands.ExecuteCommand(cmd);
    }

    /// <summary>
    /// Performs the actual setting of cell values, including raising events for any changes made.
    /// </summary>
    /// <param name="changes"></param>
    /// <returns></returns>
    internal bool SetCellValuesImpl(List<CellChange> changes)
    {
        var changeEvents = new List<ChangeEventArgs>();
        foreach (var change in changes)
        {
            var currValue = GetValue(change.Row, change.Col);
            var set = TrySetCellValueImpl(change.Row, change.Col, change.NewValue, false);
            var newValue = GetValue(change.Row, change.Col);
            if (set && currValue != newValue)
            {
                changeEvents.Add(new ChangeEventArgs(change.Row, change.Col, currValue, newValue));
            }
        }

        CellsChanged?.Invoke(this, changeEvents);
        return changes.Any();
    }

    /// <summary>
    /// Clears all cell values in the region
    /// </summary>
    /// <param name="range">The range in which to clear all cells</param>
    public void ClearCells(BRange range)
    {
        var cmd = new ClearCellsCommand(range);
        Commands.ExecuteCommand(cmd);
    }

    internal void ClearCellsImpl(BRange range)
    {
        ClearCellsImpl(range.GetNonEmptyPositions());
    }

    internal void ClearCellsImpl(IEnumerable<(int row, int col)> positions)
    {
        var changeArgs = new List<ChangeEventArgs>();
        foreach (var posn in positions)
        {
            var cell = this.GetCell(posn.row, posn.col) as Cell;
            var oldValue = cell!.GetValue();
            cell!.Clear();
            var newVal = cell.GetValue();
            if (oldValue != newVal)
            {
                changeArgs.Add(new ChangeEventArgs(posn.row, posn.col, oldValue, newVal));
            }
        }

        this.CellsChanged?.Invoke(this, changeArgs);
    }

    #endregion

    private void ValidationOnValidatorChanged(object? sender, ValidatorChangedEventArgs e)
    {
        foreach (var region in e.RegionsAffected)
        {
            ValidateRegion(region);
        }
    }

    private void ValidateRegion(IRegion region)
    {
        var cellsAffected = _cellDataStore.GetNonEmptyPositions(region).ToList();
        foreach (var (row, col) in cellsAffected)
        {
            var cell = _cellDataStore.Get(row, col);
            var result = Validation.Validate(cell.GetValue(), row, col);
            cell.IsValid = result.IsValid;
        }

        MarkDirty(cellsAffected);
    }

    /// <summary>
    /// Registers a cell editor component with a unique name.
    /// If the editor already exists, it will override the existing.
    /// </summary>
    /// <param name="name">A unique name for the editor</param>
    /// <typeparam name="T"></typeparam>
    public void RegisterEditor<T>(string name) where T : ICellEditor
    {
        if (!_editorTypes.ContainsKey(name))
            _editorTypes.Add(name, typeof(T));
        _editorTypes[name] = typeof(T);
    }

    /// <summary>
    /// Registers a cell renderer component with a unique name.
    /// If the renderer already exists, it will override the existing.
    /// </summary>
    /// <param name="name">A unique name for the renderer</param>
    /// <typeparam name="T"></typeparam>
    public void RegisterRenderer<T>(string name) where T : BaseRenderer
    {
        if (!_renderComponentTypes.TryAdd(name, typeof(T)))
            _renderComponentTypes[name] = typeof(T);
    }

    private void RegisterDefaultEditors()
    {
        RegisterEditor<TextEditorComponent>("text");
        RegisterEditor<DateTimeEditorComponent>("datetime");
        RegisterEditor<TextEditorComponent>("boolean");
        RegisterEditor<SelectEditorComponent>("select");
        RegisterEditor<TextareaEditorComponent>("textarea");
    }

    private void RegisterDefaultRenderers()
    {
        RegisterRenderer<TextRenderer>("text");
        RegisterRenderer<SelectRenderer>("select");
        RegisterRenderer<NumberRenderer>("number");
        RegisterRenderer<BoolRenderer>("boolean");
    }

    /// <summary>
    /// Call when the sheet requires re-render
    /// </summary>
    internal void Invalidate()
    {
        if (SheetInvalidated != null)
            SheetInvalidated(this, new SheetInvalidateEventArgs(_dirtyCells));
        _dirtyCells.Clear();
    }

    /// <summary>
    /// Marks the cell as dirty and requiring re-render
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    internal void MarkDirty(int row, int col)
    {
        _dirtyCells.Add((row, col));
    }

    /// <summary>
    /// Mark the cells specified by positions dirty.
    /// </summary>
    /// <param name="positions"></param>
    internal void MarkDirty(IEnumerable<(int row, int col)> positions)
    {
        foreach (var position in positions)
        {
            MarkDirty(position.row, position.col);
        }
    }

    /// <summary>
    /// Inserts delimited text from the given position
    /// And assigns cell's values based on the delimited text (tabs and newlines)
    /// Returns the region of cells that surrounds all cells that are affected
    /// </summary>
    /// <param name="text">The text to insert</param>
    /// <param name="inputPosition">The position where the insertion starts</param>
    /// <param name="newLineDelim">The delimiter that specifies a new-line</param>
    /// <returns>The region of cells that were affected</returns>
    public Region? InsertDelimitedText(string text, CellPosition inputPosition, string newLineDelim = "\n")
    {
        if (inputPosition.IsInvalid)
            return null;

        if (text.EndsWith('\n'))
            text = text.Substring(0, text.Length - 1);
        var lines = text.Split(newLineDelim);

        // We may reach the end of the sheet, so we only need to paste the rows up until the end.
        var endRow = Math.Min(inputPosition.Row + lines.Length - 1, NumRows - 1);
        // Keep track of the maximum end column that we are inserting into
        // This is used to determine the region to return.
        // It is possible that each line is of different cell lengths, so we return the max for all lines
        var maxEndCol = -1;

        var valChanges = new List<CellChange>();

        int lineNo = 0;
        for (int row = inputPosition.Row; row <= endRow; row++)
        {
            var lineSplit = lines[lineNo].Split('\t');
            // Same thing as above with the number of columns
            var endCol = Math.Min(inputPosition.Col + lineSplit.Length - 1, NumCols - 1);

            maxEndCol = Math.Max(endCol, maxEndCol);

            int cellIndex = 0;
            for (int col = inputPosition.Col; col <= endCol; col++)
            {
                valChanges.Add(new CellChange(row, col, lineSplit[cellIndex]));
                cellIndex++;
            }

            lineNo++;
        }

        this.SetCellValues(valChanges);

        return new Region(inputPosition.Row, endRow, inputPosition.Col, maxEndCol);
    }

    #region FORMAT

    /// <summary>
    /// Returns the format that is visible at the cell position row, col.
    /// The order to determine which format is visible is
    /// 1. Cell format (if it exists)
    /// 2. Column format
    /// 3. Row format
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public CellFormat? GetFormat(int row, int col)
    {
        var cell = GetCell(row, col);
        var rowFormat = RowFormats.Get(row);
        var colFormat = ColFormats.Get(col);
        if (cell.Formatting != null)
            return cell.Formatting;
        if (colFormat != null)
            return colFormat;
        else
            return rowFormat;
    }

    /// <summary>
    /// Sets the format for a particular range
    /// </summary>
    /// <param name="cellFormat"></param>
    /// <param name="range"></param>
    public void SetFormat(CellFormat cellFormat, BRange range)
    {
        var cmd = new SetRangeFormatCommand(cellFormat, range);
        Commands.ExecuteCommand(cmd);
    }


    /// <summary>
    /// Performs the setting of formats to the range given, returning the individual cells that were affected.
    /// </summary>
    /// <param name="cellFormat"></param>
    /// <param name="range"></param>
    internal IEnumerable<CellChangedFormat> SetFormatImpl(CellFormat cellFormat, BRange range)
    {
        var changes = new List<CellChangedFormat>();
        foreach (var region in range.Regions)
        {
            changes.AddRange(SetFormatImpl(cellFormat, region));
        }

        return changes;
    }

    /// <summary>
    /// Performs the setting of formats to the region and returns the individual cells that were affected.
    /// </summary>
    /// <param name="cellFormat"></param>
    /// <param name="region"></param>
    /// <returns></returns>
    private List<CellChangedFormat> SetFormatImpl(CellFormat cellFormat, IRegion region)
    {
        // Keep track of all changes to individual cells
        var changes = new List<CellChangedFormat>();
        var colRegions = new List<ColumnRegion>();
        var rowRegions = new List<RowRegion>();

        if (region is ColumnRegion columnRegion)
        {
            changes.AddRange(SetColumnFormatImpl(cellFormat, columnRegion));
            colRegions.Add(columnRegion);
        }


        else if (region is RowRegion rowRegion)
        {
            changes.AddRange(SetRowFormatImpl(cellFormat, rowRegion));
            rowRegions.Add(rowRegion);
        }
        else
        {
            var sheetRegion = region.GetIntersection(this.Region);
            if (sheetRegion != null)
            {
                var positions = new BRange(this, sheetRegion).Positions;
                foreach (var cellPosition in positions)
                {
                    if (!_cellDataStore.Contains(cellPosition.row, cellPosition.col))
                        _cellDataStore.Set(cellPosition.row, cellPosition.col, new Cell());
                    var cell = _cellDataStore.Get(cellPosition.row, cellPosition.col);
                    var oldFormat = cell!.Formatting?.Clone();
                    cell!.MergeFormat(cellFormat);
                    changes.Add(new CellChangedFormat(cellPosition.row, cellPosition.col, oldFormat, cellFormat));
                }
            }
        }

        var args = new FormatChangedEventArgs(changes, colRegions, rowRegions);
        EmitFormatChanged(args);

        return changes;
    }

    internal void EmitFormatChanged(FormatChangedEventArgs args)
    {
        FormatsChanged?.Invoke(this, args);
    }

    private IEnumerable<CellChangedFormat> SetColumnFormatImpl(CellFormat cellFormat, ColumnRegion region)
    {
        // Keep track of individual cell changes
        var changes = new List<CellChangedFormat>();

        ColFormats.Add(new OrderedInterval<CellFormat>(region.Start.Col, region.End.Col, cellFormat));
        // Set the specific format of any non-empty cells in the column range (empty cells are covered by the range format).
        // We do this because cell formatting takes precedence in rendering over col & range formats.
        // So if the cell already has a format, it should be merged.
        var nonEmpty = this.GetNonEmptyCellPositions(region);
        foreach (var posn in nonEmpty)
        {
            var cell = _cellDataStore.Get(posn.row, posn.col);
            var oldFormat = cell!.Formatting?.Clone();
            if (cell.Formatting == null)
                cell.Formatting = cellFormat.Clone();
            else
                cell.Formatting.Merge(cellFormat);

            changes.Add(new CellChangedFormat(posn.row, posn.col, oldFormat, cell.Formatting));
        }

        // Look at the region(s) of overlap with row formats - must make these cells exist and assign formats
        var overlappingRegions = new List<IRegion>();
        foreach (var rowInterval in RowFormats.GetAllIntervals())
        {
            overlappingRegions.Add(new Region(rowInterval.Start, rowInterval.End, region.Start.Col,
                                              region.End.Col));
        }

        foreach (var overlapRegion in overlappingRegions)
        {
            var sheetRegion = overlapRegion.GetIntersection(this.Region);
            if (sheetRegion != null)
            {
                var positions = new BRange(this, sheetRegion).Positions;
                foreach (var position in positions)
                {
                    if (!_cellDataStore.Contains(position.row, position.col))
                        _cellDataStore.Set(position.row, position.col, new Cell());
                    var cell = _cellDataStore.Get(position.row, position.col);
                    var oldFormat = cell!.Formatting?.Clone();
                    _cellDataStore.Get(position.row, position.col)!.MergeFormat(cellFormat);
                    changes.Add(new CellChangedFormat(position.row, position.col, oldFormat, cellFormat));
                }
            }
        }

        return changes;
    }

    private IEnumerable<CellChangedFormat> SetRowFormatImpl(CellFormat cellFormat, RowRegion region)
    {
        // Keep track of individual cell changes
        var changes = new List<CellChangedFormat>();

        RowFormats.Add(new OrderedInterval<CellFormat>(region.Start.Row, region.End.Row, cellFormat));
        // Set the specific format of any non-empty cells in the column range (empty cells are covered by the range format).
        // We do this because cell formatting takes precedence in rendering over col & range formats.
        // So if the cell already has a format, it should be merged.
        var nonEmpty = this.GetNonEmptyCellPositions(region);
        foreach (var posn in nonEmpty)
        {
            var cell = _cellDataStore.Get(posn.row, posn.col);
            var oldFormat = cell!.Formatting?.Clone();
            if (cell.Formatting == null)
                cell.Formatting = cellFormat.Clone();
            else
                cell.Formatting.Merge(cellFormat);

            changes.Add(new CellChangedFormat(posn.row, posn.col, oldFormat, cell.Formatting));
        }

        // Look at the region(s) of overlap with col formats - must make these cells exist and assign formats
        var overlappingRegions = new List<IRegion>();
        foreach (var colInterval in ColFormats.GetAllIntervals())
        {
            overlappingRegions.Add(new Region(region.Start.Row, region.End.Row, colInterval.Start,
                                              colInterval.End));
        }

        foreach (var overlapRegion in overlappingRegions)
        {
            var sheetRegion = overlapRegion.GetIntersection(this.Region);
            if (sheetRegion != null)
            {
                var posns = new BRange(this, sheetRegion).Positions;
                foreach (var posn in posns)
                {
                    if (!_cellDataStore.Contains(posn.row, posn.col))
                        _cellDataStore.Set(posn.row, posn.col, new Cell());
                    var cell = _cellDataStore.Get(posn.row, posn.col);
                    var oldFormat = cell!.Formatting?.Clone();
                    _cellDataStore.Get(posn.row, posn.col)!.MergeFormat(cellFormat);
                    changes.Add(new CellChangedFormat(posn.row, posn.col, oldFormat, cellFormat));
                }
            }
        }

        return changes;
    }


    /// <summary>
    /// Sets the cell format to the format specified. Note the format is set to the format
    /// and is not merged. If the cell is not in our data store it is created.
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <param name="cellFormat"></param>
    /// <returns>A record of what change occured.</returns>
    internal CellChangedFormat SetCellFormat(int row, int col, CellFormat cellFormat)
    {
        CellFormat? oldFormat = null;
        if (!_cellDataStore.Contains(row, col))
            _cellDataStore.Set(row, col, new Cell() { Formatting = cellFormat });
        else
        {
            var cell = _cellDataStore.Get(row, col);
            oldFormat = cell!.Formatting;
            cell!.Formatting = cellFormat;
        }

        return new CellChangedFormat(row, col, oldFormat, cellFormat);
    }

    #endregion

    public string? GetRegionAsDelimitedText(IRegion inputRegion, char tabDelimiter = '\t', string newLineDelim = "\n")
    {
        if (inputRegion.Area == 0)
            return string.Empty;

        var intersection = inputRegion.GetIntersection(this.Region);
        if (intersection == null)
            return null;

        var range = intersection.Copy();

        var strBuilder = new StringBuilder();

        var r0 = range.TopLeft.Row;
        var r1 = range.BottomRight.Row;
        var c0 = range.TopLeft.Col;
        var c1 = range.BottomRight.Col;

        for (int row = r0; row <= r1; row++)
        {
            for (int col = c0; col <= c1; col++)
            {
                var cell = this.GetCell(row, col);
                var value = cell.GetValue();
                if (value == null)
                    strBuilder.Append("");
                else
                {
                    if (value is string s)
                    {
                        strBuilder.Append(s.Replace(newLineDelim, " ").Replace(tabDelimiter, ' '));
                    }
                    else
                    {
                        strBuilder.Append(value);
                    }
                }

                if (col != c1)
                    strBuilder.Append(tabDelimiter);
            }

            strBuilder.Append(newLineDelim);
        }

        return strBuilder.ToString();
    }

    #region MERGES

    #endregion

    private void SelectionOnSelectionChanged(object? sender, IEnumerable<IRegion> e)
    {
        var cellsSelected = this.GetCellsInRegions(e);
        this.CellsSelected?.Invoke(this, new CellsSelectedEventArgs(cellsSelected));
    }
}