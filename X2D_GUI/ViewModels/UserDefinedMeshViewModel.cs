using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GeotechSlopeApp.Backend;
using GeotechSlopeApp.Models;

namespace GeotechSlopeApp.ViewModels;

/// <summary>Editable row for the node coordinate table.</summary>
public class UdmNodeRow : INotifyPropertyChanged
{
    private int _nodeId;
    private string _x = "0";
    private string _y = "0";

    public int NodeId { get => _nodeId; set { _nodeId = value; OnPropertyChanged(); } }
    public string X    { get => _x;      set { _x = value;      OnPropertyChanged(); } }
    public string Y    { get => _y;      set { _y = value;      OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>Editable row for the element connectivity table.</summary>
public class UdmElementRow : INotifyPropertyChanged
{
    private int _elemId;
    private string _n1 = "1", _n2 = "2", _n3 = "3";
    private string _n4 = "", _n5 = "", _n6 = "", _n7 = "", _n8 = "";
    private string _etype = "1";

    public int    ElemId { get => _elemId; set { _elemId = value; OnPropertyChanged(); } }
    public string N1     { get => _n1;     set { _n1 = value;     OnPropertyChanged(); } }
    public string N2     { get => _n2;     set { _n2 = value;     OnPropertyChanged(); } }
    public string N3     { get => _n3;     set { _n3 = value;     OnPropertyChanged(); } }
    public string N4     { get => _n4;     set { _n4 = value;     OnPropertyChanged(); } }
    public string N5     { get => _n5;     set { _n5 = value;     OnPropertyChanged(); } }
    public string N6     { get => _n6;     set { _n6 = value;     OnPropertyChanged(); } }
    public string N7     { get => _n7;     set { _n7 = value;     OnPropertyChanged(); } }
    public string N8     { get => _n8;     set { _n8 = value;     OnPropertyChanged(); } }
    public string Etype  { get => _etype;  set { _etype = value;  OnPropertyChanged(); } }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}

/// <summary>
/// "Structured Mesh" subtab: parametric Q4/Q8 generators plus manual node/element tables,
/// paste text, or load an example, then generate the mesh into the shared canvas.
/// </summary>
public sealed class UserDefinedMeshViewModel : INotifyPropertyChanged
{
    private readonly MeshViewModel _mesh;
    private ScriptViewModel? _script;
    private BoundaryConditionsViewModel? _boundaryConditions;
    private MaterialPropertiesViewModel? _materialProperties;
    private FEAnalysisViewModel? _feAnalysis;

    private string _elementType = "Q4";
    private bool _smithEmb2dQ8LocalOrder;
    private string? _embankmentDatContent;
    private string _status = "Define nodes and elements, then click Generate.";
    private bool _statusIsError;
    private string _textInput = "";
    private UdmNodeRow? _selectedNodeRow;
    private UdmElementRow? _selectedElementRow;

    // ── Text-import format options ─────────────────────────────────────────────
    public IReadOnlyList<string> TextFormatOptions { get; } = new[]
    {
        "Auto-detect",
        "Nodes only",
        "Elements only",
    };
    private string _textFormat = "Auto-detect";
    public string TextFormat
    {
        get => _textFormat;
        set { _textFormat = value ?? "Auto-detect"; OnPropertyChanged(); }
    }

    // ── Element type options ───────────────────────────────────────────────────
    public IReadOnlyList<string> ElementTypeOptions { get; } = new[] { "Q4", "Q8" };

    public string ElementType
    {
        get => _elementType;
        set
        {
            if (_elementType == value) return;
            var v = (value ?? "Q4").Trim().ToUpperInvariant();
            _elementType = v is "Q4" or "Q8" ? v : "Q4";
            if (string.Equals(_elementType, "Q4", StringComparison.OrdinalIgnoreCase))
                _q8MeshMode = "Embankment";
            _smithEmb2dQ8LocalOrder = string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_q8MeshMode, "Embankment", StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowN4));
            OnPropertyChanged(nameof(ShowN5N6));
            OnPropertyChanged(nameof(ShowN7N8));
            OnPropertyChanged(nameof(ShowParametricMeshParams));
            OnPropertyChanged(nameof(ShowQ8MeshParams));
            OnPropertyChanged(nameof(ParametricMeshParamsHeader));
            OnPropertyChanged(nameof(ShowMeshModeCombo));
            OnPropertyChanged(nameof(ShowLoadReferenceDeck));
            OnPropertyChanged(nameof(ShowQ8EmbankmentPanel));
            OnPropertyChanged(nameof(ShowQ8RectangularPanel));
            UpdateElementRowPlaceholders();
            RefreshQ8MeshPreview();
        }
    }

    // Column visibility helpers
    public bool ShowN4    => _elementType is "Q4" or "Q8";
    public bool ShowN5N6  => _elementType is "Q8";
    public bool ShowN7N8  => _elementType is "Q8";

    // ── Q8 parametric mesh (User-Defined Mesh) ─────────────────────────────────
    public enum Q8MeshGenerationMode { Embankment, Rectangular }

    public IReadOnlyList<string> Q8MeshModeOptions { get; } = new[] { "Embankment", "Rectangular" };

    private string _q8MeshMode = "Embankment";
    public string Q8MeshMode
    {
        get => _q8MeshMode;
        set
        {
            var v = value ?? "Embankment";
            if (_q8MeshMode == v) return;
            _q8MeshMode = v;
            _smithEmb2dQ8LocalOrder = string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase)
                && string.Equals(_q8MeshMode, "Embankment", StringComparison.OrdinalIgnoreCase);
            OnPropertyChanged();
            OnPropertyChanged(nameof(ShowQ8EmbankmentPanel));
            OnPropertyChanged(nameof(ShowQ8RectangularPanel));
            if (string.Equals(_q8MeshMode, "Rectangular", StringComparison.OrdinalIgnoreCase))
                RefreshRectangularExtentsFromGeometry();
            RefreshQ8MeshPreview();
        }
    }

    public bool ShowParametricMeshParams =>
        _elementType is "Q4" or "Q8";
    /// <summary>Backward-compatible alias for XAML bindings.</summary>
    public bool ShowQ8MeshParams => ShowParametricMeshParams;

    public string ParametricMeshParamsHeader =>
        string.Equals(_elementType, "Q4", StringComparison.OrdinalIgnoreCase)
            ? "Q4 Mesh Parameters"
            : "Q8 Mesh Parameters";

    public bool ShowMeshModeCombo =>
        string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase);

    public bool ShowLoadReferenceDeck =>
        string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase);

    public bool ShowQ8EmbankmentPanel =>
        ShowParametricMeshParams
        && (string.Equals(_elementType, "Q4", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_q8MeshMode, "Embankment", StringComparison.OrdinalIgnoreCase));
    public bool ShowQ8RectangularPanel =>
        ShowParametricMeshParams
        && string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase)
        && string.Equals(_q8MeshMode, "Rectangular", StringComparison.OrdinalIgnoreCase);

    public string EmbankmentMaterialHint =>
        "Layered etype: add horizontal layer cuts in Geometry Definition, assign materials per layer, then Generate.";

    private string _q8MeshDensity = "1";
    public string Q8MeshDensity
    {
        get => _q8MeshDensity;
        set { _q8MeshDensity = value ?? "1"; OnPropertyChanged(); RefreshQ8MeshPreview(); }
    }

    private string _p64Nx1 = "20", _p64Nx2 = "10", _p64Ny1 = "10", _p64Ny2 = "5";
    private string _p64W1 = "20", _p64S1 = "20", _p64W2 = "20", _p64H1 = "10", _p64H2 = "5";

    public string P64Nx1 { get => _p64Nx1; set { _p64Nx1 = value ?? "20"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64Nx2 { get => _p64Nx2; set { _p64Nx2 = value ?? "10"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64Ny1 { get => _p64Ny1; set { _p64Ny1 = value ?? "10"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64Ny2 { get => _p64Ny2; set { _p64Ny2 = value ?? "5"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64W1  { get => _p64W1;  set { _p64W1  = value ?? "20"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64S1  { get => _p64S1;  set { _p64S1  = value ?? "20"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64W2  { get => _p64W2;  set { _p64W2  = value ?? "20"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64H1  { get => _p64H1;  set { _p64H1  = value ?? "10"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string P64H2  { get => _p64H2;  set { _p64H2  = value ?? "5";  OnPropertyChanged(); RefreshQ8MeshPreview(); } }

    private bool _rectUseGeometryExtent = true;
    private string _rectXMin = "0", _rectXMax = "60", _rectYMin = "0", _rectYMax = "15";
    private string _rectElementsX = "20", _rectElementsY = "10";
    private string _q8RectGeometryHint = "";

    public bool RectUseGeometryExtent
    {
        get => _rectUseGeometryExtent;
        set
        {
            if (_rectUseGeometryExtent == value) return;
            _rectUseGeometryExtent = value;
            OnPropertyChanged();
            RefreshRectangularExtentsFromGeometry();
            RefreshQ8MeshPreview();
        }
    }

    public string Q8RectGeometryHint
    {
        get => _q8RectGeometryHint;
        private set { _q8RectGeometryHint = value ?? ""; OnPropertyChanged(); }
    }

    public string RectXMin       { get => _rectXMin;       set { _rectXMin = value ?? "0";  OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string RectXMax       { get => _rectXMax;       set { _rectXMax = value ?? "60"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string RectYMin       { get => _rectYMin;       set { _rectYMin = value ?? "0";  OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string RectYMax       { get => _rectYMax;       set { _rectYMax = value ?? "15"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string RectElementsX  { get => _rectElementsX;  set { _rectElementsX = value ?? "20"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }
    public string RectElementsY  { get => _rectElementsY;  set { _rectElementsY = value ?? "10"; OnPropertyChanged(); RefreshQ8MeshPreview(); } }

    private string _q8PreviewDomainText = "";
    private string _q8PreviewNodes = "—";
    private string _q8PreviewElements = "—";

    public string Q8PreviewDomainText { get => _q8PreviewDomainText; private set { _q8PreviewDomainText = value; OnPropertyChanged(); } }
    public string Q8PreviewNodes      { get => _q8PreviewNodes;      private set { _q8PreviewNodes = value;      OnPropertyChanged(); } }
    public string Q8PreviewElements   { get => _q8PreviewElements;   private set { _q8PreviewElements = value;   OnPropertyChanged(); } }

    // ── Node table ─────────────────────────────────────────────────────────────
    public ObservableCollection<UdmNodeRow> NodeRows { get; } = new();

    public UdmNodeRow? SelectedNodeRow
    {
        get => _selectedNodeRow;
        set
        {
            if (ReferenceEquals(_selectedNodeRow, value)) return;
            _selectedNodeRow = value;
            if (value != null && _selectedElementRow != null)
            {
                _selectedElementRow = null;
                OnPropertyChanged(nameof(SelectedElementRow));
            }
            OnPropertyChanged();
            SyncCanvasHighlightFromTableSelection();
        }
    }

    // ── Element table ──────────────────────────────────────────────────────────
    public ObservableCollection<UdmElementRow> ElementRows { get; } = new();

    public UdmElementRow? SelectedElementRow
    {
        get => _selectedElementRow;
        set
        {
            if (ReferenceEquals(_selectedElementRow, value)) return;
            _selectedElementRow = value;
            if (value != null && _selectedNodeRow != null)
            {
                _selectedNodeRow = null;
                OnPropertyChanged(nameof(SelectedNodeRow));
            }
            OnPropertyChanged();
            SyncCanvasHighlightFromTableSelection();
        }
    }

    // ── Status ─────────────────────────────────────────────────────────────────
    public string Status
    {
        get => _status;
        private set { _status = value; OnPropertyChanged(); }
    }

    public bool StatusIsError
    {
        get => _statusIsError;
        private set { _statusIsError = value; OnPropertyChanged(); }
    }

    // ── Text import ────────────────────────────────────────────────────────────
    public string TextInput
    {
        get => _textInput;
        set { _textInput = value; OnPropertyChanged(); }
    }

    // ── Mesh refinement (passthrough to MeshViewModel; drives Generate Hybrid/Quad) ──────────────
    /// <summary>Global density preset options (mirrors the Unstructured Mesh subtab).</summary>
    public IReadOnlyList<string> GlobalDensityPresets => _mesh.GlobalDensityPresets;

    public string GlobalDensityPreset
    {
        get => _mesh.GlobalDensityPreset;
        set { _mesh.GlobalDensityPreset = value; OnPropertyChanged(); OnPropertyChanged(nameof(GlobalMeshSize)); }
    }

    /// <summary>Target element size used by the hybrid/quad sweep generators.</summary>
    public string GlobalMeshSize
    {
        get => _mesh.GlobalMeshSize;
        set { _mesh.GlobalMeshSize = value; OnPropertyChanged(); }
    }

    /// <summary>When true, the generators refine elements toward boundaries using the near-size + growth factor.</summary>
    public bool BoundaryGradationEnabled
    {
        get => _mesh.BoundaryGradationEnabled;
        set { _mesh.BoundaryGradationEnabled = value; OnPropertyChanged(); }
    }

    public string NearBoundaryMeshSize
    {
        get => _mesh.NearBoundaryMeshSize;
        set { _mesh.NearBoundaryMeshSize = value; OnPropertyChanged(); }
    }

    public string BoundaryGrowthFactor
    {
        get => _mesh.BoundaryGrowthFactor;
        set { _mesh.BoundaryGrowthFactor = value; OnPropertyChanged(); }
    }

    // ── Commands ───────────────────────────────────────────────────────────────
    public ICommand AddNodeRowCommand    { get; }
    public ICommand RemoveNodeRowCommand { get; }
    public ICommand AddElementRowCommand    { get; }
    public ICommand RemoveElementRowCommand { get; }
    public ICommand GenerateCommand  { get; }
    public ICommand GenerateHybridMeshCommand { get; }
    public ICommand GenerateQuadSweepMeshCommand { get; }
    public ICommand DiscretizeCommand { get; }
    public ICommand ClearCommand     { get; }
    public ICommand ParseTextCommand { get; }
    public ICommand LoadExampleQ4Command { get; }
    public ICommand LoadExampleQ8Command { get; }
    public ICommand LoadP64ReferenceDeckCommand { get; }
    public ICommand LoadEx2PresetCommand { get; }
    public ICommand CopyTextCommand  { get; }

    public void SetGenerateContext(
        ScriptViewModel? script,
        BoundaryConditionsViewModel? boundaryConditions,
        MaterialPropertiesViewModel? materialProperties = null,
        FEAnalysisViewModel? feAnalysis = null)
    {
        _script = script;
        _boundaryConditions = boundaryConditions;
        _materialProperties = materialProperties;
        _feAnalysis = feAnalysis;
    }

    public UserDefinedMeshViewModel(MeshViewModel mesh)
    {
        _mesh = mesh ?? throw new ArgumentNullException(nameof(mesh));

        AddNodeRowCommand       = new RelayCommand(AddNodeRow);
        RemoveNodeRowCommand    = new RelayCommand(RemoveSelectedNodeRow);
        AddElementRowCommand    = new RelayCommand(AddElementRow);
        RemoveElementRowCommand = new RelayCommand(RemoveSelectedElementRow);
        GenerateCommand         = new RelayCommand(ExecuteGenerate);
        GenerateHybridMeshCommand = new RelayCommand(ExecuteGenerateHybridMesh);
        GenerateQuadSweepMeshCommand = new RelayCommand(ExecuteGenerateQuadSweepMesh);
        DiscretizeCommand = new RelayCommand(ExecuteDiscretize);
        ClearCommand            = new RelayCommand(ExecuteClear);
        ParseTextCommand        = new RelayCommand(ExecuteParseText);
        LoadExampleQ4Command    = new RelayCommand(() => LoadBuiltinExample("Q4"));
        LoadExampleQ8Command    = new RelayCommand(() => LoadBuiltinExample("Q8"));
        LoadP64ReferenceDeckCommand = new RelayCommand(ExecuteLoadP64ReferenceDeck);
        LoadEx2PresetCommand    = new RelayCommand(ExecuteLoadEx2Preset);
        CopyTextCommand         = new RelayCommand(CopyCurrentMeshAsText);
        RefreshRectangularExtentsFromGeometry();
        RefreshQ8MeshPreview();
    }

    /// <summary>Sync rectangular domain textboxes from geometry surface / vertex bounds (same as structured triangle mesh).</summary>
    public void RefreshRectangularExtentsFromGeometry()
    {
        if (!_rectUseGeometryExtent)
        {
            Q8RectGeometryHint = "Using manual X/Y bounds below.";
            return;
        }

        var geom = _mesh.GeometryDefinitionVM;
        if (geom == null)
        {
            Q8RectGeometryHint = "Geometry not linked — using manual bounds.";
            return;
        }

        if (geom.TryGetClosedBoundaryAxisPolygon(out var poly) && poly.Count >= 3)
        {
            double minX = poly.Min(p => p.X);
            double maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y);
            double maxY = poly.Max(p => p.Y);
            RectXMin = minX.ToString("G6", CultureInfo.InvariantCulture);
            RectXMax = maxX.ToString("G6", CultureInfo.InvariantCulture);
            RectYMin = minY.ToString("G6", CultureInfo.InvariantCulture);
            RectYMax = maxY.ToString("G6", CultureInfo.InvariantCulture);
            Q8RectGeometryHint = poly.Count == 4
                ? $"Mapped to 4-corner geometry boundary ({maxX - minX:G6} × {maxY - minY:G6}). Generate does not change geometry."
                : $"Mesh clipped to geometry outline ({poly.Count} vertices). Generate does not change geometry.";
            return;
        }

        if (geom.GeometrySurfaces.Count > 0)
        {
            Q8RectGeometryHint =
                "Complete the surface polygon (lock S1 corners) so the mesh fits the filled region.";
            return;
        }

        var (minXg, maxXg, minYg, maxYg) = geom.GetMeshBoundingBoxBounds();
        double wg = maxXg - minXg;
        double hg = maxYg - minYg;
        if (wg > 1e-12 && hg > 1e-12)
        {
            RectXMin = minXg.ToString("G6", CultureInfo.InvariantCulture);
            RectXMax = maxXg.ToString("G6", CultureInfo.InvariantCulture);
            RectYMin = minYg.ToString("G6", CultureInfo.InvariantCulture);
            RectYMax = maxYg.ToString("G6", CultureInfo.InvariantCulture);
            Q8RectGeometryHint =
                $"No closed boundary loop — using vertex bbox {wg:G6} × {hg:G6}. Add ≥3 points in order along the outline.";
        }
        else
            Q8RectGeometryHint = "Add geometry vertices or define a surface polygon.";
    }

    // ── Row management ─────────────────────────────────────────────────────────

    private void AddNodeRow()
    {
        int nextId = NodeRows.Count > 0 ? NodeRows.Max(r => r.NodeId) + 1 : 1;
        NodeRows.Add(new UdmNodeRow { NodeId = nextId });
    }

    private void RemoveSelectedNodeRow()
    {
        if (SelectedNodeRow != null)
            NodeRows.Remove(SelectedNodeRow);
    }

    private void SyncCanvasHighlightFromTableSelection()
    {
        if (_selectedNodeRow == null && _selectedElementRow == null)
        {
            _mesh.ApplyUserDefinedTableHighlight(null, null, null);
            return;
        }

        if (_selectedNodeRow != null)
        {
            _mesh.ApplyUserDefinedTableHighlight(_selectedNodeRow.NodeId, null, null);
            return;
        }

        _mesh.ApplyUserDefinedTableHighlight(
            null,
            _selectedElementRow!.ElemId,
            CollectConnectivityNodeIds(_selectedElementRow));
    }

    private IEnumerable<int> CollectConnectivityNodeIds(UdmElementRow row)
    {
        var ids = new List<int>();
        void add(string? text)
        {
            if (TryParseNodeRef(text, out int id))
                ids.Add(id);
        }

        add(row.N1);
        add(row.N2);
        add(row.N3);
        if (ShowN4) add(row.N4);
        if (ShowN5N6)
        {
            add(row.N5);
            add(row.N6);
        }
        if (ShowN7N8)
        {
            add(row.N7);
            add(row.N8);
        }

        return ids;
    }

    private void AddElementRow()
    {
        int nextId = ElementRows.Count > 0 ? ElementRows.Max(r => r.ElemId) + 1 : 1;
        ElementRows.Add(new UdmElementRow { ElemId = nextId });
    }

    private void RemoveSelectedElementRow()
    {
        if (SelectedElementRow != null)
            ElementRows.Remove(SelectedElementRow);
    }

    private void UpdateElementRowPlaceholders()
    {
        // no-op for now; visibility is driven by ShowN4/N5N6/N7N8
    }

    // ── Text parsing ───────────────────────────────────────────────────────────

    private void ExecuteParseText()
    {
        var text = _textInput?.Trim();
        if (string.IsNullOrWhiteSpace(text))
        {
            SetStatus("Paste text to import first.", isError: true);
            return;
        }

        try
        {
            bool parsedNodes = false, parsedElems = false;
            string format = _textFormat;

            // Auto-detect: if text contains "NODES" or "ELEMENTS" keywords use sectioned parse
            if (format == "Auto-detect")
            {
                bool hasNodeSec = text.IndexOf("NODES", StringComparison.OrdinalIgnoreCase) >= 0;
                bool hasElemSec = text.IndexOf("ELEMENTS", StringComparison.OrdinalIgnoreCase) >= 0;
                if (hasNodeSec || hasElemSec)
                {
                    ParseSectioned(text, out parsedNodes, out parsedElems);
                }
                else
                {
                    // Try to parse all lines as a space/comma delimited node table first
                    // Heuristic: if every line has exactly 3 tokens it's likely nodes
                    var lines = SplitLines(text);
                    var firstTokenCount = lines.FirstOrDefault(l => l.Length > 0)?.Length ?? 0;
                    if (firstTokenCount == 3)
                        parsedNodes = TryParseNodeLines(lines);
                    else
                        parsedElems = TryParseElementLines(lines);
                }
            }
            else if (format == "Nodes only")
            {
                parsedNodes = TryParseNodeLines(SplitLines(text));
            }
            else if (format == "Elements only")
            {
                parsedElems = TryParseElementLines(SplitLines(text));
            }

            if (!parsedNodes && !parsedElems)
            {
                SetStatus("Could not parse text. Use format: id x y (nodes) or id n1 n2 n3 [n4..] [etype] (elements).", isError: true);
                return;
            }

            var parts = new List<string>();
            if (parsedNodes) parts.Add($"{NodeRows.Count} nodes");
            if (parsedElems) parts.Add($"{ElementRows.Count} elements");
            SetStatus($"Parsed {string.Join(", ", parts)}. Check tables, then click Generate.", isError: false);
        }
        catch (Exception ex)
        {
            SetStatus($"Parse error: {ex.Message}", isError: true);
        }
    }

    private void ParseSectioned(string text, out bool parsedNodes, out bool parsedElems)
    {
        parsedNodes = false;
        parsedElems = false;

        var lines = text.Split('\n');
        var nodeLines = new List<string[]>();
        var elemLines = new List<string[]>();
        string? section = null;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("!") || line.StartsWith("#") || line.StartsWith("//"))
                continue;

            if (line.Equals("NODES", StringComparison.OrdinalIgnoreCase)) { section = "nodes"; continue; }
            if (line.Equals("ELEMENTS", StringComparison.OrdinalIgnoreCase) ||
                line.Equals("CONNECTIVITY", StringComparison.OrdinalIgnoreCase)) { section = "elems"; continue; }

            var tokens = line.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0) continue;

            if (section == "nodes")      nodeLines.Add(tokens);
            else if (section == "elems") elemLines.Add(tokens);
        }

        if (nodeLines.Count > 0) parsedNodes = ApplyNodeLines(nodeLines);
        if (elemLines.Count > 0) parsedElems = ApplyElementLines(elemLines);
    }

    private static string[][] SplitLines(string text)
    {
        return text.Split('\n')
            .Select(l =>
            {
                var trimmed = l.Trim();
                if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("!") ||
                    trimmed.StartsWith("#") || trimmed.StartsWith("//"))
                    return Array.Empty<string>();
                return trimmed.Split(new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries);
            })
            .Where(t => t.Length > 0)
            .ToArray();
    }

    private bool TryParseNodeLines(string[][] tokenLines)
    {
        var rows = new List<(int id, string x, string y)>();
        foreach (var tokens in tokenLines)
        {
            if (tokens.Length < 3) return false;
            if (!int.TryParse(tokens[0], out int id)) return false;
            rows.Add((id, tokens[1], tokens[2]));
        }
        if (rows.Count == 0) return false;
        NodeRows.Clear();
        foreach (var (id, x, y) in rows)
            NodeRows.Add(new UdmNodeRow { NodeId = id, X = x, Y = y });
        return true;
    }

    private bool TryParseElementLines(string[][] tokenLines)
    {
        var rows = new List<UdmElementRow>();
        foreach (var tokens in tokenLines)
        {
            if (tokens.Length < 4) return false;
            if (!int.TryParse(tokens[0], out int id)) return false;
            var r = new UdmElementRow { ElemId = id, N1 = tokens[1], N2 = tokens[2], N3 = tokens[3] };
            if (tokens.Length >= 5) r.N4 = tokens[4];
            if (tokens.Length >= 6) r.N5 = tokens[5];
            if (tokens.Length >= 7) r.N6 = tokens[6];
            if (tokens.Length >= 8) r.N7 = tokens[7];
            if (tokens.Length >= 9) r.N8 = tokens[8];
            if (tokens.Length >= 10) r.Etype = tokens[9];
            rows.Add(r);
        }
        if (rows.Count == 0) return false;
        ElementRows.Clear();
        foreach (var r in rows) ElementRows.Add(r);
        return true;
    }

    private bool ApplyNodeLines(List<string[]> lines)
    {
        var rows = new List<(int id, string x, string y)>();
        foreach (var tokens in lines)
        {
            if (tokens.Length < 3) continue;
            if (!int.TryParse(tokens[0], out int id)) continue;
            rows.Add((id, tokens[1], tokens[2]));
        }
        if (rows.Count == 0) return false;
        NodeRows.Clear();
        foreach (var (id, x, y) in rows)
            NodeRows.Add(new UdmNodeRow { NodeId = id, X = x, Y = y });
        return true;
    }

    private bool ApplyElementLines(List<string[]> lines)
    {
        var rows = new List<UdmElementRow>();
        foreach (var tokens in lines)
        {
            if (tokens.Length < 4) continue;
            if (!int.TryParse(tokens[0], out int id)) continue;
            var r = new UdmElementRow { ElemId = id, N1 = tokens[1], N2 = tokens[2], N3 = tokens[3] };
            if (tokens.Length >= 5) r.N4 = tokens[4];
            if (tokens.Length >= 6) r.N5 = tokens[5];
            if (tokens.Length >= 7) r.N6 = tokens[6];
            if (tokens.Length >= 8) r.N7 = tokens[7];
            if (tokens.Length >= 9) r.N8 = tokens[8];
            if (tokens.Length >= 10) r.Etype = tokens[9];
            rows.Add(r);
        }
        if (rows.Count == 0) return false;
        ElementRows.Clear();
        foreach (var r in rows) ElementRows.Add(r);
        return true;
    }

    // ── Generate ───────────────────────────────────────────────────────────────

    /// <summary>Populate node/element tables from Script Editor mesh import (Smith coords as-is).</summary>
    public void LoadFromDatMesh(
        string elementType,
        IReadOnlyList<(int id, double x, double y)> nodes,
        IReadOnlyList<(int id, int[] conn, int etype)> elements,
        string? datContent = null)
    {
        var kind = (elementType ?? "Q4").Trim().ToUpperInvariant();
        if (kind is not ("T3" or "T6" or "Q4" or "Q8"))
            kind = "Q4";
        if (kind is "T3" or "T6")
        {
            _elementType = kind;
            OnPropertyChanged(nameof(ElementType));
            OnPropertyChanged(nameof(ShowN4));
            OnPropertyChanged(nameof(ShowN5N6));
            OnPropertyChanged(nameof(ShowN7N8));
            OnPropertyChanged(nameof(ShowParametricMeshParams));
            OnPropertyChanged(nameof(ShowQ8MeshParams));
            OnPropertyChanged(nameof(ParametricMeshParamsHeader));
            OnPropertyChanged(nameof(ShowMeshModeCombo));
            OnPropertyChanged(nameof(ShowLoadReferenceDeck));
            OnPropertyChanged(nameof(ShowQ8EmbankmentPanel));
            OnPropertyChanged(nameof(ShowQ8RectangularPanel));
            UpdateElementRowPlaceholders();
            RefreshQ8MeshPreview();
        }
        else
            ElementType = kind;
        _smithEmb2dQ8LocalOrder = kind == "Q8" && !UsesSerendipityQ8Connectivity(datContent);
        _embankmentDatContent = datContent;
        SyncEmbankmentParamsFromDat(datContent, kind);

        NodeRows.Clear();
        foreach (var (id, x, y) in nodes)
        {
            NodeRows.Add(new UdmNodeRow
            {
                NodeId = id,
                X = x.ToString("G", CultureInfo.InvariantCulture),
                Y = y.ToString("G", CultureInfo.InvariantCulture),
            });
        }

        ElementRows.Clear();
        foreach (var (id, conn, etype) in elements)
        {
            var row = new UdmElementRow
            {
                ElemId = id,
                Etype = etype.ToString(CultureInfo.InvariantCulture),
            };
            if (conn.Length > 0) row.N1 = conn[0].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 1) row.N2 = conn[1].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 2) row.N3 = conn[2].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 3) row.N4 = conn[3].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 4) row.N5 = conn[4].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 5) row.N6 = conn[5].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 6) row.N7 = conn[6].ToString(CultureInfo.InvariantCulture);
            if (conn.Length > 7) row.N8 = conn[7].ToString(CultureInfo.InvariantCulture);
            ElementRows.Add(row);
        }

        if (!TryGenerateMeshFromTables())
            SetStatus(
                $"Imported {NodeRows.Count} nodes, {ElementRows.Count} elements — fix table errors and click Generate to show on canvas.",
                isError: true);
    }

    /// <summary>
    /// Pushes <see cref="NodeRows"/> / <see cref="ElementRows"/> into <see cref="MeshViewModel"/>
    /// so Mesh, Load Conditions, and Boundary Conditions canvases display the mesh.
    /// </summary>
    public bool TryGenerateMeshFromTables()
    {
        if (NodeRows.Count == 0)
        {
            SetStatus("Add at least one node row.", isError: true);
            return false;
        }
        if (ElementRows.Count == 0)
        {
            SetStatus("Add at least one element row.", isError: true);
            return false;
        }

        // Determine connectivity count
        bool isT6 = _elementType == "T6";
        bool isQ4 = _elementType == "Q4";
        bool isQ8 = _elementType == "Q8";
        int connNod = isQ8 ? 8 : isT6 ? 6 : isQ4 ? 4 : 3;

        // Parse node rows into a dictionary: NodeId → Point
        var nodeMap = new Dictionary<int, Point>();
        foreach (var nr in NodeRows)
        {
            if (!double.TryParse(nr.X, NumberStyles.Float, CultureInfo.InvariantCulture, out double x))
            { SetStatus($"Node {nr.NodeId}: invalid X = '{nr.X}'.", isError: true); return false; }
            if (!double.TryParse(nr.Y, NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            { SetStatus($"Node {nr.NodeId}: invalid Y = '{nr.Y}'.", isError: true); return false; }
            if (nodeMap.ContainsKey(nr.NodeId))
            { SetStatus($"Duplicate node ID {nr.NodeId}.", isError: true); return false; }
            nodeMap[nr.NodeId] = new Point(x, y);
        }

        int maxNodeId = nodeMap.Keys.Max();
        if (nodeMap.Keys.Min() != 1 || nodeMap.Count != maxNodeId)
        {
            SetStatus("Node IDs must be contiguous 1..N for canvas and BC markers.", isError: true);
            return false;
        }

        // Parse and validate element rows
        var parsedElems = new List<(int elemId, int[] nodes, int etype)>();
        foreach (var er in ElementRows)
        {
            int[] nodes = new int[connNod];
            if (!TryParseNodeRef(er.N1, out nodes[0])) { SetStatus($"Elem {er.ElemId}: invalid N1.", isError: true); return false; }
            if (!TryParseNodeRef(er.N2, out nodes[1])) { SetStatus($"Elem {er.ElemId}: invalid N2.", isError: true); return false; }
            if (!TryParseNodeRef(er.N3, out nodes[2])) { SetStatus($"Elem {er.ElemId}: invalid N3.", isError: true); return false; }
            if (connNod >= 4 && !TryParseNodeRef(er.N4, out nodes[3])) { SetStatus($"Elem {er.ElemId}: invalid N4.", isError: true); return false; }
            if (connNod >= 5 && !TryParseNodeRef(er.N5, out nodes[4])) { SetStatus($"Elem {er.ElemId}: invalid N5.", isError: true); return false; }
            if (connNod >= 6 && !TryParseNodeRef(er.N6, out nodes[5])) { SetStatus($"Elem {er.ElemId}: invalid N6.", isError: true); return false; }
            if (connNod >= 7 && !TryParseNodeRef(er.N7, out nodes[6])) { SetStatus($"Elem {er.ElemId}: invalid N7.", isError: true); return false; }
            if (connNod >= 8 && !TryParseNodeRef(er.N8, out nodes[7])) { SetStatus($"Elem {er.ElemId}: invalid N8.", isError: true); return false; }

            // verify all referenced nodes exist
            for (int k = 0; k < connNod; k++)
                if (!nodeMap.ContainsKey(nodes[k]))
                { SetStatus($"Elem {er.ElemId}: node {nodes[k]} not found in node table.", isError: true); return false; }

            if (!int.TryParse(er.Etype, NumberStyles.Integer, CultureInfo.InvariantCulture, out int et) || et < 1)
                et = 1;
            parsedElems.Add((er.ElemId, nodes, et));
        }

        bool useEmbElementCoords = isQ8 && _smithEmb2dQ8LocalOrder
            && !DatMeshBoundaryImporter.IsExplicitMeshSourceDeck(_embankmentDatContent);
        int embNx1 = 20, embNx2 = 10, embNy1 = 10, embNy2 = 5;
        double embW1 = 20, embS1 = 20, embW2 = 20, embH1 = 10, embH2 = 5;
        double embYOffset = 0;
        if (useEmbElementCoords && !string.IsNullOrWhiteSpace(_embankmentDatContent))
        {
            DatMeshBoundaryImporter.TryGetEmbankmentMeshParams(
                _embankmentDatContent, out embNx1, out embNx2, out embNy1, out embNy2,
                out embW1, out embS1, out embW2, out embH1, out embH2);
            DatMeshBoundaryImporter.TryGetMeshCoordYOffset(_embankmentDatContent, out embYOffset);
        }

        // Populate mesh collections (suspend script auto-refresh — each Add would otherwise rebuild the full .dat)
        _mesh.SelectedMeshInformationRow = null;
        SelectedNodeRow = null;
        SelectedElementRow = null;
        using var pauseDatRefresh = _script?.SuspendDatRefresh();
        using var bulkMeshCanvas = _mesh.BeginMeshCanvasBulkUpdate();
        _mesh.DiscretizationNodes.Clear();
        _mesh.MeshElements.Clear();
        _mesh.MeshInformationRows.Clear();
        _mesh.BoundaryEdgeNodes.Clear();

        for (int id = 1; id <= maxNodeId; id++)
            _mesh.DiscretizationNodes.Add(new MeshDiscretizationNodeDisplay { NodeId = id, Location = nodeMap[id] });

        for (int i = 0; i < parsedElems.Count; i++)
        {
            var (elemId, nodes, et) = parsedElems[i];
            Point p1, p2, p3, p4, p5, p6, p7, p8;
            if (useEmbElementCoords)
            {
                var pts = P64EmbankmentMeshBuilder.GetElementPointsFromConnectivity(
                    elemId, nodes, embNx1, embNx2, embNy1, embNy2, embW1, embS1, embW2, embH1, embH2, embYOffset);
                p1 = new Point(pts[0].x, pts[0].y);
                p2 = new Point(pts[1].x, pts[1].y);
                p3 = new Point(pts[2].x, pts[2].y);
                p4 = new Point(pts[3].x, pts[3].y);
                p5 = new Point(pts[4].x, pts[4].y);
                p6 = new Point(pts[5].x, pts[5].y);
                p7 = new Point(pts[6].x, pts[6].y);
                p8 = new Point(pts[7].x, pts[7].y);
            }
            else
            {
                p1 = nodeMap[nodes[0]];
                p2 = nodeMap[nodes[1]];
                p3 = nodeMap[nodes[2]];
                p4 = connNod >= 4 ? nodeMap[nodes[3]] : default;
                p5 = connNod >= 5 ? nodeMap[nodes[4]] : default;
                p6 = connNod >= 6 ? nodeMap[nodes[5]] : default;
                p7 = connNod >= 7 ? nodeMap[nodes[6]] : default;
                p8 = connNod >= 8 ? nodeMap[nodes[7]] : default;
            }

            PointCollection outline;
            double cx, cy;
            bool isQuadDisplay = false, isQ8Display = false, isT6Display = false;

            if (isQ8)
            {
                outline = Q8MeshDisplayHelper.BuildCornerQuadOutline(
                    p1.X, p1.Y, p2.X, p2.Y, p3.X, p3.Y, p4.X, p4.Y,
                    p5.X, p5.Y, p6.X, p6.Y, p7.X, p7.Y, p8.X, p8.Y,
                    _smithEmb2dQ8LocalOrder);
                (cx, cy) = Q8MeshDisplayHelper.CornerCentroid(
                    _smithEmb2dQ8LocalOrder, p1, p2, p3, p4, p5, p6, p7, p8);
                isQuadDisplay = true; isQ8Display = true;
            }
            else if (isQ4)
            {
                outline = new PointCollection { p1, p2, p3, p4 };
                cx = (p1.X + p2.X + p3.X + p4.X) / 4.0;
                cy = (p1.Y + p2.Y + p3.Y + p4.Y) / 4.0;
                isQuadDisplay = true;
            }
            else if (isT6)
            {
                outline = new PointCollection { p1, p4, p2, p5, p3, p6 };
                cx = (p1.X + p2.X + p3.X) / 3.0;
                cy = (p1.Y + p2.Y + p3.Y) / 3.0;
                isT6Display = true;
            }
            else
            {
                outline = new PointCollection { p1, p2, p3 };
                cx = (p1.X + p2.X + p3.X) / 3.0;
                cy = (p1.Y + p2.Y + p3.Y) / 3.0;
            }

            EnsureCounterClockwise(outline);

            _mesh.MeshElements.Add(new MeshElementDisplay
            {
                Nodes = outline,
                CenterX = cx, CenterY = cy,
                DisplayId = elemId,
                IsActive = true,
                IsHighlighted = false,
                IsQuadrilateral = isQuadDisplay,
                IsQuadraticQuad = isQ8Display,
                IsQuadraticTriangle = isT6Display,
                Q8SmithEmb2dLocalOrder = isQ8Display && _smithEmb2dQ8LocalOrder,
            });

            _mesh.MeshInformationRows.Add(new MeshInformationRow
            {
                SmithEmb2dLocalOrder = isQ8 && _smithEmb2dQ8LocalOrder,
                ElementId = elemId,
                Node1 = nodes[0], Node2 = nodes[1], Node3 = nodes[2],
                Node4 = connNod >= 4 ? nodes[3] : 0,
                Node5 = connNod >= 5 ? nodes[4] : 0,
                Node6 = connNod >= 6 ? nodes[5] : 0,
                Node7 = connNod >= 7 ? nodes[6] : 0,
                Node8 = connNod >= 8 ? nodes[7] : 0,
                Etype = et,
                N1X = p1.X, N1Y = p1.Y,
                N2X = p2.X, N2Y = p2.Y,
                N3X = p3.X, N3Y = p3.Y,
                N4X = p4.X, N4Y = p4.Y,
                N5X = p5.X, N5Y = p5.Y,
                N6X = p6.X, N6Y = p6.Y,
                N7X = p7.X, N7Y = p7.Y,
                N8X = p8.X, N8Y = p8.Y,
                Kind = isQ8 ? "Q8" : isQ4 ? "Q4" : isT6 ? "T6" : "T3",
                Status = "Active"
            });
        }

        _mesh.IsDiscretized = true;

        MeshCanvasAxisFitter.FitToPoints(_mesh.GeometryDefinitionVM, nodeMap.Values);
        SyncCanvasHighlightFromTableSelection();
        // Bulk scope ends here; NotifyMeshCanvasDataChanged runs from Dispose.

        SetStatus(
            $"Generated {parsedElems.Count} {_elementType} elements, {nodeMap.Count} nodes on Mesh / Load / BC canvases. Geometry unchanged.",
            isError: false);
        return true;
    }

    /// <summary>Generate a hybrid mesh from the project geometry (structured quads where mappable,
    /// graded conforming triangles elsewhere) via the shared <c>HybridMeshDriver</c> on the parent
    /// mesh view-model, and surface the result in this tab's status bar.</summary>
    private void ExecuteGenerateHybridMesh()
    {
        Status = _mesh.GenerateHybridMesh(useQ8: string.Equals(ElementType, "Q8", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Generate an all-quad vertical-sweep mesh from the project geometry (imaginary vertical
    /// lines decompose the domain into rectangle/trapezium columns meshed with quads only) via the
    /// shared <c>GradedSurfaceQuadGenerator</c> on the parent mesh view-model.</summary>
    private void ExecuteGenerateQuadSweepMesh()
    {
        Status = _mesh.GenerateQuadSweepMesh(useQ8: string.Equals(ElementType, "Q8", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Discretize all boundaries and material-layer interfaces (horizontal and inclined)
    /// via the same routine as the Unstructured Mesh "Discretize" button on the parent view-model.</summary>
    private void ExecuteDiscretize()
    {
        Status = _mesh.RunRs2dDiscretize();
    }

    private void ExecuteGenerate()
    {
        if (string.Equals(_elementType, "Q4", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGenerateQ4EmbankmentDeck())
                return;
        }

        if (string.Equals(_elementType, "Q8", StringComparison.OrdinalIgnoreCase))
        {
            if (TryGenerateQ8Deck())
                return;
        }

        if (TryGenerateMeshFromTables())
            UpdateScriptEditorAfterGenerate();
    }

    /// <summary>
    /// Builds a parametric Q4 embankment mesh and updates canvas + Script Editor.
    /// </summary>
    private bool TryGenerateQ4EmbankmentDeck()
    {
        if (_boundaryConditions == null || _script == null)
        {
            SetStatus("Generate Q4: script/boundary view models are not wired.", isError: true);
            return false;
        }

        ElementType = "Q4";
        Q8MeshMode = "Embankment";
        _smithEmb2dQ8LocalOrder = false;
        _mesh.ApplyElementTypeFromScript("Q4");

        if (!TryParseEmbankmentParams(out var nx1, out var nx2, out var ny1, out var ny2,
                out var w1, out var s1, out var w2, out var h1, out var h2, out var err))
        {
            SetStatus(err, isError: true);
            return false;
        }

        var opts = P64EmbankmentDatWriter.BuildWriteOptionsFromViewModels(
            nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2,
            _materialProperties,
            _feAnalysis,
            _script.ScriptContent,
            meshType: "Q4");
        var built = P64EmbankmentDatWriter.BuildDefaultMesh(opts);
        var datContent = P64EmbankmentDatWriter.WriteExplicitProgram64Deck(built, opts);
        var modeLabel = opts.NpTypes > 1
            ? $"p64 Q4 two-layer embankment ({opts.NpTypes} materials)"
            : "p64 Q4 embankment";

        if (!DatMeshBoundaryImporter.TryImportToViewModels(datContent, this, _boundaryConditions, out var summary))
        {
            SetStatus("Failed to import generated Q4 deck.", isError: true);
            return false;
        }

        _embankmentDatContent = datContent;
        _script!.ApplyGeneratedP64Deck(datContent);
        _mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
        _mesh.ShowMeshElements = true;
        _mesh.NotifyMeshCanvasDataChanged();
        _boundaryConditions.ShowMarkersAfterMeshImport();

        SetStatus(
            $"Generated {modeLabel}: {summary.NNodes} nodes, {summary.NElements} elements — mesh canvas and Script Editor updated. Geometry unchanged (use Geometry → From mesh boundary to replace outline).",
            isError: false);
        return true;
    }

    /// <summary>
    /// Builds a parametric Q8 mesh from UI settings (embankment or rectangular) and updates canvas + Script Editor.
    /// </summary>
    private bool TryGenerateQ8Deck()
    {
        if (_boundaryConditions == null || _script == null)
        {
            SetStatus("Generate Q8: script/boundary view models are not wired.", isError: true);
            return false;
        }

        ElementType = "Q8";
        _mesh.ApplyElementTypeFromScript("Q8");

        string datContent;
        string modeLabel;
        string? layerNote = null;
        string? validationNote = null;
        bool isEmbankment = string.Equals(_q8MeshMode, "Embankment", StringComparison.OrdinalIgnoreCase);
        _smithEmb2dQ8LocalOrder = isEmbankment;
        var geometry = _mesh.GeometryDefinitionVM;

        if (!TryParseMeshDensity(out var meshDensity, out var densityErr))
        {
            SetStatus(densityErr, isError: true);
            return false;
        }

        if (isEmbankment)
        {
            if (!TryParseEmbankmentParams(
                    out var nx1, out var nx2, out var ny1, out var ny2,
                    out var w1, out var s1, out var w2, out var h1, out var h2,
                    out var err, scaleSubdivisions: false))
            {
                SetStatus(err, isError: true);
                return false;
            }

            nx1 = ApplyMeshDensity(nx1, meshDensity);
            nx2 = ApplyMeshDensity(nx2, meshDensity);

            var opts = P64EmbankmentDatWriter.BuildWriteOptionsFromViewModels(
                nx1, nx2, ny1, ny2, w1, s1, w2, h1, h2,
                _materialProperties,
                _feAnalysis,
                _script?.ScriptContent,
                meshType: "Q8",
                geometry: geometry);

            if (!P64EmbankmentDatWriter.TryBuildLayerAwareMesh(
                    opts, geometry, _materialProperties, meshDensity,
                    out var built, out err))
            {
                SetStatus(err, isError: true);
                return false;
            }

            datContent = P64EmbankmentDatWriter.WriteExplicitProgram64Deck(built!.Mesh, built.Options);
            layerNote = built.AlignmentNote;
            validationNote = built.ValidationMessage;
            modeLabel = built.Options.NpTypes > 1
                ? $"p64 Q8 layered embankment ({built.Options.NpTypes} materials)"
                : "p64 Q8 embankment";
        }
        else
        {
            if (!TryParseRectangularParams(out var xmin, out var xmax, out var ymin, out var ymax,
                    out var nxe, out var nye, out var boundaryPolygon, out var geomNote, out var err,
                    scaleSubdivisions: false))
            {
                SetStatus(err, isError: true);
                return false;
            }

            nxe = ApplyMeshDensity(nxe, meshDensity);

            var baseOpts = new StructuredRectangularQ8DatWriter.WriteOptions
            {
                XMin = xmin, XMax = xmax, YMin = ymin, YMax = ymax,
                ElementsX = nxe, ElementsY = nye,
                BoundaryPolygon = boundaryPolygon,
                MappedToGeometry = boundaryPolygon != null,
            };

            if (!StructuredRectangularQ8DatWriter.TryBuildLayerAwareMesh(
                    baseOpts, geometry, _materialProperties, meshDensity,
                    out var built, out err))
            {
                SetStatus(err, isError: true);
                return false;
            }

            datContent = StructuredRectangularQ8DatWriter.WriteExplicitDeck(built!.Mesh, built.Options);
            layerNote = built.AlignmentNote;
            validationNote = built.ValidationMessage;
            modeLabel = built.Options.NpTypes > 1
                ? $"layered rectangular Q8 ({built.Options.NpTypes} materials)"
                : string.IsNullOrEmpty(geomNote)
                    ? "structured rectangular Q8"
                    : $"structured rectangular Q8 ({geomNote})";
        }

        if (!DatMeshBoundaryImporter.TryImportToViewModels(datContent, this, _boundaryConditions, out var summary))
        {
            SetStatus("Failed to import generated Q8 deck.", isError: true);
            return false;
        }

        if (geometry != null
            && _materialProperties != null
            && geometry.MaterialLayerCutElevations.Count > 0)
        {
            DatEtypeLayeringSync.TrySyncLayeringFromMeshEtype(
                datContent, _mesh, _materialProperties, geometry, out _);
        }

        _embankmentDatContent = datContent;
        _script!.ApplyGeneratedP64Deck(datContent);
        _mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
        _mesh.ShowMeshElements = true;
        _mesh.NotifyMeshCanvasDataChanged();
        _boundaryConditions.ShowMarkersAfterMeshImport();

        var status = $"Generated {modeLabel}: {summary.NNodes} nodes, {summary.NElements} elements — mesh canvas and Script Editor updated.";
        if (!string.IsNullOrEmpty(layerNote))
            status += $" Layer alignment: {layerNote}.";
        if (!string.IsNullOrEmpty(validationNote))
            status += $" {validationNote}";
        if (geometry?.MaterialLayerCutElevations.Count == 0)
            status += " Add layers in Geometry Definition to enforce material interfaces.";
        else
            status += " Geometry unchanged (use Geometry → From mesh boundary to replace outline).";

        SetStatus(status, isError: false);
        return true;
    }

    private void ExecuteLoadP64ReferenceDeck()
    {
        if (_boundaryConditions == null || _script == null)
        {
            SetStatus("Load reference deck: script/boundary view models are not wired.", isError: true);
            return;
        }

        var canonical = P64EmbankmentDeckPaths.ResolveProgram64Explicit();
        if (string.IsNullOrEmpty(canonical) || !File.Exists(canonical))
        {
            SetStatus("Reference deck p64_embankment_program64.dat not found.", isError: true);
            return;
        }

        ElementType = "Q8";
        Q8MeshMode = "Embankment";
        _smithEmb2dQ8LocalOrder = true;
        _mesh.ApplyElementTypeFromScript("Q8");

        var datContent = File.ReadAllText(canonical);
        if (!DatMeshBoundaryImporter.TryImportToViewModels(datContent, this, _boundaryConditions, out var summary))
        {
            SetStatus("Failed to import reference p64 Q8 deck.", isError: true);
            return;
        }

        _embankmentDatContent = datContent;
        _script!.ApplyGeneratedP64Deck(datContent);
        _mesh.MeshPopupSelectedTabIndex = MeshViewModel.MeshPopupUserDefinedTabIndex;
        _mesh.ShowMeshElements = true;
        _mesh.NotifyMeshCanvasDataChanged();
        _boundaryConditions.ShowMarkersAfterMeshImport();

        SetStatus(
            $"Loaded reference p64 deck: {summary.NNodes} nodes, {summary.NElements} elements. Geometry unchanged.",
            isError: false);
    }

    private void UpdateScriptEditorAfterGenerate()
    {
        if (_script == null)
            return;

        if ((_elementType is "Q8" or "Q4")
            && !string.IsNullOrWhiteSpace(_embankmentDatContent))
        {
            _script.ApplyGeneratedP64Deck(_embankmentDatContent);
            return;
        }

        _script.TrySyncDiscretizedMeshToScriptDat();
    }

    // ── Q8 parametric helpers ──────────────────────────────────────────────────

    private void RefreshQ8MeshPreview()
    {
        if (!ShowParametricMeshParams)
        {
            Q8PreviewDomainText = "";
            Q8PreviewNodes = "—";
            Q8PreviewElements = "—";
            return;
        }

        if (!TryParseMeshDensity(out var density, out _))
            density = 1.0;

        bool isQ4 = string.Equals(_elementType, "Q4", StringComparison.OrdinalIgnoreCase);
        string meshKind = isQ4 ? "Q4" : "Q8";

        if (isQ4 || string.Equals(_q8MeshMode, "Embankment", StringComparison.OrdinalIgnoreCase))
        {
            if (!TryParseEmbankmentParamsForPreview(density, out var nx1, out var nx2, out var ny1, out var ny2,
                    out var w1, out var s1, out var w2, out var h1, out var h2))
            {
                Q8PreviewDomainText = "Enter valid embankment parameters.";
                Q8PreviewNodes = "—";
                Q8PreviewElements = "—";
                return;
            }

            var (nn, nels) = P64EmbankmentMeshBuilder.PreviewEmbankmentCounts(nx1, nx2, ny1, ny2, meshKind);
            double xMax = w1 + s1 + w2;
            double yMax = h1 + h2;
            Q8PreviewDomainText = $"Domain X: 0 … {xMax:G6}   Y: 0 … {yMax:G6} (Smith crest-at-top, {meshKind})";
            Q8PreviewNodes = nn > 0 ? nn.ToString(CultureInfo.InvariantCulture) : "—";
            Q8PreviewElements = nels.ToString(CultureInfo.InvariantCulture);
        }
        else
        {
            if (!TryParseRectangularParamsForPreview(density, out var xmin, out var xmax, out var ymin, out var ymax,
                    out var nxe, out var nye, out var boundaryPolygon, out _))
            {
                Q8PreviewDomainText = Q8RectGeometryHint.Length > 0
                    ? Q8RectGeometryHint
                    : "Enter valid rectangular domain parameters.";
                Q8PreviewNodes = "—";
                Q8PreviewElements = "—";
                return;
            }

            int nn, nels;
            if (boundaryPolygon is { Count: >= 3 })
            {
                try
                {
                    var preview = StructuredRectangularQ8MeshBuilder.BuildWithStats(
                        xmin, xmax, ymin, ymax, nxe, nye,
                        new StructuredRectangularQ8MeshBuilder.BuildOptions { BoundaryPolygon = boundaryPolygon });
                    nn = preview.Mesh.Nn;
                    nels = preview.Mesh.Nels;
                }
                catch
                {
                    (nn, nels) = StructuredRectangularQ8MeshBuilder.PreviewCounts(nxe, nye);
                }
            }
            else
                (nn, nels) = StructuredRectangularQ8MeshBuilder.PreviewCounts(nxe, nye);

            var geomTag = boundaryPolygon is { Count: 4 } ? "bilinear quad map"
                : boundaryPolygon != null ? "clipped to polygon"
                : "axis box";
            Q8PreviewDomainText =
                $"Domain X: {xmin:G6} … {xmax:G6}   Y: {ymin:G6} … {ymax:G6} ({geomTag})";
            Q8PreviewNodes = nn.ToString(CultureInfo.InvariantCulture);
            Q8PreviewElements = nels.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static int ApplyMeshDensity(int n, double density) =>
        Math.Max(1, (int)Math.Round(n * density, MidpointRounding.AwayFromZero));

    private bool TryParseMeshDensity(out double density, out string error)
    {
        error = "";
        if (!double.TryParse(_q8MeshDensity?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out density)
            || density < 0.25)
        {
            error = "Mesh density must be a number ≥ 0.25.";
            density = 1;
            return false;
        }
        return true;
    }

    private bool TryParseEmbankmentParams(
        out int nx1, out int nx2, out int ny1, out int ny2,
        out double w1, out double s1, out double w2, out double h1, out double h2,
        out string error,
        bool scaleSubdivisions = true)
    {
        nx1 = nx2 = ny1 = ny2 = 0;
        w1 = s1 = w2 = h1 = h2 = 0;
        error = "";

        if (!TryParseMeshDensity(out var density, out error))
            return false;

        if (!TryParsePositiveInt(_p64Nx1, "Elements X (zone 1)", out var rawNx1, out error) ||
            !TryParsePositiveInt(_p64Nx2, "Elements X (zone 2)", out var rawNx2, out error) ||
            !TryParsePositiveInt(_p64Ny1, "Elements Y (main)", out var rawNy1, out error) ||
            !TryParsePositiveInt(_p64Ny2, "Elements Y (slope)", out var rawNy2, out error) ||
            !TryParsePositiveDouble(_p64W1, "Width w1", out w1, out error) ||
            !TryParsePositiveDouble(_p64S1, "Slope length s1", out s1, out error) ||
            !TryParsePositiveDouble(_p64W2, "Crest width w2", out w2, out error) ||
            !TryParsePositiveDouble(_p64H1, "Height h1", out h1, out error) ||
            !TryParsePositiveDouble(_p64H2, "Height h2", out h2, out error))
            return false;

        if (scaleSubdivisions)
        {
            nx1 = ApplyMeshDensity(rawNx1, density);
            nx2 = ApplyMeshDensity(rawNx2, density);
            ny1 = ApplyMeshDensity(rawNy1, density);
            ny2 = ApplyMeshDensity(rawNy2, density);
        }
        else
        {
            nx1 = rawNx1;
            nx2 = rawNx2;
            ny1 = rawNy1;
            ny2 = rawNy2;
        }
        return true;
    }

    private bool TryParseRectangularParams(
        out double xmin, out double xmax, out double ymin, out double ymax,
        out int nxe, out int nye,
        out IReadOnlyList<(double x, double y)>? boundaryPolygon,
        out string geometryNote,
        out string error,
        bool scaleSubdivisions = true)
    {
        xmin = xmax = ymin = ymax = 0;
        nxe = nye = 0;
        boundaryPolygon = null;
        geometryNote = "";
        error = "";

        if (!TryParseMeshDensity(out var density, out error))
            return false;

        if (!TryResolveRectangularDomain(out xmin, out xmax, out ymin, out ymax,
                out boundaryPolygon, out geometryNote, out error))
            return false;

        if (!TryParsePositiveInt(_rectElementsX, "Elements in X", out var rawNxe, out error) ||
            !TryParsePositiveInt(_rectElementsY, "Elements in Y", out var rawNye, out error))
            return false;

        if (scaleSubdivisions)
        {
            nxe = ApplyMeshDensity(rawNxe, density);
            nye = ApplyMeshDensity(rawNye, density);
        }
        else
        {
            nxe = rawNxe;
            nye = rawNye;
        }
        return true;
    }

    private bool TryResolveRectangularDomain(
        out double xmin, out double xmax, out double ymin, out double ymax,
        out IReadOnlyList<(double x, double y)>? boundaryPolygon,
        out string geometryNote,
        out string error)
    {
        xmin = xmax = ymin = ymax = 0;
        boundaryPolygon = null;
        geometryNote = "";
        error = "";

        if (_rectUseGeometryExtent)
        {
            var geom = _mesh.GeometryDefinitionVM;
            if (geom == null)
            {
                error = "Geometry is not linked — disable 'Fit to geometry' or link Geometry Definition.";
                return false;
            }

            if (geom.TryGetClosedBoundaryAxisPolygon(out var poly) && poly.Count >= 3)
            {
                xmin = poly.Min(p => p.X);
                xmax = poly.Max(p => p.X);
                ymin = poly.Min(p => p.Y);
                ymax = poly.Max(p => p.Y);
                boundaryPolygon = poly.Select(p => (p.X, p.Y)).ToList();
                geometryNote = poly.Count == 4 ? "4-corner boundary" : $"polygon {poly.Count} verts";
                return xmax > xmin && ymax > ymin;
            }

            if (geom.GeometrySurfaces.Count > 0)
            {
                error =
                    "Complete the surface polygon for S1 (lock vertices or pick boundary lines) so the structured Q8 mesh fits the geometry region.";
                return false;
            }

            var (minX, maxX, minY, maxY) = geom.GetMeshBoundingBoxBounds();
            xmin = minX;
            xmax = maxX;
            ymin = minY;
            ymax = maxY;
            if (xmax <= xmin || ymax <= ymin)
            {
                error = "Geometry has no positive extent (add vertices or define a surface polygon).";
                return false;
            }
            geometryNote = "vertex bbox (no closed loop)";
            return true;
        }

        if (!double.TryParse(_rectXMin?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out xmin) ||
            !double.TryParse(_rectXMax?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out xmax) ||
            !double.TryParse(_rectYMin?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ymin) ||
            !double.TryParse(_rectYMax?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out ymax))
        {
            error = "Enter valid numeric domain bounds (X min/max, Y min/max).";
            return false;
        }

        if (xmax <= xmin || ymax <= ymin)
        {
            error = "X max must exceed X min and Y max must exceed Y min.";
            return false;
        }
        return true;
    }

    private bool TryParseEmbankmentParamsForPreview(
        double density,
        out int nx1, out int nx2, out int ny1, out int ny2,
        out double w1, out double s1, out double w2, out double h1, out double h2)
    {
        nx1 = nx2 = ny1 = ny2 = 0;
        w1 = s1 = w2 = h1 = h2 = 0;
        if (!TryParsePositiveInt(_p64Nx1, "", out var rawNx1, out _) ||
            !TryParsePositiveInt(_p64Nx2, "", out var rawNx2, out _) ||
            !TryParsePositiveInt(_p64Ny1, "", out var rawNy1, out _) ||
            !TryParsePositiveInt(_p64Ny2, "", out var rawNy2, out _) ||
            !TryParsePositiveDouble(_p64W1, "", out w1, out _) ||
            !TryParsePositiveDouble(_p64S1, "", out s1, out _) ||
            !TryParsePositiveDouble(_p64W2, "", out w2, out _) ||
            !TryParsePositiveDouble(_p64H1, "", out h1, out _) ||
            !TryParsePositiveDouble(_p64H2, "", out h2, out _))
            return false;

        nx1 = ApplyMeshDensity(rawNx1, density);
        nx2 = ApplyMeshDensity(rawNx2, density);
        ny1 = ApplyMeshDensity(rawNy1, density);
        ny2 = ApplyMeshDensity(rawNy2, density);
        return true;
    }

    private bool TryParseRectangularParamsForPreview(
        double density,
        out double xmin, out double xmax, out double ymin, out double ymax,
        out int nxe, out int nye,
        out IReadOnlyList<(double x, double y)>? boundaryPolygon,
        out string geometryNote)
    {
        xmin = xmax = ymin = ymax = 0;
        nxe = nye = 0;
        boundaryPolygon = null;
        geometryNote = "";
        if (!TryResolveRectangularDomain(out xmin, out xmax, out ymin, out ymax,
                out boundaryPolygon, out geometryNote, out _))
            return false;
        if (!TryParsePositiveInt(_rectElementsX, "", out var rawNxe, out _) ||
            !TryParsePositiveInt(_rectElementsY, "", out var rawNye, out _))
            return false;

        nxe = ApplyMeshDensity(rawNxe, density);
        nye = ApplyMeshDensity(rawNye, density);
        return true;
    }

    private static bool TryParsePositiveInt(string? text, string label, out int value, out string error)
    {
        error = "";
        value = 0;
        if (!int.TryParse(text?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) || value < 1)
        {
            error = string.IsNullOrEmpty(label)
                ? ""
                : $"Enter {label} as an integer ≥ 1.";
            return false;
        }
        return true;
    }

    private static bool TryParsePositiveDouble(string? text, string label, out double value, out string error)
    {
        error = "";
        value = 0;
        if (!double.TryParse(text?.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value) || value <= 0)
        {
            error = string.IsNullOrEmpty(label)
                ? ""
                : $"Enter {label} as a positive number.";
            return false;
        }
        return true;
    }

    private void ResetQ8EmbankmentDefaults()
    {
        Q8MeshMode = "Embankment";
        Q8MeshDensity = "1";
        P64Nx1 = "20";
        P64Nx2 = "10";
        P64Ny1 = "10";
        P64Ny2 = "5";
        P64W1 = "20";
        P64S1 = "20";
        P64W2 = "20";
        P64H1 = "10";
        P64H2 = "5";
    }

    /// <summary>Smith &amp; Griffiths Example 2 two-layer slope (book figure).</summary>
    private void ResetQ8Ex2Defaults()
    {
        Q8MeshMode = "Embankment";
        Q8MeshDensity = "1";
        P64Nx1 = "5";
        P64Nx2 = "5";
        P64Ny1 = "5";
        P64Ny2 = "5";
        P64W1 = "1.2";
        P64S1 = "2";
        P64W2 = "1.2";
        P64H1 = "1";
        P64H2 = "1";
    }

    private void ExecuteLoadEx2Preset()
    {
        ResetQ8Ex2Defaults();
        if (string.Equals(_elementType, "T3", StringComparison.OrdinalIgnoreCase)
            || string.Equals(_elementType, "T6", StringComparison.OrdinalIgnoreCase))
            ElementType = "Q4";
        NodeRows.Clear();
        ElementRows.Clear();
        SetStatus(
            "Ex2 two-layer embankment preset (w1=1.2, nx=5). Add 2 materials for layered etype, then Generate Mesh.",
            isError: false);
    }

    /// <summary>
    /// Copies Smith embankment parametric fields from a loaded p64 Q8 deck into the UDM panel
    /// so density-based regeneration stays in the same physical domain as the script.
    /// </summary>
    private void SyncEmbankmentParamsFromDat(string? datContent, string elementKind)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return;

        P610AnalysisOptionsHelper.TryParse(datContent, out var options);
        var meshType = !string.IsNullOrWhiteSpace(options?.MeshType)
            ? options.MeshType.Trim()
            : elementKind;
        if (!string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(meshType, "Q4", StringComparison.OrdinalIgnoreCase))
            return;
        if (!DatMeshBoundaryImporter.IsSmithEmbankmentQ8Deck(options, datContent)
            && !IsSmithEmbankmentQ4Deck(options, datContent))
            return;

        DatMeshBoundaryImporter.TryGetEmbankmentMeshParams(
            datContent,
            out var nx1, out var nx2, out var ny1, out var ny2,
            out var w1, out var s1, out var w2, out var h1, out var h2);

        Q8MeshMode = "Embankment";
        _smithEmb2dQ8LocalOrder = string.Equals(meshType, "Q8", StringComparison.OrdinalIgnoreCase);
        P64Nx1 = nx1.ToString(CultureInfo.InvariantCulture);
        P64Nx2 = nx2.ToString(CultureInfo.InvariantCulture);
        P64Ny1 = ny1.ToString(CultureInfo.InvariantCulture);
        P64Ny2 = ny2.ToString(CultureInfo.InvariantCulture);
        P64W1 = w1.ToString("G", CultureInfo.InvariantCulture);
        P64S1 = s1.ToString("G", CultureInfo.InvariantCulture);
        P64W2 = w2.ToString("G", CultureInfo.InvariantCulture);
        P64H1 = h1.ToString("G", CultureInfo.InvariantCulture);
        P64H2 = h2.ToString("G", CultureInfo.InvariantCulture);
        OnPropertyChanged(nameof(ShowQ8EmbankmentPanel));
        OnPropertyChanged(nameof(ShowQ8RectangularPanel));
        RefreshQ8MeshPreview();
    }

    private static bool IsSmithEmbankmentQ4Deck(P610AnalysisOptions? options, string? datContent)
    {
        if (string.IsNullOrWhiteSpace(datContent))
            return false;
        var meshType = !string.IsNullOrWhiteSpace(options?.MeshType)
            ? options.MeshType.Trim()
            : P610AnalysisOptionsHelper.TryResolveMeshTypeFromDat(datContent, out var mt) ? mt : "";
        if (!string.Equals(meshType, "Q4", StringComparison.OrdinalIgnoreCase))
            return false;
        return datContent.Contains("embankment_nx1", StringComparison.OrdinalIgnoreCase)
            || datContent.Contains("embankment_w1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(FemCppProgramRouter.ResolveProgramId(datContent), "p64", StringComparison.OrdinalIgnoreCase);
    }

    // ── Clear ──────────────────────────────────────────────────────────────────

    private void ExecuteClear()
    {
        _mesh.SelectedMeshInformationRow = null;
        SelectedNodeRow = null;
        SelectedElementRow = null;
        _mesh.DiscretizationNodes.Clear();
        _mesh.MeshElements.Clear();
        _mesh.MeshInformationRows.Clear();
        _mesh.BoundaryEdgeNodes.Clear();
        _mesh.IsDiscretized = false;
        SetStatus("Mesh cleared.", false);
    }

    // ── Load built-in examples ─────────────────────────────────────────────────

    private void LoadBuiltinExample(string kind)
    {
        ElementType = kind;
        NodeRows.Clear();
        ElementRows.Clear();

        switch (kind)
        {
            case "Q4":
                ResetQ8EmbankmentDefaults();
                Q8MeshMode = "Embankment";
                _smithEmb2dQ8LocalOrder = false;
                NodeRows.Clear();
                ElementRows.Clear();
                SetStatus(
                    "Q4 embankment preset — adjust parameters below, then click Generate Mesh.",
                    isError: false);
                break;

            case "Q8":
                ResetQ8EmbankmentDefaults();
                _smithEmb2dQ8LocalOrder = true;
                SetStatus(
                    "Q8 embankment preset — adjust parameters below, then click Generate Mesh (default ≈1141 nodes, 350 elements).",
                    isError: false);
                break;
        }
    }

    private string BuildExampleText(string kind)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"! Structured Mesh — {kind} example");
        sb.AppendLine("NODES");
        foreach (var n in NodeRows)
            sb.AppendLine($"{n.NodeId}  {n.X}  {n.Y}");
        sb.AppendLine("ELEMENTS");
        foreach (var e in ElementRows)
        {
            sb.Append($"{e.ElemId}  {e.N1}  {e.N2}  {e.N3}");
            if (!string.IsNullOrWhiteSpace(e.N4)) sb.Append($"  {e.N4}");
            if (!string.IsNullOrWhiteSpace(e.N5)) sb.Append($"  {e.N5}");
            if (!string.IsNullOrWhiteSpace(e.N6)) sb.Append($"  {e.N6}");
            if (!string.IsNullOrWhiteSpace(e.N7)) sb.Append($"  {e.N7}");
            if (!string.IsNullOrWhiteSpace(e.N8)) sb.Append($"  {e.N8}");
            sb.Append($"  {e.Etype}");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    // ── Copy current mesh as text ──────────────────────────────────────────────

    private void CopyCurrentMeshAsText()
    {
        if (NodeRows.Count == 0 && ElementRows.Count == 0)
        {
            SetStatus("Nothing to copy — add nodes and elements first.", isError: true);
            return;
        }
        var text = BuildExampleText(_elementType);
        try { System.Windows.Clipboard.SetText(text); }
        catch { /* clipboard access can fail in headless contexts */ }
        SetStatus("Copied to clipboard.", isError: false);
    }

    // ── Helpers ────────────────────────────────────────────────────────────────

    private static bool UsesSerendipityQ8Connectivity(string? datContent) =>
        DatMeshBoundaryImporter.IsSerendipityRectangularQ8Deck(datContent);

    private static bool TryParseNodeRef(string? s, out int value)
    {
        value = 0;
        return !string.IsNullOrWhiteSpace(s) &&
               int.TryParse(s.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
               value > 0;
    }

    private static void EnsureCounterClockwise(PointCollection pts)
    {
        if (pts.Count < 3) return;
        double area = 0;
        for (int i = 0, j = pts.Count - 1; i < pts.Count; j = i++)
            area += (pts[j].X + pts[i].X) * (pts[j].Y - pts[i].Y);
        if (area > 0) // clockwise → reverse
        {
            var rev = pts.Reverse().ToList();
            pts.Clear();
            foreach (var p in rev) pts.Add(p);
        }
    }

    private void SetStatus(string msg, bool isError)
    {
        Status = msg;
        StatusIsError = isError;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? n = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
}
