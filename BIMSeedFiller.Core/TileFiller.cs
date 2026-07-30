using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using BIMSeedFiller.Core.Data;
using BIMSeedFiller.Core.DataModel;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMSeedFiller
{
    public static class TileFiller
    {
        private static Dictionary<ObjectId, List<Tile>> allResults = new Dictionary<ObjectId, List<Tile>>();

        public static List<Tile> GetTilesByBoundary(ObjectId boundaryId)
        {
            if (allResults.ContainsKey(boundaryId))
                return allResults[boundaryId];
            return null;
        }

        public static bool File(PromptEntityResult seedResult, PromptEntityResult boundaryResult)
        {
            if (seedResult.Status != PromptStatus.OK || boundaryResult.Status != PromptStatus.OK)
                return false;

            SeedData seed = ExtractSeedData(seedResult);
            BoundaryData boundary = ExtractBoundaryData(boundaryResult);
            if (seed == null || boundary == null) return false;

            if (!CreateLayer()) return false;

            Database db = HostApplicationServices.WorkingDatabase;
            List<Tile> allTiles = new List<Tile>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline boundaryPoly = (Polyline)tr.GetObject(boundary.GeometryId, OpenMode.ForRead);
                Extents3d box = boundaryPoly.GeometricExtents;

                Point3d[] samplePoints = new Point3d[]
                {
                    new Point3d(box.MinPoint.X, box.MinPoint.Y, 0),
                    new Point3d(box.MaxPoint.X, box.MinPoint.Y, 0),
                    new Point3d(box.MinPoint.X, box.MaxPoint.Y, 0),
                    new Point3d(box.MaxPoint.X, box.MaxPoint.Y, 0),
                    new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MinPoint.Y, 0),
                    new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MaxPoint.Y, 0),
                    new Point3d(box.MinPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0),
                    new Point3d(box.MaxPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0)
                };

                double cos = Math.Cos(seed.Rotation);
                double sin = Math.Sin(seed.Rotation);
                Vector3d u = new Vector3d(cos * seed.Width, sin * seed.Width, 0);
                Vector3d v = new Vector3d(-sin * seed.Height, cos * seed.Height, 0);
                double det = seed.Width * seed.Height;

                int minCol = int.MaxValue, maxCol = int.MinValue;
                int minRow = int.MaxValue, maxRow = int.MinValue;

                foreach (Point3d pt in samplePoints)
                {
                    double offsetX = pt.X - seed.Center.X;
                    double offsetY = pt.Y - seed.Center.Y;
                    double a = (offsetX * v.Y - offsetY * v.X) / det;
                    double b = (offsetY * u.X - offsetX * u.Y) / det;
                    int col = (int)Math.Floor(a);
                    int row = (int)Math.Floor(b);
                    if (col < minCol) minCol = col;
                    if (col > maxCol) maxCol = col;
                    if (row < minRow) minRow = row;
                    if (row > maxRow) maxRow = row;
                }

                minCol--; minRow--; maxCol++; maxRow++;

                List<(Polyline rect, Point3d center)> generatedRects = new List<(Polyline, Point3d)>();
                for (int i = minCol; i <= maxCol; i++)
                {
                    for (int j = minRow; j <= maxRow; j++)
                    {
                        Point3d tileCenter = seed.Center + i * u + j * v;
                        Polyline tileRect = CreateRectangle(tileCenter, seed.Width, seed.Height, seed.Rotation, db, tr);
                        generatedRects.Add((tileRect, tileCenter));
                    }
                }

                foreach (var (tileRect, tileCenter) in generatedRects)
                {
                    if (IsCompletelyOutside(tileRect, boundaryPoly, box))
                    {
                        tileRect.UpgradeOpen();
                        tileRect.Erase();
                    }
                    else if (IsFullyInside(tileRect, boundaryPoly))
                    {
                        allTiles.Add(new StandardTile
                        {
                            Geometry = tileRect,
                            Width = seed.Width,
                            Height = seed.Height,
                            Rotation = seed.Rotation,
                            Area = tileRect.Area,
                            IsClipped = false,
                            Center = tileCenter
                        });
                    }
                    else
                    {
                        List<Polyline> clipped = ComputeIntersection(tileRect, boundaryPoly, tr, db);
                        tileRect.UpgradeOpen();
                        tileRect.Erase();

                        foreach (Polyline clip in clipped)
                        {
                            allTiles.Add(new ClippedTile
                            {
                                Geometry = clip,
                                Area = clip.Area,
                                IsClipped = true
                            });
                        }
                    }
                }

                tr.Commit();
            }

            SetLayerOrder("边界", "填充种子后", boundary.GeometryId);
            allResults[boundary.GeometryId] = allTiles;
            return allTiles.Count > 0;
        }

        public static SeedData ExtractSeedData(PromptEntityResult seedResult)
        {
            if (seedResult == null) return null;
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBObject obj = tr.GetObject(seedResult.ObjectId, OpenMode.ForRead);
                if (!(obj is Polyline polyline)) return null;

                Point3d p0 = polyline.GetPoint3dAt(0);
                Point3d p1 = polyline.GetPoint3dAt(1);
                Point3d p2 = polyline.GetPoint3dAt(2);
                Point3d p3 = polyline.GetPoint3dAt(3);

                double width = p0.DistanceTo(p1);
                double height = p1.DistanceTo(p2);
                Vector3d edge = p1 - p0;
                double rotation = Math.Atan2(edge.Y, edge.X);
                Point3d center = new Point3d(
                    (p0.X + p1.X + p2.X + p3.X) / 4.0,
                    (p0.Y + p1.Y + p2.Y + p3.Y) / 4.0,
                    0.0
                );

                tr.Commit();
                return new SeedData
                {
                    Width = width,
                    Height = height,
                    Rotation = rotation,
                    Center = center,
                    Geometry = polyline
                };
            }
        }

        private static BoundaryData ExtractBoundaryData(PromptEntityResult boundaryResult)
        {
            if (boundaryResult == null) return null;
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                DBObject obj = tr.GetObject(boundaryResult.ObjectId, OpenMode.ForRead);
                if (!(obj is Polyline polyline)) return null;

                BoundaryData data = new BoundaryData
                {
                    GeometryId = boundaryResult.ObjectId,
                    BoundingBox = polyline.GeometricExtents,
                    VertexCount = polyline.NumberOfVertices
                };
                tr.Commit();
                return data;
            }
        }

        private static bool CreateLayer()
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (layerTable == null) return false;

                if (layerTable.Has("填充种子后"))
                {
                    tr.Commit();
                    return true;
                }

                LayerTableRecord newLayer = new LayerTableRecord { Name = "填充种子后" };
                layerTable.UpgradeOpen();
                layerTable.Add(newLayer);
                tr.AddNewlyCreatedDBObject(newLayer, true);
                tr.Commit();
                return true;
            }
        }

        private static Polyline CreateRectangle(Point3d pointCenter, double width, double height, double rotation, Database db, Transaction tr)
        {
            Polyline rect = new Polyline(4);

            double halfW = width / 2.0;
            double halfH = height / 2.0;
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);
            Point2d[] localOffsets =
            {
                new Point2d(-halfW, -halfH),
                new Point2d( halfW, -halfH),
                new Point2d( halfW,  halfH),
                new Point2d(-halfW,  halfH)
            };

            for (int i = 0; i < localOffsets.Length; i++)
            {
                double worldX = pointCenter.X + (localOffsets[i].X * cos - localOffsets[i].Y * sin);
                double worldY = pointCenter.Y + (localOffsets[i].X * sin + localOffsets[i].Y * cos);
                rect.AddVertexAt(i, new Point2d(worldX, worldY), 0, 0, 0);
            }
            rect.Closed = true;
            rect.Layer = "填充种子后";

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            btr.AppendEntity(rect);
            tr.AddNewlyCreatedDBObject(rect, true);
            return rect;
        }

        private static bool IsCompletelyOutside(Polyline rect, Polyline boundaryGeom, Extents3d boundaryBox)
        {
            Extents3d a = rect.GeometricExtents;
            if (a.MinPoint.X > boundaryBox.MaxPoint.X || a.MaxPoint.X < boundaryBox.MinPoint.X ||
                a.MinPoint.Y > boundaryBox.MaxPoint.Y || a.MaxPoint.Y < boundaryBox.MinPoint.Y)
                return true;

            for (int i = 0; i < 4; i++)
            {
                Point3d p1 = rect.GetPoint3dAt(i);
                Point3d p2 = rect.GetPoint3dAt((i + 1) % 4);
                Point3dCollection pts = new Point3dCollection();
                Line tempLine = new Line(p1, p2);
                boundaryGeom.IntersectWith(tempLine, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                tempLine.Dispose();
                if (pts.Count > 0)
                    return false;
            }

            if (IsPointInsidePolyline(rect.GetPoint3dAt(0), boundaryGeom))
                return false;

            return true;
        }

        private static bool IsFullyInside(Polyline rect, Polyline boundaryGeom)
        {
            for (int i = 0; i < 4; i++)
            {
                if (!IsPointInsidePolyline(rect.GetPoint3dAt(i), boundaryGeom))
                    return false;
            }

            for (int i = 0; i < 4; i++)
            {
                Point3d p1 = rect.GetPoint3dAt(i);
                Point3d p2 = rect.GetPoint3dAt((i + 1) % 4);
                Point3dCollection pts = new Point3dCollection();
                Line tempLine = new Line(p1, p2);
                boundaryGeom.IntersectWith(tempLine, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                tempLine.Dispose();
                if (pts.Count > 0)
                    return false;
            }
            return true;
        }

        private static bool IsPointInsidePolyline(Point3d point, Polyline boundaryGeom)
        {
            if (!boundaryGeom.Closed) return false;

            Point2d pt = new Point2d(point.X, point.Y);
            bool inside = false;
            int n = boundaryGeom.NumberOfVertices;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if (boundaryGeom.GetBulgeAt(j) == 0)
                {
                    Point2d p1 = boundaryGeom.GetPoint2dAt(j);
                    Point2d p2 = boundaryGeom.GetPoint2dAt(i);

                    if ((p1.Y > pt.Y) != (p2.Y > pt.Y) &&
                        pt.X < (p2.X - p1.X) * (pt.Y - p1.Y) / (p2.Y - p1.Y) + p1.X)
                    {
                        inside = !inside;
                    }
                }
                else
                {
                    double bulge = boundaryGeom.GetBulgeAt(j);
                    Point2d p1 = boundaryGeom.GetPoint2dAt(j);
                    Point2d p2 = boundaryGeom.GetPoint2dAt(i);

                    double chordLen = p1.GetDistanceTo(p2);
                    double theta = Math.Atan2(Math.Abs(bulge), 1) * 4 * Math.Sign(bulge);
                    double radius = chordLen / (2 * Math.Sin(Math.Abs(theta) / 2));
                    if (radius <= 0) continue;

                    double midX = (p1.X + p2.X) / 2, midY = (p1.Y + p2.Y) / 2;
                    double dist = Math.Sqrt(Math.Max(0, radius * radius - (chordLen / 2) * (chordLen / 2)));
                    double midAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) + (bulge > 0 ? Math.PI / 2 : -Math.PI / 2);
                    double centerX = midX + dist * Math.Cos(midAngle);
                    double centerY = midY + dist * Math.Sin(midAngle);

                    double startAngle = Math.Atan2(p1.Y - centerY, p1.X - centerX);
                    double endAngle = startAngle + theta;
                    int samples = 10;

                    for (int k = 0; k < samples; k++)
                    {
                        double ang1 = startAngle + theta * k / samples;
                        double ang2 = startAngle + theta * (k + 1) / samples;
                        double sx = centerX + radius * Math.Cos(ang1);
                        double sy = centerY + radius * Math.Sin(ang1);
                        double ex = centerX + radius * Math.Cos(ang2);
                        double ey = centerY + radius * Math.Sin(ang2);

                        if ((sy > pt.Y) != (ey > pt.Y) &&
                            pt.X < (ex - sx) * (pt.Y - sy) / (ey - sy) + sx)
                        {
                            inside = !inside;
                        }
                    }
                }
            }

            return inside;
        }

        private static void SetLayerOrder(string topLayerName, string bottomLayerName, ObjectId boundaryId)
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (!layerTable.Has(topLayerName) || !layerTable.Has(bottomLayerName))
                {
                    tr.Commit();
                    return;
                }

                BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
                BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
                DrawOrderTable drawOrder = (DrawOrderTable)tr.GetObject(btr.DrawOrderTableId, OpenMode.ForWrite);

                ObjectIdCollection topIds = new ObjectIdCollection();
                ObjectIdCollection bottomIds = new ObjectIdCollection();
                foreach (ObjectId objId in btr)
                {
                    Entity ent = tr.GetObject(objId, OpenMode.ForRead) as Entity;
                    if (ent == null) continue;
                    if (ent.Layer == topLayerName) topIds.Add(objId);
                    else if (ent.Layer == bottomLayerName) bottomIds.Add(objId);
                }
                if (!topIds.Contains(boundaryId))
                {
                    Entity ent = tr.GetObject(boundaryId, OpenMode.ForRead) as Entity;
                    if (ent != null && ent.Layer == topLayerName) topIds.Add(boundaryId);
                }

                if (topIds.Count > 0 && bottomIds.Count > 0)
                {
                    drawOrder.MoveToBottom(bottomIds);
                    drawOrder.MoveToTop(topIds);
                }
                tr.Commit();
            }
        }

        private static List<Polyline> ComputeIntersection(Polyline tileRect, Polyline boundary,
            Transaction tr, Database db)
        {
            List<Point3d> allInternalPoints = new List<Point3d>();

            for (int i = 0; i < 4; i++)
            {
                Point3d start = tileRect.GetPoint3dAt(i);
                Point3d end = tileRect.GetPoint3dAt((i + 1) % 4);

                Point3dCollection intPts = new Point3dCollection();
                using (Line tempLine = new Line(start, end))
                    boundary.IntersectWith(tempLine, Intersect.OnBothOperands, intPts, IntPtr.Zero, IntPtr.Zero);

                List<Point3d> edgePoints = new List<Point3d> { start };
                List<Point3d> sortedInts = new List<Point3d>();
                foreach (Point3d pt in intPts) sortedInts.Add(pt);
                sortedInts.Sort((a, b) => start.DistanceTo(a).CompareTo(start.DistanceTo(b)));
                edgePoints.AddRange(sortedInts);
                edgePoints.Add(end);

                for (int j = 0; j < edgePoints.Count - 1; j++)
                {
                    Point3d mid = new Point3d(
                        (edgePoints[j].X + edgePoints[j + 1].X) / 2,
                        (edgePoints[j].Y + edgePoints[j + 1].Y) / 2, 0);

                    if (IsPointInsidePolyline(mid, boundary))
                    {
                        AddUniquePoint(allInternalPoints, edgePoints[j]);
                        AddUniquePoint(allInternalPoints, edgePoints[j + 1]);
                    }
                }
            }

            if (allInternalPoints.Count < 3) return new List<Polyline>();

            List<Point3d> sortedPoints = ComputeConvexHull(allInternalPoints);
            return new List<Polyline> { CreateCleanPolyline(sortedPoints, tr, db) };
        }

        private static List<Point3d> ComputeConvexHull(List<Point3d> points)
        {
            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count <= 2) return new List<Point3d>(sorted);

            List<Point3d> hull = new List<Point3d>();

            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            int lowerCount = hull.Count;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                Point3d p = sorted[i];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            if (hull.Count > 0)
                hull.RemoveAt(hull.Count - 1);

            return hull;
        }

        private static double Cross(Point3d o, Point3d a, Point3d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        private static Polyline CreateCleanPolyline(List<Point3d> points, Transaction tr, Database db)
        {
            if (points.Count < 3) return null;

            Polyline pline = new Polyline(points.Count);
            for (int i = 0; i < points.Count; i++)
                pline.AddVertexAt(i, new Point2d(points[i].X, points[i].Y), 0, 0, 0);
            pline.Closed = true;
            pline.Layer = "填充种子后";

            BlockTable bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
            BlockTableRecord btr = (BlockTableRecord)tr.GetObject(bt[BlockTableRecord.ModelSpace], OpenMode.ForWrite);
            btr.AppendEntity(pline);
            tr.AddNewlyCreatedDBObject(pline, true);
            return pline;
        }

        private static void AddUniquePoint(List<Point3d> list, Point3d pt)
        {
            foreach (Point3d existing in list)
            {
                if (pt.DistanceTo(existing) < Tolerance.Global.EqualPoint) return;
            }
            list.Add(pt);
        }

        // ==================== 预览功能（纯数学实现，不创建 AutoCAD 实体） ====================

        public static List<List<Point2d>> Preview(PromptEntityResult seedResult, PromptEntityResult boundaryResult)
        {
            if (seedResult.Status != PromptStatus.OK || boundaryResult.Status != PromptStatus.OK)
                return new List<List<Point2d>>();

            SeedData seed = ExtractSeedData(seedResult);
            BoundaryData boundary = ExtractBoundaryData(boundaryResult);
            if (seed == null || boundary == null) return new List<List<Point2d>>();

            List<Point2d> boundaryPoints = GetBoundaryPoints(boundaryResult.ObjectId);
            if (boundaryPoints.Count < 3) return new List<List<Point2d>>();

            Extents3d box = boundary.BoundingBox;
            Point3d[] samplePoints = new Point3d[]
            {
                new Point3d(box.MinPoint.X, box.MinPoint.Y, 0),
                new Point3d(box.MaxPoint.X, box.MinPoint.Y, 0),
                new Point3d(box.MinPoint.X, box.MaxPoint.Y, 0),
                new Point3d(box.MaxPoint.X, box.MaxPoint.Y, 0),
                new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MinPoint.Y, 0),
                new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MaxPoint.Y, 0),
                new Point3d(box.MinPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0),
                new Point3d(box.MaxPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0)
            };

            double cos = Math.Cos(seed.Rotation);
            double sin = Math.Sin(seed.Rotation);
            Vector3d u = new Vector3d(cos * seed.Width, sin * seed.Width, 0);
            Vector3d v = new Vector3d(-sin * seed.Height, cos * seed.Height, 0);
            double det = seed.Width * seed.Height;

            int minCol = int.MaxValue, maxCol = int.MinValue;
            int minRow = int.MaxValue, maxRow = int.MinValue;

            foreach (Point3d pt in samplePoints)
            {
                double offsetX = pt.X - seed.Center.X;
                double offsetY = pt.Y - seed.Center.Y;
                double a = (offsetX * v.Y - offsetY * v.X) / det;
                double b = (offsetY * u.X - offsetX * u.Y) / det;
                int col = (int)Math.Floor(a);
                int row = (int)Math.Floor(b);
                if (col < minCol) minCol = col;
                if (col > maxCol) maxCol = col;
                if (row < minRow) minRow = row;
                if (row > maxRow) maxRow = row;
            }
            minCol--; minRow--; maxCol++; maxRow++;

            List<List<Point2d>> allPolygons = new List<List<Point2d>>();

            for (int i = minCol; i <= maxCol; i++)
            {
                for (int j = minRow; j <= maxRow; j++)
                {
                    Point3d tileCenter = seed.Center + i * u + j * v;
                    List<Point2d> tileCorners = GetTileCorners(tileCenter, seed.Width, seed.Height, seed.Rotation);

                    if (IsPolygonCompletelyOutside(tileCorners, boundaryPoints))
                        continue;

                    if (IsPolygonFullyInside(tileCorners, boundaryPoints))
                    {
                        allPolygons.Add(tileCorners);
                    }
                    else
                    {
                        List<Point2d> clipped = ComputePreviewClippedPolygon(tileCorners, boundaryPoints);
                        if (clipped.Count >= 3)
                            allPolygons.Add(clipped);
                    }
                }
            }

            return allPolygons;
        }

        private static List<Point2d> GetTileCorners(Point3d center, double width, double height, double rotation)
        {
            double halfW = width / 2.0;
            double halfH = height / 2.0;
            double cos = Math.Cos(rotation);
            double sin = Math.Sin(rotation);

            Point2d[] local = new Point2d[]
            {
                new Point2d(-halfW, -halfH),
                new Point2d( halfW, -halfH),
                new Point2d( halfW,  halfH),
                new Point2d(-halfW,  halfH)
            };

            List<Point2d> world = new List<Point2d>();
            foreach (var p in local)
            {
                double x = center.X + (p.X * cos - p.Y * sin);
                double y = center.Y + (p.X * sin + p.Y * cos);
                world.Add(new Point2d(x, y));
            }
            return world;
        }

        private static List<Point2d> GetBoundaryPoints(ObjectId boundaryId)
        {
            List<Point2d> points = new List<Point2d>();
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                Polyline pl = tr.GetObject(boundaryId, OpenMode.ForRead) as Polyline;
                if (pl == null) return points;

                for (int i = 0; i < pl.NumberOfVertices; i++)
                {
                    double bulge = pl.GetBulgeAt(i);
                    Point2d p1 = pl.GetPoint2dAt(i);
                    Point2d p2 = pl.GetPoint2dAt((i + 1) % pl.NumberOfVertices);

                    if (bulge == 0.0)
                    {
                        points.Add(p1);
                    }
                    else
                    {
                        double chordLen = p1.GetDistanceTo(p2);
                        double theta = Math.Atan2(Math.Abs(bulge), 1) * 4 * Math.Sign(bulge);
                        double radius = chordLen / (2 * Math.Sin(Math.Abs(theta) / 2));
                        if (radius <= 0) continue;

                        double midX = (p1.X + p2.X) / 2, midY = (p1.Y + p2.Y) / 2;
                        double dist = Math.Sqrt(Math.Max(0, radius * radius - (chordLen / 2) * (chordLen / 2)));
                        double midAngle = Math.Atan2(p2.Y - p1.Y, p2.X - p1.X) + (bulge > 0 ? Math.PI / 2 : -Math.PI / 2);
                        double centerX = midX + dist * Math.Cos(midAngle);
                        double centerY = midY + dist * Math.Sin(midAngle);

                        double startAngle = Math.Atan2(p1.Y - centerY, p1.X - centerX);
                        double endAngle = startAngle + theta;
                        int samples = 10;
                        for (int k = 0; k <= samples; k++)
                        {
                            double ang = startAngle + theta * k / samples;
                            points.Add(new Point2d(centerX + radius * Math.Cos(ang), centerY + radius * Math.Sin(ang)));
                        }
                    }
                }
                tr.Commit();
            }
            return points;
        }

        private static bool IsPolygonCompletelyOutside(List<Point2d> poly, List<Point2d> boundary)
        {
            double minX = poly.Min(p => p.X), maxX = poly.Max(p => p.X);
            double minY = poly.Min(p => p.Y), maxY = poly.Max(p => p.Y);
            double bMinX = boundary.Min(p => p.X), bMaxX = boundary.Max(p => p.X);
            double bMinY = boundary.Min(p => p.Y), bMaxY = boundary.Max(p => p.Y);
            if (maxX < bMinX || minX > bMaxX || maxY < bMinY || minY > bMaxY)
                return true;

            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a1 = poly[i], a2 = poly[(i + 1) % poly.Count];
                for (int j = 0; j < boundary.Count; j++)
                {
                    Point2d b1 = boundary[j], b2 = boundary[(j + 1) % boundary.Count];
                    if (LineSegmentsIntersect(a1, a2, b1, b2))
                        return false;
                }
            }

            return !IsPointInPolygon(poly[0], boundary);
        }

        private static bool IsPolygonFullyInside(List<Point2d> poly, List<Point2d> boundary)
        {
            foreach (var p in poly)
                if (!IsPointInPolygon(p, boundary)) return false;

            for (int i = 0; i < poly.Count; i++)
            {
                Point2d a1 = poly[i], a2 = poly[(i + 1) % poly.Count];
                for (int j = 0; j < boundary.Count; j++)
                {
                    Point2d b1 = boundary[j], b2 = boundary[(j + 1) % boundary.Count];
                    if (LineSegmentsIntersect(a1, a2, b1, b2))
                        return false;
                }
            }
            return true;
        }

        private static bool IsPointInPolygon(Point2d point, List<Point2d> polygon)
        {
            bool inside = false;
            int n = polygon.Count;
            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                if ((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y) &&
                    point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y) + polygon[i].X)
                    inside = !inside;
            }
            return inside;
        }

        private static bool LineSegmentsIntersect(Point2d p1, Point2d p2, Point2d q1, Point2d q2)
        {
            double Cross(Point2d a, Point2d b, Point2d c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
            double d1 = Cross(q1, q2, p1);
            double d2 = Cross(q1, q2, p2);
            double d3 = Cross(p1, p2, q1);
            double d4 = Cross(p1, p2, q2);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static List<Point2d> ComputePreviewClippedPolygon(List<Point2d> tileCorners, List<Point2d> boundary)
        {
            List<Point2d> internalPoints = new List<Point2d>();

            for (int i = 0; i < tileCorners.Count; i++)
            {
                Point2d start = tileCorners[i];
                Point2d end = tileCorners[(i + 1) % tileCorners.Count];
                List<Point2d> edgePoints = new List<Point2d> { start };

                for (int j = 0; j < boundary.Count; j++)
                {
                    Point2d b1 = boundary[j];
                    Point2d b2 = boundary[(j + 1) % boundary.Count];
                    if (LineSegmentsIntersect(start, end, b1, b2))
                    {
                        Point2d inter = ComputeLineIntersection(start, end, b1, b2);
                        if (!double.IsNaN(inter.X))
                            edgePoints.Add(inter);
                    }
                }
                edgePoints.Add(end);
                edgePoints.Sort((a, b) => start.GetDistanceTo(a).CompareTo(start.GetDistanceTo(b)));

                for (int k = 0; k < edgePoints.Count - 1; k++)
                {
                    Point2d mid = new Point2d((edgePoints[k].X + edgePoints[k + 1].X) / 2,
                                              (edgePoints[k].Y + edgePoints[k + 1].Y) / 2);
                    if (IsPointInPolygon(mid, boundary))
                    {
                        AddUniquePoint2D(internalPoints, edgePoints[k]);
                        AddUniquePoint2D(internalPoints, edgePoints[k + 1]);
                    }
                }
            }

            if (internalPoints.Count < 3) return new List<Point2d>();
            return ComputeConvexHull2D(internalPoints);
        }

        private static Point2d ComputeLineIntersection(Point2d p1, Point2d p2, Point2d p3, Point2d p4)
        {
            double x1 = p1.X, y1 = p1.Y, x2 = p2.X, y2 = p2.Y;
            double x3 = p3.X, y3 = p3.Y, x4 = p4.X, y4 = p4.Y;
            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-10) return new Point2d(double.NaN, double.NaN);
            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            return new Point2d(x1 + t * (x2 - x1), y1 + t * (y2 - y1));
        }

        private static void AddUniquePoint2D(List<Point2d> list, Point2d pt)
        {
            foreach (var p in list)
                if (pt.GetDistanceTo(p) < 1e-6) return;
            list.Add(pt);
        }

        private static List<Point2d> ComputeConvexHull2D(List<Point2d> points)
        {
            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count <= 2) return new List<Point2d>(sorted);

            double Cross(Point2d o, Point2d a, Point2d b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
            List<Point2d> hull = new List<Point2d>();

            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            int lowerCount = hull.Count;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                Point2d p = sorted[i];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }
            if (hull.Count > 0) hull.RemoveAt(hull.Count - 1);
            return hull;
        }
    }
}