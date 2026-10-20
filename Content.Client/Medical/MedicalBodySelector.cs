using System.Linq;
using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client.Medical;

/// <summary>
/// Front-facing anatomical target map. Patient anatomy and diagnostic information are optional;
/// the HUD deliberately supplies neither.
/// </summary>
public sealed class MedicalBodySelector : Control
{
    private readonly Dictionary<string, ZoneButton> _buttons = new();
    public event Action<string>? OnSelect;

    public MedicalBodySelector()
    {
        SetWidth = 120;
        SetHeight = 200;
        HorizontalAlignment = HAlignment.Center;
        ToolTip = Loc.GetString("medical-body-selector-help");
        var group = new ButtonGroup();
        AddZone("Head", new Vector2(45, 3), new Vector2(30, 31),
            [new(8, 0), new(22, 0), new(30, 9), new(27, 24), new(20, 31), new(10, 31), new(3, 24), new(0, 9)]);
        AddZone("Torso", new Vector2(40, 37), new Vector2(40, 39),
            [new(2, 0), new(38, 0), new(40, 17), new(37, 39), new(3, 39), new(0, 17)]);
        AddZone("Abdomen", new Vector2(43, 78), new Vector2(34, 15),
            [new(0, 0), new(34, 0), new(32, 15), new(2, 15)]);
        AddZone("Groin", new Vector2(45, 95), new Vector2(30, 10),
            [new(0, 0), new(30, 0), new(29, 10), new(1, 10)]);
        // The patient's right is on the viewer's left.
        AddZone("ArmRight", new Vector2(17, 40), new Vector2(21, 62),
            [new(10, 0), new(21, 3), new(17, 28), new(10, 62), new(0, 59), new(4, 25)]);
        AddZone("ArmLeft", new Vector2(82, 40), new Vector2(21, 62),
            [new(0, 3), new(11, 0), new(17, 25), new(21, 59), new(11, 62), new(4, 28)]);
        AddZone("HandRight", new Vector2(5, 104), new Vector2(25, 29),
            [new(10, 2), new(22, 5), new(21, 19), new(14, 26), new(4, 23), new(4, 12)], true);
        AddZone("HandLeft", new Vector2(90, 104), new Vector2(25, 29),
            [new(3, 5), new(15, 2), new(21, 12), new(21, 23), new(11, 26), new(4, 19)], true);
        AddZone("LegRight", new Vector2(40, 108), new Vector2(18, 66),
            [new(4, 0), new(18, 0), new(17, 66), new(0, 66)]);
        AddZone("LegLeft", new Vector2(62, 108), new Vector2(18, 66),
            [new(0, 0), new(14, 0), new(18, 66), new(1, 66)]);
        AddZone("FootRight", new Vector2(31, 177), new Vector2(27, 23),
            [new(9, 0), new(24, 0), new(24, 18), new(0, 18), new(0, 10)], true);
        AddZone("FootLeft", new Vector2(62, 177), new Vector2(27, 23),
            [new(3, 0), new(18, 0), new(27, 10), new(27, 18), new(3, 18)], true);

        void AddZone(string category, Vector2 position, Vector2 size, Vector2[] polygon, bool padded = false)
        {
            var button = new ZoneButton(position, size, polygon, padded)
            {
                Name = category,
                Text = category,
                Group = group,
            };
            button.OnPressed += _ =>
            {
                button.Pressed = true;
                OnSelect?.Invoke(category);
            };
            _buttons.Add(category, button);
            AddChild(button);
        }
        SetState("Torso");
    }

    public void SetState(string selected, IEnumerable<string>? attached = null,
        IEnumerable<string>? injured = null, bool disabled = false, bool analyzerLabels = false)
    {
        var present = attached?.ToHashSet();
        var wounds = injured?.ToHashSet();
        foreach (var (category, button) in _buttons)
        {
            var missing = present != null && !present.Contains(category);
            button.Text = missing ? Loc.GetString("medical-panel-missing", ("part", category))
                : analyzerLabels ? Loc.GetString("medical-target-zone", ("part", category)) : category;
            button.ToolTip = button.Text;
            button.Disabled = disabled || missing;
            button.Missing = missing;
            button.Injured = wounds?.Contains(category) == true;
            button.Pressed = category == selected;
        }
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        foreach (var button in _buttons.Values)
            button.Measure(button.ZoneSize);
        return new Vector2(120, 200);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        foreach (var button in _buttons.Values)
            button.Arrange(UIBox2.FromDimensions(button.ZonePosition, button.ZoneSize));
        return finalSize;
    }

    private sealed class ZoneButton : Button
    {
        public readonly Vector2 ZonePosition;
        public readonly Vector2 ZoneSize;
        private readonly Vector2[] _polygon;
        private readonly Vector2[] _vertices;
        private readonly bool _padded;
        public bool Missing;
        public bool Injured;

        public ZoneButton(Vector2 position, Vector2 size, Vector2[] polygon, bool padded)
        {
            ZonePosition = position;
            ZoneSize = size;
            _polygon = polygon;
            _vertices = new Vector2[polygon.Length];
            _padded = padded;
            Label.Visible = false;
        }

        protected override Vector2 MeasureOverride(Vector2 availableSize) => ZoneSize;

        protected override bool HasPoint(Vector2 point)
        {
            if (!base.HasPoint(point))
                return false;
            if (_padded)
                return true;
            // Every zone is convex, so all edge cross products have the same sign.
            for (var i = 0; i < _polygon.Length; i++)
            {
                var edge = _polygon[(i + 1) % _polygon.Length] - _polygon[i];
                var offset = point - _polygon[i];
                if (edge.X * offset.Y - edge.Y * offset.X < 0)
                    return false;
            }
            return true;
        }

        protected override void Draw(DrawingHandleScreen handle)
        {
            var color = Missing ? Color.FromHex("#292F38")
                : Disabled ? Color.FromHex("#505966")
                : Injured ? Color.FromHex("#A65345") : Color.FromHex("#607D8B");
            if (!Disabled && IsHovered)
                color = Color.FromHex("#9EBECC");
            if (!Disabled && Pressed)
                color = Color.FromHex("#49C9C1");
            for (var i = 0; i < _polygon.Length; i++)
                _vertices[i] = _polygon[i] * UIScale;
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleFan, _vertices, color);
            var border = Pressed && !Disabled ? Color.White : Color.FromHex("#A0B3BE");
            for (var i = 0; i < _vertices.Length; i++)
                handle.DrawLine(_vertices[i], _vertices[(i + 1) % _vertices.Length], border);
            if (Missing)
            {
                handle.DrawLine(new Vector2(4, 4) * UIScale, (ZoneSize - new Vector2(4, 4)) * UIScale, border);
                handle.DrawLine(new Vector2(ZoneSize.X - 4, 4) * UIScale, new Vector2(4, ZoneSize.Y - 4) * UIScale, border);
            }
        }
    }
}
