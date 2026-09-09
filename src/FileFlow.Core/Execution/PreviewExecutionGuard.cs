using System.Runtime.CompilerServices;
using FileFlow.Core.Preview;

namespace FileFlow.Core.Execution;

internal static class PreviewExecutionGuard
{
    private sealed class Attempt { public int Claimed; }
    private static readonly ConditionalWeakTable<OperationPreview, Attempt> Attempts = new();

    internal static bool TryClaim(OperationPreview preview) =>
        Interlocked.CompareExchange(ref Attempts.GetValue(preview, _ => new Attempt()).Claimed, 1, 0) == 0;
}
