using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BIMSeedFiller
{
    internal class TileStatistics
    {
        public struct Result
        {
            public int StandardCount;    // 未裁剪种子个数
            public int ClippedCount;     // 裁切种子个数
            public double StandardArea;  // 未裁剪种子总面积
            public double ClippedArea;   // 裁切种子总面积
        }

        /// <summary>
        /// 对瓦片列表进行分类统计
        /// </summary>
        /// <param name="tiles">待统计的瓦片列表</param>
        /// <returns>统计结果</returns>
        public static Result Calculate(List<Tile> tiles)
        {
            Result result = new Result();

            if (tiles == null || tiles.Count == 0)
                return result;

            foreach (Tile tile in tiles)
            {
                if (tile.IsClipped)
                {
                    result.ClippedCount++;
                    result.ClippedArea += tile.Area;
                }
                else
                {
                    result.StandardCount++;
                    result.StandardArea += tile.Area;
                }
            }

            return result;
        }
    }
}
