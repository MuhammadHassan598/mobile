namespace EmpireSim.Core.Services;

/// <summary>What a press of Back did.</summary>
public enum BackOutcome
{
    /// <summary>Nothing: the player is on the main screen with nothing open (the platform's own Back, such as leaving the app, applies).</summary>
    None,
    /// <summary>The topmost open layer (a popup, a country card, a sub-view) was closed; the page underneath stays.</summary>
    ClosedLayer,
    /// <summary>The app was sent to the previous screen.</summary>
    Navigated,
}

/// <summary>
/// The one navigation history of the app, behind the single Back button (the arrow in the top bar) and the device's own Back (Android).
/// <list type="bullet">
/// <item><b>Screens.</b> Every screen the player moves forward to is remembered; Back returns to the screen they actually came from
/// (Command Control → Colonies → one colony → Back → Colonies → Back → Command Control), never blindly to the menu. The main screen
/// (<see cref="Home"/>) is the root: going to it forgets the trail, and Back from a screen with no history falls back to it.</item>
/// <item><b>Layers.</b> A popup, a country card or a sub-view registers itself as a <i>layer</i> while it is open (<see cref="OpenLayer"/>, in the app a
/// <c>BackLayer</c> component placed inside it). Back closes the topmost layer first and only then leaves the screen underneath.</item>
/// <item><b>Memory.</b> Small pieces of screen state worth keeping (the map's selected country) are remembered by key; <see cref="ArrivedByBack"/>
/// says whether the current screen was reached by going back, so a screen can restore what the player left rather than start afresh.</item>
/// </list>
/// The class knows nothing about Blazor: the app attaches a <see cref="Attach">navigator</see> that actually changes the page, and reports each page change
/// with <see cref="Visited"/>. It is thread-safe, and layer handlers are expected to marshal onto the UI thread themselves.
/// </summary>
public sealed class NavigationHistory
{
    /// <summary>The main game screen: the root of the history.</summary>
    public const string Home = "/";

    /// <summary>How many screens are remembered (the oldest are forgotten first).</summary>
    public const int MaxDepth = 60;

    private sealed class Layer { public Func<Task> Close = () => Task.CompletedTask; }

    private readonly object _gate = new();
    private readonly List<string> _back = new();
    private readonly List<Layer> _layers = new();
    private readonly Dictionary<string, string> _memory = new();
    private string _current = Home;
    private string? _pendingBack;
    private bool _arrivedByBack;
    private Action<string>? _navigator;

    /// <summary>Raised whenever what Back would do may have changed (a screen change, a layer opened or closed).</summary>
    public event Action? Changed;

    // ================= Reading =================

    /// <summary>The screen the player is on, as a normalised relative location ("/", "/map", "/colonies/colony-java").</summary>
    public string Current { get { lock (_gate) return _current; } }

    /// <summary>The screen Back would return to (null when there is none).</summary>
    public string? Previous { get { lock (_gate) return _back.Count > 0 ? _back[^1] : null; } }

    /// <summary>The screens behind the current one, oldest first.</summary>
    public IReadOnlyList<string> Trail { get { lock (_gate) return _back.ToList(); } }

    /// <summary>How many layers (popups, cards, sub-views) are open now.</summary>
    public int LayerCount { get { lock (_gate) return _layers.Count; } }

    /// <summary>Whether the current screen was reached by going back (so it should restore what the player left).</summary>
    public bool ArrivedByBack { get { lock (_gate) return _arrivedByBack; } }

    /// <summary>
    /// Whether Back has anything to do: a layer to close, a screen to return to, or (from a screen with no history) the main screen to fall back to.
    /// False only on the main screen with nothing open.
    /// </summary>
    public bool CanGoBack { get { lock (_gate) return _layers.Count > 0 || _back.Count > 0 || !Same(_current, Home); } }

    // ================= The app side =================

    /// <summary>Normalises a location: relative, with a leading slash, no fragment, no trailing slash ("map/?focus=x#y" → "/map?focus=x").</summary>
    public static string Normalize(string? location)
    {
        string text = (location ?? "").Trim();
        int hash = text.IndexOf('#');
        if (hash >= 0) text = text[..hash];
        string query = "";
        int q = text.IndexOf('?');
        if (q >= 0) { query = text[q..]; text = text[..q]; }
        text = "/" + text.Trim('/');
        return text + (query == "?" ? "" : query);
    }

    private static bool Same(string a, string b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    /// <summary>Starts a fresh history at a location (the app has just started, or reconnected).</summary>
    public void Reset(string location)
    {
        lock (_gate)
        {
            _back.Clear(); _layers.Clear(); _pendingBack = null; _arrivedByBack = false;
            _current = Normalize(location);
        }
        Changed?.Invoke();
    }

    /// <summary>Gives the history the means to change the page. The app calls it once; <paramref name="navigator"/> should marshal onto the UI thread itself.</summary>
    public void Attach(Action<string> navigator) { lock (_gate) _navigator = navigator; }

    /// <summary>Removes <paramref name="navigator"/> (only if it is still the attached one).</summary>
    public void Detach(Action<string> navigator) { lock (_gate) if (_navigator == navigator) _navigator = null; }

    /// <summary>Whether <paramref name=location/> is the screen the history itself has just sent the app back to (so its arrival is a return, not a visit).</summary>
    public bool IsPendingBack(string location) { lock (_gate) return _pendingBack is not null && Same(Normalize(location), _pendingBack); }

    /// <summary>A page change has begun: the screen it leads to is not (yet) known to be a return.</summary>
    public void Navigating() { lock (_gate) _arrivedByBack = false; }

    /// <summary>
    /// The page changed. A page the history itself sent the app to is a return (nothing is added); the main screen forgets the trail; anything else is a
    /// new visit and the screen left is remembered. <paramref name="platformBack"/> says the page change was the platform's own back (the browser's
    /// history, Alt+Left): a page that is the one behind the current screen is then treated as going back too.
    /// </summary>
    public void Visited(string location, bool platformBack = false)
    {
        string next = Normalize(location);
        lock (_gate)
        {
            if (Same(next, _current)) { _pendingBack = null; return; }
            if (_pendingBack is not null && Same(next, _pendingBack))
            {
                _pendingBack = null; _current = next; _arrivedByBack = true;
            }
            else
            {
                _pendingBack = null;
                if (Same(next, Home))
                {
                    _back.Clear(); _current = next; _arrivedByBack = false;
                }
                else if (platformBack && _back.Count > 0 && Same(next, _back[^1]))
                {
                    _back.RemoveAt(_back.Count - 1); _current = next; _arrivedByBack = true;
                }
                else
                {
                    if (_back.Count == 0 || !Same(_back[^1], _current)) _back.Add(_current);
                    if (_back.Count > MaxDepth) _back.RemoveRange(0, _back.Count - MaxDepth);
                    _current = next; _arrivedByBack = false;
                }
            }
        }
        Changed?.Invoke();
    }

    // ================= Layers =================

    /// <summary>
    /// Registers an open layer. While it is open, Back calls <paramref name="close"/> before it leaves the page. Dispose the result when the layer closes
    /// by any other route (it is then forgotten); a layer that Back has already closed is forgotten at once.
    /// </summary>
    public IDisposable OpenLayer(Func<Task> close)
    {
        var layer = new Layer { Close = close };
        lock (_gate) _layers.Add(layer);
        Changed?.Invoke();
        return new Handle(this, layer);
    }

    private void Forget(Layer layer)
    {
        bool removed;
        lock (_gate) removed = _layers.Remove(layer);
        if (removed) Changed?.Invoke();
    }

    private sealed class Handle : IDisposable
    {
        private NavigationHistory? _owner; private readonly Layer _layer;
        public Handle(NavigationHistory owner, Layer layer) { _owner = owner; _layer = layer; }
        public void Dispose() { var o = Interlocked.Exchange(ref _owner, null); o?.Forget(_layer); }
    }

    // ================= Back =================

    /// <summary>
    /// One press of Back: closes the topmost layer if any is open; otherwise returns to the previous screen; from a screen with no history, falls back to
    /// the main screen; on the main screen with nothing open, does nothing (<see cref="BackOutcome.None"/>).
    /// </summary>
    public BackOutcome Back()
    {
        Layer? layer = null; string? target = null; Action<string>? navigator;
        lock (_gate)
        {
            navigator = _navigator;
            if (_layers.Count > 0)
            {
                layer = _layers[^1];
                _layers.RemoveAt(_layers.Count - 1);   // forgotten now: a second Back moves on even if the layer's own close does nothing
            }
            else if (navigator is not null)
            {
                if (_back.Count > 0) { target = _back[^1]; _back.RemoveAt(_back.Count - 1); }
                else if (!Same(_current, Home)) target = Home;
                if (target is not null) _pendingBack = target;
            }
        }

        if (layer is not null)
        {
            try { _ = layer.Close().ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
            catch { /* a layer that cannot close is already forgotten */ }
            Changed?.Invoke();
            return BackOutcome.ClosedLayer;
        }
        if (target is null || navigator is null) return BackOutcome.None;
        navigator(target);
        return BackOutcome.Navigated;
    }

    // ================= Memory =================

    /// <summary>Remembers a small piece of screen state under a key (null forgets it).</summary>
    public void Remember(string key, string? value)
    {
        lock (_gate) { if (value is null) _memory.Remove(key); else _memory[key] = value; }
    }

    /// <summary>What was remembered under a key (null if nothing).</summary>
    public string? Recall(string key) { lock (_gate) return _memory.GetValueOrDefault(key); }
}
