using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace MemoryLeak.Controllers
{
    [Route("api")]
    [ApiController]
    public class ApiController : ControllerBase
    {
        private readonly ILogger _logger;

        public ApiController(ILogger<ApiController> logger)
        {
            _logger = logger;

            Interlocked.Increment(ref DiagnosticsController.Requests);
        }

        private static readonly ConcurrentBag<string> s_staticStrings = new();
        private static readonly WeakReference<ConcurrentBag<string>> s_weakStrings = new(new ConcurrentBag<string>());

        [HttpGet("bigstring")]
        public ActionResult<string> GetBigString()
        {
            return new string('x', 10 * 1024);
        }

        [HttpGet("bigstring-with-collect")]
        public ActionResult<string> GetBigStringWithCollect()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            return new string('x', 10 * 1024);
        }

        [ThreadStatic]
        private static string t_bigString;

        [HttpGet("bigstring-cached")]
        public ActionResult<string> GetBigStringCached()
        {
            return t_bigString ??= new string('x', 10 * 1024);
        }

        [HttpGet("staticstring")]
        public ActionResult<string> GetStaticString()
        {
            string bigString = new('x', 10 * 1024);
            s_staticStrings.Add(bigString);
            return bigString;
        }

        [HttpGet("weakstring")]
        public ActionResult<string> GetWeakString()
        {
            string bigString = new('x', 10 * 1024);

            if (!s_weakStrings.TryGetTarget(out ConcurrentBag<string> strings))
            {
                _logger.LogInformation("WeakStrings got collected, creating new bag");

                lock (s_weakStrings)
                {
                    strings = new ConcurrentBag<string>();
                    s_weakStrings.SetTarget(strings);
                }
            }

            strings.Add(bigString);
            return bigString;
        }

        [HttpGet("loh/{size=85000}")]
        public int GetLOH(int size)
        {
            return new byte[size].Length;
        }

        private static readonly string s_tempPath = Path.GetTempPath();

        [HttpGet("fileprovider")]
        public void GetFileProvider()
        {
            var fp = new PhysicalFileProvider(s_tempPath);
            fp.Watch("*.*");
        }

        [HttpGet("httpclient1")]
        public async Task<int> GetHttpClient1(string url)
        {
            using var httpClient = new HttpClient();
            var result = await httpClient.GetAsync(url);
            return (int)result.StatusCode;
        }

        private static readonly HttpClient s_httpClient = new();

        [HttpGet("httpclient2")]
        public async Task<int> GetHttpClient2(string url)
        {
            var result = await s_httpClient.GetAsync(url);
            return (int)result.StatusCode;
        }

        [HttpGet("array/{size}")]
        public byte[] GetArray(int size)
        {
            var array = new byte[size];

            var random = new Random();
            random.NextBytes(array);

            return array;
        }

        private static readonly ArrayPool<byte> s_arrayPool = ArrayPool<byte>.Create();

        private class PooledArray : IDisposable
        {
            public byte[] Array { get; }

            public PooledArray(int size)
            {
                this.Array = s_arrayPool.Rent(size);
            }

            public void Dispose()
            {
                s_arrayPool.Return(this.Array);
            }
        }

        [HttpGet("pooledarray/{size}")]
        public byte[] GetPooledArray(int size)
        {
            var pooledArray = new PooledArray(size);

            var random = new Random();
            random.NextBytes(pooledArray.Array);

            this.HttpContext.Response.RegisterForDispose(pooledArray);

            return pooledArray.Array;
        }
    }
}
