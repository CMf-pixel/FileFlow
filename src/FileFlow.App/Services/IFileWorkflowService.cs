using FileFlow.Core.Execution;
using FileFlow.Core.Preview;
using FileFlow.Core.Rules;

namespace FileFlow.App.Services;

public interface IFileWorkflowService
{
    Task<OperationPreview> CreatePreviewAsync(FileRule rule);
    Task<ExecutionResult> ExecuteAsync(OperationPreview preview);
}
