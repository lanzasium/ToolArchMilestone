using Microsoft.UI.Dispatching;
using System;
using ToolArchMilestone.Core.Services.Interfaces;

namespace ToolArchMilestone.Services
{
    public class WinUIDispatcherService : IDispatcherService
    {
        private readonly DispatcherQueue _dispatcherQueue;

        public WinUIDispatcherService()
        {
            _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        }

        public void TryEnqueue(Action action)
        {
            _dispatcherQueue.TryEnqueue(() => action());
        }
    }
}
