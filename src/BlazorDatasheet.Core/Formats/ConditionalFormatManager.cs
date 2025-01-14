﻿using BlazorDatasheet.Core.Data;
using BlazorDatasheet.Core.Data.Cells;
using BlazorDatasheet.Core.Events;
using BlazorDatasheet.Core.Events.Layout;
using BlazorDatasheet.Core.Interfaces;
using BlazorDatasheet.DataStructures.Geometry;
using BlazorDatasheet.DataStructures.RTree;
using BlazorDatasheet.DataStructures.Util;

namespace BlazorDatasheet.Core.Formats;

public class ConditionalFormatManager
{
    private readonly Sheet _sheet;

    private readonly List<ConditionalFormatAbstractBase> _registered = new();

    private readonly RTree<ConditionalFormatSpatialData> _cfTree = new();

    public event EventHandler<ConditionalFormatPreparedEventArgs> ConditionalFormatPrepared;

    public ConditionalFormatManager(Sheet sheet, CellStore cellStore, RowInfoStore rowInfoStore, ColumnInfoStore columnInfoStore)
    {
        _sheet = sheet;
        cellStore.CellsChanged += HandleCellsChanged;
        rowInfoStore.RowInserted += HandleRowInserted;
        rowInfoStore.RowRemoved += HandleRowRemoved;
        columnInfoStore.ColumnRemoved += HandleColRemoved;
        columnInfoStore.ColumnInserted += HandleColInserted;
    }

    private void HandleRowRemoved(object? sender, RowRemovedEventArgs e)
    {
        foreach (var cf in _registered)
        {
            cf.HandleRowRemoved(e.Index);
        }
    }

    private void HandleRowInserted(object? sender, RowInsertedEventArgs e)
    {
        foreach (var cf in _registered)
        {
            cf.HandleRowInserted(e.Index);
        }
    }

    private void HandleColRemoved(object? sender, ColumnRemovedEventArgs e)
    {
        foreach (var cf in _registered)
        {
            cf.HandleColRemoved(e.ColumnIndex);
        }
    }

    private void HandleColInserted(object? sender, ColumnInsertedEventArgs e)
    {
        foreach (var cf in _registered)
        {
            cf.HandleColInserted(e.ColumnIndex);
        }
    }

    /// <summary>
    /// Applies the conditional format specified by "key" to all cells in a region
    /// </summary>
    /// <param name="region"></param>
    /// <param name="conditionalFormat"></param>
    public void Apply(IRegion? region, ConditionalFormatAbstractBase conditionalFormat)
    {
        Apply(new SheetRange(_sheet, region), conditionalFormat);
    }

    /// <summary>
    /// Applies the conditional format to the region
    /// </summary>
    /// <param name="range"></param>
    /// <param name="conditionalFormat"></param>
    public void Apply(SheetRange? range, ConditionalFormatAbstractBase conditionalFormat)
    {
        if (range == null)
            return;
        if (!_registered.Contains(conditionalFormat))
        {
            _registered.Add(conditionalFormat);
            conditionalFormat.Order = _registered.Count - 1;
            conditionalFormat.RegionsChanged += ConditionalFormatOnRegionsChanged;
        }

        conditionalFormat.Add(range);
        conditionalFormat.Prepare(_sheet);
    }

    private void ConditionalFormatOnRegionsChanged(object? sender, ConditionalFormatRegionsChangedEventArgs e)
    {
        if (sender == null)
            return;
        var cf = (ConditionalFormatAbstractBase)sender;
        // Update the cf tree with the new regions & remove old regions
        foreach (var region in e.RegionsRemoved)
        {
            var env = region.ToEnvelope();
            var spatialData = _cfTree.Search(env)
                .FirstOrDefault(x => x.ConditionalFormat == cf && x.Envelope.IsSameAs(env));
            if (spatialData != null)
                _cfTree.Delete(spatialData);
        }

        _cfTree.BulkLoad(e.RegionsAdded.Select(x => new ConditionalFormatSpatialData(cf, x)));
    }

    private void HandleCellsChanged(object? sender, IEnumerable<CellPosition> args)
    {
        // Simply prepare all cells that the conditional format belongs to (if shared)
        var handled = new HashSet<int>();
        foreach (var arg in args)
        {
            var cfs = GetFormatsAppliedToPosition(arg.row, arg.col);
            foreach (var format in cfs)
            {
                if (handled.Contains(format.Order))
                    continue;
                // prepare format (re-compute shared format cache etch.)
                if (format.IsShared)
                {
                    format.Prepare(_sheet);
                    _sheet.MarkDirty(format.GetPositions());
                }

                handled.Add(format.Order);
            }
        }
    }

    private IEnumerable<ConditionalFormatAbstractBase> GetFormatsAppliedToPosition(int row, int col)
    {
        var region = new Region(row, col);
        return _cfTree.Search(region.ToEnvelope())
            .Select(x => x.ConditionalFormat);
    }

    /// <summary>
    /// Applies the conditional format specified by "key" to a particular cell. If setting the format to a number of cells,
    /// prefer setting via a region.
    /// </summary>
    /// <param name="key"></param>
    /// <param name="region"></param>
    public void Apply(ConditionalFormatAbstractBase format, int row, int col)
    {
        Apply(new Region(row, col), format);
    }

    /// <summary>
    /// Returns the format that results from applying all conditional formats to this cell
    /// </summary>
    /// <param name="row"></param>
    /// <param name="col"></param>
    /// <returns></returns>
    public CellFormat? GetFormatResult(int row, int col)
    {
        if (!_sheet.Region.Contains(row, col))
            return null;
        var cfs = GetFormatsAppliedToPosition(row, col);
        CellFormat? initialFormat = null;
        foreach (var format in cfs)
        {
            var apply = format.Predicate?.Invoke(new CellPosition(row, col), _sheet);
            if (apply == false)
                continue;
            var calced = format.CalculateFormat(row, col, _sheet);
            if (initialFormat == null)
                initialFormat = calced;
            else
                initialFormat.Merge(calced);
            if (apply == true && format.StopIfTrue)
                break;
        }

        return initialFormat;
    }

    internal class ConditionalFormatSpatialData : ISpatialData
    {
        internal readonly ConditionalFormatAbstractBase _conditionalFormat;
        public ref readonly ConditionalFormatAbstractBase ConditionalFormat => ref _conditionalFormat;
        private readonly Envelope _envelope;
        public ref readonly Envelope Envelope => ref _envelope;

        internal ConditionalFormatSpatialData(ConditionalFormatAbstractBase cf, IRegion region)
        {
            _envelope = region.ToEnvelope();
            _conditionalFormat = cf;
        }
    }
}