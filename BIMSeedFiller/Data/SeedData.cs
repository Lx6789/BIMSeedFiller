using Autodesk.AutoCAD.Geometry;
using Autodesk.AutoCAD.DatabaseServices;

namespace BIMSeedFiller
{
    internal class SeedData
    {
        public double Width;
        public double Height;
        public double Rotation;
        public Point3d Center;
        public Polyline Geometry;
    }
}
