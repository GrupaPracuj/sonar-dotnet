namespace System.Net.Http
{
    public class HttpClient
    {
        public System.Threading.Tasks.Task<string> GetStringAsync(string url) => null;
        public System.Threading.Tasks.Task<string> GetStringAsync(string url, System.Threading.CancellationToken cancellationToken) => null;
    }
}

namespace Tests.Diagnostics
{
    public class OrderClient
    {
        private readonly System.Net.Http.HttpClient _httpClient;

        public System.Threading.Tasks.Task<string> GetOrder(string id, System.Threading.CancellationToken cancellationToken) =>
            _httpClient.GetStringAsync("/orders/" + id); // Noncompliant {{Pass the available CancellationToken to this call to another service, so it can be cancelled or time out.}}

        public System.Threading.Tasks.Task<string> GetOrderAlreadyPassed(string id, System.Threading.CancellationToken cancellationToken) =>
            _httpClient.GetStringAsync("/orders/" + id, cancellationToken);
    }
}
