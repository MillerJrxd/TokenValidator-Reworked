using Microsoft.Extensions.DependencyInjection;
using System.Net.Http;
using System.Text;

namespace TokenValidator.Utils
{
    public class HttpQuery : IDisposable
    {
        private readonly HttpClient _client;
        private readonly SemaphoreSlim _requestSemaphore = new(8, 8);

        public HttpQuery()
        {
            var factory = (App.Current.Properties["ServiceProvider"] as IServiceProvider)?
                .GetRequiredService<IHttpClientFactory>();

            _client = factory?.CreateClient("SCPClient") ?? new HttpClient();
        }

        public void Dispose()
        {
            _client.Dispose();
            _requestSemaphore.Dispose();
            GC.SuppressFinalize(this);
        }

        public string Post(string url, string postData)
        {
            return PostAsync(url, postData).GetAwaiter().GetResult();
        }

        public async Task<string> PostAsync(string url, string postData, CancellationToken cancellationToken = default)
        {
            var content = new StringContent(
                postData,
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            try
            {
                await _requestSemaphore.WaitAsync(cancellationToken);

                using (var request = new HttpRequestMessage(HttpMethod.Post, url))
                {
                    request.Content = content;

                    using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
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
                _requestSemaphore.Release();
            }
        }

        public string Get(string url)
        {
            return GetAsync(url).GetAwaiter().GetResult();
        }

        public async Task<string> GetAsync(string url, CancellationToken cancellationToken = default)
        {
            try
            {
                await _requestSemaphore.WaitAsync(cancellationToken);

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    using (var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
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
                _requestSemaphore.Release();
            }
        }
    }
}
