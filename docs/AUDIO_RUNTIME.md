# Audio 基础垂直切片

`Engine.Features.Audio` 建立逻辑 Clip、Bus、代际 Voice、确定性 Voice 抢占与可替换 Backend 的边界。当前没有平台音频 Backend 或 OGG/WAV 解码器，因此它是可测试的运行时基础，不应描述成已经能从扬声器播放声音。

## Clip Library

```csharp
var clips = new AudioLibrary();

AudioClipRef shot = clips.Register(
    "player.shot",
    "audio/player-shot.ogg",
    new AudioClipMetadata(
        Duration: TimeSpan.FromMilliseconds(150),
        Channels: 1,
        SampleRate: 48_000));
```

`AudioSourceRef` 是逻辑来源，不是文件流、Native Buffer 或 GPU/Audio Handle。后续 ContentAssets/Audio Decoder 负责将它解析为 Backend 可播放资源。

## Runtime 与 Voice

```csharp
using var audio = new AudioRuntime(clips, backend, maxVoices: 32);

AudioPlayOptions options = new(
    AudioBusRef.Sfx,
    Volume: 0.8f,
    Pan: -0.2f,
    Pitch: 1.0f,
    Priority: 5);

AudioVoiceRef voice = audio.Play(shot, options);

audio.SetVoiceVolume(voice, 0.5f);
audio.Stop(voice);
```

Voice 使用 Slot + Generation。停止、自然完成或抢占后，旧引用不能误操作复用 Slot 中的新声音。

Runtime 每帧调用 `Update()` 回收 Backend 已完成的非循环 Voice。

## Bus

内置稳定逻辑 Bus：

- `AudioBusRef.Master`
- `AudioBusRef.Music`
- `AudioBusRef.Sfx`

也可以在启动阶段 `RegisterBus`。最终音量为：

```text
Voice Volume × Bus Volume × Master Volume
```

Bus Mute 和 Volume 修改会立即重放到活动 Voice。第一版只有 Master + 单层 Bus，不提供任意 Bus DAG、Effect Send 或 Ducking。

## Voice 上限与抢占

容量耗尽时：

1. 只考虑 Priority 小于或等于新请求的 Voice。
2. 优先抢占最低 Priority。
3. Priority 相同时抢占最早启动的 Voice。
4. 如果所有 Voice 都受更高 Priority 保护，`TryPlay` 返回 `false`；`Play` 抛出明确异常。

这允许 Music 使用较高 Priority，短促 SFX 在预算内确定性抢占。

## Backend 所有权

`IAudioBackend` 负责：

- `Play`
- `SetMix`
- `IsPlaying`
- `Stop`
- `Dispose`

Runtime 默认借用 Backend；`ownsBackend: true` 时幂等 Dispose 会先停止所有活动 Voice，再释放 Backend。Backend Handle 只存在于适配接口，不暴露给 Gameplay。

## 当前边界

- 无真实 OpenAL/SDL/miniaudio Backend。
- 无 WAV/OGG/MP3 解码和 Content Manifest。
- 无 Streaming Ring Buffer；`Streaming` 目前只是 Metadata。
- 无 Fade、Crossfade、3D Spatial Audio、HRTF、DSP Graph 或录音。
- 尚未接入 Hosting、GameSdk、模板或性能诊断。
- Audio 默认不进入确定性 Gameplay State；需要玩法影响时由游戏发布明确命令/Signal。

下一切片应先选择跨平台、NativeAOT 可分发的 Backend，并用真实短 SFX 与 Streaming Music 验证设备丢失、关闭顺序和无声 CI Fake Backend。
