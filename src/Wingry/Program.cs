using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Wingry.Core;

namespace Wingry;

public static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        // Arayüzsüz kipler: zamanlanmış görevler ve yönetici işçisi.
        if (args.Length > 0)
        {
            switch (args[0])
            {
                case "--worker":
                    return ElevatedWorker.Run(args.Skip(1).ToArray());
                case "--task":
                    return HeadlessTasks.Run(args.Skip(1).ToArray());
            }
        }

        WinRT.ComWrappersSupport.InitializeComWrappers();
        Application.Start(p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
        return 0;
    }
}
