namespace GameEngine.Features.Physics;

using System.Numerics;
using System.Runtime.CompilerServices;

public readonly record struct AABB(Vector2 Min, Vector2 Max)
{
    public bool Intersects(in AABB other)
    {
        return !(Max.X < other.Min.X || Min.X > other.Max.X ||
                 Max.Y < other.Min.Y || Min.Y > other.Max.Y);
    }
}

public class ColliderEntity
{
    public uint InstanceId { get; }
    public string Tag { get; }
    public AABB Bounds { get; set; }

    public ColliderEntity(uint id, string tag, AABB bounds)
    {
        InstanceId = id;
        Tag = tag;
        Bounds = bounds;
    }
}

public class SpatialHashGrid
{
    private readonly int _cellSize;
    private readonly Dictionary<long, List<ColliderEntity>> _buckets = new();

    public SpatialHashGrid(int cellSize = 64)
    {
        _cellSize = cellSize;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private long HashCellKey(int x, int y)
    {
        // 将 2D 网格坐标打包为唯一的 64-bit 组合 Hash Key
        return ((long)x << 32) | (uint)y;
    }

    public void Clear()
    {
        foreach (var bucket in _buckets.Values)
        {
            bucket.Clear();
        }
    }

    /// <summary>
    /// 将碰撞实体插入空间网格桶
    /// </summary>
    public void Insert(ColliderEntity entity)
    {
        int minX = (int)Math.Floor(entity.Bounds.Min.X / _cellSize);
        int maxX = (int)Math.Floor(entity.Bounds.Max.X / _cellSize);
        int minY = (int)Math.Floor(entity.Bounds.Min.Y / _cellSize);
        int maxY = (int)Math.Floor(entity.Bounds.Max.Y / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = HashCellKey(x, y);
                if (!_buckets.TryGetValue(key, out var bucket))
                {
                    bucket = new List<ColliderEntity>(16);
                    _buckets[key] = bucket;
                }
                bucket.Add(entity);
            }
        }
    }

    /// <summary>
    /// 高性能对标 GMS place_meeting：检测目标预测位置是否与指定 Tag 的物体重叠
    /// </summary>
    public bool PlaceMeeting(AABB predictedBounds, string targetTag, out ColliderEntity? hitEntity)
    {
        hitEntity = null;

        int minX = (int)Math.Floor(predictedBounds.Min.X / _cellSize);
        int maxX = (int)Math.Floor(predictedBounds.Max.X / _cellSize);
        int minY = (int)Math.Floor(predictedBounds.Min.Y / _cellSize);
        int maxY = (int)Math.Floor(predictedBounds.Max.Y / _cellSize);

        for (int x = minX; x <= maxX; x++)
        {
            for (int y = minY; y <= maxY; y++)
            {
                long key = HashCellKey(x, y);
                if (_buckets.TryGetValue(key, out var bucket))
                {
                    for (int i = 0; i < bucket.Count; i++)
                    {
                        var target = bucket[i];
                        if (target.Tag == targetTag && predictedBounds.Intersects(target.Bounds))
                        {
                            hitEntity = target;
                            return true; // 命中碰撞！立刻返回 $O(1)$
                        }
                    }
                }
            }
        }

        return false;
    }
}
