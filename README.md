# BIMSeedFiller - AutoCAD 种子填充插件

基于 AutoCAD .NET API 开发的二维种子填充插件，实现带旋转角度的矩形种子在任意闭合边界内的自动铺满与精确裁剪。支持直线与圆弧混合边界，提供填充与统计两个命令。

## 功能

| 命令 | 功能 |
|------|------|
| `Seed` | 选择种子和边界，自动铺满整个边界内部区域 |
| `Statistics` | 选择一个已填充的边界，统计完整种子和裁切种子的个数及面积 |

## 运行环境

- AutoCAD 2022
- Windows 10/11 64位
- .NET Framework 4.8

## 快速开始

### 加载插件

1. 启动 AutoCAD 2022
2. 在命令行输入 `NETLOAD`，回车
3. 选择 `BIMSeedFiller.dll`，点击打开

### 使用步骤

1. 用 `PLINE` 命令绘制闭合边界，图层设为 **"边界"**
2. 用 `RECTANG` 或 `PLINE` 命令绘制矩形种子，图层设为 **"种子"**（种子必须完全位于边界内部，且为闭合矩形）
3. 命令行输入 `Seed`，按提示依次选择种子和边界
4. 等待程序自动完成填充
5. 命令行输入 `Statistics`，选择边界查看统计结果

## 技术亮点

### 旋转网格全覆盖算法

通过方向向量与行列式反算，将包围盒采样点投影到旋转网格坐标系，计算最小行列范围并扩展一圈，保证网格绝对覆盖任意形状边界。

### 精确的内外判断

实现三道筛选（包围盒排斥、边相交检测、顶点射线法），并在完全在内判断中增加边相交检测，防止凹角误判。

### 裁剪方案探索

先后尝试 Region 布尔运算、MPolygon、手工点集重建三种方案，最终采用边修剪 + Andrew 凸包算法实现稳定裁剪。

### 完整的工程架构

OOP 继承体系（Tile/StandardTile/ClippedTile）、职责分离（Commands/InputCollector/TileFiller/TileStatistics）、Dictionary 实现多次填充独立统计。

## 项目结构
BIMSeedFiller/
├── Commands.cs # 插件命令入口（Seed、Statistics）<br>
├── InputCollector.cs # 用户输入获取与校验<be>
├── TileFiller.cs # 核心填充算法<be>
├── TileStatistics.cs # 统计功能<br>
├── DataModel<br>
├   ├── Tile.cs # 瓦片基类<br>
├   ├── StandardTile.cs # 完整矩形瓦片<br>
├   └── ClippedTile.cs # 裁剪后瓦片<br>
├── Data<br>
├   ├── SeedData.cs # 种子数据容器<br>
├   └── BoundaryData.cs # 边界数据容器<br>
└── BIMSeedFiller.csproj # 项目文件<br>
