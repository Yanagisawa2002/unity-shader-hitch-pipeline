# Shader Hitch Pipeline：整任务证据缺口与 RTX 5090 迁移审阅

日期：2026-09-15。状态：**审阅完成；执行暂停；整任务净收益尚未证实。**

审阅基于独立检出 `D:/CodexWork/shader-whole-task-20260915`，PR 4 源码头
`9c7fc7b75fa7ac1bca2e407821414cf5941e9b8c`，以及暂停前的未提交诊断改动。
PR 4 仍为 OPEN / Draft，未合并。本轮未登录服务器，也未启动硬件实验、下载、安装、Unity 导入或构建。
原[发现阶段方案](D:/CodexWork/shader-whole-task-20260915/Docs/WHOLE_TASK_PROTOCOL_2026-09-15.md)保持原样；本报告指出其尚缺的验收和需要修订的比较设计。

## 1. 已通过什么：历史实验与本轮检查分开

| 证据 | 实际结果及可用结论 | 精确文件 |
|---|---|---|
| 历史 Boat Attack 修复后完整实验 | 16/16 进程完成原生路线；disabled 整体观察时间中位数 32.970 s，scheduled 33.487 s；没有调度器整任务提速证据。所有进程保留，最大 Update 含 observed-budget 的 90.256 ms。 | [完整报告](D:/CodexWork/shader-whole-task-20260915/Docs/EXTERNAL_BOAT_ATTACK_2026-09-14.md:113)、[原生验证回执](D:/CodexWork/shader-whole-task-20260915/Docs/Verification/boatattack-native-20260914.json)、[独立审计](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/boat-attack-20260914/independent-audit.json) |
| 历史驱动身份检查修复 | 重复驱动文件哈希约 562–563 ms 降到 0.27–0.42 ms；初次完整哈希成本仍保留。这是自身取证开销修复，已经包含在 PR 4，不能再计为预热收益。 | [诊断时间证据](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/boat-attack-20260914/attestation-diagnostic-timings.json)、[报告因果分析](D:/CodexWork/shader-whole-task-20260915/Docs/EXTERNAL_BOAT_ATTACK_2026-09-14.md:78) |
| 历史官方 URP 四场景实验 | 16/16 进程通过四条完整 Timeline、原始 CSV、相机绑定及正常退出要求。disabled / scheduled 中位数为 264.786502 / 264.757952 s；固定时长路线上的约 0.029 s 差异不能支持净收益。每进程都有约 212–221 ms 早期 Update；另有未解释的 381.3446 ms，必须保留。 | [完整报告](D:/CodexWork/shader-whole-task-20260915/Docs/EXTERNAL_URP_SAMPLE_2026-09-14.md:133)、[原生验证回执](D:/CodexWork/shader-whole-task-20260915/Docs/Verification/urp-sample-native-20260914.json)、[独立审计](D:/CodexWork/shader-whole-task-20260915/Docs/Evidence/urp-sample-20260914/independent-audit.json) |
| 本轮微型离线检查 | 6/6 分析契约测试通过；记录的 PowerShell 语法检查没有错误。只验证合成输入处理、缺失值及诊断数据隔离，不能证明 C# 编译、真实场景或性能。后续源码改动尚未完成全量验证。 | [6 项日志](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/preparation/analysis-contract-tests.log)、[范围回执](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/preparation/lightweight-validation.json) |
| 本轮完整 Python 检查 | **未通过**：两个 Python 环境均报告 `Ran 76 / FAILED (errors=2)`，两个模块因缺少 `jsonschema` 未加载。不能报告为 76 项通过；没有为此安装依赖。 | [默认环境日志](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/preparation/python-functional-tests.log)、[捆绑环境日志](D:/CodexWork/shader-whole-task-20260915/work/whole-task-20260915/preparation/python-functional-bundled-runtime.log) |

历史两个实验属于 Unity 6000.1.0f1 / Windows / D3D12 / AMD R9700、训练及试跑后缓存保留的同一硬件条件。
本轮核对的是已发布报告和回执，旧原始二进制与逐帧捕获不在此检出中，**没有重新校验其全部原始字节，也没有复现实验**。
旧 native entry 数量增长不能等同于真实编译 miss 或有效覆盖率。GPU 时间、显示呈现时间与新服务器收益均未验证。

## 2. 最小完整任务和最终输出边界

选择已有官方 URP 3D Sample 17.1.5，不再增加宿主项目。输入锁定于
[source-lock.json](D:/CodexWork/shader-whole-task-20260915/Integrations/Urp3DSample/source-lock.json)：
原始归档 924,710,979 字节，SHA-256
`c2a2bcbd8bac1340be683b33ca53e1fddc78c2ae80fa3a0b5406bd616df6628f`。

一个任务单位是：**启动 Player → 原 BenchmarkScene → Terminal、Garden、Oasis、Cockpit
依次完成异步加载、原有 5 秒渲染预热、Timeline 重置及整条路线 → 原始汇总 CSV 发布 → 正常退出并完成证据写出。**
四条路线约为 68.8 / 50.117 / 35 / 85 秒；不是每场景截取 500 帧。
原生 CSV 与四个 Finished 状态共同决定完成，见
[退出条件](D:/CodexWork/shader-whole-task-20260915/Integrations/Urp3DSample/Adapter/PsoUrpSampleCapture.cs:162)。

真实消费者是原场景相机、URP 渲染通道和 GPU；shader、材质及渲染状态产生真实 draws。
最终业务输出是这些路线产生的画面和原始基准 CSV，collection/plan 是中间产物。
无需添加 GPU 结果回读作为新任务要求，但需要验证实际渲染内容；CSV、相机回调或进程退出码各自都不足以证明画面正确。
最低正确性包含固定输入/构建哈希、四场景相机与 Timeline 绑定、完整路线和 CSV、真实 GPU/API、无渲染错误，
以及独立正确性运行中的固定检查点画面对照。跨 API 对照允许预先解释的数值差异，不能接受缺材质、粉色 shader 或缺失通道。
画面对照与深入 profiler 不进入正式计时。

时间边界必须并列记录：启动至首个真实渲染、各次加载和预热、整个 Update 区间、CSV 完成、操作系统观察到的退出。
历史观察器至首个 Update 有 3.520540–3.842977 秒盲区；新增 BeforeSplash profiler 也不能覆盖它之前的进程启动。
无法归因的区间单列保留。CPU Update、渲染提交、GPU 完成和屏幕呈现使用不同名称，不互相替代。

## 3. 真正缺少的实验与公平比较

以下是恢复执行后的依赖顺序，当前均未启动。

| 优先级 | 当前缺口 | 最小必要补充与通过条件 |
|---|---|---|
| P0：实现与宿主正确性 | 新 profiler、导出器、observer-only 和 6000.5 适配尚未编译；原归档没有下载、导入或构建。完整 Python 检查也未通过。 | 补齐固定依赖后通过相关 CPU/编译检查；核验归档；完成一次固定引擎的导入、迁移差异审阅与完整 IL2CPP Player 正确性运行。保存所有失败和修补差异。编译通过仍不能代替四场景验收。 |
| P1：是否存在可优化的真实瓶颈 | 旧 CPU 长帧没有 shader/PSO 创建的关键路径归因。新方案三次带 profiler 运行也不能证明其自身开销无影响。 | 在原始路线控制上做完整诊断，保留首次和重复启动、早期与场景切换长帧。核对实际可用的 shader/PSO marker、主/渲染线程等待、嵌套样本及跨线程关联；再用关闭深入 profiler 的低侵入运行核对扰动。不能把同帧相关或并行样本之和称为阻塞时间。没有可复现机会时停止优化实验；缺 marker 时结论是未测到，不能说不存在。 |
| P1：缓存条件可比较 | 训练、诊断和基线都会预热后续进程，首次启动不是驱动冷缓存。 | 分开记录应用、OS、驱动缓存；任何冷缓存主张都需可复现、受支持且各 arm 等价的控制。若只能保留缓存，正式结论限定为该条件，不能声称解决首次用户启动。随机或平衡顺序不能消除不可逆缓存污染。 |
| P2：常规方案及独立原生方案 | 原 disabled arm 仍有 shader retention/phase bridge 成本；项目自己的 all-at-once 不是独立外部基线。Unity 6.5 的原生自动预热尚未纳入旧方案。 | A：原应用 5 秒预热路线，仅公共低侵入观察器；B：直接 Unity GraphicsStateCollection 预热，在真实 collection 可用和既有加载窗口内调用，绕过项目调度器；C：只冻结一个项目候选。6.5 必须评估原生 Graphics Settings 预加载/渐进设置；若与 B 的时机和调用不同，增加原生 arm，若合并则提供等价证据。共同 shader 保留和关闭预热控制用于分离项目成本。所有方案用同一完整路线、质量、资源和训练信息。 |
| P2：覆盖、收益与代价闭环 | 480 个跨阶段记录项不是 480 个唯一 PSO；旧 397→781 增长不证明 miss。只报预热批次速度会遗漏启动、训练和常驻资源。 | 验证当前引擎实际支持的 miss 跟踪与 collection 完成情况，在诊断阶段记录首次真实消费和残余创建；同时量化启动、运行、内存、构建/训练/计划生成成本。缺少可解释身份时覆盖率保持 unavailable。 |
| P3：独立确认、规模与统计 | 原方案“每 arm 至少 4 进程”只够起始 pilot；没有足够精度保证，也没有真实小/中/大规模扫描。 | 根据独立 pilot 的进程差值波动与预先规定的最小有意义改善，封存正式样本量、平衡随机区组顺序、预算和停止规则。以完整进程/区组为统计单位，报告配对差值、不确定区间、所有最大值和失败；不能把上万帧当独立重复。全部四场景保留并报告 draw、variant、可识别状态和资源量，不能自动将其命名为三档规模。 |

### 需要补进原方案的判断规则

- **原生基线选择：** Unity 6.5 提供预加载 collection、启动 Warmup、异步执行、首场景前后预热和每帧渐进数量配置。
  正式比较前按相同可用信息和用户等待预算选择合理配置，并记录所有设置；不可故意采用不合时机的阻塞调用削弱基线。
  见 [Unity Graphics Settings](https://docs.unity3d.com/6000.5/Documentation/Manual/class-GraphicsSettings.html#shader-loading)。
- **主指标：** 每个完整过程的 CPU Update 超过 33.333 ms 的超预算时间总量；同时报告 16.67/33.333/50/200/500 ms 阈值计数、p95/p99 和最大值。
  启动等待、全部加载/预热、整体退出时间和内存是必要约束。固定 Timeline 总时长或某一批次快一点，不能单独判胜。
- **总成本：** 对每个方案记录归档准备、导入、shader/IL2CPP 构建、真实路线训练、合并/计划生成、额外包体及磁盘峰值；
  每次启动记录 collection 读取、身份检查、预热、shader/collection 常驻内存、CPU/GPU 资源和最终序列化。
  分别报告首次部署及 1/5/20 次启动摊销：`准备 wall time / N + 每次启动至正常退出 wall time`。
  对照本身所需构建成本也计入，并报告项目增量。帧超预算时间不能再加到整体 wall time 中重复计费；GPU/呈现、能耗未测就保持 unavailable。
- **范围：** 训练与正式确认分开，候选在确认前冻结。训练过的四条路线可以支持“固定内容重复启动”的结论；独立进程不等于未见内容泛化。
  若要主张跨规模泛化，再预先定义真实输入的三个工作量档位，并在每档内保持所有方案输出等价。
  不通过增加无实际消费的 shader 变体制造机会，也不从四场景中只发表最有利场景。
- **停止条件：** 正确性失败、缓存条件不可比较、无足够可归因机会、或不确定区间无法支持有意义改善时，报告 NO-GO 或证据不足及恢复条件。
  旧 381.3446 ms 等不利样本不能删掉或以成功替补覆盖。

## 4. Ubuntu 容器 + RTX 5090 能否支持

### 已知服务器条件与尚未核验的能力

以下硬件信息由协调任务的本轮只读核验提供，本任务没有独立登录复核：Ubuntu 22.04.5 Docker，
RTX 5090 32607 MiB、驱动 580.76.05，检查时 GPU 空闲；Vulkan ICD、GLX/EGL 库存在，DISPLAY/X 服务为空。
`cpu.max=2500000/100000` 是 **25 核时间额度**，cpuset 0–207 不代表 208 个独占核；内存额度 90 GiB。
系统盘 30 GiB、数据盘 50 GiB；GCC 11.4、CMake 3.22.1 可见，Unity/dotnet/dxc/nvcc 在 PATH 未找到，不等于全盘不存在。

这足以把 Linux/Vulkan 作为候选环境，尚不能证明 Unity 能在容器内以 RTX 5090 运行原始渲染路线。
仍需匹配版本的构建工具链/模块与许可证、原始完整资产、真实 NVIDIA Vulkan 设备及可用的图形 surface/显示路径。
CUDA 工具链不是此完整任务的必需项。`-nographics` 在 batch mode 下不初始化图形设备，不能用作 PSO/画面验收；
无显示的 GPU 离屏输出若经验证，也只能形成明确标注的离屏条件，不能宣称屏幕呈现改善。
见 [Unity Player 命令行说明](https://docs.unity3d.com/6000.5/Documentation/Manual/PlayerCommandLineArguments.html)。

### 当前源码的移植边界

| 边界 | 源码证据 | 最小变更及结论影响 |
|---|---|---|
| 当前完整构建/运行只支持 Windows D3D12 | [构建器](D:/CodexWork/shader-whole-task-20260915/Integrations/Urp3DSample/Adapter/Editor/PsoUrpSampleBuild.cs:35)、[启动脚本](D:/CodexWork/shader-whole-task-20260915/Tools/Invoke-PsoExternalPlayer.ps1:30)、[验收器](D:/CodexWork/shader-whole-task-20260915/Tools/pso_urp_capture.py:87)、[正式冻结器](D:/CodexWork/shader-whole-task-20260915/Tools/pso_urp_comparison.py:50) | 需要 Linux Player/Vulkan 构建、进程监控、路径/锁/资源采集和明确的新环境验证器；不能复制 `.exe` 或只换 GPU 名称。原基准相机、路线、CSV 保留。 |
| 核心并非全部锁死 D3D12 | [构建验证器已有 Linux 映射](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Editor/PsoBuildValidator.cs:120)、[原生预热调用](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Runtime/PsoUnityGraphicsStateWarmupBackend.cs:105) | 可复用调度逻辑和 Unity 原生抽象；实际 Linux/Vulkan API 行为、资源释放和输出正确性仍需验证。不能把纯 .NET/分析脚本可移植当作完整管线可用。 |
| collection 与成本不能跨旧硬件沿用 | [平台/API 校验](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Runtime/PsoUnityGraphicsStateWarmupBackend.cs:32)、[设备/输入兼容检查](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Core/PsoCompatibility.cs:139) | 对 Linux/Vulkan/5090 重新 trace、构建计划和校准；既不能沿用 R9700，也不能沿用本地 4090 的成本。 |
| 驱动身份与已校准成本复用依赖 Windows | [非 Windows 返回 unavailable](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Runtime/PsoWindowsDriverIdentity.cs:42)、[成本身份检查](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Core/PsoCompatibility.cs:64) | 完整成本复用需要 Linux 驱动身份提供器及版本化兼容契约；容器用户态库与宿主驱动关系要可追溯。仅记录 `nvidia-smi` 版本字符串不等同于原字节证明。可先验证“不复用已校准成本”的范围受限路径，但那不能证明完整成本复用功能；此缺口也不等于所有原生冷启动/预热路径都无法运行。 |
| 引擎升级改变实际算法路径 | [默认后端选择](D:/CodexWork/shader-whole-task-20260915/Packages/com.yanagisawa.shader-hitch-pipeline/Runtime/PsoUnityGraphicsStateWarmupBackend.cs:63) | `auto` 在 6000.5 返回 progressive，旧 6000.1 返回 native-async-bulk；6000.5 还有不同 API 重载。每个 arm 固定并证明实际调用路径。否则同时改变引擎、后端、OS、GPU 的结果无法归因于某一项。 |
| 容器额度和磁盘尚不满足旧预算 | [原资源预算](D:/CodexWork/shader-whole-task-20260915/Docs/WHOLE_TASK_PROTOCOL_2026-09-15.md:108) | 50 GiB 数据盘不满足原方案 80 GiB 导入/构建附加预算加 20 GiB 保留。预算是估计，实际峰值未知；不能把 30+50 GiB 当作单工作卷容量。先核实受支持构建机能否生成 Linux IL2CPP Player，再评估只部署 Player/证据的较小服务器占用方案。当前不安装、不扩盘。 |

容器正式记录还需包含 CPU 时间额度、cpuset、实际 Unity worker 设置及节流统计；不能仅凭
`SystemInfo.processorCount` 把校准条件写成 208 核。旧 URP 首次构建 1958.10 s、最终 Player
2,450,900,245 字节只能帮助估计准备量，不能替代 Linux 的磁盘峰值测量。

**判断：当前实现不能直接在这台 Linux 5090 上完成既有 Windows/D3D12 验收。
经上述必要适配后，可能形成新的 Linux/Vulkan 条件；是否值得适配仍取决于图形能力、磁盘和机会诊断。**
不预设 5090 会带来调度净收益；新条件即使成功，也不继承旧 Windows 驱动哈希修复收益、D3D12 性能或屏幕呈现结论。

## 5. 可以省掉的实验与建议次序

1. **先解决证据缺口，暂不迁移。** 先完成 P0 审核及实施前的环境/存储可行性决定；Shader 不必因为服务器已经租用就优先上机。
   原 Windows 路径改动较少，Linux 路径需要新验收条件。选择哪条路径应在恢复授权时写入新冻结方案。
2. **有可归因机会后才比较策略。** 先做 P1 的少量完整原始路线诊断；不立即重复历史 4-arm × 4-process 全矩阵，
   不先跑参数搜索或 observed-budget 第四个项目策略。正式重复数由统计精度决定，不能因为旧方案写了 4 次就宣布足够。
3. **不重做已解释的旧缺陷展示。** 无需再重放驱动重复哈希修复前的全部慢实验；保留历史证据，并在新目标验证相关功能即可。
   不用重新渲染图表或重复输出截图代替缺少的正确性/因果证据。
4. **不增加任务替身。** 暂不引入 Boat Attack、Megacity 等第二宿主，不用合成 PSO 洪泛、CUDA 小程序、Wine/DXVK 转译或缩短路线来替代官方完整任务。
   跨宿主泛化与小/中/大规模只在要提出对应广泛主张时追加。
5. **交付可以是有证据的 NO-GO。** 最小有价值交付是完整路线正确性、瓶颈诊断、公平原生对照与总成本/不确定性；
   若不满足继续条件，保留失败、不利样本、不可用指标和明确恢复条件，无须强行得到正收益。

本报告完成后继续保持审阅状态。未提交实现和已有日志留在独立检出；没有占用硬件锁或本任务重型进程，
没有发布终结 handoff、推送未验证实现或触碰其他项目。
