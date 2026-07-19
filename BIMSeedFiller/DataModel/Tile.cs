using Autodesk.AutoCAD.Runtime; 
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput; 
using Autodesk.AutoCAD.DatabaseServices; 

namespace BIMSeedFiller
{
    internal class Tile
    {
        public Polyline Geometry;
        public double Area;
        public bool IsClipped;
    }
}
