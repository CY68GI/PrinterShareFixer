using PrinterShareFixer.Core.Models;
using PrinterShareFixer.Core.Runtime;

namespace PrinterShareFixer.Core;

/// <summary>按顺序执行修复方案，并回报每一步的状态。</summary>
public sealed class RepairEngine(ILogSink log)
{
    public async Task<RepairReport> RunAsync(
        RepairProfile profile,
        RepairOptions options,
        IProgress<StepUpdate>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var context = new RepairContext(options, log);
        var updates = new List<StepUpdate>();
        var startedAt = DateTimeOffset.Now;
        var abort = false;

        log.Write($"===== 开始执行「{profile.DisplayName} 打印机共享修复」=====");
        log.Write($"管理员权限：{(context.IsElevated ? "是" : "否")}");

        void Report(int index, RepairStep step, StepState state, string message, IReadOnlyList<string>? details = null)
        {
            var update = new StepUpdate(index, step, state, message, details);
            var existing = updates.FindIndex(u => u.Index == index);
            if (existing >= 0)
            {
                updates[existing] = update;
            }
            else
            {
                updates.Add(update);
            }

            progress?.Report(update);
        }

        for (var index = 0; index < profile.Steps.Count; index++)
        {
            var step = profile.Steps[index];

            if (abort)
            {
                Report(index, step, StepState.Skipped, "前序步骤失败，已跳过。");
                continue;
            }

            if (!options.IsStepEnabled(step.OptionKey))
            {
                Report(index, step, StepState.Skipped, "已在高级选项中关闭，跳过。");
                log.Write($"跳过（已关闭）：{step.Title}");
                continue;
            }

            if (step.RequiresElevation && !context.IsElevated)
            {
                Report(index, step, StepState.Skipped, "需要管理员权限，已跳过。");
                log.Write($"跳过（需要管理员权限）：{step.Title}");
                continue;
            }

            log.Write(string.Empty);
            log.Write($"[{index + 1}/{profile.Steps.Count}] {step.Title}");
            if (step.Description is { Length: > 0 })
            {
                log.Write($"  说明：{step.Description}");
            }

            foreach (var command in step.Commands)
            {
                log.Write($"  $ {command}");
            }

            Report(index, step, StepState.Running, "正在执行…");

            StepResult result;
            try
            {
                result = await step.Handler(context, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                Report(index, step, StepState.Skipped, "已被用户取消。");
                log.Write("已被用户取消。");
                throw;
            }
            catch (Exception ex)
            {
                result = StepResult.Fail($"执行出错：{ex.Message}");
                log.Write($"  异常：{ex}");
            }

            log.Write($"  结果：{result.State} - {result.Message}");
            foreach (var detail in result.Details ?? [])
            {
                log.Write($"    · {detail}");
            }

            Report(index, step, result.State, result.Message, result.Details);

            if (result.State == StepState.Failed && step.AbortOnFailure)
            {
                abort = true;
            }
        }

        var finishedAt = DateTimeOffset.Now;
        var logFile = (log as FileLogSink)?.FilePath;
        var report = new RepairReport
        {
            ProfileKey = profile.Key,
            ProfileName = profile.DisplayName,
            StartedAt = startedAt,
            FinishedAt = finishedAt,
            Updates = updates,
            LogFile = logFile,
        };

        log.Write(string.Empty);
        log.Write($"===== 结束：成功 {report.SucceededCount} 项，警告 {report.WarningCount} 项，失败 {report.FailedCount} 项，跳过 {report.SkippedCount} 项，用时 {(finishedAt - startedAt).TotalSeconds:F1} 秒 =====");

        return report;
    }
}
