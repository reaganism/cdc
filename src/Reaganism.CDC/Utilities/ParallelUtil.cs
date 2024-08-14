using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Reaganism.CDC.Utilities;

internal static class ParallelUtil
{
    public static void Execute(List<Action> actions)
    {
        Parallel.ForEach(
            actions,
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            x => x()
        );
    }
}