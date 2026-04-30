using Microsoft.JSInterop;

namespace NivoTask.Client.Services;

public class LocalStorageService
{
    private readonly IJSRuntime _js;

    public LocalStorageService(IJSRuntime js) => _js = js;

    public async Task<string?> GetAsync(string key)
        => await _js.InvokeAsync<string?>("localStorage.getItem", key);

    public async Task SetAsync(string key, string value)
        => await _js.InvokeVoidAsync("localStorage.setItem", key, value);

    public async Task RemoveAsync(string key)
        => await _js.InvokeVoidAsync("localStorage.removeItem", key);

    public async Task<int?> GetIntAsync(string key)
    {
        var raw = await GetAsync(key);
        return int.TryParse(raw, out var v) ? v : null;
    }

    public async Task SetIntAsync(string key, int value)
        => await SetAsync(key, value.ToString());
}
