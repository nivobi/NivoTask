namespace NivoTask.Client.Services;

public class ThemeService
{
    private readonly LocalStorageService _storage;
    private bool _isDarkMode;
    private bool _initialized;

    private const string StorageKey = "darkMode";

    public ThemeService(LocalStorageService storage) => _storage = storage;

    public bool IsDarkMode => _isDarkMode;

    public event Action? OnThemeChanged;

    public async Task InitializeAsync()
    {
        if (_initialized) return;
        var stored = await _storage.GetAsync(StorageKey);
        _isDarkMode = stored == "true";
        _initialized = true;
    }

    public async Task ToggleAsync()
    {
        _isDarkMode = !_isDarkMode;
        await _storage.SetAsync(StorageKey, _isDarkMode ? "true" : "false");
        OnThemeChanged?.Invoke();
    }

    public async Task SetDarkModeAsync(bool value)
    {
        if (_isDarkMode == value) return;
        _isDarkMode = value;
        await _storage.SetAsync(StorageKey, _isDarkMode ? "true" : "false");
        OnThemeChanged?.Invoke();
    }
}
