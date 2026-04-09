using System.Threading.Tasks;

namespace Engie.Mca.Common.Execution;

public static class StepDelay
{
    public static Task DelayAsync(int milliseconds)
    {
        return milliseconds > 0 ? Task.Delay(milliseconds) : Task.CompletedTask;
    }
}