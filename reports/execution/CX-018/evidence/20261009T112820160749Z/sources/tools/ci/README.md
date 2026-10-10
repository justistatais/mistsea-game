# 构建入口

在项目根目录运行 `python tools/ci/build.py test`。首次依赖配置运行 `configure`，此后 restore 采用 locked mode。`build` 只编译，`smoke` 启动 Godot 托管场景，`content` 编译内部内容并执行正式发布拒绝检查。

SDK 由 global.json 固定。本机可读取 toolchain/installed.windows-x64.json；迁移机器时安装同版 SDK/Godot，并通过 PATH 提供工具。安装清单不是源码内硬编码路径。

每次执行在 reports/build/UTC时间目录保存实际命令、退出码、日志和测试结果，失败保留 failure.json。显式失败探针和正式发布拒绝记录预期非零原因。未运行真人项目保留 not_run，不计产品覆盖。测试数据与实际子进程终止证据保存在该次目录 testdata；进程终止实验不等于断电实验。
