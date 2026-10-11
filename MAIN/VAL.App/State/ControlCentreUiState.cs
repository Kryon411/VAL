namespace VAL.App.State
{
    public sealed class ControlCentreUiState
    {
        public int Version { get; set; } = 2;
        public GeometryState ControlCentre { get; set; } = new(0, 0, 0, 0);
        public bool ControlCentreIsCustom { get; set; }
        public DockGeometryState Dock { get; set; } = new();
        public bool LayoutMode { get; set; }

        public static ControlCentreUiState Default => new();

        public ControlCentreUiState Normalize()
        {
            Dock ??= new DockGeometryState();
            if (Version < 2)
            {
                // Old files did not distinguish automatic placement from user customization.
                ControlCentreIsCustom = ControlCentre.HasPosition;
                Dock.IsCustom = Dock.X != 72 || Dock.Y != 56 || Dock.W != 560 || Dock.H != 460;
            }
            Version = 2;
            LayoutMode = false;
            if (!ControlCentre.HasPosition)
            {
                ControlCentre = new GeometryState(0, 0, 0, 0);
                ControlCentreIsCustom = false;
            }

            Dock.X = double.IsFinite(Dock.X) ? Dock.X : 72;
            Dock.Y = double.IsFinite(Dock.Y) ? Dock.Y : 56;
            Dock.W = !double.IsFinite(Dock.W) || Dock.W <= 0 ? 560 : Dock.W;
            Dock.H = !double.IsFinite(Dock.H) || Dock.H <= 0 ? 460 : Dock.H;
            return this;
        }
    }

    public sealed class DockGeometryState
    {
        public double X { get; set; } = 72;
        public double Y { get; set; } = 56;
        public double W { get; set; } = 560;
        public double H { get; set; } = 460;
        public bool IsOpen { get; set; }
        public bool IsCustom { get; set; }
    }

    public struct GeometryState
    {
        public GeometryState(double x, double y, double w, double h)
        {
            X = x;
            Y = y;
            W = w;
            H = h;
        }

        public double X { get; set; }
        public double Y { get; set; }
        public double W { get; set; }
        public double H { get; set; }
        public bool HasPosition => double.IsFinite(X) && double.IsFinite(Y) &&
            double.IsFinite(W) && double.IsFinite(H) && W > 0 && H > 0;
    }
}
