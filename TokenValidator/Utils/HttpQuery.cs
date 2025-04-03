using System.Net.Http;
using System.Text;

namespace TokenValidator.Utils
{
    public class HttpQuery
    {
        private static readonly HttpClient Client = new HttpClient();

        static HttpQuery()
        {
            Client.DefaultRequestHeaders.Add("User-Agent", "TokenValidator/3.0.0");
            Client.Timeout = TimeSpan.FromSeconds(30);
        }

        public static string Post(string url, string postData)
        {
            var content = new StringContent(
                postData,
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            try
            {
                var response = Client.PostAsync(url, content).Result;
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception ex)
            {
                throw new Exception($"HTTP Post failed: {ex.Message}", ex);
            }
        }

        public static async Task<string> PostAsync(string url, string postData)
        {
            var content = new StringContent(
                postData,
                Encoding.UTF8,
                "application/x-www-form-urlencoded");

            try
            {
                var response = await Client.PostAsync(url, content);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"HTTP Post failed: {ex.Message}", ex);
            }
        }

        public static string Get(string url)
        {
            try
            {
                var response = Client.GetAsync(url).Result;
                response.EnsureSuccessStatusCode();
                return response.Content.ReadAsStringAsync().Result;
            }
            catch (Exception ex)
            {
                throw new Exception($"HTTP Get failed: {ex.Message}", ex);
            }
        }

        public static async Task<string> GetAsync(string url)
        {
            try
            {
                var response = await Client.GetAsync(url);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadAsStringAsync();
            }
            catch (Exception ex)
            {
                throw new Exception($"HTTP Get failed: {ex.Message}", ex);
            }
        }
    }
}
