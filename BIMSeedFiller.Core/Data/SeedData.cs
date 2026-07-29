using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;

namespace BIMSeedFiller.Core.Data
{
    public class SeedData
    {
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public Point3d Center { get; set; }
        public Polyline Geometry { get; set; }
    }
}
