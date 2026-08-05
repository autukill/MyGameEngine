namespace GameEngine.Core.Application.Handlers;

using GameEngine.Core.Application.Commands;
using GameEngine.Core.Domain.Aggregates;
using GameEngine.Core.Domain.Entities;
using GameEngine.Core.Domain.ValueObjects;

/// <summary>
/// 共享内核级命令处理器。
/// 只处理 Spawn/Destroy 这种**跨切片共享**命令。
/// 切片专属命令的 Handler 应放在对应 Vertical Slice 的 Application 子目录。
/// </summary>
public static class SceneCommandHandlers
{
    /// <summary>处理 SpawnInstanceCommand</summary>
    public static GameInstance Handle(SpawnInstanceCommand cmd)
    {
        var instance = cmd.Scene.Spawn(
            cmd.ObjectTypeName,
            cmd.Position,
            cmd.Depth);

        Console.WriteLine($"[Handler] Spawned {instance}");
        return instance;
    }

    /// <summary>处理 DestroyInstanceCommand</summary>
    public static void Handle(DestroyInstanceCommand cmd)
    {
        cmd.Scene.Destroy(cmd.InstanceId);
        Console.WriteLine($"[Handler] Destroyed instance {cmd.InstanceId}");
    }
}
