# Audio 短音效黄金路径

Audio 已从纯逻辑运行时推进到可真实播放的短音效闭环：

- `Engine.Features.Audio`：逻辑 Clip、Bus、代际 Voice、WAV 解码、抢占与诊断。
- `Engine.Features.Audio.OpenAL`：Silk.NET OpenAL/OpenAL Soft 设备后端。
- `ContentAssets`：声明式 WAV 加载、包依赖、引用计数与回滚。
- `Engine.Hosting`：可选设备装配、每步回收和关闭顺序。

## Hosting 快速开始

```csharp
using var game = GameApplication
    .Create(options)
    .UseAudio()
    .UseDefault2DRenderer(renderer => renderer.UseContent(GameAssets.Packages.Root))
    .ConfigureScene("main", context =>
    {
        AudioClipRef shot = context.GetAudioClip("player.shot");
        context.Audio.Play(shot, AudioPlayOptions.Sfx);
    })
    .Build();
```

`.UseAudio()` 默认最多提供 32 个逻辑 Voice。OpenAL 设备不可用时默认回退到 `SilentAudioBackend`：游戏继续运行，循环、完成、停止和抢占语义仍然有效，但不产生声音。

需要在开发机严格发现设备问题时：

```csharp
.UseAudio(new AudioHostingOptions(
    MaxVoices: 64,
    FailureMode: AudioInitializationFailureMode.Throw))
```

无窗口 smoke 应显式使用 `ForceSilentBackend: true`，不探测物理设备。

## 声明式短音效

`assets.json` 可以声明静态 WAV：

```json
{
  "schemaVersion": 1,
  "id": "game.audio",
  "dependencies": [],
  "audioClips": [
    {
      "name": "player.shot",
      "path": "audio/player-shot.wav",
      "streaming": false
    }
  ]
}
```

构建管线会复制 WAV 并生成 `GameAssets.AudioClips.PlayerShot`。运行时在包装配阶段同步解码；路径不能逃逸包目录，重名、缺失文件、非 WAV、非法 PCM 和 `streaming: true` 都会在包可见前失败并回滚。

当前 WAV 解码器支持：

- RIFF/WAVE；
- 未压缩整数 PCM；
- PCM8 或 PCM16；
- Mono 或 Stereo；
- 8 kHz 到 384 kHz；
- 完整帧对齐的静态数据。

游戏音效优先使用 Mono，既便于 Pan，也减少解码内存。

## Runtime、Bus 与 Voice

```csharp
AudioPlayOptions options = new(
    AudioBusRef.Sfx,
    Volume: 0.8f,
    Pan: -0.2f,
    Pitch: 1.0f,
    Priority: 5);

AudioVoiceRef voice = context.Audio.Play(GameAssets.AudioClips.PlayerShot, options);
context.Audio.SetVoiceVolume(voice, 0.5f);
context.Audio.Stop(voice);
```

内置 Bus 为 `Master`、`Music`、`Sfx`。最终增益是 `Voice × Bus × Master`；Mute 和音量变化会立即应用到活动 Voice。

Voice 使用 Slot + Generation。容量耗尽时先选择 Priority 不高于新请求的 Voice，再按最低 Priority、最早启动顺序抢占。没有合法候选时 `TryPlay` 返回 `false`，`Play` 抛出异常。

`CaptureDiagnostics()` 返回值快照：活动数、容量、请求、启动、拒绝、抢占和后端 Stop 总数。Audio 不进入确定性 Gameplay State；需要影响玩法时，由游戏保存自己的命令或 Signal。

## 资源与关闭顺序

OpenAL 为首次播放的已解码 Clip 创建并缓存 Buffer，同一短音效的并发 Voice 共享 Buffer。`AudioLibrary.Remove` 会通知后端：没有活动 Voice 时立即删除 Buffer；仍在播放时延迟到最后一个 Voice 结束。

Hosting 的顺序是：

1. Content Package 删除逻辑 Clip；
2. `AudioRuntime` 停止活动 Voice；
3. OpenAL 删除 Source/Buffer、释放 Context 和 Device。

`LoadedContentPackage.Dispose()`、Runtime 和 Backend Dispose 都是幂等的。

## 当前边界

- 本阶段只做预加载短音效，不做 Streaming Music。
- 不支持 OGG/Opus/MP3/FLAC、异步解码或 Ring Buffer。
- Audio Clip 暂不参与 Content Hot Reload；修改音频后需重启应用。
- 不支持 Fade、Crossfade、Ducking、3D/HRTF、DSP Graph、录音或设备切换。
- `SilentAudioBackend` 是确定的运行时回退，不把“没有声音”伪装成设备成功。

下一切片应实现流式音乐数据源与 OGG/Opus 解码，同时保留现有 `AudioClipRef`、Bus 和 Gameplay 调用方式。
