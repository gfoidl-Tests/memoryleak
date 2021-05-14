using System;
using System.Diagnostics;
using System.Runtime;
using System.Threading;
using Microsoft.AspNetCore.Mvc;

namespace MemoryLeak.Controllers
{
    public static class GlobalGC
    {
        public static string GC = GCSettings.IsServerGC ? "Server" : "Workstation";
    }

    [Route("api")]
    [ApiController]
    public class DiagnosticsController : ControllerBase
    {
        private static readonly Process s_process = Process.GetCurrentProcess();
        private static TimeSpan s_oldCPUTime = TimeSpan.Zero;
        private static DateTime s_lastMonitorTime = DateTime.UtcNow;
        private static DateTime s_lastRpsTime = DateTime.UtcNow;
        private static double s_cpu, s_rps;
        private static readonly double s_refreshRate = TimeSpan.FromSeconds(1).TotalMilliseconds;
        public static long Requests;

        [HttpGet("collect")]
        public ActionResult GetCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return Ok();
        }

        [HttpGet("diagnostics")]
        public ActionResult GetDiagnostics()
        {
            var now = DateTime.UtcNow;
            s_process.Refresh();

            var cpuElapsedTime = now.Subtract(s_lastMonitorTime).TotalMilliseconds;

            if (cpuElapsedTime > s_refreshRate)
            {
                var newCPUTime = s_process.TotalProcessorTime;
                var elapsedCPU = (newCPUTime - s_oldCPUTime).TotalMilliseconds;
                s_cpu = elapsedCPU * 100 / Environment.ProcessorCount / cpuElapsedTime;

                s_lastMonitorTime = now;
                s_oldCPUTime = newCPUTime;
            }

            var rpsElapsedTime = now.Subtract(s_lastRpsTime).TotalMilliseconds;
            if (rpsElapsedTime > s_refreshRate)
            {
                s_rps = Interlocked.Exchange(ref Requests, 0) * 1000 / rpsElapsedTime;
                s_lastRpsTime = now;
            }

            var diagnostics = new
            {
                PID = s_process.Id,

                // The memory occupied by objects.
                Allocated = GC.GetTotalMemory(false),

                // The working set includes both shared and private data. The shared data includes the pages that contain all the 
                // instructions that the process executes, including instructions in the process modules and the system libraries.
                WorkingSet = s_process.WorkingSet64,

                // The value returned by this property represents the current size of memory used by the process, in bytes, that 
                // cannot be shared with other processes.
                PrivateBytes = s_process.PrivateMemorySize64,

                // The number of generation 0 collections
                Gen0 = GC.CollectionCount(0),

                // The number of generation 1 collections
                Gen1 = GC.CollectionCount(1),

                // The number of generation 2 collections
                Gen2 = GC.CollectionCount(2),

                CPU = s_cpu,

                RPS = s_rps
            };

            return new ObjectResult(diagnostics);
        }
    }
}
