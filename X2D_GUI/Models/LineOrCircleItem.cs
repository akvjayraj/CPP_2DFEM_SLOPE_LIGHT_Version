namespace GeotechSlopeApp.Models;

public class LineOrCircleItem
{
    public string DisplayText { get; set; } = string.Empty;
    public int Value { get; set; }

    public LineOrCircleItem(string displayText, int value)
    {
        DisplayText = displayText;
        Value = value;
    }

    public override string ToString() => DisplayText;
}
