using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using System.Collections.Generic;

namespace BIMSeedFiller
{
    public class Commands
    {

        /// <summary>
        /// 种子填充
        /// </summary>
        [CommandMethod("Seed")]
        public void Seed()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;
            // 1. 获取用户选择的边界和种子
            PromptEntityResult seedResult = InputCollector.GetSeed(ed);
            PromptEntityResult boundaryResult = InputCollector.GetBoundary(ed);
            // 2. 调用 TileFiller.Fill(种子, 边界)
            bool isFillingCompleted = TileFiller.File(seedResult, boundaryResult);
            // 3. 提示用户完成
            if (isFillingCompleted)
            {
                ed.WriteMessage("\n填充完毕...");
            }
            else
            {
                ed.WriteMessage("\n填充失败...");
            }
        }

        /// <summary>
        /// 种子统计
        /// </summary>
        [CommandMethod("Statistics")]
        public void Statistics()
        {
            Editor ed = Application.DocumentManager.MdiActiveDocument.Editor;

            // 1. 获取用户选择的边界
            PromptEntityResult boundaryResult = InputCollector.GetBoundary(ed);
            if (boundaryResult.Status != PromptStatus.OK)
            {
                ed.WriteMessage("\n未选择有效边界，将统计最后一次填充结果。");
            }

            // 2. 获取填充结果
            List<Tile> tiles = TileFiller.GetTilesByBoundary(boundaryResult.ObjectId);
            if (tiles == null || tiles.Count == 0)
            {
                ed.WriteMessage("\n没有可统计的填充结果，请先运行 Seed 命令。");
                return;
            }

            // 3. 调用统计模块进行计算
            TileStatistics.Result stats = TileStatistics.Calculate(tiles);

            // 4. 在命令行输出统计结果
            ed.WriteMessage($"\n========== 种子填充统计 ==========");
            ed.WriteMessage($"\n未裁剪种子：{stats.StandardCount} 个，总面积：{stats.StandardArea:F2}");
            ed.WriteMessage($"\n裁切种子：{stats.ClippedCount} 个，总面积：{stats.ClippedArea:F2}");
        }
    }
}
