using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;

namespace CodexZhLauncher
{
    internal static class DownloadResumeTests
    {
        internal static void Run(Action<bool, string> check)
        {
            var folder = Path.Combine(Path.GetTempPath(), "CodexResumeTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            var bytes = new byte[] { 1, 2, 3, 4, 5, 6 };
            string hash;
            using (var sha = SHA256.Create())
                hash = BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "");
            var plan = new CodexDownloadPlan
            {
                FileName = "Codex-1.2.3.0-windows-x64.msix",
                Length = bytes.Length,
                Sha256 = hash,
                Url = new Uri("https://codexapp.agentsmirror.com/latest/win-x64")
            };
            var target = Path.Combine(folder, CodexDownloads.CacheFileName(plan));
            var partial = target + ".partial";
            try
            {
                File.WriteAllBytes(partial, bytes.Take(2).ToArray());
                using (var client = new HttpClient(new FakeMirror((request, attempt) =>
                {
                    check(request.Headers.Range.ToString() == "bytes=2-", "resume_range_sent_" + attempt);
                    if (attempt == 1)
                    {
                        check(request.RequestUri.Host == "codexapp-r2.agentsmirror.com", "direct_mirror_first");
                        return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable);
                    }
                    check(request.RequestUri.Host == plan.Url.Host, "falls_back_to_second_mirror");
                    return Partial(bytes.Skip(2).ToArray(), 2, bytes.Length);
                })))
                {
                    var saved = CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult();
                    check(File.ReadAllBytes(saved).SequenceEqual(bytes) && !File.Exists(partial), "resumed_file_verified_and_committed");
                }
                using (var client = new HttpClient(new FakeMirror((request, attempt) => { throw new Exception("Cache hit should not use network"); })))
                {
                    check(CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult() == target,
                        "verified_cache_avoids_network");
                    File.Move(target, Path.Combine(folder, Guid.NewGuid().ToString("N") + "-" + plan.FileName));
                    check(CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult() == target,
                        "previous_version_cache_reused");
                }

                File.Delete(target);
                File.WriteAllBytes(partial, new byte[] { 8, 8 });
                using (var client = new HttpClient(new FakeMirror((request, attempt) =>
                {
                    if (attempt == 1) return Partial(bytes.Skip(1).ToArray(), 1, bytes.Length);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                })))
                {
                    var saved = CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult();
                    check(File.ReadAllBytes(saved).SequenceEqual(bytes), "wrong_content_range_rejected_and_http_200_restarts");
                }

                File.Delete(target);
                File.WriteAllBytes(partial, new byte[] { 8, 8 });
                using (var client = new HttpClient(new FakeMirror((request, attempt) =>
                {
                    if (attempt == 1) return Partial(bytes.Skip(2).ToArray(), 2, bytes.Length);
                    check(request.Headers.Range == null, "hash_mismatch_discards_partial_before_retry");
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
                })))
                {
                    var saved = CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult();
                    check(File.ReadAllBytes(saved).SequenceEqual(bytes), "fresh_retry_recovers_corrupted_partial");
                }

                File.Delete(target);
                File.WriteAllBytes(partial, bytes.Take(2).ToArray());
                using (var stop = new CancellationTokenSource())
                using (var client = new HttpClient(new FakeMirror((request, attempt) =>
                {
                    stop.Cancel();
                    throw new OperationCanceledException();
                })))
                {
                    try
                    {
                        CodexDownloads.DownloadAsync(plan, folder, null, stop.Token, client).GetAwaiter().GetResult();
                        throw new Exception("Expected paused download");
                    }
                    catch (OperationCanceledException)
                    {
                        check(File.ReadAllBytes(partial).SequenceEqual(bytes.Take(2)), "paused_partial_preserved");
                    }
                }

                var redirectRequests = 0;
                using (var client = new HttpClient(new FakeMirror((request, attempt) =>
                {
                    if (!CodexDownloads.IsMirrorAddress(request.RequestUri)) throw new Exception("Redirect escaped mirror allowlist");
                    redirectRequests++;
                    var response = new HttpResponseMessage(HttpStatusCode.Redirect);
                    response.Headers.Location = new Uri("https://example.com/outside-mirror");
                    return response;
                })))
                {
                    try
                    {
                        CodexDownloads.DownloadAsync(plan, folder, null, CancellationToken.None, client).GetAwaiter().GetResult();
                        throw new Exception("Expected blocked redirect");
                    }
                    catch (IOException)
                    {
                        check(redirectRequests == 3 && File.Exists(partial), "untrusted_redirect_blocked_before_request");
                    }
                }
            }
            finally
            {
                foreach (var file in Directory.EnumerateFiles(folder)) File.Delete(file);
                Directory.Delete(folder);
            }
        }

        private static HttpResponseMessage Partial(byte[] bytes, long offset, long total)
        {
            var response = new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentRange = new ContentRangeHeaderValue(offset, total - 1, total);
            return response;
        }

        private sealed class FakeMirror : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, int, HttpResponseMessage> respond;
            private int attempts;
            internal FakeMirror(Func<HttpRequestMessage, int, HttpResponseMessage> respond) { this.respond = respond; }
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken token)
            {
                return Task.FromResult(respond(request, ++attempts));
            }
        }
    }
}
