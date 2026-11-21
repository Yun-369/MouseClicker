附加停止路径：`D:\Agent\devverify-mc\logs\20261121-193909-background-schedule\verify-stops4.txt`，29 PASS / 0 FAIL / 1 SKIP，退出码 0；此轮注入已验证自有目标身份，跳过实际选窗；紧急停止使用 WM_HOTKEY 消息，托盘停止通过菜单项触发，非物理按键或人工托盘点击。完整真实入口选窗证据见 verify-closeout。

> 2026-11-21：新增针对所选前台窗口的后台连点和录制回放，以及延迟或指定本地时间启动。设置页勾选“后台窗口”，点击“选择当前前台窗口”后在倒计时内切到目标并将指针放到目标客户区；目标仅在本次进程内有效。连点的“更多设置”和录制回放区分别提供定时入口；底部“停止当前任务”可取消等待。后台通过 Windows 窗口消息投递，不保证游戏、原始输入应用或权限不同的程序接收；宏不使用后台模式。
>
> 定向证据：`D:\Agent\devverify-mc\logs\20261121-193909-background-schedule\verify-closeout.txt`，31 PASS / 0 FAIL，退出码 0；录制样本为测试程序内独立数据。Release 构建 0 警告 / 0 错误，退出码 0；小窗截图在该目录的 `visual-closeout`。历史记录中的后台与定时“已移除”仅描述当时版本。
# MouseClicker r9 实际流程验收清单（更新于 2026-11-16）

> 自定义启停快捷键（2026-11-20）：在“设置”中点“修改”，按组合（Esc 取消），点击“应用热键”生效；“保存设置”保存已生效的组合和触发模式。默认 F8；支持 F1–F12，及搭配 Ctrl 或 Alt 的字母、数字和空格，可加 Shift。待应用组合有明确提示；运行期间不能改键。紧急停止仍为 Ctrl+Shift+F12。唯一推荐入口：`D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe`，四个运行文件与本轮 Release 已逐字节核对。
>
> 本轮证据：`D:\Agent\devverify-mc\logs\20261120-162531-custom-hotkey\`。产品构建 0 警告 / 0 错误；验证工程构建 2 个既有 CS1998 警告 / 0 错误；退出码均为 0。定向检查：热键注册与旧设置迁移 2/0、录制等 Step1 检查 17/0、长按修饰键边界 4/0，退出码均为 0。最终版本设置页两种尺寸截图在 `delivery-screens`；最终候选实际 F8 启停及运行中禁改已验证。此前候选以真实键盘验证 Ctrl+K 捕获、冲突后旧组合启停、长按释放、Esc 取消与录制过滤；该录制优化副本动作数未变、未抽稀，保存在证据目录。随后增加了 Windows 键额外修饰过滤与旧设置迁移修正；最终版本已定向验证旧设置迁移和 Ctrl/Alt/Shift 过滤，未重复物理录制，Windows 键额外修饰过滤尚无运行验证。捕获对话框打开期间紧急停止未做真实组合实测。旧轮次截图与统计均为历史记录。

> 之前的紧凑排版（历史记录，2026-11-17）：默认外框 900×640，最小外框 800×560，侧栏约 150 像素；连点常用项按行并排，固定坐标和多点配置紧随位置模式，录制和回放共用工作区，热键与修饰键同排。唯一推荐入口为 `D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe`。最终 Release 构建 0 警告 / 0 错误、退出码 0；受影响 UI 检查 15 PASS / 0 FAIL，条件显示与保持检查在两种尺寸分别 10/0、11/0，加载录制与宏切页保持在两种尺寸均 5/0，以上退出码均为 0；最小尺寸展开及滚动检查 15/0、退出码 0。五页截图及模式截图、原始输出和退出码：`D:\Agent\devverify-mc\logs\20261117-010229-layout-rows\`，其中 `screens-final` 为两种外框尺寸五页截图。trial 的四个运行文件已与该 Release 逐字节核对一致。以下旧轮次记录与截图保留为历史证据。

> 上一轮紧凑版（历史记录，2026-11-17）：默认窗口外框 900×640，最小外框 800×560，侧栏 150 像素；唯一推荐入口 D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe。本轮截图及验证在 D:\Agent\devverify-mc\logs\20261117-004238-compact-ui\；下列较早版本的尺寸与截图为历史记录。
> 上一轮版本（历史记录）：清爽精简界面，唯一推荐入口 D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe。本轮证据 D:\Agent\devverify-mc\logs\20261116-234213-simple-ui\；下方较早轮次的界面、结果和截图均为历史记录。
> 上轮交付（2026-11-16 侧栏收尾，历史记录）：唯一推荐运行入口为 D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe；目录中的四个运行文件与本轮 Release 逐字节一致。旧 performance-20261116 包及下文旧轮次截图、统计为历史记录。侧栏收尾证据：D:\Agent\devverify-mc\logs\20261116-sidebar-closeout\；真实多屏及跨 DPI 专项不在本轮范围。
本清单保留 r9 各轮源码对应的实际运行证据；当前侧栏收尾的构建和界面检查见文末。产品范围保留原分类 **1、2、3、4、5、6、10**；宏动作只允许 `move`、`click`、`wait`、`loop`、`ref`、`key`、`text`。INFO 不计通过；各套件中的 SKIP 表示该项在那次套件运行中未执行，后续人工覆盖情况另列，不改写历史统计。每个真实 WinForms 验收脚本均以 PowerShell 7 `-STA` 串行运行，并通过 `Acc-Finish` 写入实际退出码。

## 历史 r9 收尾证据和构建

证据根目录：`D:\Agent\devverify-mc\logs\20261114-180131-r9-closeout\`。每个模块目录包含完整转录、命令、退出码、ACC 统计，以及该模块生成的样本和夹具私有遥测。较早的失败尝试也保留在该根目录中；下表只统计标出的最终运行目录。

| 操作 | 实际命令 | 退出码 | 结果 |
| --- | --- | ---: | --- |
| 产品 Release 构建 | `dotnet build "D:\Agent\MouseClicker\MouseClicker.csproj" -c Release` | 0 | 0 警告 / 0 错误，日志 `build-product-final2` |
| 验证工程 Release 构建 | `dotnet build "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release` | 0 | 2 个既有 CS1998 警告 / 0 错误，日志 `build-devverify-final4` |
| 夹具隔离 | `pwsh -STA -NoProfile -ExecutionPolicy Bypass -File "D:\Agent\devverify-mc\acceptance\verify-fixture-isolation.ps1" -OutDir "D:\Agent\devverify-mc\logs\20261114-180131-r9-closeout\fixture-isolation-final"` | 0 | 4 通过 / 0 失败 / 0 跳过 / 0 INFO |
| M1–M8 | 各模块均用 `pwsh -STA -NoProfile -ExecutionPolicy Bypass -File <模块脚本> -OutDir <该模块最终目录>` | 均为 0 | 见下表；每条命令、逐项结果和子进程退出码均保存在对应目录 |
| 回收站定向验证 | `dotnet D:\Agent\devverify-mc\bin\Release\net8.0-windows\mc-devverify.dll --run=recycle-policy`，工作目录为 `m8-policy-final3` | 0 | 9 通过 / 0 失败 |

### M1–M8 汇总

| 模块 | 通过 | 失败 | 跳过 | INFO | 最终运行目录 |
| --- | ---: | ---: | ---: | ---: | --- |
| M1 基础连点 | 15 | 0 | 0 | 0 | `m1-final` |
| M2 热键 / 托盘 / 设置 | 3 | 0 | 2 | 0 | `m2-final3` |
| M3 录制 | 4 | 0 | 2 | 0 | `m3-final` |
| M4 录制文件与回放 | 10 | 0 | 1 | 0 | `m4-final` |
| M5 录制编辑与优化 | 15 | 0 | 0 | 1 | `m5-final` |
| M6 基础宏 | 17 | 0 | 0 | 0 | `m6-final` |
| M7 前台窗口绑定 | 11 | 0 | 1 | 0 | `m7-final` |
| M8 录制库与回收站 | 13 | 0 | 0 | 2 | `m8-final` |
| **合计** | **88** | **0** | **6** | **3** | INFO 与 SKIP 均未计入通过 |

夹具隔离和定向回收站状态验证是独立套件，不包含在 88 项里。README 与交接文件都采用此最终日志口径；早期记录中的 63/62 属于不同旧轮次，不是本轮统计。

## 截至 2026-11-14 的完整回归与人工验收基线

本轮完整回归证据根目录：D:\Agent\devverify-mc\logs\20261114-192232-final-regression\。原 r9 界面验收证据仍在 D:\Agent\devverify-mc\logs\20261114-180131-r9-closeout\，没有覆盖或重跑 M1–M8。不同套件独立统计，不相加为一个“全部通过”数字。

| 项目 | 实际命令 | 退出码 | 结果与证据 |
| --- | --- | ---: | --- |
| M1–M8 界面验收（r9） | 各模块 PowerShell 7 STA 脚本，命令见旧证据目录内 command.txt | 各模块 0 | 88 PASS / 0 FAIL / 6 SKIP / 3 INFO；见 20261114-180131-r9-closeout |
| 夹具隔离 | pwsh -STA -NoProfile -ExecutionPolicy Bypass -File "D:\Agent\devverify-mc\acceptance\verify-fixture-isolation.ps1" -OutDir "D:\Agent\devverify-mc\logs\20261114-180131-r9-closeout\fixture-isolation-final" | 0 | 4 PASS / 0 FAIL；旧证据 |
| 回收站定向验证 | dotnet D:\Agent\devverify-mc\bin\Release\net8.0-windows\mc-devverify.dll --run=recycle-policy | 0 | 9 PASS / 0 FAIL；旧证据 m8-policy-final3 |
| 默认回归（最终验证器） | dotnet run --project "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release --no-build | 0 | 221 PASS / 0 FAIL / 0 SKIP；stdout/stderr/exit-code/stats 在 default-regression-after-encoding-fix |
| 桌面套件（2026-11-14 历史运行） | dotnet run --project "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release --no-build -- --desktop | 1 | 245 PASS / 1 FAIL / 0 SKIP；该次 S16.17 因 Caps Lock 前置条件不满足而在发送前停止；当时未执行前台获取。最新复测见下节。 |
| 宏编辑器真实进程 UIA | pwsh -STA -NoProfile -ExecutionPolicy Bypass -File "D:\Agent\devverify-mc\logs\20261114-192232-final-regression\pwsh-utf8-bootstrap.ps1" -TargetScript "D:\Agent\devverify-mc\macro-editor-check.ps1" | 0 | 18 PASS / 0 FAIL；详见 macro-editor-uia |
| 验证工程 Release 构建 | dotnet build "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release | 0 | 2 个既有 CS1998 警告 / 0 错误；verifier-rebuild-after-log-fix |
| 产品 Release 构建 | 本轮未执行 | — | 本轮未修改产品源码；沿用 20261114-180131-r9-closeout 中产品 Release 0 警告 / 0 错误的构建证据 |

编码修复前的完整尝试保留在同一新目录：default-regression 为 221/0、退出码 0，但内部 Step18 子进程探针的嵌套诊断乱码；desktop-suite 为 243/1、退出码 1，S16 EnsureFocus 出现 15 秒 Control.Invoke 超时。新日志收集器副本将 StandardOutputEncoding、StandardErrorEncoding 和文件写出编码统一为 UTF-8；Step18 子进程也显式输出 UTF-8，并由父进程按 UTF-8 解码。encoding-probe 逐字验证中文、英文、数字及 stdout/stderr。最终默认回归和 UIA 输出无替换字符。原 r9 日志未修改。

编码修复期间第一次构建退出码 1、47 个错误，原因是源码快照 .cs 被 SDK 当作验证工程输入；快照已改为 .cs.txt 并留存于 source-before-encoding-fix。随后验证工程构建退出码 0、2 个 CS1998 警告、0 错误。此错误和恢复均有独立日志，没有放宽测试断言。

截至 2026-11-14 的 M1–M8 自动化结果为 88 PASS / 0 FAIL / 6 SKIP / 3 INFO，其中 6 项 SKIP 分布为 M2 2 项、M3 2 项、M4 1 项、M7 1 项。该历史自动化统计保持不变；后续人工证据覆盖情况按当前功能状态列在本清单末尾。其后 S16.17 定向复测已通过，详见下节。

## S16.17 定向真实输入复测及最新桌面套件（2026-11-16）

新证据根目录：D:\Agent\devverify-mc\logs\20261116-133518-s1617-recheck\。没有修改 C# 源码，因此未重建；两项均通过既有 UTF-8 收集器和 Release --no-build 运行。每项目录保留实际命令、完整 stdout/stderr、退出码和统计。

| 项目 | 实际命令 | 退出码 | 结果与证据 |
| --- | --- | ---: | --- |
| S16.17 定向检查 | dotnet run --project "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release --no-build -- --run=step16 --realinput | 0 | 16 passed / 0 failed；s1617-realinput/ |
| 桌面套件（最新） | dotnet run --project "D:\Agent\devverify-mc\mc-devverify.csproj" -c Release --no-build -- --desktop | 0 | 246 passed / 0 failed；desktop-suite/ |

定向输出验证真实按键 a、Enter、b、Tab、c 后文本恰为 a\r\nb\tc，Enter 未意外确认，目标和焦点保持，fg=True、sent=True。真实 Ctrl+Enter 捕获 DialogResult.OK；Esc 经 EditValue 调用边界捕获 DialogResult.Cancel，调用方仍得到原值“原始”。按键发送使用 Down/Up 配对，Ctrl 修饰键有 finally 释放路径，自有弹窗在 finally 关闭且消息泵退出。桌面套件中的 Step18 故障注入嵌套 FAIL 行按设计出现；最终顶层汇总 246/0、退出码 0。

只读前置快照显示 Caps Lock 关闭且 Ctrl/Alt/Shift 均未按下；用户确认暂停其他键鼠操作并允许自有弹窗取得前台。复测后无本轮产品、夹具或验证器进程残留，settings.json、library.json 均未创建；产品日志仍为 8545 字节，时间戳仍为 2026-11-14 18:54:29。源证据及状态快照见上述目录中的 preconditions.txt 与 cleanup-and-user-state.txt。

## 模块结果

### M1：基础连点 — 15/0/0/0

最终证据：`m1-final/logs/m1-click.txt`、`m1-final/fixture-1/`。验证跟随光标、固定点左右键、中键、顺序和随机多点、按住后停止释放、F8 启动和 Ctrl+Shift+F12 紧急停止。多点顺序为 `P1,P2,P1,P2`；紧急停止前确认目标正在运行且夹具已收到 Down，停止后夹具收到 Up、产品回到空闲，观察窗没有新点击。

### M2：热键、托盘与设置 — 3/0/2/0

最终证据：`m2-final3/logs/m2-hotkey-tray.txt`。F8 启停后夹具在配置客户区点恰收到两次下压；修改间隔为 1234 并重启后读回 1234/1234。托盘按钮当前无法通过 UIA 操作，物理长按和运行中热键限制未执行，各记为 SKIP。`m2-final` 和 `m2-final2` 的失败尝试完整保留；最终脚本修正为仅按夹具私有日志的左键 Down 和目标坐标计数，避免把 Up 事件或非目标坐标噪声混成点击。

### M3：录制 — 4/0/2/0

最终证据：`m3-final/logs/m3-record.txt`。验证开始录制隐私提示、录制期间 F8 互斥提示、注入输入被产品按设计过滤，以及停止后回到空闲。无物理设备下没有取得可录制事件，因此物理录制保真、物理录制停止后保存及重载均为 SKIP；M5/M6 的受控样本和宏保存不替代这一完整流程。

### M4：录制文件与回放 — 10/0/1/0

最终证据：`m4-final/logs/m4-playback.txt`、`m4-final/recs/m4-controlled.json`。受控样本验证单轮、三轮、2×、0.5× 暂停/继续/停止和损坏 JSON 拒绝，精确检查 Down/Up 数量、点位和运行时间。DPI / 虚拟桌面不匹配确认流程需要不同显示环境，记为 SKIP。

### M5：录制编辑与优化 — 15/0/0/1

最终证据：`m5-final/logs/m5-optimize.txt`、`m5-final/recs/`。真实界面验证优化预览、系统另存、原文件字节不变、优化结果重载回放、等待编辑（250ms，累计时间线重算）、事件成对修改及另存重载、RDP 轨迹抽稀。抽稀保留拖拽段、按键/滚轮/Down/Up 与总时间线，源文件不变。1 项 INFO 说明物理录制仍需硬件验收；本轮未用优化或另存结果声称完成物理录制流程。

### M6：基础宏 — 17/0/0/0

最终证据：`m6-final/logs/m6-macro.txt`、`m6-final/macros/`、`m6-final/macroA/`、`m6-final/macroB/`。验证七动作宏加载与执行、引用与键盘/文本输入、编辑器真实保存和主界面采用、真实 OLE 行拖动、拖动后的自动保存和重开顺序、跨目录另存后相对录制引用仍解析到原文件、引用执行命中，以及取消另存保持状态。宏保存和宏执行不等于物理录制后的停止、保存、重载验收。

### M7：前台窗口绑定 — 11/0/1/0

最终证据：`m7-final/logs/m7-bind.txt`、`m7-final/macros/`、`m7-final/recs/` 与 `m7-final/fixture-1/`。窗口选择器真实选中本轮夹具；取点保存客户区坐标，移动目标后仍命中相同客户区点。M7.5 的失焦、最小化、关闭分别用新建目标窗口和有限长宏独立执行；每个触发前检查绑定正确、停止按钮可见、夹具已收到动作且宏尚未自然结束。

- **失焦**：键盘 `ref` 样本是 Down → 延迟 → Up。触发前夹具收到 B Down，`GetAsyncKeyState` 仍显示按住；把前台切到产品窗口后，停因是“目标窗口不在前台”，停止按钮消失、开始宏恢复、没有抢回焦点，之后没有后续动作，B Up 到达且异步键状态清零。
- **最小化**：目标仍在运行且收到完整点击对后最小化；停因匹配“目标窗口被最小化”，宏回空闲，后续计数稳定，测试结束恢复目标窗口。
- **关闭**：目标仍在运行且收到完整点击对后关闭本轮夹具；目标 PID 退出，停因匹配“目标窗口已关闭或不可见”，宏回空闲、开始宏恢复。销毁后的窗口不要求接收 Up，也没有把遥测停止误当成无后续发送证明。
- M7.4 跨 DPI、多显示器和负坐标为 1 项历史 SKIP，旧统计原样保留；当前状态为“本轮范围外”，不代表通过，也不属于必须完成的待验项。

### M8：录制库与回收站 — 13/0/0/2

最终证据：`m8-final/logs/m8-recycle.txt`、`m8-final/recs/`、`m8-final/recycle-parser-samples/`。`$I` 解析读取文件开头 8 字节版本号：v1 路径偏移 24；v2 偏移 24 读取字符数量、路径从偏移 28 开始。受控样本验证 v1、v2、未知版本、版本头截断、字符数越界、长度不足和路径终止不完整；未知布局明确失败。真实删除分支只使用本轮唯一录制和独立 `.keep.json` 副本。

当前 D: 回收站策略实读为 `NukeOnDelete=0`（已启用）。确认对话框指名本轮文件后，产品将该唯一文件放入回收站；按规范化完整原始路径和唯一身份匹配到真实 `$I` v2 记录，并确认库条目从 1 项变为 0。验证后只清理精确匹配的本轮 `$I/$R` 对，未清空回收站、未操作其他回收项、未改变回收站策略。独立副本保留在 `m8-final/recs/`。2 项 INFO 分别记录本轮策略分支与测试源已由产品删除后的收尾状态。

### 回收站定向测试 — 9/9，独立统计

最终证据：`m8-policy-final3/stdout.txt` 和 `m8-policy-final3/recycle-policy-evidence/`。覆盖已启用 DWORD 0、已关闭 DWORD 1、键缺失、值缺失、错误类型、未知 DWORD、读取异常，以及两种拒绝提示和无修改结果。已关闭提示含关闭原因；未知状态提示含“无法确认”。两个拒绝路径均未调用删除确认回调，逐字节比对源文件与 `library.json`，条目仍为 1 项。测试使用内存策略接缝，没有写入或改动真实注册表策略。

## 夹具隔离、用户状态与日志

- 夹具隔离证据：`fixture-isolation-final/logs/verify-fixture-isolation.txt`。4 项通过：两个实例的私有遥测目录不同、窗口进程 PID 与启动 PID 一致、计数互不串扰、停止 A 不会关闭 B 且 B 仍能继续计数。
- `tools/test-window.ps1` 支持 `-TelemetryDir`；每个夹具实例使用自己的遥测目录，启动 PID 与窗口归属绑定，收尾只关闭本轮自建夹具。
- 测试前 `settings.json` 与 `library.json` 均不存在；M2/M5/M6/M7/M8 各自捕获并恢复状态，最终仍不存在。没有结束用户正在使用的 MouseClicker 实例；本轮自有产品和夹具进程在收尾时退出。
- 旧 r9 界面验收期间产品日志随流程追加，未回滚；本轮新回归前后 run-20261114.log 均为 8545 字节，最后修改时间为 2026-11-14 18:54:29。

## 截至 2026-11-14 尚未执行的人工项目（历史状态）

截至 2026-11-14 的回归轮次无法物理操作键鼠或改变显示布局；这是历史限制。当前验收状态、交付范围和后续人工证据以本文件末尾的 2026-11-16 记录为准。

1. **物理热键与长按**：在产品窗口前台，实际按 F8 启停；使用物理长按模式按住、松开触发键；再运行时按 Ctrl+Shift+F12，检查回到空闲并释放输入。现有 F8 自动化是自建窗口上的合成热键路径。
2. **托盘操作（2026-11-14 历史步骤）**：用主窗口关闭按钮 X 隐藏到托盘，再从托盘选择“显示主窗口”；菜单按实际项目验证“开始 / 停止”、“停止”和“退出”，不假设存在单独的“隐藏”菜单项。当前结果见 2026-11-16 最终记录。
3. **物理录制完整流程**：开始录制并确认提示；用真实鼠标移动、点击、拖拽、滚轮和键盘输入；停止后保存 JSON，再从界面重新加载，检查事件顺序、Down/Up 配对、扫描码和扩展键标记。
4. **M7.4 特殊显示环境（历史范围项）**：原计划涉及跨 DPI、多显示器及负坐标。M7.4 的原始 SKIP 保留；本轮明确将跨 DPI、多显示器、负坐标及显示拓扑变化排除在验收范围外，不改为通过，也不继续安排专项测试或阻塞交付。
5. **DPI / 虚拟桌面不匹配提示**：该历史步骤已由 2026-11-16 有效录制元数据副本的真实界面加载、提示和取消零输入检查完成；不需要改变 Windows 显示设置。

真实回收站落地路径已在本轮 D: 启用策略下通过；这些待验收项不包括该路径。

### 2026-11-16 人工试用验收（最终记录）

证据目录：D:\Agent\devverify-mc\logs\20261116-140052-manual-acceptance\。

本轮未修改产品代码或验证代码，未重新构建，也未重跑已有有效自动化套件。以下人工结果独立于自动化统计。

#### 人工验收结果

| 项目 | 结果 | 证据 |
| --- | --- | --- |
| 物理录制、保存、界面重载、完整回放 | **通过（最终样本）**：真实鼠标移动、左/右键、短拖拽、滚轮和 mc17+Enter 已采集；系统对话框保存；产品界面显式重载校验通过；两次单轮回放均自然结束 | recordings\safe-retry-2\recording-20261116-154349.json、recording-safe-retry-2-analysis.json、playback-before.txt、playback-after.txt |
| 最终录制样本安全与字段 | **通过**：1701 events / 19986 ms；96 DPI、1920×1080 虚拟桌面；测试窗区域 1266,754–1886,1032。左键 Down/Up 3/3、右键 2/2、滚轮事件 27；键盘 Down/Up 5/5，VK/扫描码/Extended 字段保留；窗外鼠标输入 0、配对错误 0、结束时 held 0 | recording-safe-retry-2-analysis.json；SHA-256 25F6FFF8C436692A01DDFEC3B3ED712FB7762232A3CDB50AA57257F4D062FAF3 |
| 最终样本回放效果 | **通过**：产品日志记录两次 1×、单轮回放并自然完成（15:47:12.034–15:47:34.162、15:47:42.680–15:48:04.880）。计数在两秒后稳定；左右键、滚轮和键盘计数增加；held=none、Ctrl/Alt/Shift=0；输入文本为 mc17mc17（两次回车）。用户确认只看到自有测试窗变化 | playback-before.txt、playback-after.txt、fixture-telemetry-attempt2\、产品日志 C:\Users\Administrator\AppData\Local\MouseClicker\logs\run-20261116.log |
| 早期录制尝试 | **保留为无效样本，不回放**：recording-20261116-141348.json 含窗外输入且左键不平衡；其回放由用户真实按 Ctrl+Shift+F12 停止。recording-20261116-153642.json 含两对窗外点击，因为录制仍运行时在聊天窗口回复；这是本轮步骤安排造成的污染，未用于回放，也不据此归为产品缺陷 | recording-analysis.json、recording-safe-retry-analysis.json、replay-interruption.txt |
| 物理 F8 切换 | **通过**：用户实际按 F8 启动、再按 F8 停止；产品日志记录 37 次点击 | physical-checkpoint.txt、产品日志 |
| 物理长按模式 | **通过**：用户确认实际按住并松开 F8；日志记录多次短周期、累计 57 次点击后停止。该记录不表述为一次连续 57 点击运行；夹具最终无按住输入 | hotkey-f8-before-longhold\、fixture-telemetry-attempt2\、产品日志 |
| Ctrl+Shift+F12 紧急停止 | **通过停止运行中的回放**：用户真实按键后产品记录手动停止；用户未观察到其他窗口变化。此项独立于最终安全样本的自然完成 | replay-interruption.txt、产品日志 |
| 运行中修改热键限制 | **通过**：任务运行期间，用户观察到启停热键选择、触发模式及“应用热键”均禁用；未改值或保存。日志记录两次固定点启动后停止 | hotkey-runtime-check.txt、UI\MainForm.cs、产品日志 |
| 托盘隐藏与显示 | **通过**：主窗口 X 隐藏至托盘，PID 10940 仍运行；托盘“显示主窗口”恢复成功 | tray-hide-check.txt |
| 托盘“开始 / 停止”与独立“停止” | **通过**：菜单启停产生固定点点击并停止；独立“停止”菜单项也停止运行，计数停止增长且 held=none | tray-toggle-check.txt、tray-stop-item-check.txt、产品日志 |
| 托盘“退出” | **通过**：用户从托盘选择“退出”后，检查确认产品 PID 10940 已结束 | final-cleanup-check.txt |
| 文件元数据不匹配提示与取消零输入 | **通过**：产品真实界面加载有效 DPI/虚拟桌面元数据副本；两次均显示与差异字段一致的提示，用户取消后确认空闲且开始回放可用。日志无回放启动行，夹具输入遥测保持零 | D:\Agent\devverify-mc\logs\20261116-164103-environment-prompt-check\dpi-cancel-observation.json、virtualwidth-cancel-observation.json、product-log-appended.txt |
| 真实跨 DPI、多显示器、负坐标与拓扑变化（M7.4） | **本轮范围外**：M7.4 原始 SKIP 保留；元数据副本只验证不匹配提示及取消零输入，不代表真实坐标行为通过。本项不是本轮待验项或交付阻塞项 | 不安排本轮专项测试 |

第一次失败的遥测尝试及历史通过结果均保留；不以被污染样本或宏/优化样本替代最终物理录制闭环。

#### 环境不匹配提示与取消零输入（2026-11-16 后续验收）

本轮证据目录：D:\Agent\devverify-mc\logs\20261116-164103-environment-prompt-check\。未修改产品代码、验证代码或 Windows 显示设置，没有重新构建或重跑自动化套件。基准样本只通过界面加载并确认校验通过，没有回放。

| 样本 | 操作及证据结果 |
| --- | --- |
| recording-dpi-120.json | 在有效基准副本中只将 DPI 96 改为 120。产品界面加载校验通过；真实点击开始回放后提示 DPI 差异，用户选择取消并确认回到空闲、开始回放可用。产品日志无回放启动行，夹具输入计数为零、held=none。证据：dpi-loaded-before-start.json、dpi-cancel-observation.json。 |
| recording-virtualwidth-1921.json | 只将 VirtualWidth 从 1920 改为 1921。产品界面加载校验通过；用户提供的提示正文明确指出录制 1921×1080、当前 1920×1080，随后选择取消并确认回到空闲、开始回放可用。产品日志无回放启动行，夹具计数、鼠标、键盘、文本两次快照保持零；标题未单独转录。证据：virtualwidth-loaded-before-start.json、virtualwidth-cancel-observation.json、post-run-state.json。 |

两项结论限于“文件元数据不匹配时提示正确，用户取消后不执行样本”。样本点在准备时位于夹具内；最终遥测显示夹具矩形已有变化，因此本轮不证明坐标换算或真实显示环境行为。完整用户提示转录、夹具遥测和产品日志新增段保存在证据目录。

#### 自动化结果仍按各自证据报告

- M1–M8 界面验收仍为 **88 PASS / 0 FAIL / 6 SKIP / 3 INFO**；历史统计不因后续人工证据改写。当前状态中 M2.3、M2.x、M3.D、M3.E、M4.7 五项已有后续人工证据覆盖；M7.4 的原始 SKIP 保留并标记为本轮范围外，不改成通过，也不要求补验。3 项 INFO 仍不计通过。证据：D:\Agent\devverify-mc\logs\20261114-180131-r9-closeout\ 及本节对应人工证据。
- 夹具隔离 **4 PASS / 0 FAIL**，回收站定向验证 **9 PASS / 0 FAIL**，沿用同一 r9 收尾证据目录。
- 默认回归 **221 / 0**、桌面套件 **246 / 0**、宏编辑器 UIA **18 / 0**、Step16 定向真实输入 **16 / 0**，沿用 D:\Agent\devverify-mc\logs\20261116-133518-s1617-recheck\。
- 自动化套件与人工操作结果不相加；以上结果不构成“全部功能均已验收”的统计。

#### 本轮验收范围结论

本轮面向已经验证的单显示器使用环境交付。跨 DPI、多显示器、负坐标及显示拓扑变化不在本轮验收范围；不再安排对应测试，也不阻塞交付。现有相关代码与已验证行为保持不变。
#### 用户状态与收尾

测试前后 Local、Roaming 和项目目录中的 settings.json、library.json 均不存在；未点击“保存设置”，无需恢复这些文件。产品日志 C:\Users\Administrator\AppData\Local\MouseClicker\logs\run-20261116.log 因人工验收追加，最终长度 4907 字节、最后修改时间 2026-11-16 15:48:04；日志保留，未回滚。产品 PID 10940 与 STA 夹具 PID 4524 均已由托盘退出/关闭操作结束，无本轮窗口或进程残留。当前程序路径：D:\Agent\MouseClicker\bin\Release\net8.0-windows\MouseClicker.exe。

#### 环境提示验证后的用户状态与收尾

本轮 Local、Roaming 和项目目录中的 settings.json、library.json 均仍不存在；没有保存设置或更改 Windows 显示设置。产品日志 `C:\Users\Administrator\AppData\Local\MouseClicker\logs\run-20261116.log` 从 4907 字节增至 5176 字节，新增 269 字节只有三条录制样本加载/校验记录，没有回放启动记录；日志保留，未回滚。核对产品路径和夹具私有遥测归属后，结束本轮自有 PID 26548 与 28612；早期不可见夹具 PID 28436 在 UI 验收前已停止。清理检查确认三个已知测试 PID 均已退出。详情：`D:\Agent\devverify-mc\logs\20261116-164103-environment-prompt-check\cleanup-check.json`。当前程序路径：`D:\Agent\MouseClicker\bin\Release\net8.0-windows\MouseClicker.exe`。

### 2026-11-16 试用包整理与启动检查

- 试用目录：D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\
- 启动路径：D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe
- 目录只含 MouseClicker.exe、MouseClicker.dll、MouseClicker.deps.json、MouseClicker.runtimeconfig.json 和本说明文件；未放入用户数据、录制样本、运行日志或验证工程。
- 运行要求：Windows x64，并安装 .NET 8 Windows Desktop Runtime x64。包为依赖框架部署，不是免运行库版本。PE apphost 为 x64，runtimeconfig 依赖 Microsoft.NETCore.App 8.0 与 Microsoft.WindowsDesktop.App 8.0。检查机器已安装 Windows Desktop Runtime 8.0.0、8.0.22、8.0.31。
- 启动检查：启动前未发现 MouseClicker 实例。从试用目录启动 PID 4560；主窗口标题“鼠标连点器”，窗口句柄有效且响应正常。用户确认窗口可见并通过托盘“退出”结束；随后检查 PID 4560 已退出。
- 收尾状态：启动检查后未发现 settings.json 或 library.json。产品日志观察值为 run-20261114.log 8545 字节、run-20261116.log 5176 字节（最后修改时间 2026-11-16 08:59:39 UTC）；未观察到本次启动带来的日志追加。本轮自有产品进程已结束。

### 2026-11-16 侧栏布局最终收尾

唯一推荐试用程序：D:\Agent\MouseClicker\dist\MouseClicker-win-x64-trial\MouseClicker.exe（需 .NET 8 Windows Desktop Runtime x64）。旧 performance-20261116 目录保留为历史版本，不作为当前入口。四个运行文件与本轮 Release 逐字节一致。

- 布局：表单横向行不再在父容器尚未确定宽度时提前换行，消除卡片多算高度；最小窗口的位置模式行使用合适宽度。录制页禁用“轨迹抽稀”文字可读且仍禁用。日志和多点列表保留空间，长宏路径可换行显示。
- 产品构建：dotnet build "D:\Agent\MouseClicker\MouseClicker.csproj" -c Release，退出码 0，0 警告、0 错误；记录为 logs\20261116-sidebar-closeout\build-ship.txt 与 build-ship-exit.txt。
- 受影响 UI：pwsh -STA -NoProfile -ExecutionPolicy Bypass -File "D:\Agent\devverify-mc\ui-check.ps1" -OutDir "D:\Agent\devverify-mc\logs\20261116-sidebar-closeout\ship-ui"；15 PASS / 0 FAIL / 0 SKIP，退出码 0。覆盖运行中切页、全局停止、紧急停止、热键限制以及录制、回放和宏入口。
- 加载后切页：retention\retention-check.ps1 通过真实系统对话框加载本轮副本；录制摘要、开始回放可用状态、2× 倍速及轮数切页前后一致；宏名称、完整路径、2 轮摘要和开始宏可用状态一致。2 PASS / 0 FAIL / 0 SKIP，退出码 0；证据在 retention\stdout-final.txt、exit-final.txt、recording-state.json、macro-state.json，长路径显示见 macro-loaded.png。本测试不执行样本。
- 截图：ship-screens\default-1.png 至 default-5.png 与 minimum-1.png 至 minimum-5.png，按连点、录制、宏、日志、设置排序。实际从当前试用目录启动后捕获，恢复默认尺寸记录在 capture-summary.txt。
- 用户状态：测试前后 Local settings.json 和 library.json 均不存在；自有测试进程结束。产品 run-20261116.log 有追加并保留。历史自动套件统计及原始 SKIP 不因本轮检查而改写。
### 清爽精简界面（2026-11-16 至 17）

五页侧栏和固定状态/停止入口保留。连点页优先显示按键、间隔（ms）、位置、停止方式和开始连点；固定、多点、次数和时长按选择显示。点击动作、随机偏移、间隔上限及预设在“更多设置”中；已有高级配置会自动展开。录制加载后显示文件名、动作数、时长与回放设置，编辑/优化可展开。宏工作区在未加载时提供新建和打开入口，加载后显示名称、动作数、轮数，完整路径通过“查看路径”展开。设置页合并热键与常规设置，紧急停止 Ctrl+Shift+F12 仍显示。

- 产品 Release 构建：dotnet build "D:\Agent\MouseClicker\MouseClicker.csproj" -c Release，退出码 0，0 警告、0 错误；证据 build-release.txt 和 build-release-exit.txt。
- 更新后的 UIA：pwsh -STA -NoProfile -ExecutionPolicy Bypass -File "D:\Agent\devverify-mc\ui-check.ps1" -OutDir "D:\Agent\devverify-mc\logs\20261116-234213-simple-ui\uia-release"；15 PASS / 0 FAIL，退出码 0。紧急停止检查向自有产品窗口投递 WM_HOTKEY，验证对应停止分支；不冒称物理按键测试。
- 默认与最小模式检查：10 PASS / 0 FAIL 和 11 PASS / 0 FAIL，退出码均为 0，检查模式显示、输入保留以及折叠和切换位置后焦点离开隐藏输入；证据 modes-check-release-out.txt、focus-final-out.txt。
- 旧设置样本：1 PASS / 0 FAIL，退出码 0；随机间隔 80–220、偏移 9 和固定坐标恢复，更多设置自动展开。证据 legacy-settings-check-release-out.txt、legacy-settings-actual.json。测试前原 settings.json 不存在；本轮仅在文件仍与样本逐字节相同时清理样本。
- 独立录制/宏加载及折叠：5 PASS / 0 FAIL，退出码 0；真实文件对话框加载后，录制摘要、回放倍速和轮数、宏摘要及路径详情、切页状态保留；编辑/优化和默认关闭的轨迹抽稀可展开。证据 loaded-check-final-release-out.txt、recording-state.json、macro-state.json。
- 本轮早期一次整合 UIA 等待异常后被终止，退出码 -1，保留 uia-out.txt 与 uia-exit.txt、uia-final 目录；调整检查顺序后的 uia-release 是最终完整运行结果。各套件不相加为全部功能通过。
- 五页默认/最小截图：release-screens\default-1.png 至 default-5.png、minimum-1.png 至 minimum-5.png；其余截图 click-more.png、click-fixed.png、click-multi.png、minimum-click-multi.png、recording-loaded.png。文件均在本轮证据目录，试用目录四个运行文件与最终 Release 逐字节一致，trial-byte-comparison.txt 留档。

仅面向当前已验收的单显示器环境；不安排跨 DPI、多显示器专项。历史功能范围、历史验收和原始跳过统计不因本轮 UI 修改改写。
### 2026-11-17 紧凑窗口与验证

默认外框 900×640（实测客户区 884×601），最小外框 800×560（实测客户区 784×521）；侧栏宽 150。连点与录制页按操作顺序单列排列；需要时页面内容纵向滚动，侧栏、底部状态与停止入口固定。宏页面继续使用单个工作区。该尺寸为当前单显示器实测值。

- 构建：dotnet build "D:\Agent\MouseClicker\MouseClicker.csproj" -c Release；退出码 0，0 警告、0 错误。证据 build-initial.txt、build-initial-exit.txt。
- 受影响 UIA：使用本轮 ui-check-release.ps1（现有 ui-check.ps1 的副本，仅将程序路径指定为当前 Release；尺寸使用 900×640），15 PASS / 0 FAIL，退出码 0；输出 uia\stdout.txt 与 uia\exit-code.txt。运行中切页、全局停止、热键限制和紧急停止分支均有实际检查；紧急停止向自有窗口投递 WM_HOTKEY，不称为物理热键测试。
- 模式及焦点：默认 10 PASS、最小 11 PASS，均无 FAIL，退出码 0；检查条件显示、输入保留、折叠后焦点转移。旧配置恢复 1 PASS / 0 FAIL，退出码 0。各输出分别见 modes-check-out.txt、modes-minimum-out.txt、legacy-settings-check-out.txt。
- 加载和切页：默认及最小尺寸各 5 PASS / 0 FAIL，退出码 0；通过系统文件对话框加载独立录制及宏样本，检查摘要、路径详情、参数和切页保留，证据 loaded-check-final-out.txt 与 loaded-minimum-out.txt；长路径截图 minimum-macro-expanded.png。
- 最小尺寸滚动：15 PASS / 0 FAIL，退出码 0；多点及更多设置滚动到底后，相应控件完整显示在页脚上方，且窗口未显示横向滚动条。证据 scroll-advanced-out.txt、minimum-click-multi-bottom.png 与 minimum-more-bottom.png。首次探针对未启用滚动条的默认范围误判，记录保存在 scroll-out-first.txt；修正为检查实际窗口样式后重测。
- 最终截图从试用目录实际启动捕获：final-screens\default-1.png 至 default-5.png、minimum-1.png 至 minimum-5.png 和 restored-default.png；实际尺寸记录在 final-screens\capture-summary.txt。其他固定点、多点、更多设置和录制加载截图位于证据根目录。
- 试用目录四个运行文件与本轮 Release 逐字节一致（trial-byte-comparison.txt）。测试前后 Local 的 settings.json、library.json 不存在；本轮自有进程已结束，产品 run-20261117.log 测试期间追加并保留。历史自动验收及跳过统计继续分别引用，不与本轮结果相加。