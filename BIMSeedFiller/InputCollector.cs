using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using System;

namespace BIMSeedFiller
{
    internal static class InputCollector
    {
        /// <summary>
        /// 获取种子
        /// </summary>
        /// <param name="ed"></param>
        /// <returns></returns>
        public static PromptEntityResult GetSeed(Editor ed)
        {
            PromptEntityResult result;
            bool isValid = false;

            do
            {
                result = ed.GetEntity("请选择种子...");
                if (result.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n未选中任何对象，请重新选择。");
                }

                isValid = ValidateSeed(result.ObjectId, ed);
                if (!isValid)
                {
                    ed.WriteMessage("\n所选对象不是合法种子，请重新选择。");
                }

            } while (!(result.Status == PromptStatus.OK && isValid));

            return result;
        }

        /// <summary>
        /// 获取边界
        /// </summary>
        /// <param name="ed"></param>
        /// <returns></returns>
        public static PromptEntityResult GetBoundary(Editor ed) 
        {
            PromptEntityResult result;
            bool isValid = false;

            do
            {
                result = ed.GetEntity("\n请选择边界...");
                if (result.Status != PromptStatus.OK)
                {
                    ed.WriteMessage("\n未选中任何对象，请重新选择。");
                }

                isValid = ValidateBoundary(result.ObjectId, ed);
                if (!isValid)
                {
                    ed.WriteMessage("\n所选对象不是合法边界，请重新选择。");
                }

            } while (!(result.Status == PromptStatus.OK && isValid));

            return result;
        }

        /// <summary>
        /// 检查种子是否合法
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="ed"></param>
        /// <returns></returns>
        private static bool ValidateSeed(ObjectId objectId, Editor ed)
        {
            // 开启数据库和事务
            Database db = HostApplicationServices.WorkingDatabase;
            // 开启操作数据库的事务（操作AutoCAD的数据存储）
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. 获取种子（相当于unity里面的gameobject,DBObject 是 AutoCAD 里所有图形对象的基类）
                DBObject obj = tr.GetObject(objectId, OpenMode.ForRead);

                // 2. 检查能否正确获得种子
                if (obj == null) 
                {
                    ed.WriteMessage("\n错误：未发现种子。");
                    return false;
                }
                
                // 3. 检查是不是 Polyline
                if (!(obj is Polyline))
                {
                    ed.WriteMessage("\n错误：所选对象不是多段线(Polyline)。");
                    return false;
                }
                Polyline polyline = (Polyline)obj;

                // 4. 检查图层名是不是 "种子"
                if (polyline.Layer != "种子")
                {
                    ed.WriteMessage("\n错误：图层名字必须是“种子”。");
                    return false;
                }

                // 5. 检查是否闭合：Polyline.Closed == true？
                if (!polyline.Closed)
                {
                    ed.WriteMessage("\n错误：种子未闭合。");
                    return false;
                }

                // 6. 检查是否全由直线构成：遍历所有顶点，确保每个顶点的 Bulge 值都为 0。
                bool hasArc = false;
                for (int i = 0; i < polyline.NumberOfVertices; i++)
                {
                    if (polyline.GetBulgeAt(i) != 0.0)
                    {
                        hasArc = true;
                        break;
                    }
                }
                if (hasArc)
                {
                    ed.WriteMessage("\n错误：种子图形不能包含圆弧段。");
                    return false;
                }

                // 7. 检查顶点数是不是 4
                if (polyline.NumberOfVertices != 4)
                {
                    ed.WriteMessage("\n错误：种子图形必须是矩形（4个顶点）。");
                    return false;
                }

                // 8. 检查是否为矩形（直角验证）：
                //    获取四个顶点，计算四条边的向量，然后验证三个角是否都是直角。
                //    （两个向量垂直的充要条件是数量积为0）
                // 8.1 获取四个顶点
                Point3d p0 = polyline.GetPoint3dAt(0);
                Point3d p1 = polyline.GetPoint3dAt(1);
                Point3d p2 = polyline.GetPoint3dAt(2);
                Point3d p3 = polyline.GetPoint3dAt(3);

                // 8.2 获取向量
                Vector3d v01 = p1 - p0;
                Vector3d v12 = p2 - p1;
                Vector3d v23 = p3 - p2;
                Vector3d v30 = p0 - p3;

                // 8.3 检查三个角是否为直角（点积接近0，考虑浮点误差）
                if (Math.Abs(v01.DotProduct(v12)) > Tolerance.Global.EqualPoint ||
                    Math.Abs(v12.DotProduct(v23)) > Tolerance.Global.EqualPoint ||
                    Math.Abs(v23.DotProduct(v30)) > Tolerance.Global.EqualPoint ||
                    Math.Abs(v30.DotProduct(v01)) > Tolerance.Global.EqualPoint)
                {
                    ed.WriteMessage("\n错误：种子图形必须是矩形（四个角需为直角）。");
                    return false;
                }

                // 9. 检查面积是否大于 0
                if (polyline.Area <= 0)
                {
                    ed.WriteMessage("\n错误：种子图形面积必须大于0。");
                    return false;
                }

                // 10 提交事务并返回true
                tr.Commit();
                return true;
            }
        }

        /// <summary>
        /// 检查边界的合法性
        /// </summary>
        /// <param name="objectId"></param>
        /// <param name="ed"></param>
        /// <returns></returns>
        private static bool ValidateBoundary(ObjectId objectId, Editor ed)
        {
            // 开启数据库和事务
            Database db = HostApplicationServices.WorkingDatabase;
            // 开启操作数据库的事务（操作AutoCAD的数据存储）
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                // 1. 获取边界
                DBObject obj = tr.GetObject(objectId, OpenMode.ForRead);

                // 2. 检查能否正确获得边界
                if (obj == null)
                {
                    ed.WriteMessage("\n错误：未发现边界。");
                    return false;
                }

                // 3. 检查是不是 Polyline
                if (!(obj is Polyline))
                {
                    ed.WriteMessage("\n错误：所选对象不是多段线(Polyline)。");
                    return false;
                }
                Polyline polyline = (Polyline)obj;

                // 4. 检查图层名是不是 "边界"
                if (polyline.Layer != "边界")
                {
                    ed.WriteMessage("\n错误：图层名字必须是“边界”。");
                    return false;
                }

                // 5. 检查是否闭合：Polyline.Closed == true？
                if (!polyline.Closed)
                {
                    ed.WriteMessage("\n错误：边界未闭合。");
                    return false;
                }

                // 6. 边界的面积必须大于0
                if (polyline.Area <= 0)
                {
                    ed.WriteMessage("\n错误：边界图形面积必须大于0。");
                    return false;
                }

                // 7. 提交事务并返回true
                tr.Commit();
                return true;
            }
        }
    }
}
