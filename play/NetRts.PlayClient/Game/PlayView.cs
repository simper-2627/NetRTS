using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using NetRts.Protocol;

namespace NetRts.PlayClient.Game;

/// <summary>The C# side of <c>wwwroot/js/playview.js</c>: the shared map renderer in one game screen.</summary>
public sealed class PlayView : IAsyncDisposable
{
    private readonly IJSObjectReference _module;
    private readonly IJSObjectReference _view;

    private PlayView(IJSObjectReference module, IJSObjectReference view, Lore lore)
    {
        _module = module;
        _view = view;
        Lore = lore;
    }

    public Lore Lore { get; }

    /// <summary>Creates the view; pointer and key input on it is reported to <paramref name="input"/>'s [JSInvokable] methods.</summary>
    public static async Task<PlayView> CreateAsync<T>(IJSRuntime js, ElementReference frame, ElementReference canvas, ElementReference overlay, ElementReference minimap, DotNetObjectReference<T> input)
        where T : class
    {
        var module = await js.InvokeAsync<IJSObjectReference>("import", "./js/playview.js");
        var lore = new Lore(await module.InvokeAsync<LoreTables>("loreTables"));
        var view = await module.InvokeAsync<IJSObjectReference>("createView", frame, canvas, overlay, minimap, input);
        return new PlayView(module, view, lore);
    }

    public ValueTask SetMapAsync(MapDto map) => _view.InvokeVoidAsync("setMap", map);

    /// <summary>Teaches <see cref="Lore"/> how the spectator site words these engine messages.</summary>
    public async ValueTask RetellAsync(IEnumerable<string> messages)
    {
        var unknown = Lore.Unknown(messages).ToList();
        if (unknown.Count > 0)
        {
            Lore.Learn(unknown, await _module.InvokeAsync<string[]>("retell", unknown));
        }
    }

    public ValueTask SetStateAsync(RenderState state) => _view.InvokeVoidAsync("setState", state);

    /// <summary>Draw the fog as this slot sees it.</summary>
    public ValueTask SetVisionAsync(int slot) => _view.InvokeVoidAsync("setVision", slot);

    public ValueTask SetSelectionAsync(Selection selection) =>
        _view.InvokeVoidAsync("setSelection", selection.Units, selection.Building is { } b ? new[] { b } : [], selection.Inspected);

    /// <summary>"attack" or "build" while the next left click picks a target; null otherwise.</summary>
    public ValueTask SetModeAsync(string? mode) => _view.InvokeVoidAsync("setMode", mode);

    /// <summary>A short marker where an order went: "move", "attack" or "rally".</summary>
    public ValueTask FlashAsync(int x, int y, string kind) => _view.InvokeVoidAsync("flash", x, y, kind);

    public ValueTask LookAtAsync(int x, int y) => _view.InvokeVoidAsync("lookAt", x, y);

    /// <summary>The build ghost: <paramref name="mask"/> has one string per row, '1' where the building may go.</summary>
    public ValueTask SetGhostAsync(BuildingType type, int owner, IReadOnlyList<string> mask) => _view.InvokeVoidAsync("setGhost", type, owner, mask);

    public async ValueTask DisposeAsync()
    {
        try
        {
            await _view.InvokeVoidAsync("dispose");
            await _view.DisposeAsync();
            await _module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
        }
    }
}
