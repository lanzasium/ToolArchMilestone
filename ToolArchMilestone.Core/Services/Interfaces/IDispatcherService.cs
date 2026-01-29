using System;

namespace ToolArchMilestone.Core.Services.Interfaces
{
    public interface IDispatcherService
    {
        void TryEnqueue(Action action);
    }
}
