# The hatch API, verified

Everything on this page was read out of the game's own assembly
(`Sea Power_Data/Managed/Seapower-Scripts.dll`) — type names, field names, method signatures, the
`public`/`protected`/`private` flags, and which parameters are optional. It is not inferred from
INI files or from behaviour.

Version read: the `Seapower-Scripts.dll` supplied for 0.8.x (4,490 types).

## The short version

```csharp
// One cell (one silo / tube): open and close its hatches.
bool opened  = weaponContainer.openHatches();
bool closed  = weaponContainer.closeHatches();
bool isOpen  = weaponContainer._areHatchesOpen;

// One mount: its cover / system animation.
bool opened  = weaponSystem.openSystem();
weaponSystem.closeSystem(0f);          // float = delay
```

That is the whole feature. No simulation, no animation poking: these are the same methods the
game's own launch sequence calls.

## `SeaPower.WeaponContainer` — one cell

```csharp
class WeaponContainer : SeaPower.WeaponAttacher
{
    public List<GameObject> _hatches;            // the actual hatch models of this cell
    public ObjectCodeAnimation _openAnimation;
    public ObjectCodeAnimation _closeAnimation;
    public AudioClip _hatchSoundClip;
    public bool _playOpenAnimation;
    public bool _playCloseAnimation;
    public bool _areHatchesOpen;                  // live state
    public AudioSource _audioSource;
    public double _closeStartTime;
    public float _closeStartDelay;
    public bool _triggerCloseAnimation;           // a pending auto-close after firing
    public WeaponSystemLauncher _weaponSystem;
    public List<WeaponBase> _weapons;
    public Ammunition _loadedAmmunition;
    public Ammunition _initialAmmunition;
    public int _initialAmmoCount;
    public int _containerIndex;
    public List<Vector3> _attachmentPositions;
    public List<double> _cellReloadEnd;
    public List<ReactiveProperty<string>> SlotStatus;

    public void addHatch(GameObject hatch);
    public bool openHatches();
    public bool closeHatches();
    public void update();
    public void launch(ObjectBase, Vector3, Vector3, float, string);
    // …ammunition handling…
}
```

One container is one cell. `_hatches` holds that cell's hatch GameObjects, so a container with four
hatches is a four-lid box launcher; `openHatches()` moves them all, which is the correct unit of
"an individual silo".

## Exact behaviour, read from the IL

The method bodies settle the details that matter for a manual control:

```csharp
bool WeaponContainer.openHatches()          // mirror image for closeHatches()
{
    if (_openAnimation == null) return false;        // no animation -> nothing can happen
    if (_areHatchesOpen) { _areHatchesOpen = true; return false; }   // already open
    if (_playOpenAnimation) return false;            // already running
    _openAnimation.playAnim(null);
    if (_hatchSoundClip != null) { _audioSource.clip = _hatchSoundClip;
                                   _audioSource.Stop(); _audioSource.Play(); }
    _playOpenAnimation = true;
    return true;
}

void WeaponContainer.update()               // runs every frame
{
    if (_playOpenAnimation) {
        ToggleWeaponsVisibility(true);      // missiles become visible as the lid opens
        if (!_openAnimation.update()) {     // animation finished
            _playOpenAnimation = false;
            _areHatchesOpen = true;
        }
    }
    if (_playCloseAnimation) { /* mirror: hides weapons, clears _playCloseAnimation */ }
}
```

Consequences the mod accounts for:

* **The bool return is not the state.** It means "started something"; opening an already-open cell
  returns `false`. Success is judged by `_areHatchesOpen`, `_playOpenAnimation` and
  `_playCloseAnimation`, never by the return value alone.
* **`_areHatchesOpen` only becomes `true` when the animation finishes** (or on the already-open
  early return), so "open or opening" is `_areHatchesOpen || _playOpenAnimation`. The menu uses
  that, minus `_playCloseAnimation`, which makes a second click during a close start opening again
  instead of stopping half way.
* **No animation, no hatch.** A container with hatch models but a null `_openAnimation` can never
  move, so those cells are hidden and reported rather than silently ignored.
* **`_triggerCloseAnimation` is the pending auto-close** the launch sequence parks on a cell;
  a manual open clears it, otherwise the hatch shuts again on the next update.
* **`WeaponSystem.closeSystem(float)` stores its argument into `_closeStartDelay`** — it is a delay
  in seconds, so `0f` closes immediately. `openSystem()` refuses while a load, unload or close
  animation is playing, and requires `_openSystemAnimation`.

## `SeaPower.WeaponSystem` and its subclasses — one mount

```csharp
class WeaponSystem : SeaPower.BaseSystem
{
    public WeaponParameters _vwp;
    protected HatchesState _hatchesState;        // protected: not readable from a mod
    public List<WeaponContainer> _containers;
    public List<WeaponBase> _launchedWeapons;
    public bool _isReloading;
    public bool _executingEngageTask;
    public bool _isOffline;
    public bool openSystem();
    public void closeSystem(float);
    public void OnUpdate();
}

class WeaponSystemLauncher : WeaponSystem
{
    public bool openHatches(int);          // mount-wide, argument meaning undocumented
    public void closeHatches(int, float);
    private List<int> _containersToClose;
}

class BaseSystem
{
    public ObjectCodeAnimation _openSystemAnimation;
    public ObjectCodeAnimation _closeSystemAnimation;
}
```

`WeaponSystemGun` and `WeaponSystemCIWS` derive from `WeaponSystem` too, and their containers
normally have no hatches — hence `IncludeGuns` is off by default.

The launcher's `openHatches(int)` / `closeHatches(int, float)` were deliberately **not** used: their
integer argument is not documented, and looping `WeaponContainer.openHatches()` gives exact per-cell
control with no ambiguity.

## `SeaPower.WeaponParameters` (`WeaponSystem._vwp`) — what the INI set up

```csharp
public string _sectionName, _systemName, _moduleName;
public string _openSystemAnimation;       // name of the code animation, from the INI
public string _closeSystemAnimation;
public int _numberOfHatchesPerContainers; // hatches per cell, from the INI
public GameObject _mountObject;
public WeaponMount _mount;
public GameObject _containerBaseObject;
public bool _areContainersRotatable, _areContainersJoinedTogether;
```

So the INI does configure hatches after all — `_numberOfHatchesPerContainers` and the
`AssociatedMagazine`-style open/close animation names — but only as *data*; the runtime logic lives
in `WeaponContainer`.

## Related enums

```csharp
enum HatchesState { HatchesOpen, HatchesClosed, OpeningHatches, ClosingHatches }
enum EngageState  { …, OpeningHatches, ClosingHatches, … }
```

`EngageState.OpeningHatches` explains the `TargetTooFarToOpenSystem` result other mods report: a
launch is refused when there is not enough time to open the system first. Interesting for a future
option ("open ahead of the order"), not needed for manual control.

## The menu surface (also verified)

| Member | Signature |
|---|---|
| `SeapowerUI.ViewModels.ObjectBaseViewModel.GetContextMenuItems` | `TrulyObservableCollection<ContextMenuItem> GetContextMenuItems()` — no parameters |
| `ObjectBaseViewModel._objectBase` | `public ObjectBase` |
| `ContextMenuItem..ctor` | `(string label = null, TrulyObservableCollection<ContextMenuItem> items = null, ICommand command = null, ReadOnlyReactiveProperty<bool> enabled = null, ReadOnlyReactiveProperty<bool> isChecked = null, string tooltip = null, …)` — **every** parameter optional |
| `ContextMenuItem..ctor` | `(string labelSection, string labelKey, …)` — first two required |
| `SeaPower.DelegateCommand..ctor` | `(Action<object>)` |
| `SeaPower.LanguageResourceHandler.getText` | `(string section, string key, bool logError = …, bool verboseError = …)` |
| `SeaPower.Globals._playerTaskforce` | `public Taskforce` |
| `SeaPower.FileManager.Directories` | `TrulyObservableCollection<SearchDirectory>` with `IsChecked`, `DirectoryInfo` |

Because both `ContextMenuItem` constructors have only optional parameters, positional calls are
ambiguous, so the mod uses named arguments everywhere.

## How this was read

No game, compiler or packager involved: a .NET assembly is a PE file whose metadata tables list
every type, field, method, parameter name and `[opt]` flag. `tools/dump-game-api.ps1` produces the
same information on a Windows machine, and `tools/read-metadata.py` reads it without Windows at all.
