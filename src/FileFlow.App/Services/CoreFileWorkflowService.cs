using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;

namespace FileFlow.App.Services;

public sealed class CoreFileWorkflowService(FileRulePreviewer previewer, FileOperationExecutor executor) : IFileWorkflowService
{
    public Task<OperationPreview> CreatePreviewAsync(FileRule rule) => Task.Run(() => previewer.CreatePreview(rule));
    public Task<ExecutionResult> ExecuteAsync(OperationPreview preview) => Task.Run(() => executor.Execute(preview));
}
