using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GeotechSlopeApp.Models.Support;

public enum FeSupportType
{
    Anchor,
    SoilNail,
    Geotextile,
    Pile,
    SheetPileWall,
    AnchoredSheetPileWall,
    Strut,
    ShotcreteFace,
    GabionWall,
    RetainingWall,
    MseWall,
    GenericWall
}

public static class FeSupportTypeHelpers
{
    public static bool IsFaceSupport(FeSupportType type) => type is
        FeSupportType.ShotcreteFace
        or FeSupportType.GabionWall
        or FeSupportType.RetainingWall
        or FeSupportType.MseWall
        or FeSupportType.GenericWall;

    public static bool IsBarElement(FeSupportType type) => !IsFaceSupport(type);

    public static string Family(FeSupportType type) => type switch
    {
        FeSupportType.Anchor or FeSupportType.SoilNail or FeSupportType.Geotextile => "Reinforcement",
        FeSupportType.Pile or FeSupportType.SheetPileWall
            or FeSupportType.AnchoredSheetPileWall or FeSupportType.Strut => "Deep / strut",
        _ => "Wall / face"
    };

    public static string DisplayName(FeSupportType type) => type switch
    {
        FeSupportType.Anchor => "Anchor",
        FeSupportType.SoilNail => "Soil Nail",
        FeSupportType.Geotextile => "Geotextile",
        FeSupportType.Pile => "Pile",
        FeSupportType.SheetPileWall => "Sheet Pile Wall",
        FeSupportType.AnchoredSheetPileWall => "Anchored Sheet Pile",
        FeSupportType.Strut => "Strut",
        FeSupportType.ShotcreteFace => "Shotcrete",
        FeSupportType.GabionWall => "Gabion Wall",
        FeSupportType.RetainingWall => "Retaining Wall",
        FeSupportType.MseWall => "MSE Wall",
        FeSupportType.GenericWall => "Generic Wall",
        _ => type.ToString()
    };

    public static string DatToken(FeSupportType type) => type.ToString().ToLowerInvariant();
}

/// <summary>Named product set: FE stiffness plus optional LEM-style capacity fields.</summary>
public sealed class FeSupportPropertySet : INotifyPropertyChanged
{
    private int _id = 1;
    private string _name = "Support 1";
    private FeSupportType _type = FeSupportType.SoilNail;
    private double _axialStiffnessKn = 5.0e4;
    private double _prestressKn;
    private double _outOfPlaneSpacingM = 1.5;
    private double _tensileCapacityKn = 150.0;
    private double _bondStrengthKnPerM = 50.0;
    private double _bondLengthM = 3.0;
    private double _freeLengthM;
    private double _shearCapacityKn = 100.0;
    private int _installStage = 1;
    private bool _excludeFromSrf = true;

    public int Id { get => _id; set => Set(ref _id, value); }
    public string Name { get => _name; set => Set(ref _name, value ?? ""); }
    public FeSupportType Type { get => _type; set { if (Set(ref _type, value)) OnPropertyChanged(nameof(TypeName)); } }
    public string TypeName => FeSupportTypeHelpers.DisplayName(Type);
    public double AxialStiffnessKn { get => _axialStiffnessKn; set => Set(ref _axialStiffnessKn, Math.Max(0.0, value)); }
    public double PrestressKn { get => _prestressKn; set => Set(ref _prestressKn, value); }
    public double OutOfPlaneSpacingM { get => _outOfPlaneSpacingM; set => Set(ref _outOfPlaneSpacingM, Math.Max(0.05, value)); }
    public double TensileCapacityKn { get => _tensileCapacityKn; set => Set(ref _tensileCapacityKn, Math.Max(0.0, value)); }
    public double BondStrengthKnPerM { get => _bondStrengthKnPerM; set => Set(ref _bondStrengthKnPerM, Math.Max(0.0, value)); }
    public double BondLengthM { get => _bondLengthM; set => Set(ref _bondLengthM, Math.Max(0.0, value)); }
    public double FreeLengthM { get => _freeLengthM; set => Set(ref _freeLengthM, Math.Max(0.0, value)); }
    public double ShearCapacityKn { get => _shearCapacityKn; set => Set(ref _shearCapacityKn, Math.Max(0.0, value)); }
    public int InstallStage { get => _installStage; set => Set(ref _installStage, Math.Max(0, value)); }
    public bool ExcludeFromSrf { get => _excludeFromSrf; set => Set(ref _excludeFromSrf, value); }

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(name);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

/// <summary>One 2-node support placement (head on the face, tail in the mass).</summary>
public sealed class FeSupportElement : INotifyPropertyChanged
{
    private int _id = 1;
    private int _propertySetId = 1;
    private double _headX;
    private double _headY;
    private double _tailX;
    private double _tailY;

    public int Id { get => _id; set => Set(ref _id, value); }
    public int PropertySetId { get => _propertySetId; set => Set(ref _propertySetId, value); }
    public double HeadX { get => _headX; set => Set(ref _headX, value); }
    public double HeadY { get => _headY; set => Set(ref _headY, value); }
    public double TailX { get => _tailX; set => Set(ref _tailX, value); }
    public double TailY { get => _tailY; set => Set(ref _tailY, value); }

    public double LengthM
    {
        get
        {
            var dx = TailX - HeadX;
            var dy = TailY - HeadY;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (Equals(field, value)) return;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        if (name is nameof(HeadX) or nameof(HeadY) or nameof(TailX) or nameof(TailY))
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LengthM)));
    }
}
