using System.Net.Http;
using System.Text;

namespace TokenValidator.Utils
{
    public class HttpQuery
    {
        private static readonly HttpClient Client;
        private static readonly SemaphoreSlim RequestSemaphore = new(8, 8);
        private const int DefaultTimeoutSeconds = 30;

        static HttpQuery()
        {
            var handler = new HttpClientHandler
            {
                MaxConnectionsPerServer = 20,
                AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
            };

            Client = new HttpClient(handler)
            {
                Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds)
            };

            Client.DefaultRequestHeaders.Add("User-Agent", "SCP SL Token Validation Tool");
            Client.DefaultRequestHeaders.Add("Accept", "application/json");
            Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
            Client.DefaultRequestHeaders.Connection.Add("keep-alive");
        }

        public static string Post(string url, string postData)
        {
            return PostAsync(url, postData).GetAwaiter().GetResult();
        }

        public static async Task<string> PostAsync(string url, string postData, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(
                postData,
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            try
            {
                await RequestSemaphore.WaitAsync(cancellationToken);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = content;

                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Logging.LogException(ex);
                throw new TimeoutException("The request timed out. Please check your network connection.\nA log has been created.");
            }
            catch (HttpRequestException ex)
            {
                Logging.LogException(ex);
                throw new Exception($"HTTP Post failed: {ex.Message}\nA log has been created.", ex);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                throw new Exception($"HTTP Post failed: {ex.Message}\nA log has been created.", ex);
            }
            finally
            {
                RequestSemaphore.Release();
            }
        }

        public static string Get(string url)
        {
            return GetAsync(url).GetAwaiter().GetResult();
        }

        public static async Task<string> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                await RequestSemaphore.WaitAsync(cancellationToken);

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
                    {
                        response.EnsureSuccessStatusCode();
                        return await response.Content.ReadAsStringAsync();
                    }
                }
            }
            catch (TaskCanceledException ex)
            {
                Logging.LogException(ex);
                throw new TimeoutException("The request timed out. Please check your network connection.\nA log has been created.");
            }
            catch (HttpRequestException ex)
            {
                Logging.LogException(ex);
                throw new Exception($"HTTP Get failed: {ex.Message}\nA log has been created.", ex);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
                throw new Exception($"HTTP Get failed: {ex.Message}\nA log has been created.", ex);
            }
            finally
            {
                RequestSemaphore.Release();
            }
        }

        public static void ResetClient()
        {
            try
            {
                Client.DefaultRequestHeaders.Clear();
                Client.DefaultRequestHeaders.Add("User-Agent", "SCP SL Token Validation Tool");
                Client.DefaultRequestHeaders.Add("Accept", "application/json");
                Client.DefaultRequestHeaders.Add("Accept-Encoding", "gzip, deflate");
                Client.DefaultRequestHeaders.Connection.Add("keep-alive");

                Client.Timeout = TimeSpan.FromSeconds(DefaultTimeoutSeconds);
            }
            catch (Exception ex)
            {
                Logging.LogException(ex);
            }
        }
    }
}
