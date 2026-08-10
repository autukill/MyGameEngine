# Audio 短音效黄金路径

Audio 已从纯逻辑运行时推进到可真实播放的短音效闭环：

- `Engine.Features.Audio`：逻辑 Clip、Bus、代际 Voice、WAV 解码、抢占与诊断。
- `Engine.Features.Audio.OpenAL`：Silk.NET OpenAL/OpenAL Soft 设备后端。
- `ContentAssets`：声明式 WAV 加载、包依赖、引用计数与回滚。
- `Engine.Hosting`：可选设备装配、每步回收和关闭顺序。

## 先理解 Audio 术语

可以先记住一条最小链路：

```text
WAV 文件 / 程序生成波形 → PCM → AudioLibrary 中的 Clip → Play 创建 Voice → Bus 混音 → Backend 输出
```

| 术语 | 在本引擎中的含义 | 直觉类比 |
|---|---|---|
| Sample | 某一瞬间的声音振幅数值 | 波形上的一个点 |
| Sample Rate | 每秒采集多少次；例如 48 kHz 表示每秒 48,000 个 Sample | 动画的帧率，但对象是声音波形 |
| Channel | Mono 是一个声道，Stereo 是左右两个声道 | 一条或两条同步波形 |
| PCM | 未压缩的 Sample 序列；当前短音效最终都以 PCM8/PCM16 进入运行时 | 可直接交给音频设备的像素数据 |
| Audio Clip | 可重复播放的一份逻辑声音资源，包含元数据和可选的已解码 PCM | Sprite 资源 |
| `AudioClipRef` | Clip 的强类型逻辑名称，不携带 PCM，也不代表正在播放 | `SpriteRef` / 资源钥匙 |
| Voice | 一次实际播放实例；同一个 Clip 可以同时产生多个 Voice | 同一个 Sprite 的多个 GameInstance |
| `AudioVoiceRef` | 正在播放的 Voice 句柄，用于停止或调整这一次播放 | 可失效的实例引用 |
| Bus | 一组声音的混音通道；内置 `Master`、`Music`、`Sfx` | 音量分组或调音台轨道 |
| Buffer | Backend 缓存的 PCM 数据；同一 Clip 的并发 Voice 可以共享 | GPU 纹理资源 |
| Backend | 真正对接设备的实现；当前为 OpenAL 或不发声的 Silent Backend | 渲染后端 |

`Volume` 是单次 Voice 音量，`Pan` 控制左右声道位置，`Pitch` 同时改变播放速度与音高，`Loop` 控制是否循环，`Priority` 在 Voice 容量不足时参与抢占决策。

“Source”在音频资料中容易产生歧义：`AudioSourceRef` 是 Clip 的来源标识，例如文件路径或 `procedural://` 诊断 URI；OpenAL Source 则是后端内部的播放对象。本引擎用 `AudioVoiceRef` 暴露一次播放，不要求 Gameplay 直接管理 OpenAL Source。

需要特别区分：

- `AudioClipRef` 表示“播放哪段声音”；
- `AudioPlayOptions.Sfx` 表示“通过哪个 Bus、采用什么播放参数”；
- `AudioVoiceRef` 表示“刚刚开始的这一次播放”。

因此 `context.Audio.Play(hitClip, AudioPlayOptions.Sfx)` 不是直接播放引用中的数据。Runtime 会先用 `hitClip` 从共享 `AudioLibrary` 找到 Clip，再创建一个 Voice，经 `Sfx` 和 `Master` Bus 混音，最后交给 Backend。

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

## Clip 的两种注册入口

`assets.json` 只是 Clip 的一种来源，并不要求所有声音都必须来自文件。当前有两条入口，注册完成后得到的都是 `AudioClipRef`，播放 API 完全相同。

### 入口一：声明式 WAV

适合正式美术/音频资源：脚步、受击、环境声、配音片段等。构建阶段会校验、复制资源并生成强类型引用，包卸载时自动移除 Clip：

```csharp
context.Audio.Play(
    GameAssets.AudioClips.PlayerShot,
    AudioPlayOptions.Sfx);
```

### 入口二：运行时注册 PCM

适合极短的合成提示音、原型、测试和无外部授权素材的 Playground。游戏先生成或取得 PCM，再直接注册到 `AudioLibrary`：

```csharp
var clip = new AudioClipRef("flappy.hit");
if (!context.AudioClips.TryGet(clip, out _))
{
    byte[] pcm = BuildHitTone();
    clip = context.AudioClips.RegisterDecoded(
        "flappy.hit",
        "procedural://flappy.hit",
        new DecodedAudioClip(
            pcm,
            AudioSampleFormat.Signed16,
            channels: 1,
            sampleRate: 48_000));
}

context.Audio.Play(clip, AudioPlayOptions.Sfx);
```

`procedural://flappy.hit` 只是诊断用的来源标识，不是文件路径。Flappy Bird Playground 的拍动、得分和撞击音都使用这条路径：代码生成正弦波并加入衰减包络，然后通过 `RegisterDecoded` 注册。具体实现见 [`playgrounds/FlappyBird/Program.cs`](../playgrounds/FlappyBird/Program.cs)。因此它的 `assets.json` 只有 Texture/Sprite，没有 `audioClips`，仍然可以正常播放音效。

两条入口的选择建议：

- 正式、可由内容作者替换的音效优先放入 `assets.json`；
- 简单电子提示音、测试夹具和原型可以生成 PCM；
- 不要为了回避资产管线，把大型 WAV 转写为 C# 字节数组；
- 程序生成的 Clip 应在 Scene 装配或启动阶段注册一次，不要在每帧或每次 `Play` 前重新生成；
- 重复进入 Scene 时先 `TryGet`，避免重名注册；若需要由 Scene 独占和卸载，优先使用 Content Package 所有权。

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

一般的一次性音效不需要保存 `AudioVoiceRef`：

```csharp
context.Audio.Play(hitClip, AudioPlayOptions.Sfx);
```

只有需要提前停止、运行时调音量或查询是否仍在播放时，才保存返回值：

```csharp
AudioVoiceRef voice = context.Audio.Play(loopingClip, options);
context.Audio.SetVoiceVolume(voice, 0.5f);
context.Audio.Stop(voice);
```

高频且允许被拒绝的反馈音优先使用 `TryPlay`，避免 Voice 已满时用异常表达正常资源竞争；必须发声且容量不足代表配置错误时使用 `Play`。

`CaptureDiagnostics()` 返回值快照：活动数、容量、请求、启动、拒绝、抢占和后端 Stop 总数。Audio 不进入确定性 Gameplay State；需要影响玩法时，由游戏保存自己的命令或 Signal。

## 资源与关闭顺序

OpenAL 为首次播放的已解码 Clip 创建并缓存 Buffer，同一短音效的并发 Voice 共享 Buffer。`AudioLibrary.Remove` 会通知后端：没有活动 Voice 时立即删除 Buffer；仍在播放时延迟到最后一个 Voice 结束。

Hosting 的顺序是：

1. Content Package 删除逻辑 Clip；
2. `AudioRuntime` 停止活动 Voice；
3. OpenAL 删除 Source/Buffer、释放 Context 和 Device。

`LoadedContentPackage.Dispose()`、Runtime 和 Backend Dispose 都是幂等的。

## 测试边界

`Audio.Tests` 负责 Clip/Bus/Voice、抢占、WAV 解码和 Silent Backend 的纯逻辑行为；`Audio.OpenAL.Tests` 负责 Backend 生命周期以及声明式内容到设备边界的集成路径。

```powershell
dotnet run --project src/Engine.Features/Audio.OpenAL.Tests/Audio.OpenAL.Tests.csproj -c Release
```

声明式集成测试会在安全的临时目录创建真实 PCM16 Mono WAV 与 `assets.json`，再经过 `ContentPackageManager → AudioLibrary → OpenAlAudioBackend/CreateOrSilent` 完成加载和播放。它同时验证包释放先移除逻辑 Clip，已有 Voice 停止后才释放 Backend Buffer。测试音量为零且允许回退到 Silent Backend，因此适合没有物理音频设备的开发机和 CI；实际听感仍需人工设备测试。

## 实战经验

- Gameplay 结果先落地，再播放声音。伤害、得分、死亡不能依赖“声音是否成功开始”，这样 Silent Backend、静音和 Voice 抢占都不会改变玩法。
- 短音效默认使用 `Sfx` Bus，背景音乐使用 `Music` Bus；不要把每个音效都直接挂到 `Master`，否则玩家无法分别调节音乐与音效。
- 多数定位音效使用 Mono。Stereo 更适合已经包含左右空间信息、无需运行时 Pan 的素材。
- 同一个 Clip 可以频繁播放；Backend 会共享 Buffer，不需要为了并发枪声复制 Clip。
- 连续快速触发的音效要关注 Voice 数量、Priority 和听感叠加。必要时由游戏增加冷却、合并反馈或调低旧 Voice 优先级。
- `--smoke` 使用 `ForceSilentBackend: true` 时，`Play`、Voice 生命周期和资源释放仍会执行，只是不访问扬声器；它用于验证逻辑，不用于验证实际听感。
- 音量听感不是线性的。资源制作阶段避免削波，运行时为多个同时播放的音效留出余量，不要默认把所有 Clip 都推到最大响度。
- 当前程序生成 PCM 的 `byte[]` 由 `AudioLibrary` 中的 `DecodedAudioClip` 持有；适合短音效，不适合长音乐。

## 当前边界

- 本阶段只做预加载短音效，不做 Streaming Music。
- 不支持 OGG/Opus/MP3/FLAC、异步解码或 Ring Buffer。
- Audio Clip 暂不参与 Content Hot Reload；修改音频后需重启应用。
- 不支持 Fade、Crossfade、Ducking、3D/HRTF、DSP Graph、录音或设备切换。
- `SilentAudioBackend` 是确定的运行时回退，不把“没有声音”伪装成设备成功。

下一切片应实现流式音乐数据源与 OGG/Opus 解码，同时保留现有 `AudioClipRef`、Bus 和 Gameplay 调用方式。
