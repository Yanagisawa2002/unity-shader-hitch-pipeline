# Linux Player 机会诊断：已完成准备与启动条件

2026-09-15 恢复授权后的状态：**轻量源码准备已完成；nativeExecutionReady=false。**
协调任务的现有五分钟巡检负责后续唤醒；没有新建重复自动任务。
旧只读暂停已解除。本轮仅使用隔离的本地轻量依赖/编译环境，未启动 Unity、下载场景或模块、登录服务器或进行硬件实验。
本文件接续[审阅结论](D:/CodexWork/shader-whole-task-20260915/Docs/WHOLE_TASK_REVIEW_5090_2026-09-15.md)，不改写原有历史证据或首次发现方案的冻结哈希。

## 已落实的有限适配

| 项目 | 已实现行为 | 验证边界 |
|---|---|---|
| 版本化 Linux/后端观察 | [PsoWholeTaskCell](D:/CodexWork/shader-whole-task-20260915/Integrations/ExternalScenes/Adapter/PsoWholeTaskCell.cs) 要求显式 `linux-vulkan-v1` 或 `windows-d3d12-v1`，匹配实际 6000.5.9f1 Player、API、GPU 和构建/内容身份；启动及退出采集 cgroup、CPU、实际 Unity job worker 数、内存与内核驱动原始元数据。 | 目前仅识别 cgroup v2 命名空间根 `0::/`，其他布局返回 unavailable 并拒绝进入已验证条件。可见逻辑 CPU、时间额度与实际 job worker 数分别记录，任何一项漂移都会阻止配对验收。 |
| Linux 成本复用边界 | 保留原核心兼容规则；诊断观察的 `driverBytesAttested=false`、`calibratedLinuxCostReuseSupported=false`。 | 未实现 Linux 驱动字节证明，未开放已校准成本复用。新平台仍需重新 trace/plan；元数据来源不会冒充 Windows 驱动文件证明。 |
| 直接原生 progressive 对照 | [PsoNativeProgressiveControl](D:/CodexWork/shader-whole-task-20260915/Integrations/ExternalScenes/Adapter/PsoNativeProgressiveControl.cs) 直接调用 `GraphicsStateCollection.WarmUpProgressively(count, JobHandle, false)`；不经过项目 scheduler/backend wrapper。显式固定 count，每个 Update 最多一次、最多一个未完成 job，只在原有 Warming 窗口提交。 | 保留 loading 加四场景的原 collection、文件哈希、平台/API/count 校验、实际逐调用及完成记录；shutdown fence 单列。无调用、少场景、错误 API、并行提交或部分完成不能通过 native-path 验收。仍未在真实 Player 上调用此路径。 |
| 固定画面检查点 | [PsoRenderCheckpointRecorder](D:/CodexWork/shader-whole-task-20260915/Integrations/ExternalScenes/Adapter/PsoRenderCheckpointRecorder.cs) 在每场景原有预热的 1 秒，以及 Running 路线的 10%/50%/90% 各记录一张 PNG，共 16 张。记录实际 Timeline 时间、相机姿态、请求/读回帧、像素尺寸和哈希。 | 不改变相机或 Timeline；第一个越过目标的真实帧最多晚 0.25 秒，超出就保留证据并失败。要求非 batchmode 的真实图形 Player/end-of-frame 路径。截图与深入 profiler 分开运行。 |
| 画面验收与 profiler 扰动门槛 | [离线证据检查](D:/CodexWork/shader-whole-task-20260915/Tools/pso_whole_task_evidence.py) 校验 PNG CRC、像素行解压长度、1920×1080、真实姿态关联和目标时间；独立画面审阅回执需绑定每张图与 capture 哈希。profiler 配对核对相同 Player/hash/参数/环境及完整流程，记录有符号差值。 | 平整的合成 PNG 即使结构正确也不会自动获得 visualAccepted。profiler 实际启用状态与请求不符会失败；配对工具永远不自动认证低开销、因果关系或性能收益。 |
| 显式跨编译目标 | [宿主构建器](D:/CodexWork/shader-whole-task-20260915/Integrations/Urp3DSample/Adapter/Editor/PsoUrpSampleBuild.cs) 区分 Windows/D3D12 与 Linux/Vulkan，设置对应运行平台、IL2CPP、原 PC High 和完整原场景；[准备器](D:/CodexWork/shader-whole-task-20260915/Tools/prepare_urp_sample.py) 固定新引擎 Linux 依赖。 | 借用的 Windows Editor 自身仍以 D3D12 导入；其 API 与目标 Linux Player Vulkan 分开记录。宿主尚无原场景依赖，整套 adapter/构建过程不能仅凭共享库编译宣布通过。 |

## 本轮实际检查

可随源码审阅的[验证回执](D:/CodexWork/shader-whole-task-20260915/Docs/Verification/whole-task-preparation-20260915.json)记录改动文件哈希与检查范围；
[保留日志副本](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/whole-task-preparation-20260915/README.md)已纳入交付。

- **93/93 Python 检查通过**，包括 9 项新增证据反例。缺少 `jsonschema` 的旧失败保留；本轮在本任务 venv 安装 `jsonschema==4.26.0` 后重跑。
  [最终日志](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/whole-task-preparation-20260915/python-worker-identity-tests.log)、[依赖锁](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/whole-task-preparation-20260915/python-requirements-lock.txt)。
- **共享诊断 C# 编译通过**：实际 Unity 6000.5.9f1 Managed 引用，`UNITY_6000_5_OR_NEWER`，0 错误、7 个已有 legacy DTO 未赋值字段警告；未启动 Editor。
  范围为 profiler/export、运行观察、直接 native control、检查点组件及其包 Runtime/Core 引用。
  [编译日志](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/whole-task-preparation-20260915/shared-diagnostics-worker-identity-compile.log)。
- [两份 PowerShell 语法检查](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/resumed-preparation/powershell-syntax.json)没有错误；`git diff --check` 通过。
  原 URP Benchmark/Cinemachine 宿主依赖未导入，所以完整宿主编译、画面、native collection 行为、二进制 profiler 覆盖与 GPU/呈现测量均未验证。

## 在 Windows 构建、服务器仅运行的具体条件

Unity 官方支持使用 Windows 上的 Linux IL2CPP cross-compiler 构建 Linux Player，前提是安装 IL2CPP 模块、正确配置 IL2CPP，并有工具链所需磁盘。
因此服务器不必先安装 Unity Editor。[Unity 跨编译说明](https://docs.unity3d.com/6000.5/Documentation/Manual/linux-il2cpp-crosscompiler.html)。

本地只读库存见[build-readiness.json](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/resumed-preparation/build-readiness.json)：

- Editor：`D:/CodexWork/babel-dot-20260915/Artifacts/dot-20260915/unity/Editor/Unity.exe`，6000.5.9f1。
  PlaybackEngines 目前只有 `windowsstandalonesupport`，**Linux IL2CPP 支持模块未就绪**。该 Editor 属于已有任务，不直接改动其安装目录。
- 其 `Data/Resources/PackageManager/Editor/manifest.json` SHA-256 为
  `93736756f132f53b981d9abbf86832c143ca92cdbaf414fa0f0f366f2f3f70db`。
  它指定 `com.unity.toolchain.win-x86_64-linux`、`com.unity.sysroot.base`、`com.unity.sdk.linux-x86_64` 均为 **1.1.0**；旧 `*-linux-x86_64` 名称已标 deprecated。
  采用该实际 Editor 清单，不能机械套用旧文档包名或其 2 GB 大小估计。压缩、展开、工作峰值尚未测量。
- 08:18 UTC 本地 D: 可用 239.672 GiB；这只是当时容量读数，**不是本机硬件队列放行**。
  获得本地重型阶段资格后，需有任务独享/明确可用的匹配 Linux 模块与 cross-compiler，并将 UPM/TEMP/sysroot 缓存置于任务 D: 目录；不更改全局缓存。
- 固定原 URP 归档及所有原始成员验证、6000.5 导入迁移差异和 Graphics Settings 实际自动预热配置仍需核验。
  原始路线控制与手工 native progressive 控制必须证明没有额外自动启动预热；Unity 自动预热方案的正式对照选择仍留在机会诊断之后。

准备器已提供参数：`--editor-version 6000.5.9f1 --build-cell linux-vulkan-v1`。
Editor 阶段提供 `-ExpectedUnityVersion 6000.5.9f1 -BuildCell linux-vulkan-v1`。
这些参数准备好了，当前没有执行它们。所有大文件准备、导入和构建继续经
[Invoke-PsoWholeTaskStage](D:/CodexWork/shader-whole-task-20260915/Tools/Invoke-PsoWholeTaskStage.ps1)检查本地三个前驱 handoff、互斥锁、负载与容量。

### Player-only 载荷与磁盘门槛

服务器只部署生成的 Linux x86-64 Player、它的完整 `_Data`/UnityPlayer/native 依赖、固定启动清单和轻量监控/分析工具。
不上传 Editor、项目 Library、源归档、Windows Player、NuGet/UPM 缓存或私有凭据文件。
对实际载荷逐文件记录相对路径、字节数、SHA-256 和可执行权限；核对生成的 ELF、完整数据文件及 native 依赖。
静态 ELF/依赖检查并不替代容器 Vulkan 运行验证。

本次建议的 **Linux 阶段上限** 与旧 Windows 导入预算不同，尚待实际 Player 索引确认：

| 阶段 | 开始前必须满足的占用条件 |
|---|---|
| 首次传输及展开 | 设已生成载荷展开量为 E、传输包为 A。要求 `E ≤ 6 GiB`、`A ≤ 6 GiB`；实际剩余空间至少 `A + E + 2 GiB 阶段杂项 + 10 GiB 保留`。未生成 Player 前，E/A 均 unavailable。不得假设旧 2.45 GB Windows Player 就是 Linux 大小。 |
| 一次深入 profiler 运行 | 已展开载荷和所有已保留证据之外，另需至少 `8 GiB 本次完整捕获上限 + 2 GiB 杂项 + 10 GiB 保留`。监控触及上限时停止本次 owned 进程并保留失败；不能截掉 raw 尾部后把完整路线诊断判为成功。8 GiB 是限制，不是已测大小或完整覆盖保证。 |
| 后续运行 | 每次按实际已占用量重新检查，保存所有结果。多份完整 raw 会累积，50 GiB 不保证整个 pilot 都放得下；条件不足就不启动下一次，不删除失败记录腾出“好结果”空间。 |
| 若机会成立，需要训练/最终计划 | Linux training Player 在 5090 产生新 trace/collection → 下载并核验哈希到构建机 → 原宿主 ProcessInbox/安装计划/最终 Linux Player 构建 → 上传新索引载荷。训练及最终 Player 若并存，将两者 E/A 和证据存量都计入。初始诊断不预先做这轮训练或策略矩阵。 |

Linux 共享规则要求至少保留 10 GiB 数据盘空间；上表沿用该下限，并另设本阶段杂项空间。
新 GPU 暂归 Data Layout，只有协调任务授予 Shader 对应阶段后才可传输/运行；每个重型远程命令持有实际主机锁直至所有子进程退出。
需要能生成旧分析约定的 `command.json`、`process.json`、`stage.json` 的 Linux 进程监控适配，包含实际起止/退出码/资源/锁释放记录；**该远程启动监控尚未完成 native 验证**。
当前不调用 SSH 帮助程序、不建立显示服务、不安装模块、不扩盘。

## 最小机会诊断次序

此处是限定的下一阶段步骤，不是性能确认矩阵。每次均保持同一个完整四场景路线，失败/超时不替换。

1. **先过构建和平台条件。** 完整宿主 IL2CPP 构建成功，固定 Player 全文件索引；容器 NVIDIA Vulkan 真设备和可用图形 surface 已被验证。
   必须使用原图形 Player 子目标。不能以 `-nographics`、server build、软件 renderer 或未经验证的离屏替身满足这一步。
2. **D0：首个完整 profiler 诊断。** 原路线控制，深入 profiler 开启、截图关闭。保留首次应用进程、所有长帧和启动盲区；不称其为驱动冷缓存。
   正确性尚需下一步画面验证，D0 不能自行获得性能结论。
3. **C0：独立画面正确性运行。** 同 Player，深入 profiler 关闭，启用 16 个固定检查点；所有四条路线与原 CSV 完成。
   校验 PNG/姿态后实际查看所有检查点并写入绑定 capture/image 哈希的审阅回执。缺点、错误画面或未知渲染差异阻止后续比较。
4. **P1–P4：有限 profiler 扰动与重复性检查。** 在容量允许时按预声明 `OFF / ON / ON / OFF` 四次完整进程，截图全关。
   相同 session 标签和工作参数，仅输出路径/深入 profiler 标志不同。分别将 P1/P2、P4/P3 组成两个方向的配对，保留 D0 和 C0。
   缓存持续保留，配对差值只能描述观察到的扰动，不能证明独立冷缓存或“零开销”。
5. **手工关键路径判断。** 结合 metadata frame join、实际可用 marker、线程样本与等待、加载/退出时间检查重复阻塞。
   无 marker 或缺 raw 覆盖时保持 unknown；无可复现机会则止步并写 NO-GO/证据限制。
   只有机会成立后，才申请新平台 trace/plan 和一个固定 `count=16` 的原生 progressive 正确性控制；这不是策略参数搜索或正式优劣确认。

共同 Player 参数的形状如下，真实文件路径须由持锁监控器绑定到新阶段目录：

```text
-force-vulkan -screen-fullscreen 0 -screen-width 1920 -screen-height 1080
-pso-external-capture -pso-external-observer-only -pso-disable-warmup
-pso-whole-task-cell linux-vulkan-v1 -pso-session shader-discovery-01
-pso-output <new-capture-directory> -logFile <new-stage-player.log>
```

D0/P2/P3 加 `-pso-whole-task-profile`；C0 仅加 `-pso-fixed-checkpoints`。
后续原生控制再加 `-pso-native-progressive-control -pso-native-progressive-count 16 -pso-native-progressive-plan <verified-plan.json>`。
原生控制必须与原路线控制采用相同完整最终 Player/内容，而不能用不同编译或不同渲染质量来比较。

离线分析入口已可用：

- `Tools/pso_whole_task_analysis.py <stage> --expected-unity 6000.5.9f1 --expected-cell linux-vulkan-v1 --expected-gpu <observed-and-frozen-exact-name> --output <new-audit.json>`。
  对检查点加入 `--visual-review <review.json>`；对 native control 加 `--native-count 16`；有导出目录时再加 `--profile-export`。
- `Tools/compare_pso_profiler.py <off-audit.json> <on-audit.json> --output <new-pair.json>` 只产生带局限的完整过程差值。
- 二进制可下载后在相同版本构建机通过 `PsoWholeTaskProfilerExport.Export` 离线导出；raw 文件保留，导出帧覆盖不完整时不会假装整个进程都已归因。

## 当前 ready 与阻塞汇总

**Ready：** 源码/内容锁、实际 API 共享库编译、93 项离线检查、显式平台和后端回执、固定画面检查点、profiler 配对分析与上述有限执行顺序。

**尚缺：** 本机队列资格及匹配 Linux 模块；原宿主输入与完整导入/构建/迁移验收；容器实际图形路径与阶段监控验收；
当前远程资源授权及实测 E/A/捕获存储余量；新 Player 的真实调用、画面和 profiler 覆盖。
已授权的可逆准备继续有效，无需以旧 review pause 请求重复许可；上述技术条件满足后由协调任务推进下一阶段。
