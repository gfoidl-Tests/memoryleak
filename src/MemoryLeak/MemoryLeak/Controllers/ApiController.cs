using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;

namespace MemoryLeak.Controllers
{
    [Route("api")]
    [ApiController]
    public class ApiController : ControllerBase
    {
        public ApiController()
        {
            Interlocked.Increment(ref DiagnosticsController.Requests);
        }

        private static readonly ConcurrentBag<string> s_staticStrings = new();

        [HttpGet("staticstring")]
        public ActionResult<string> GetStaticString()
        {
            var bigString = new string('x', 10 * 1024);
            s_staticStrings.Add(bigString);
            return bigString;
        }

        [HttpGet("bigstring")]
        public ActionResult<string> GetBigString()
        {
            return new string('x', 10 * 1024);
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
                Array = s_arrayPool.Rent(size);
            }

            public void Dispose()
            {
                s_arrayPool.Return(Array);
            }
        }

        [HttpGet("pooledarray/{size}")]
        public byte[] GetPooledArray(int size)
        {
            var pooledArray = new PooledArray(size);

            var random = new Random();
            random.NextBytes(pooledArray.Array);

            HttpContext.Response.RegisterForDispose(pooledArray);

            return pooledArray.Array;
        }
    }
}
