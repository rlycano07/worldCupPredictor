using Microsoft.JSInterop;

namespace WorldCupPredict.Services;

public sealed class ThemeService(LocalStorageService localStorage, IJSRuntime jsRuntime)
{
    private const string StorageKey = "world-cup-predictor-theme";

    public string CurrentTheme { get; private set; } = "light";
    public bool IsDark => CurrentTheme == "dark";
    public event Action? Changed;

    public async Task InitializeAsync()
    {
        CurrentTheme = await localStorage.GetAsync<string>(StorageKey) ?? "light";
        await ApplyThemeAsync();
        Changed?.Invoke();
    }

    public async Task ToggleAsync()
    {
        CurrentTheme = IsDark ? "light" : "dark";
        await localStorage.SetAsync(StorageKey, CurrentTheme);
        await ApplyThemeAsync();
        Changed?.Invoke();
    }

    private async Task ApplyThemeAsync() =>
        await jsRuntime.InvokeVoidAsync("worldCupPredictor.setTheme", CurrentTheme);
}
