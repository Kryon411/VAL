namespace VAL
{
    public sealed class ControlCentreUiState
    {
        public int Version { get; set; } = 1;
        public GeometryState ControlCentre { get; set; } = new(0, 0, 0, 0);
        public DockGeometryState Dock { get; set; } = new();
        public bool LayoutMode { get; set; }

        public static ControlCentreUiState Default => new();

        public ControlCentreUiState Normalize()
        {
            Version = 1;
            if (ControlCentre.W <= 0 || ControlCentre.H <= 0)
            {
                ControlCentre = new GeometryState(ControlCentre.X, ControlCentre.Y, 36, 36);
            }

            Dock.W = Dock.W <= 0 ? 560 : Dock.W;
            Dock.H = Dock.H <= 0 ? 460 : Dock.H;
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
        public bool HasPosition => W > 0 && H > 0;
    }
}
