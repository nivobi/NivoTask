using Microsoft.AspNetCore.Components.WebAssembly.Http;

namespace NivoTask.Client.Identity;

public class CookieHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.SetBrowserRequestCredentials(BrowserRequestCredentials.Include);
        request.Headers.Add("X-Requested-With", "XMLHttpRequest");
        return await base.SendAsync(request, cancellationToken);
    }
}
