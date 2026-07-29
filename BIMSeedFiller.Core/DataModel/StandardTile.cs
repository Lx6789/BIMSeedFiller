using Autodesk.AutoCAD.Geometry;

namespace BIMSeedFiller.Core.DataModel
{
    public class StandardTile : Tile
    {
        public double Width;
        public double Height;
        public double Rotation;
        public Point3d Center;
    }
}
