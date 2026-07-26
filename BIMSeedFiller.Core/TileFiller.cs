using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace BIMSeedFiller
{
    internal static class TileFiller
    {
        // 字典存储：边界ObjectId获取对应的填充瓦片列表
        private static Dictionary<ObjectId, List<Tile>> allResults = new Dictionary<ObjectId, List<Tile>>();

        /// <summary>
        /// 根据边界ObjectId获取对应的填充结果
        /// </summary>
        /// <param name="boundaryId"></param>
        /// <returns></returns>
        public static List<Tile> GetTilesByBoundary(ObjectId boundaryId)
        {
            if (allResults.ContainsKey(boundaryId))
                return allResults[boundaryId];
            return null;
        }

        /// <summary>
        /// 填充瓦片 
        /// </summary>
        /// <param name="seedResult"></param>
        /// <param name="boundaryResult"></param>
        /// <returns></returns>
        public static bool File(PromptEntityResult seedResult, PromptEntityResult boundaryResult)
        {
            // 1. 检查数据是否存在
            if (seedResult.Status != PromptStatus.OK || boundaryResult.Status != PromptStatus.OK)
                return false;

            // 2. 填充数据
            SeedData seed = ExtractSeedData(seedResult);
            BoundaryData boundary = ExtractBoundaryData(boundaryResult);
            if (seed == null || boundary == null) return false;

            // 3. 创建图层
            if (!CreateLayer()) return false;

            Database db = HostApplicationServices.WorkingDatabase;
            List<Tile> allTiles = new List<Tile>();

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 4.在包围盒内生成所有可能的瓦片
                // 4.1 获取包围盒数据
                Polyline boundaryPoly = (Polyline)tr.GetObject(boundary.GeometryId, OpenMode.ForRead);
                Extents3d box = boundaryPoly.GeometricExtents;

                // 4.2 获取包围盒的采样点
                Point3d[] samplePoints = new Point3d[]
                {
                    new Point3d(box.MinPoint.X, box.MinPoint.Y, 0),  // 左下
                    new Point3d(box.MaxPoint.X, box.MinPoint.Y, 0),  // 右下
                    new Point3d(box.MinPoint.X, box.MaxPoint.Y, 0),  // 左上
                    new Point3d(box.MaxPoint.X, box.MaxPoint.Y, 0),  // 右上
                    new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MinPoint.Y, 0), // 下中点
                    new Point3d((box.MinPoint.X + box.MaxPoint.X) / 2, box.MaxPoint.Y, 0), // 上中点
                    new Point3d(box.MinPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0), // 左中点
                    new Point3d(box.MaxPoint.X, (box.MinPoint.Y + box.MaxPoint.Y) / 2, 0)  // 右中点
                };
                if (samplePoints.Length != 8) return false;

                // 4.3 计算瓦片的行和列的最大值和最小值
                // 4.3.1 计算方向向量
                double cos = Math.Cos(seed.Rotation);
                double sin = Math.Sin(seed.Rotation);
                Vector3d u = new Vector3d(cos * seed.Width, sin * seed.Width, 0);   // 列方向
                Vector3d v = new Vector3d(-sin * seed.Height, cos * seed.Height, 0); // 行方向
                double det = seed.Width * seed.Height; // 行列式，不等于0

                // 4.3.2 遍历采样点然后找出最大行列和最小行列
                int minCol = int.MaxValue;
                int maxCol = int.MinValue;
                int minRow = int.MaxValue;
                int maxRow = int.MinValue;

                foreach (Point3d pt in samplePoints)
                {
                    double offsetX = pt.X - seed.Center.X;
                    double offsetY = pt.Y - seed.Center.Y;
                    double a = (offsetX * v.Y - offsetY * v.X) / det; // 列号
                    double b = (offsetY * u.X - offsetX * u.Y) / det; // 行号

                    int col = (int)Math.Floor(a);
                    int row = (int)Math.Floor(b);
                    if (col < minCol) minCol = col;
                    if (col > maxCol) maxCol = col;
                    if (row < minRow) minRow = row;
                    if (row > maxRow) maxRow = row;
                }

                // 4.3.3 再扩大一圈，做保险
                minCol--;
                minRow--;
                maxCol++;
                maxRow++;

                // 4.4 生成瓦片
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

                // 5. 根据边界裁剪瓦片并分类
                foreach (var (tileRect, tileCenter) in generatedRects)
                {
                    if (IsCompletelyOutside(tileRect, boundaryPoly, box))
                    {
                        // 5.1 完全处于边界外部
                        tileRect.UpgradeOpen();
                        tileRect.Erase();
                    }
                    else if (IsFullyInside(tileRect, boundaryPoly))
                    {
                        // 5.2 完全处于边界内部
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
                        // 5.3 与边界相交，尝试裁剪
                        List<Polyline> clipped = ComputeIntersection(tileRect, boundaryPoly, tr, db);
                        tileRect.UpgradeOpen();
                        tileRect.Erase();

                        if (clipped.Count > 0)
                        {
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
                }

                tr.Commit();
            }

            // 5. 调整图层顺序
            SetLayerOrder("边界", "填充种子后", boundary.GeometryId);

            // 6. 存储结果
            allResults[boundary.GeometryId] = allTiles;
            return allTiles.Count > 0;
        }

        /// <summary>
        /// 获取种子数据
        /// </summary>
        /// <param name="seedResult"></param>
        /// <returns></returns>
        private static SeedData ExtractSeedData(PromptEntityResult seedResult)
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

        /// <summary>
        /// 获取边界数据
        /// </summary>
        /// <param name="boundaryResult"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 创建图层
        /// </summary>
        /// <returns></returns>
        private static bool CreateLayer()
        {
            Database db = HostApplicationServices.WorkingDatabase;
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                LayerTable layerTable = (LayerTable)tr.GetObject(db.LayerTableId, OpenMode.ForRead);
                if (layerTable == null) return false;

                // 如果图层已存在，直接返回 true
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

        /// <summary>
        /// 创建瓷砖
        /// </summary>
        /// <param name="pointCenter"></param>
        /// <param name="width"></param>
        /// <param name="height"></param>
        /// <returns></returns>
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

            // 计算瓷砖的四个角的位置
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

        /// <summary>
        /// 判断瓦片是否完全处于边界外围
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="boundaryGeom"></param>
        /// <param name="boundaryBox"></param>
        /// <returns></returns>
        private static bool IsCompletelyOutside(Polyline rect, Polyline boundaryGeom, Extents3d boundaryBox)
        {
            // 1. 包围盒排斥
            Extents3d a = rect.GeometricExtents;
            if (a.MinPoint.X > boundaryBox.MaxPoint.X || a.MaxPoint.X < boundaryBox.MinPoint.X ||
                a.MinPoint.Y > boundaryBox.MaxPoint.Y || a.MaxPoint.Y < boundaryBox.MinPoint.Y)
            {
                return true;
            }

            // 2. 边相交检测
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

            // 3. 顶点位置判定
            if (IsPointInsidePolyline(rect.GetPoint3dAt(0), boundaryGeom))
                return false;

            return true;
        }

        /// <summary>
        /// 判断瓦片是否完全处于边界内部
        /// </summary>
        /// <param name="rect"></param>
        /// <param name="boundaryGeom"></param>
        /// <returns></returns>
        private static bool IsFullyInside(Polyline rect, Polyline boundaryGeom)
        {
            // 先检查四个顶点是否都在内部
            for (int i = 0; i < 4; i++)
            {
                if (!IsPointInsidePolyline(rect.GetPoint3dAt(i), boundaryGeom))
                    return false;
            }

            // 再检查四条边是否与边界有交点（防止凹角误判）
            for (int i = 0; i < 4; i++)
            {
                Point3d p1 = rect.GetPoint3dAt(i);
                Point3d p2 = rect.GetPoint3dAt((i + 1) % 4);
                Point3dCollection pts = new Point3dCollection();
                Line tempLine = new Line(p1, p2);
                boundaryGeom.IntersectWith(tempLine, Intersect.OnBothOperands, pts, IntPtr.Zero, IntPtr.Zero);
                tempLine.Dispose();
                if (pts.Count > 0)
                    return false; // 有交点，说明瓦片跨越了边界
            }

            return true; // 顶点全在内，且没有边与边界相交，才是真正的完全内部
        }

        /// <summary>
        /// 判断顶点位置是否完全在边界内部
        /// </summary>
        /// <param name="point"></param>
        /// <param name="boundaryGeom"></param>
        /// <returns></returns>
        private static bool IsPointInsidePolyline(Point3d point, Polyline boundaryGeom)
        {
            if (!boundaryGeom.Closed) return false;

            Point2d pt = new Point2d(point.X, point.Y);
            bool inside = false;
            int n = boundaryGeom.NumberOfVertices;

            for (int i = 0, j = n - 1; i < n; j = i++)
            {
                // 直线段
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
                // 圆弧段：离散为10段直线进行判断
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

        /// <summary>
        /// 设置图层的顺序
        /// </summary>
        /// <param name="topLayerName"></param>
        /// <param name="bottomLayerName"></param>
        /// <param name="boundaryId"></param>
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

        /// <summary>
        ///  基于“边修剪”的裁剪算法 V3。独立处理每条边，只保留内部的线段片段，
        /// 并使用凸包算法重建裁剪多边形，消除乱连线。
        /// </summary>
        /// <param name="tileRect"></param>
        /// <param name="boundary"></param>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <returns></returns>
        private static List<Polyline> ComputeIntersection(Polyline tileRect, Polyline boundary,
            Transaction tr, Database db)
        {
            List<Point3d> allInternalPoints = new List<Point3d>();

            // 1. 独立处理矩形的每条边，收集在边界内部的线段端点
            for (int i = 0; i < 4; i++)
            {
                Point3d start = tileRect.GetPoint3dAt(i);
                Point3d end = tileRect.GetPoint3dAt((i + 1) % 4);

                // 获取这条边上的交点
                Point3dCollection intPts = new Point3dCollection();
                using (Line tempLine = new Line(start, end))
                    boundary.IntersectWith(tempLine, Intersect.OnBothOperands, intPts, IntPtr.Zero, IntPtr.Zero);

                // 构建边上的有序点序列
                List<Point3d> edgePoints = new List<Point3d> { start };
                List<Point3d> sortedInts = new List<Point3d>();
                foreach (Point3d pt in intPts) sortedInts.Add(pt);
                sortedInts.Sort((a, b) => start.DistanceTo(a).CompareTo(start.DistanceTo(b)));
                edgePoints.AddRange(sortedInts);
                edgePoints.Add(end);

                // 保留内部的线段片段
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

            // 2. 使用凸包算法重新排序顶点，消除乱连线
            List<Point3d> sortedPoints = ComputeConvexHull(allInternalPoints);

            // 3. 生成最终的多边形
            return new List<Polyline> { CreateCleanPolyline(sortedPoints, tr, db) };
        }

        /// <summary>
        ///  Andrew 凸包算法（单调链），确保顶点按逆时针顺序排列。
        /// </summary>
        /// <param name="points"></param>
        /// <returns></returns>
        private static List<Point3d> ComputeConvexHull(List<Point3d> points)
        {
            // 先按 X 再按 Y 排序
            var sorted = points.OrderBy(p => p.X).ThenBy(p => p.Y).ToList();
            if (sorted.Count <= 2) return new List<Point3d>(sorted);

            List<Point3d> hull = new List<Point3d>();

            // 构建下半部分
            foreach (var p in sorted)
            {
                while (hull.Count >= 2 && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 构建上半部分
            int lowerCount = hull.Count;
            for (int i = sorted.Count - 2; i >= 0; i--)
            {
                Point3d p = sorted[i];
                while (hull.Count > lowerCount && Cross(hull[hull.Count - 2], hull[hull.Count - 1], p) <= 0)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(p);
            }

            // 移除最后一个重复的起点
            if (hull.Count > 0)
                hull.RemoveAt(hull.Count - 1);

            return hull;
        }

        /// <summary>
        /// 计算向量 O->A 与 O->B 的叉积（二维），用于判断旋转方向。
        /// 正值表示 O->B 在 O->A 的逆时针方向（保留点）。
        /// </summary>
        /// <param name="o"></param>
        /// <param name="a"></param>
        /// <param name="b"></param>
        /// <returns></returns>
        private static double Cross(Point3d o, Point3d a, Point3d b)
        {
            return (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
        }

        /// <summary>
        /// 创建闭合的多边形
        /// </summary>
        /// <param name="points"></param>
        /// <param name="tr"></param>
        /// <param name="db"></param>
        /// <returns></returns>
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

        /// <summary>
        /// 向列表中添加不重复的点（容差去重）
        /// </summary>
        /// <param name="list"></param>
        /// <param name="pt"></param>
        private static void AddUniquePoint(List<Point3d> list, Point3d pt)
        {
            foreach (Point3d existing in list)
            {
                if (pt.DistanceTo(existing) < Tolerance.Global.EqualPoint) return;
            }
            list.Add(pt);
        }
    }
}