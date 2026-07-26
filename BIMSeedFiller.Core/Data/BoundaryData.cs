using Autodesk.AutoCAD.DatabaseServices;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMSeedFiller
{
    internal class BoundaryData
    {
        public ObjectId GeometryId;
        public Extents3d BoundingBox;
        public int VertexCount;
    }
}
